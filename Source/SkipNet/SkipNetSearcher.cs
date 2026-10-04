using MigCorp.Skiptech.SkipNet.Comps;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    // I'll probably keep this in case I come up with other ways to search / experimental search options.
    public abstract class SkipNetSearcher
    {
        public readonly SkipNetCandidateFinder finder;
        public readonly MapComponent_SkipNet skipNet;

        public SkipNetSearcher(SkipNetCandidateFinder finder)
        {
            this.finder = finder;
            this.skipNet = finder.skipNet;
        }
        public abstract void Reset();

        public abstract bool TrySearchForSkipdoorPair(Pawn pawn, Region pawnRegion, Region destRegion, PawnPath directPath, TraverseParms tp, SkipNetAccessContext ac, out CompSkipdoor entry, out CompSkipdoor exit, out int popCost);

    }
}
