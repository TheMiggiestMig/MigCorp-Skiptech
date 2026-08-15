using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;
using Verse.AI.Group;

namespace MigCorp.Skiptech.Skipgate.Operations
{
    // Sends a formed skipgate caravan to a world destination.
    public class SkipgateOperation_Send : SkipgateOperation
    {
        private const int ValidityCheckInterval = 60; // mainly to check selected destination (did someone blow up a map tile? etc.)

        private Lord lord;

        public override SkipgateOperationType Type => SkipgateOperationType.Send;

        private GlobalTargetInfo destination = GlobalTargetInfo.Invalid;
        private TransportersArrivalAction arrivalAction; // The cell (when there is one) lives inside the arrival action itself (TransportersArrivalAction_LandInSpecificCell), so don't track it separately.
        private int validityTicks;

        // LordManager.RemoveLord only does lords.Remove(lord) + lord.Cleanup().
        // It never nulls lord.lordManager, so a cancelled lord is still a perfectly live object holding a valid map reference.
        // Membership of the manager's list is the only reliable liveness test (it's what Lord itself uses internally).
        private bool LordAlive => lord != null
            && lord.lordManager != null
            && lord.lordManager.lords.Contains(lord);

        // Null whenever the caravan is gone.
        public LordJob_FormSkipgateCaravan FormingCaravan => LordAlive ? lord.LordJob as LordJob_FormSkipgateCaravan : null;

        public bool HasDestination => destination.IsValid;
        public GlobalTargetInfo Destination => destination;
        public TransportersArrivalAction ArrivalAction => arrivalAction;
        protected override bool PreparationReady
        {
            get
            {
                LordJob_FormSkipgateCaravan caravan = FormingCaravan;

                return caravan != null && caravan.AllAssembled && HasDestination;
            }
        }
        private string GateLabel => (gate.parent as Building_Skipgate)?.RenamableLabel ?? gate.parent.LabelCap;
        public string DestinationLabel
        {
            get
            {
                if (!HasDestination) { return "none"; }

                WorldObject worldObject = Find.WorldObjects.WorldObjectAt<WorldObject>(destination.Tile);

                return worldObject != null ? worldObject.LabelCap : $"tile {destination.Tile}";
            }
        }

        // Required by Scribe_Deep for loading.
        public SkipgateOperation_Send(CompSkipgate gate) : base(gate)
        {
        }

        public SkipgateOperation_Send(CompSkipgate gate, Lord lord) : this(gate)
        {
            this.lord = lord;

            // TrySetDestination sets the real cost.
            requiredCharge = 0f;
        }

        // COST STUFF

        // List all the pawns in the caravan, downed or otherwise.
        // Everything else is held by those pawns, so that should cover all-the-things.
        public List<Thing> RosterThings()
        {
            List<Thing> things = new List<Thing>();

            if (!LordAlive) { return things; }

            things.AddRange(lord.ownedPawns);

            LordJob_FormSkipgateCaravan caravan = FormingCaravan;
            if (caravan != null) { things.AddRange(caravan.downedPawns); }

            return things;
        }

        // Vanilla's arrival actions and per-WorldObject float menus all want "pods".
        // Mum: We have "pods" at home.
        // (The "pods" at home):
        public IEnumerable<IThingHolder> RosterHolders()
        {
            return new List<IThingHolder> { new SkipgateRosterHolder(RosterThings()) };
        }

        // The current mass of our "pod" (including the pawns themselves).
        private float CarriedMass()
        {
            return CollectionsMassCalculator.MassUsage(RosterThings(), IgnorePawnsInventoryMode.DontIgnore, includePawnsMass: true);
        }

        // Carried mass plus whatever is still to be packed..
        private float PlannedMass()
        {
            float mass = CarriedMass();

            LordJob_FormSkipgateCaravan caravan = FormingCaravan;
            if (caravan != null)
            {
                mass += CollectionsMassCalculator.MassUsageTransferables(caravan.transferables, IgnorePawnsInventoryMode.DontIgnore, includePawnsMass: true);
            }

            return mass;
        }

        // TODO Make this a util method? Recall will probably something 90% similar too.
        public static float CalculateSendCost(CompSkipgate gate, float mass, float tiles)
        {
            CompProperties_Skipgate props = gate.Props;

            return props.sendCostBase + mass * (props.sendCostPerKg + props.sendCostPerKgPerTile * tiles);
        }

        // TODO Make this a util method? Recall will probably something 90% similar too.
        private float TilesTo(GlobalTargetInfo target)
        {
            if (!target.IsValid || gate.parent.Map == null) { return 0f; }

            return Find.WorldGrid.ApproxDistanceInTiles(gate.parent.Map.Tile, target.Tile);
        }

