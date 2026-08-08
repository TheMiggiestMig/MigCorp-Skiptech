using MigCorp.Skiptech.Skipgate.Comps;

namespace MigCorp.Skiptech.Skipgate.Actions
{
    public class SkipgateAction_Link : SkipgateAction
    {
        // If another skipgate is attempting to link to this one, this action will become active (the skipgate will no longer be Idle) since we want it to dial at the same time.
        // Let the other skipgate know that even though it's not idle, it's still good for a link.
        public bool LinkedByOther { get; private set; }
        public SkipgateAction_Link(CompSkipgate initiator) : base(initiator)
        {

        }

        public override bool TryCancelAction()
        {
            return true;
        }
    }
}
