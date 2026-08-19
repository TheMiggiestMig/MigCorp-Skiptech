using MigCorp.Skiptech.Skipgate.Comps;
using MigCorp.Skiptech.Utils;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Text;
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
        private bool sendPressed;

        // LordManager.RemoveLord only does lords.Remove(lord) + lord.Cleanup().
        // It never nulls lord.lordManager, so a cancelled lord is still a perfectly live object holding a valid map reference.
        // Membership of the manager's list is the only reliable liveness test (it's what Lord itself uses internally).
        private bool LordAlive => lord != null
            && lord.lordManager != null
            && lord.lordManager.lords.Contains(lord);

        // Null whenever the caravan is gone.
        public LordJob_FormSkipgateCaravan FormingCaravan => LordAlive ? lord.LordJob as LordJob_FormSkipgateCaravan : null;
        public bool SendPressed => sendPressed;
        public bool SendOrdered => gate.AutoSend || sendPressed;

        // Everything is charged and gathered, we're just waiting on the player to say go.
        public bool AwaitingSendOrder => ReadyToDial && !SendOrdered;
        public void ToggleSendOrder()
        {
            sendPressed = !sendPressed;
            gate.SetAutoSend(false);
        }
        public void ToggleAutoSend()
        {
            sendPressed = false;
            gate.SetAutoSend(!gate.AutoSend);
        }
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
        private string GateLabel => gate.GateLabel;
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
            if (caravan != null)
            {
                // Make sure we don't double up on downedPawns.
                // Warnings happen in ownership transfer otherwise :/
                for (int i = 0; i < caravan.downedPawns.Count; i++)
                {
                    Pawn downed = caravan.downedPawns[i];
                    if (!things.Contains(downed)) { things.Add(downed); }
                }
            }

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

        // The estimate shown on the targeter before anything is committed.
        public float EstimateCostTo(GlobalTargetInfo target)
        {
            return SkipgateCostUtil.CalculateSendCost(gate, PlannedMass(), SkipgateCostUtil.TilesBetween(gate, target));
        }

        // Actually calculate and apply the cost changes back to the capacitor.
        private void Recost(float mass)
        {
            requiredCharge = SkipgateCostUtil.CalculateSendCost(gate, mass, SkipgateCostUtil.TilesBetween(gate, destination));
            ApplyRuntimeState();
        }

        // DESTINATION STUFF
        public AcceptanceReport TrySetDestination(GlobalTargetInfo target, TransportersArrivalAction action)
        {
            // No changing of minds once the dialing starts.
            if (ending || phase != SkipgateOperationPhase.Preparing) { return "The skipgate has already started dialing."; }

            if (!target.IsValid) { return "Invalid destination."; }

            float cost = SkipgateCostUtil.CalculateSendCost(gate, PlannedMass(), SkipgateCostUtil.TilesBetween(gate, target));

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

            MessageDestinationLost();
            ClearDestination();
        }
        private void MessageDestinationLost()
        {
            Messages.Message(
                $"{GateLabel} lost its destination. Pick a new one before it can dial.",
                gate.parent,
                MessageTypeDefOf.NegativeEvent,
                historical: false);
        }
        private void ReArmAfterLostDestination()
        {
            phase = SkipgateOperationPhase.Preparing;
            dialingTicksLeft = 0;

            MessageDestinationLost();
            ClearDestination();
        }

        protected override void TickPreparing(int delta)
        {
            if (CheckCaravanLost()) { return; }

            TickDestinationValidity(delta);

            if (!ReadyToDial || !SendOrdered) { return; }

            // Final recost from what is ACTUALLY standing at the gate, rather than what the
            // caravan intended to bring. Drops anything it never managed to gather.
            Recost(CarriedMass());

            // Just in case we're no longer ready to dial after the Recost.
            if (!ReadyToDial) { return; }

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
            if (CheckCaravanLost()) { return; }

            if (!DestinationStillValid())
            {
                ReArmAfterLostDestination();
                return;
            }

            List<Thing> roster = RosterThings();
            if (roster.Count == 0)
            {
                FailOperation($"{GateLabel} had nothing left to send.");
                return;
            }

            if (!ChargeReady)
            {
                FailOperation("Insufficient charge at execution.");
                return;
            }

            // Snapshot these before the lord goes, in case something goes wrong and we need to "Return To Sender".
            Map map = gate.parent.Map;
            IntVec3 gateCell = gate.parent.Position;
            TransportersArrivalAction action = arrivalAction;
            PlanetTile tile = destination.Tile;

            ActiveTransporterInfo info = new ActiveTransporterInfo();
            info.openDelay = 0;
            info.sentTransporterDef = SkiptechDefOf.MigCorp_SkipgateSent;

            for (int i = 0; i < roster.Count; i++)
            {
                Thing thing = roster[i];

                if (thing.Spawned) { thing.DeSpawnOrDeselect(); }

                // TryAddOrTransfer rather than TryAdd, for the same reason: a carried pawn already has a
                // holdingOwner and TryAdd flatly refuses (and warns about) anything that does.
                if (info.innerContainer.TryAddOrTransfer(thing)) { continue; }

                // Still fully recoverable. Put it all back on the floor and charge nothing.
                DropAllAtGate(info, map, gateCell);
                FailOperation($"{GateLabel} could not take {thing.LabelShortCap} through. The send was aborted.");
                return;
            }

            ReleaseCaravan();

            if (!TrySpendRequiredCharge())
            {
                // Give up.
                DropAllAtGate(info, map, gateCell);
                return;
            }

            List<ActiveTransporterInfo> transporters = new List<ActiveTransporterInfo> { info };

            // Generating a destination map is slow enough that vanilla always does it inside a long event
            // (TravellingTransporters.Arrived). Doing it inline from a CompTick is no bueno.
            if (action.ShouldUseLongEvent(transporters, tile))
            {
                LongEventHandler.QueueLongEvent(
                    () => DoArrival(action, transporters, tile, map, gateCell),
                    "GeneratingMapForNewEncounter",
                    false,
                    null);
            }
            else
            {
                DoArrival(action, transporters, tile, map, gateCell);
            }

            CompleteOperation(requiredCharge);
        }

        // Functions like TravellingTransporters.DoArrivalAction, which also swallows and logs arrival exceptions.
        private void DoArrival(TransportersArrivalAction action, List<ActiveTransporterInfo> transporters, PlanetTile tile, Map originMap, IntVec3 originCell)
        {
            try
            {
                for (int i = 0; i < transporters.Count; i++) { PassPawnsToWorld(transporters[i]); }

                action.Arrived(transporters, tile);
            }
            catch (Exception ex)
            {
                SkiptechUtil.Error($"Skipgate arrival action failed: {ex}");

                for (int i = 0; i < transporters.Count; i++)
                {
                    DropAllAtGate(transporters[i], originMap, originCell);
                }

                Find.LetterStack.ReceiveLetter(
                    "Skip failed",
                    $"{GateLabel} could not complete the skip. Everything it was carrying has been dropped at the gate.",
                    LetterDefOf.NegativeEvent,
                    new TargetInfo(originCell, originMap));
            }
        }

        // Vanilla does this in TravellingTransporters.AddTransporter, we need to do it by hand.
        // Otherwise the caravan nukes the pawn.
        private static void PassPawnsToWorld(ActiveTransporterInfo info)
        {
            for (int i = info.innerContainer.Count - 1; i >= 0; i--)
            {
                if (info.innerContainer[i] is Pawn pawn && !pawn.IsWorldPawn())
                {
                    pawn.ExitMap(allowedToJoinOrCreateCaravan: false, Rot4.Invalid);
                }
            }
        }

        // Last-ditch recovery. Drop everything in dramatic fashion.
        private static void DropAllAtGate(ActiveTransporterInfo info, Map map, IntVec3 cell)
        {
            if (map == null) { return; }

            info.innerContainer.TryDropAll(cell, map, ThingPlaceMode.Near);
        }

        protected override void OnCompleted() => ReleaseCaravan();

        protected override void OnCancelled() => AbortCaravan();

        protected override void OnFailed(string reason)
        {
            Messages.Message(reason, gate.parent, MessageTypeDefOf.NegativeEvent, historical: false);

            AbortCaravan();
        }
        private void ReleaseCaravan()
        {
            if (LordAlive) { lord.lordManager.RemoveLord(lord); }
            lord = null;
        }

        private void AbortCaravan()
        {
            if (LordAlive) { CaravanFormingUtility.StopFormingCaravan(lord); }
            lord = null;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            if (phase != SkipgateOperationPhase.Preparing) { yield break; }

            yield return Gizmo_SetSendDestination();
            yield return new Command_SkipgateSendOrder(gate, this);
        }

        private Gizmo Gizmo_SetSendDestination()
        {
            return new Command_Action
            {
                defaultLabel = HasDestination ? "Change destination" : "Set destination",
                defaultDesc = HasDestination
                    ? $"Currently sending to {DestinationLabel}. Picking a new destination recalculates the charge cost."
                    : "Pick where this skipgate sends its caravan.\n\nThe gate won't start charging until a destination is set.",
                icon = CompLaunchable.LaunchCommandTex,
                action = delegate { SkipgateTargetingUtil.BeginSendTargeting(gate, this); }
            };
        }

        public override void AppendInspectLines(StringBuilder sb)
        {
            base.AppendInspectLines(sb);

            sb.AppendLine(HasDestination
                ? $"Destination: {DestinationLabel} (cost {RequiredCharge:F0})"
                : "Destination: none set");

            if (AwaitingSendOrder) { sb.AppendLine("Ready — awaiting send order."); }

            if (DebugSettings.ShowDevGizmos && FormingCaravan != null)
            {
                LordJob_FormSkipgateCaravan caravan = FormingCaravan;
                sb.AppendLine($"DEV caravan: {caravan.Status} — holding: {caravan.Holding}, assembled: {caravan.AllAssembled}");
            }
        }

        public override void ResumeAfterLoad()
        {
            base.ResumeAfterLoad();

            if (CheckCaravanLost()) { return; }

            // A destination can go stale while the save sits on disk.
            if (HasDestination && !DestinationStillValid())
            {
                MessageDestinationLost();
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
            Scribe_Values.Look(ref sendPressed, "sendPressed", false);
        }
    }
}