using MigCorp.Skiptech.Skipgate.Comps;

namespace MigCorp.Skiptech.Skipgate.Actions
{
    public class SkipgateAction_Recall : SkipgateAction
    {
        public SkipgateAction_Recall(CompSkipgate initiator) : base(initiator)
        {

        }

        public override bool TryCancelAction()
        {
            return true;
        }
    }
}
