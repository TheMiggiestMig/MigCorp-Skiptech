using Verse;

namespace MigCorp.Skiptech.SkipNet.Comps
{
    public abstract class CompProperties_Skipdoor_RestrictedBase : CompProperties
    {
        public CompProperties_Skipdoor_RestrictedBase() => compClass = typeof(CompSkipdoor_RestrictedBase);
    }
    public abstract class CompSkipdoor_RestrictedBase : ThingComp, ISkipdoorAccessible
    {
        public abstract bool CanEnter(Pawn pawn);
        public abstract bool CanExit(Pawn pawn);
        public abstract void Notify_PawnArrived(Pawn pawn, SkipNetPlan skipNetPlan, SkipdoorType type = SkipdoorType.Entry);
        public abstract void Notify_PawnTeleported(Pawn pawn, SkipNetPlan skipNetPlan, SkipdoorType type = SkipdoorType.Entry);
        public abstract int TicksUntilEnterable(Pawn pawn);
        public abstract int TicksUntilExitable(Pawn pawn);
    }
}
