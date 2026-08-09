using MigCorp.Skiptech.Skipgate.Comps;

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
        public override SkipgateOperationType Type => SkipgateOperationType.Recall;
        public SkipgateOperation_Recall(CompSkipgate gate) : base(gate)
        {
        }

        public SkipgateOperation_Recall(CompSkipgate gate, SkipgateRecallMode mode) : this(gate)
        {
            this.mode = mode;
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
