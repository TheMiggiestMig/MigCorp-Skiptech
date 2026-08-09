using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using Verse;

namespace MigCorp.Skiptech.Skipgate.Operations
{
    public enum SkipgateRecallMode
    {
        Normal,
        Emergency
    }

    // Handles teleporting pawns to the Skipgate.
    // Can only teleport pawns (or caravans) that have a Skip Beacon with them.
    // Can be initialized from either the pawn/caravan or the skipgate, but the skipgate handles the whole process once targets are set.
    public class SkipgateOperation_Recall : SkipgateOperation
    {
        private SkipgateRecallMode mode;
        private float emergencyRecallActualCost;
        public override SkipgateOperationType Type => SkipgateOperationType.Recall;
        public SkipgateOperation_Recall(CompSkipgate gate) : base(gate)
        {
        }

        public SkipgateOperation_Recall(CompSkipgate gate, string welcome, float requiredCharge, SkipgateRecallMode mode = SkipgateRecallMode.Normal) : this(gate)
        {
            Messages.Message(welcome, MessageTypeDefOf.NeutralEvent);
            this.mode = mode;

            if (mode == SkipgateRecallMode.Emergency)
            {
                this.requiredCharge = 5; // Fixed valued for Emergency. Might move this to def somewhere.
                emergencyRecallActualCost = requiredCharge;
                return;
            }
            this.requiredCharge = requiredCharge;
        }

        protected override void Execute()
        {
            if (!TrySpendRequiredCharge()) { return; }

            if (mode == SkipgateRecallMode.Emergency)
            {
                Messages.Message($"{gate} successfully performed {Type}. You will be penalized for {emergencyRecallActualCost} units.", MessageTypeDefOf.CautionInput);
                CompleteOperation(emergencyRecallActualCost);
                return;
            }
            Messages.Message($"{gate} successfully performed {Type}.", MessageTypeDefOf.NeutralEvent);
            CompleteOperation(requiredCharge);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref mode, "recallMode", SkipgateRecallMode.Normal);
            Scribe_Values.Look(ref emergencyRecallActualCost, "emergencyRecallActualCost", 0f);
        }
    }
}
