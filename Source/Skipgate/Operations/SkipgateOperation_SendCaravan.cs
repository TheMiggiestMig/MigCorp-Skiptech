using MigCorp.Skiptech.Skipgate.Comps;

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


        public override void Start()
        {
            base.Start();

            // TODO Calculate how much charge is needed based on the load.
            gate.Capacitor.TrySetTarget(66f); // testing
        }

        protected override void Execute()
        {
            throw new System.NotImplementedException();
        }
    }
}