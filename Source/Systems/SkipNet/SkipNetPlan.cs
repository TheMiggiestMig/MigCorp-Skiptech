using MigCorp.Skiptech.Comps;
using MigCorp.Skiptech.Utils;
using Verse;
using Verse.AI;
using UnityEngine;
using RimWorld;
using System;

namespace MigCorp.Skiptech.Systems.SkipNet
{
    public enum SkipNetPlanState
    {
        None,
        ExecutingEntry,
        ExecutingExit,
        Disposed
    }
    
    public class SkipNetPlan
    {
        public Pawn pawn;
        public MapComponent_SkipNet skipNet;
        public CompSkipdoor entry, exit;
        public int tickCreated;

        public LocalTargetInfo originalDest;
        public IntVec3 originalDestPostition;
        public PathEndMode originalPeMode;

        private SkipNetPlanState state = SkipNetPlanState.None;
        public SkipNetPlanState State { get { return state; } set { state = value; } }
        private bool arrived = false;
        private int nextResolveTick;

        public bool IsInvalid { get { return state == SkipNetPlanState.None; } }
        public bool IsDisposed { get { return state == SkipNetPlanState.Disposed; } }
        public bool IsDisposedOrInvalid {  get { return IsDisposed || IsInvalid; } }
        public bool Arrived { get { return arrived; } }

        public SkipNetPlan(MapComponent_SkipNet skipNet, Pawn pawn, LocalTargetInfo dest, PathEndMode peMode)
        {
            this.pawn = pawn;
            this.skipNet = skipNet;
            originalDest = dest;
            originalDestPostition = dest.Cell != default ? dest.Cell : (IntVec3)dest;
            originalPeMode = peMode;
            tickCreated = GenTicks.TicksGame;
            skipNet.RegisterPlan(pawn, this);
        }

        public void Initialize(CompSkipdoor entry, CompSkipdoor exit)
        {
            
            this.entry = entry;
            this.exit = exit;
            State = SkipNetPlanState.ExecutingEntry;
        }
        public void Resolve()
        {
            // If we haven't arrived at the entry skipdoor yet, do nothing.
            if (!arrived) { return; }

            // If we are still waiting for something, do nothing.
            if (nextResolveTick > GenTicks.TicksGame) { return; }

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
                return;
            }

            // Last check for accessibility.
            if (!IsStillAccessible() || !IsStillPathableFromEntryToExit(map, tp))
            {
                Notify_SkipNetPlanFailedOrCancelled();
                return;
            }

            // We're green to go.
            State = SkipNetPlanState.ExecutingExit;
            SkipNetUtils.TeleportPawn(pawn, exit.Position);
            Notify_SkipNetPlanExitReached();
            pawn.pather.StartPath(originalDest, originalPeMode);
        }

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
            Dispose();
        }

        public void Notify_SkipNetPlanFailedOrCancelled()
        {
            try
            {
                if (pawn?.Map == null || !pawn.Spawned || pawn.pather == null)
                {
                    MigcorpSkiptechMod.Error($"Not sure how we got here, but skipnet plan failed because pawn't.");
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
                MigcorpSkiptechMod.Error($"Hmm... I missed something in the Notify_SkipNetPlanFailedOrCancelled checks:\n{ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            State = SkipNetPlanState.Disposed;
            skipNet.disposedPawnSkipNetPlans.AddDistinct(pawn);
        }

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
            return entry.IsEnterableBy(pawn) && exit.IsExitableBy(pawn);
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
