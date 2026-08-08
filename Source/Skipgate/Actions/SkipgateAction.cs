using MigCorp.Skiptech.Skipgate.Comps;

namespace MigCorp.Skiptech.Skipgate.Actions
{
    public abstract class SkipgateAction
    {
        private CompSkipgate initiator;
        public CompSkipgate Initiator { get { return initiator; } }
        public bool Active { get; set; }

        public SkipgateAction(CompSkipgate initiator)
        {
            this.initiator = initiator;
        }

        public abstract bool TryCancelAction();
    }
}