        // The estimate shown on the targeter before anything is committed.
        public float EstimateCostTo(GlobalTargetInfo target)
        {
            return CalculateSendCost(gate, PlannedMass(), TilesTo(target));
        }

        // Actually calculate and apply the cost changes back to the capacitor.
        private void Recost(float mass)
        {
            requiredCharge = CalculateSendCost(gate, mass, TilesTo(destination));

            ApplyRuntimeState();
        }

        // DESTINATION STUFF
        public AcceptanceReport TrySetDestination(GlobalTargetInfo target, TransportersArrivalAction action)
        {
            // No changing of minds once the dialing starts.
            if (ending || phase != SkipgateOperationPhase.Preparing) { return "The skipgate has already started dialing."; }

            if (!target.IsValid) { return "Invalid destination."; }

            float cost = CalculateSendCost(gate, PlannedMass(), TilesTo(target));

            // Relevant prior to max research.
            if (!gate.Capacitor.IsWithinCapacity(cost)) { return "Charge cost exceeds the capacitor's safe operating limit."; }

            destination = target;
            arrivalAction = action;
            requiredCharge = cost;
            validityTicks = 0;

            ApplyRuntimeState();

            return true;
        }

        private void ClearDestination()
        {
            destination = GlobalTargetInfo.Invalid;
            arrivalAction = null;
            requiredCharge = 0f;
            validityTicks = 0;

            gate.Capacitor.ClearTarget();
        }

        private bool DestinationStillValid()
        {
            if (!destination.IsValid) { return false; }
            if (arrivalAction == null) { return true; }

            return arrivalAction.StillValid(RosterHolders(), destination.Tile).Accepted;
        }

        private void TickDestinationValidity(int delta)
        {
            if (!HasDestination) { return; }

            validityTicks += delta;
            if (validityTicks < ValidityCheckInterval) { return; }

            validityTicks = 0;

            Recost(PlannedMass()); // Might as well do the check here. No need to maintain 2 separate tick timers.
            if (DestinationStillValid()) { return; }

            Messages.Message(
                $"{GateLabel} lost its destination. Pick a new one before it can dial.",
                gate.parent,
                MessageTypeDefOf.NegativeEvent,
                historical: false);

            ClearDestination();
        }

        protected override void TickPreparing(int delta)
        {
            if (CheckCaravanLost()) { return; }

            TickDestinationValidity(delta);

            // One-shot recost, immediately before the base decides to start dialing.
            if (ReadyToDial) { Recost(CarriedMass()); }

            base.TickPreparing(delta);
        }

        protected override void TickDialing(int delta)
        {
            if (CheckCaravanLost()) { return; }

            base.TickDialing(delta);
        }

        // Make sure we kill the op if the caravan lord no longer exists.
        private bool CheckCaravanLost()
        {
            if (LordAlive) { return false; }

            Messages.Message(
                $"{GateLabel} lost its caravan. The send was cancelled.",
                gate.parent,
                MessageTypeDefOf.NegativeEvent,
                historical: false);

            TryCancel();

            return true;
        }

        protected override void Execute()
        {
            if (!TrySpendRequiredCharge()) { return; }

            // TODO Actual teleport.
            Messages.Message(
                $"{GateLabel} completed a send to {DestinationLabel}. (Testing - nothing was teleported.)",
                gate.parent,
                MessageTypeDefOf.NeutralEvent,
                historical: false);

            CompleteOperation(requiredCharge);
        }

        protected override void OnCompleted()
        {
            // TODO move caravan to world (if it's not going to an actual map cell).
            DisbandCaravan();
        }

        protected override void OnCancelled()
        {
            DisbandCaravan();
        }

        protected override void OnFailed(string reason)
        {
            Messages.Message(reason, gate.parent, MessageTypeDefOf.NegativeEvent, historical: false);

            DisbandCaravan();
        }

        // Make sure we kill the caravan lord if the op stops / is killed.
        private void DisbandCaravan()
        {
            if (LordAlive) { CaravanFormingUtility.StopFormingCaravan(lord); }

            lord = null;
        }

        public override void ResumeAfterLoad()
        {
            base.ResumeAfterLoad();

            //CheckCaravanLost();
            if (CheckCaravanLost()) { return; }

            // A destination can go stale while the save sits on disk.
            if (HasDestination && !DestinationStillValid())
            {
                Messages.Message(
                    $"{GateLabel} lost its destination. Pick a new one before it can dial.",
                    gate.parent,
                    MessageTypeDefOf.NegativeEvent,
                    historical: false);

                ClearDestination();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_References.Look(ref lord, "lord");
            Scribe_TargetInfo.Look(ref destination, "destination", GlobalTargetInfo.Invalid);
            Scribe_Deep.Look(ref arrivalAction, "arrivalAction");
            Scribe_Values.Look(ref validityTicks, "validityTicks", 0);
        }
    }
}