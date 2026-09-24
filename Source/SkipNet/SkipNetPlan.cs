using MigCorp.Skiptech.SkipNet.Comps;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public enum SkipNetPlanState
    {
        None,
        SkipPathsPending,
        Installed,
        Disposed
    }

    public class SkipNetPlan
    {
        public Pawn pawn;
        public CompSkipdoor entry, exit;

        public LocalTargetInfo originalDest;
        public PathEndMode originalPeMode;
        public TraverseParms tp;

        private SkipNetPlanState state = SkipNetPlanState.None;
        public SkipNetPlanState State { get { return state; } set { state = value; } }

        public bool IsInvalid { get { return state == SkipNetPlanState.None; } }
        public bool IsDisposed { get { return state == SkipNetPlanState.Disposed; } }
        public bool IsDisposedOrInvalid { get { return IsDisposed || IsInvalid; } }

        public SkipNetPlan(Pawn pawn, LocalTargetInfo dest, PathEndMode peMode, TraverseParms tp)
        {
            this.pawn = pawn;
            originalDest = dest;
            originalPeMode = peMode;
            this.tp = tp;
        }

        public void Initialize(CompSkipdoor entry, CompSkipdoor exit)
        {
            this.entry = entry;
            this.exit = exit;
        }

        private void Dispose()
        {
            if (IsDisposed) { return; }
            State = SkipNetPlanState.Disposed;
        }

        // Might remove these later.
        public void DisposeCancelled() { Dispose(); }
        public void DisposeSuperseded() { Dispose(); }
        public void DisposeCompleted() { Dispose(); }

        public bool IsStillAccessible()
        {
            if (!entry.parent.Spawned || !exit.parent.Spawned) { return false; }

            SkipNetAccessContext ac = new SkipNetAccessContext(pawn);
            return entry.IsEnterableBy(ac) && exit.IsExitableBy(ac);
        }

        public bool IsStillPathableFromExitToDest(Map map)
        {
            return map.reachability.CanReach(exit.Position, originalDest, originalPeMode, tp);
        }
    }
}
