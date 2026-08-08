using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;

namespace MigCorp.Skiptech.Skipgate
{
    public class Building_SkipgatePortal : MapPortal
    {
        public CompSkipgate OwningSkipgate { get; private set; }

        public void SetOwningSkipgate(CompSkipgate skipgate)
        {
            OwningSkipgate = skipgate;
        }
    }
}
