using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using Verse;

namespace MigCorp.Skiptech.Skipgate.Operations
{
    public enum LinkRole
    {
        Initiator, // The skipgate that starts and pays the initial link cost.
        Responder // The skipgate that just dials in once the initiator starts dialing.
    }

    // Handles linking and opening a portal (Building_SkipgatePortal) to another Skipgate.
    // One skipgate execute this operation and notifies the other to get ready (reserves it once dialing starts, but link operation can be cancelled from either end).
    // Only the executing skipgate pays the initial charge cost. Once charged, it notifies the other end to dial at the same time as this one dials.
    // Once executed, the operation stays active until either portal is taken down (either manually, or sustained loss of power).
    public class SkipgateOperation_Link : SkipgateOperation
    {
        private LinkRole role = LinkRole.Initiator;
        private Building_Skipgate otherGateBuilding;

        public override SkipgateOperationType Type => SkipgateOperationType.Link;
        public LinkRole Role => role;
        public CompSkipgate OtherGate => otherGateBuilding?.skipgateComp;

        protected override bool ChecksCapacityPolicy => false; // Link cost is exempt from the maxCharge policy.
        protected override float CancelHeat => phase == SkipgateOperationPhase.Active ? requiredCharge : 0f;

        public SkipgateOperation_Link(CompSkipgate gate) : base(gate)
        {
        }

        public SkipgateOperation_Link(CompSkipgate gate, Building_Skipgate target) : this(gate)
        {
            role = LinkRole.Initiator;
            otherGateBuilding = target;
            requiredCharge = CalculateLinkCost(gate, target);
        }

        public SkipgateOperation_Link(CompSkipgate gate, Building_Skipgate initiatorGate, float linkCost) : this(gate)
        {
            role = LinkRole.Responder;
            otherGateBuilding = initiatorGate;
            requiredCharge = linkCost;
        }

        public static float CalculateLinkCost(CompSkipgate gate, Building_Skipgate target)
        {
            float tiles = Find.WorldGrid.ApproxDistanceInTiles(gate.parent.Map.Tile, target.Map.Tile);
            return gate.Props.linkCostBase + gate.Props.linkCostPerTile * tiles;
        }

        public override AcceptanceReport CanStart()
        {
            if (otherGateBuilding == null || !otherGateBuilding.Spawned) { return "No valid target skipgate."; }
            if (otherGateBuilding == gate.parent) { return "Cannot link a skipgate to itself."; }

            if (role == LinkRole.Initiator)
            {
                CompSkipgate otherGate = OtherGate;
                if (otherGate == null || otherGate.CurrentOperation != null || otherGate.CoolingDown)
                {
                    return "Target skipgate is unavailable.";
                }
            }

            return base.CanStart();
        }

        public override void Start()
        {
            if (role == LinkRole.Responder)
            {
                phase = SkipgateOperationPhase.Preparing;
                ApplyRuntimeState();
                return;
            }

            base.Start();

            // Reserve the other gate by having it perform a Link operation as a Responder.
            if (!OtherGate.TryStartOperation(new SkipgateOperation_Link(OtherGate, (Building_Skipgate)gate.parent, requiredCharge)))
            {
                FailOperation($"Link failed: {otherGateBuilding.RenamableLabel} is unavailable.");
            }
        }

        protected override void TickPreparing(int delta)
        {
            // Only the Initiator prepares (...typical). Responder waits to be told when to dial.
            if (role == LinkRole.Responder) { return; }

            base.TickPreparing(delta);
        }

        protected override void TickActive(int delta)
        {
            // The other end vanished (destroyed gate, broken state). Break the link.
            if (MutualOther() == null)
            {
                FailOperation($"Link severed: far gate lost.");
                return;
            }

            // We ran out of power on this end, kill the link (other end also checks the same).
            if (!gate.Capacitor.Powered && gate.Capacitor.Charge <= 0f)
            {
                FailOperation($"Link collapsed: skip buffer depleted.");
            }
        }

        protected override void Execute()
        {
            SkipgateOperation_Link responder = MutualOther();

            // Re-validate at completion: still spawned, still powered, still holding OUR
            // responder op. Anything else = fizzle, no heat (OnFailed notifies the far end).
            if (responder == null || !OtherGate.Capacitor.Powered)
            {
                FailOperation($"Link to {otherGateBuilding?.RenamableLabel ?? "far gate"} failed.");
                return;
            }

            if (!TrySpendRequiredCharge()) { return; }

            Messages.Message($"Link established: {(gate.parent as Building_Skipgate).RenamableLabel} is now linked to {otherGateBuilding.RenamableLabel}", MessageTypeDefOf.PositiveEvent);

            EnterActive();
            responder.EnterActive();

            // Now we're thinking with portals!
            gate.SpawnPortal();
            responder.gate.SpawnPortal();
        }

        protected override void ApplyRuntimeState()
        {
            switch (phase)
            {
                case SkipgateOperationPhase.Preparing:
                case SkipgateOperationPhase.Dialing:
                    if (role == LinkRole.Initiator) { gate.Capacitor.SetTarget(requiredCharge); }
                    break;

                case SkipgateOperationPhase.Active:
                    gate.Capacitor.SetTarget(requiredCharge, gate.Props.linkRebuildWatts);
                    gate.Capacitor.SetLoad(requiredCharge / gate.Props.linkBufferSeconds);
                    break;
            }
        }

        protected override void OnCancelled()
        {
            // Take the other end down with us; EndFromRemote never notifies back.
            MutualOther()?.EndLinkFromRemote(SkipgateOperationEnd.Cancelled);
        }

        protected override void OnFailed(string reason)
        {
            if (phase == SkipgateOperationPhase.Active)
            {
                // A live link dying (collapse or sever) is letter-worthy since both ends take heat.
                Find.LetterStack.ReceiveLetter(
                    "Skipgate link lost",
                    $"{reason}\n\nBoth skipgates absorbed the collapsing skipfield and must disperse the heat before they can be used again.",
                    LetterDefOf.NegativeEvent,
                    new LookTargets(gate.parent, otherGateBuilding));
            }
            else
            {
                Messages.Message(reason, gate.parent, MessageTypeDefOf.NegativeEvent, historical: false);
            }

            MutualOther()?.EndLinkFromRemote(SkipgateOperationEnd.Failed);
        }

        public void EndLinkFromRemote(SkipgateOperationEnd result)
        {
            if (ending) { return; }

            ending = true;
            gate.EndOperation(this, result, heatGenerated: CancelHeat);
        }

        // The far gate's current operation, iff it's the other half of THIS link:
        // opposite role, pointing back at us.
        private SkipgateOperation_Link MutualOther()
        {
            if (otherGateBuilding == null || !otherGateBuilding.Spawned) { return null; }

            SkipgateOperation_Link other = OtherGate?.CurrentOperation as SkipgateOperation_Link;

            if (other == null || other.role == role) { return null; }
            if (other.otherGateBuilding != gate.parent) { return null; }

            return other;
        }
        public override void ResumeAfterLoad()
        {
            //if (phase == SkipgateOperationPhase.Preparing) { return; }
            SkipgateOperation_Link other = MutualOther();

            if (other == null || (phase == SkipgateOperationPhase.Active) != (other.phase == SkipgateOperationPhase.Active))
            {
                FailOperation("Link state mismatch after load.");
                return;
            }

            if (phase == SkipgateOperationPhase.Active) { gate.SpawnPortal(); }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref role, "linkRole", LinkRole.Initiator);
            Scribe_References.Look(ref otherGateBuilding, "otherGate");
        }
    }
}
