using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using Verse;

namespace MigCorp.Skiptech.Skipgate.Operations
{
    // Handles assembling a caravan (must contain at least 1 pawn), then teleporting them.
    // The pawns will hold the items when being prepared.
    public class SkipgateOperation_SendCaravan : SkipgateOperation
    {
        public override SkipgateOperationType Type => SkipgateOperationType.SendCaravan;
        public SkipgateOperation_SendCaravan(CompSkipgate gate) : base(gate)
        {
        }

        public SkipgateOperation_SendCaravan(CompSkipgate gate, string bye, float requiredCharge) : this(gate)
        {
            this.requiredCharge = requiredCharge;
            Messages.Message(bye, MessageTypeDefOf.NeutralEvent);
        }

        protected override void Execute()
        {
            if (!TrySpendRequiredCharge()) { return; }

            Messages.Message($"{gate} successfully performed {Type}.", MessageTypeDefOf.NeutralEvent);
            CompleteOperation(requiredCharge);
        }
    }
}