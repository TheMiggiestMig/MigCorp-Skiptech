using MigCorp.Skiptech.Skipgate.Comps;

namespace MigCorp.Skiptech.Skipgate.Operations
{
    public class SkipgateOperation_Link : SkipgateOperation
    {
        // Handles linking and opening a portal (Building_SkipgatePortal) to another Skipgate.
        // One skipgate execute this operation and notifies the other to get ready (reserves it, but link operation can be cancelled from either end).
        // Only the executing skipgate pays the initial charge cost. Once charged, it notifies the other end to dial at the same time as this one dials.
        // Once executed, the operation stays active until either portal is taken down (either manually, or sustained loss of power).
        public override SkipgateOperationType Type => SkipgateOperationType.Link;

        public SkipgateOperation_Link(CompSkipgate initiator) : base(initiator)
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
