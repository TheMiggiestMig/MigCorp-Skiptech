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

    public enum SkipNetPlanDisposeState
    {
        None,
        Completed,
        Superseded,
        Cancelled
    }

    public class SkipNetPlan
    {
        public Pawn pawn;
        public MapComponent_SkipNet skipNet;
        public CompSkipdoor entry, exit;
        public int tickCreated;
        public int tickLastRegionSkipdoorRebuild;


        public LocalTargetInfo originalDest;
        public PathEndMode originalPeMode;

        private SkipNetPlanState state = SkipNetPlanState.None;
        private SkipNetPlanDisposeState disposeState = SkipNetPlanDisposeState.None;
        private SkipNetPlanState planStateAtDispose = SkipNetPlanState.None;
        public SkipNetPlanState State { get { return state; } set { state = value; } }
        public SkipNetPlanDisposeState DisposeState { get { return disposeState; } }
        public SkipNetPlanState PlanStateAtDispose { get { return planStateAtDispose; } }

        private bool arrived = false;
        private int nextResolveTick;

        public bool IsInvalid { get { return state == SkipNetPlanState.None; } }
        public bool IsDisposed { get { return state == SkipNetPlanState.Disposed; } }
        public bool IsDisposedOrInvalid { get { return IsDisposed || IsInvalid; } }
        public bool Arrived { get { return arrived; } }

        public SkipNetPlan(MapComponent_SkipNet skipNet, Pawn pawn, LocalTargetInfo dest, PathEndMode peMode, int tickLastRegionSkipdoorRebuild)
        {
            this.pawn = pawn;
            this.skipNet = skipNet;
            originalDest = dest;
            originalPeMode = peMode;
            tickCreated = GenTicks.TicksGame;
            this.tickLastRegionSkipdoorRebuild = tickLastRegionSkipdoorRebuild; // To throttle the reachability checks.
        }

        public void Initialize(CompSkipdoor entry, CompSkipdoor exit)
        {

            this.entry = entry;
            this.exit = exit;
            skipNet.planner.RegisterPlan(pawn, this);
        }

        private void Dispose(SkipNetPlanDisposeState disposeState)
        {
            if (IsDisposed) { return; }

            planStateAtDispose = state;
            this.disposeState = disposeState;
            State = SkipNetPlanState.Disposed;
        }

        public void DisposeCancelled() { Dispose(SkipNetPlanDisposeState.Cancelled); }
        public void DisposeSuperseded() { Dispose(SkipNetPlanDisposeState.Superseded); }
        public void DisposeCompleted() { Dispose(SkipNetPlanDisposeState.Completed); }

        public bool IsStillAccessible()
        {
            SkipNetAccessContext ac = new SkipNetAccessContext(pawn);
            return entry.IsEnterableBy(ac) && exit.IsExitableBy(ac);
        }

        // Fast check. Regular pathing handles whether Pawn->Entry still works,
        // and the pawn already re-evaluates when it reaches the Exit for Exit->Dest.
        public bool IsStillPathableFromEntryToExit(Map map, TraverseParms tp)
        {
            return map.reachability.CanReach(entry.Position, new LocalTargetInfo(exit.parent), originalPeMode, tp);
        }

        public bool IsStillPathableFromExitToDest(Map map, TraverseParms tp)
        {
            return map.reachability.CanReach(exit.Position, originalDest, originalPeMode, tp);
        }
    }
}
