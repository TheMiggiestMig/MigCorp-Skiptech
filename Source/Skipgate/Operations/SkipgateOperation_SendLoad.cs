using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using Verse;

namespace MigCorp.Skiptech.Skipgate.Operations
{
    // Handles sending a load (like loading and sending a drop-pod, minus pawns).
    // The Skipgate will hold the items when being prepared.
    public class SkipgateOperation_SendLoad : SkipgateOperation
    {
        public override SkipgateOperationType Type => SkipgateOperationType.SendLoad;

        public SkipgateOperation_SendLoad(CompSkipgate gate) : base(gate)
        {
        }

        public SkipgateOperation_SendLoad(CompSkipgate gate, string cya) : this(gate)
        {
            Messages.Message(cya, MessageTypeDefOf.NeutralEvent);
        }

        public override void Start()
        {
            base.Start();

            // TODO Calculate how much charge is needed based on the load.
            gate.Capacitor.TrySetTarget(66f); // testing
        }

        protected override void Execute()
        {
            Messages.Message($"{gate} successfully performed {Type}.", MessageTypeDefOf.NeutralEvent);
            CompleteOperation(66f);
        }
    }
}