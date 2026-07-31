using MigCorp.Skiptech.SkipNet.Comps;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public abstract class SkipNetSearcher
    {
        public readonly SkipNetPlanner planner;
        public readonly MapComponent_SkipNet skipNet;

        public SkipNetSearcher(SkipNetPlanner planner)
        {
            this.planner = planner;
            this.skipNet = planner.skipNet;
        }
        public abstract void Reset();

        public abstract bool TrySearchForSkipdoorPair(Pawn pawn, Region pawnRegion, Region destRegion, PawnPath directPath, TraverseParms tp, SkipNetAccessContext ac, out CompSkipdoor entry, out CompSkipdoor exit, out int popCost);

    }
}
