using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MigCorp.Skiptech.Skipgate.Operations
{
    // Handles sending a load (like loading and sending a drop-pod, minus pawns).
    // The Skipgate will hold the items when being prepared.
    public class SkipgateOperation_SendLoad : SkipgateOperation
    {
        private GlobalTargetInfo destination = GlobalTargetInfo.Invalid;

        public override SkipgateOperationType Type => SkipgateOperationType.SendLoad;
        public GlobalTargetInfo Destination => destination;
        public bool HasDestination => destination.HasWorldObject;

        // Dial only when fully loaded and targeted.
        protected override bool PreparationReady =>
            HasDestination && !gate.Transporter.AnythingLeftToLoad;

        public SkipgateOperation_SendLoad(CompSkipgate gate) : base(gate)
        {
        }

        // THE cost function of the Send/Recall ladder: base + mass scaled by distance.
        public static float CalculateSendCost(CompSkipgate gate, GlobalTargetInfo target, float massKg)
        {
            float tiles = Find.WorldGrid.ApproxDistanceInTiles(gate.parent.Map.Tile, target.Tile);
            return gate.Props.sendCostBase + massKg * (gate.Props.sendCostPerKg + gate.Props.sendCostPerKgPerTile * tiles);
        }

        public static bool DestinationStillValid(GlobalTargetInfo target)
        {
            if (target.WorldObject is Caravan caravan) { return !caravan.Destroyed && caravan.IsPlayerControlled; }
            if (target.WorldObject is MapParent mapParent) { return mapParent.HasMap; }

            return false;
        }

        // Everything chosen for sending: already inside the gate + still being hauled.
        public float ManifestMass()
        {
            CompTransporter_Skipgate transporter = gate.Transporter;
            if (transporter == null) { return 0f; }

            float mass = CollectionsMassCalculator.MassUsage(transporter.innerContainer, IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload, includePawnsMass: true);

            if (!transporter.leftToLoad.NullOrEmpty())
            {
                mass += CollectionsMassCalculator.MassUsageTransferables(transporter.leftToLoad, IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload, includePawnsMass: true);
            }

            return mass;
        }

        public AcceptanceReport TrySetDestination(GlobalTargetInfo target)
        {
            if (phase != SkipgateOperationPhase.Preparing) { return "Cannot retarget while dialing."; }
            if (!DestinationStillValid(target)) { return "No valid destination."; }

            float cost = CalculateSendCost(gate, target, ManifestMass());

            // We only care about charge once we have both distance (target) and mass.
            if (ChecksCapacityPolicy && !gate.Capacitor.IsWithinCapacity(cost))
            {
                return "Charge cost exceeds the capacitor's safe operating limit.";
            }

            destination = target;
            requiredCharge = cost;
            ApplyRuntimeState();

            return true;
        }

        protected override void TickPreparing(int delta)
        {
            if (HasDestination && !DestinationStillValid(destination))
            {
                destination = GlobalTargetInfo.Invalid;
                requiredCharge = 0f;
                gate.Capacitor.ClearTarget();

                Messages.Message("Send destination lost. Select a new destination.", gate.parent, MessageTypeDefOf.NegativeEvent, historical: false);
            }

            base.TickPreparing(delta);
        }

        protected override void Execute()
        {
            // Check again before be send it. Destination map may have been Thanos snapped.
            if (!DestinationStillValid(destination))
            {
                FailOperation("Send failed: destination lost.");
                return;
            }

            if (!TrySpendRequiredCharge()) { return; }

            // TODO Actually yeet the goods HERE.
            Messages.Message(
                $"{(gate.parent as Building_Skipgate).RenamableLabel} skipped a load to {destination.Label} (cost {requiredCharge:F0}).",
                gate.parent, MessageTypeDefOf.PositiveEvent);

            CompleteOperation(requiredCharge);
        }

        protected override void OnFailed(string reason)
        {
            // TODO Dump goods, or maybe hold until next operation boots it out (or re-use if current stuff is good for another sendload op)?
            Messages.Message(reason, gate.parent, MessageTypeDefOf.NegativeEvent, historical: false);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_TargetInfo.Look(ref destination, "destination");
        }
    }
}