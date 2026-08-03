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
        public IntVec3 originalDestPostition;
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
            originalDestPostition = dest.Cell != default ? dest.Cell : (IntVec3)dest;
            originalPeMode = peMode;
            tickCreated = GenTicks.TicksGame;
            this.tickLastRegionSkipdoorRebuild = tickLastRegionSkipdoorRebuild; // To throttle the reachability checks.
        }

        public void Initialize(CompSkipdoor entry, CompSkipdoor exit)
        {

            this.entry = entry;
            this.exit = exit;
            // State = SkipNetPlanState.ExecutingEntry; // SkipNetPathSplicer informs the state now
            skipNet.planner.RegisterPlan(pawn, this);
        }

        /*
        // Handled by the TryEnterNextPathCell patchs.
        public bool Resolve()
        {
            // If we haven't arrived at the entry skipdoor yet, do nothing.
            if (!arrived) { return false; }

            // If we are still waiting for something, do nothing.
            if (nextResolveTick > GenTicks.TicksGame) { return false; }

            Map map = pawn.Map;
            TraverseParms tp = TraverseParms.For(pawn, mode: TraverseMode.ByPawn);

            // We are allowed to enter at this point, but the skipdoors may not be ready yet.
            // Check if we're waiting on anything.
            entry.IsEnterableNowBy(pawn, out int entryWaitTicks);
            exit.IsExitableNowBy(pawn, out int exitWaitTicks);
            int waitTicks = Mathf.Max(entryWaitTicks, exitWaitTicks);

            if (waitTicks > 0)
            {
                nextResolveTick = GenTicks.TicksGame + waitTicks;
                pawn.stances.SetStance(new Stance_Cooldown(waitTicks, pawn, null));
                return false;
            }

            // Last check for accessibility.
            if (!IsStillAccessible() || !IsStillPathableFromEntryToExit(map, tp))
            {
                Dispose(SkipNetPlanDisposeState.Cancelled);
                return false;
            }

            // We're green to go.
            State = SkipNetPlanState.ExecutingExit;
            SkipNetUtils.TeleportPawn(pawn, exit.Position);
            Notify_SkipNetPlanExitReached();
            pawn.pather.StartPath(originalDest, originalPeMode);

            return true;
        }
        */

        /*
        // Handled by the TryEnterNextPathCell patchs.
        public void ResetPawnMoveState()
        {
            pawn.pather.StopDead();
            pawn.stances.CancelBusyStanceSoft();
        }
        public void Notify_SkipNetPlanEntryReached()
        {
            arrived = true;

            // Let the entry and exit skipdoors know we're ready to use the SkipNet.
            // This lets them start events and change states if necessary.
            entry.Notify_PawnArrived(pawn, this, SkipdoorType.Entry);
            exit.Notify_PawnArrived(pawn, this, SkipdoorType.Exit);
            Resolve();
        }
        public void Notify_SkipNetPlanExitReached()
        {
            ResetPawnMoveState();
            entry.Notify_PawnTeleported(pawn, this, SkipdoorType.Entry);
            exit.Notify_PawnTeleported(pawn, this, SkipdoorType.Exit);
            Dispose(SkipNetPlanDisposeState.Completed);
        }

        /*
        public void Notify_SkipNetPlanFailedOrCancelled()
        {
            try
            {
                if (pawn?.Map == null || !pawn.Spawned || pawn.pather == null)
                {
                    SkiptechUtil.Error($"Not sure how we got here, but skipnet plan failed because pawn't.");
                }
                else
                {
                    ResetPawnMoveState();
                    State = SkipNetPlanState.Disposed;

                    // Check if all the conditions needed to path are in place.
                    if (originalDest.IsValid && originalPeMode != PathEndMode.None)
                    {
                        if (!originalDest.ThingDestroyed)
                        {
                            pawn.pather.StartPath(originalDest, originalPeMode);
                        }
                    }
                }
            }
            catch (NullReferenceException ex)
            {
                SkiptechUtil.Error($"Hmm... I missed something in the Notify_SkipNetPlanFailedOrCancelled checks:\n{ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                Dispose();
            }
        }
        */
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

        /// <summary>
        /// Checks if the entry and exit portals are still useable.
        /// </summary>
        /// <remarks>
        /// This only cares about <c>IsEnterableBy</c> and <c>IsExitableBy</c>.
        /// Reachability is handled by vanilla pathing.
        /// </remarks>
        /// <returns></returns>
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
