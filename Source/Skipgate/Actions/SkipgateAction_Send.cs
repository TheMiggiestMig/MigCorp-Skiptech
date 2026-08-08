using MigCorp.Skiptech.Skipgate.Comps;

namespace MigCorp.Skiptech.Skipgate.Actions
{
    public class SkipgateAction_Send : SkipgateAction
    {
        public SkipgateAction_Send(CompSkipgate initiator) : base(initiator)
        {

        }
        public override bool TryCancelAction()
        {
            return true;
        }
    }
}
