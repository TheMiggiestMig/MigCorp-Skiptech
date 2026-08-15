using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace MigCorp.Skiptech.Skipgate.Operations
{
    // Sends a formed skipgate caravan to a world destination.
    public class SkipgateOperation_Send : SkipgateOperation
    {
        private Lord lord;

        public override SkipgateOperationType Type => SkipgateOperationType.Send;

        // The capacity check happens at TrySetDestination, which is the first point at which a meaningful cost exists.
        // Nothing here but us chickens.
        protected override bool ChecksCapacityPolicy => false;

        // LordManager.RemoveLord only does lords.Remove(lord) + lord.Cleanup().
        // It never nulls lord.lordManager, so a cancelled lord is still a perfectly live object holding a valid map reference.
        // Membership of the manager's list is the only reliable liveness test (it's what Lord itself uses internally).
        private bool LordAlive => lord != null
            && lord.lordManager != null
            && lord.lordManager.lords.Contains(lord);

        // Null whenever the caravan is gone.
        public LordJob_FormSkipgateCaravan FormingCaravan =>
            LordAlive ? lord.LordJob as LordJob_FormSkipgateCaravan : null;

        protected override bool PreparationReady
        {
            get
            {
                LordJob_FormSkipgateCaravan caravan = FormingCaravan;

                return caravan != null && caravan.AllAssembled;
            }
        }

        private string GateLabel => (gate.parent as Building_Skipgate)?.RenamableLabel ?? gate.parent.LabelCap;

        // Required by Scribe_Deep for loading.
        public SkipgateOperation_Send(CompSkipgate gate) : base(gate)
        {
        }

        public SkipgateOperation_Send(CompSkipgate gate, Lord lord) : this(gate)
        {
            this.lord = lord;

            // TODO Calculate the actual charge requirement once we pick a destination.
            requiredCharge = gate.Props.sendCostBase;
        }

        protected override void TickPreparing(int delta)
        {
            if (CheckCaravanLost()) { return; }

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
                $"{GateLabel} completed a send. (C1 stub — nothing was teleported.)",
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

            CheckCaravanLost();
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_References.Look(ref lord, "lord");
        }
    }
}