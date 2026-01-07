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
        Invalid,
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
        public bool arrived = false;
        private int nextResolveTick;
        public SkipNetPlan(MapComponent_SkipNet skipNet, Pawn pawn, CompSkipdoor entry, CompSkipdoor exit, LocalTargetInfo dest, PathEndMode peMode)
        {
            originalDest = dest;
            originalDestPostition = dest.Cell != default ? dest.Cell : (IntVec3)dest;
            originalPeMode = peMode;
            this.entry = entry;
            this.exit = exit;
            this.pawn = pawn;
            this.skipNet = skipNet;
            State = SkipNetPlanState.ExecutingEntry;
            tickCreated = GenTicks.TicksGame;
            skipNet.RegisterPlan(pawn, this);
        }

        // Register a dummy SkipNetPlan.
        // This should be cleaned up by the SkipNet next tick.
        public SkipNetPlan(MapComponent_SkipNet skipNet, Pawn pawn)
        {
            this.pawn = pawn;
            this.skipNet = skipNet;
            State = SkipNetPlanState.Invalid;
            skipNet.RegisterPlan(pawn, this);
        }
        public void Resolve()
        {
            // If we haven't arrived at the entry skipdoor yet, do nothing.
            if (!arrived) { return; }

            // If we are still waiting for something, do nothing.
            if (nextResolveTick > GenTicks.TicksGame) { return; }

            Map map = pawn.Map;
            TraverseParms tp = TraverseParms.For(pawn, mode: TraverseMode.ByPawn);

            // If we can't access the required skipdoors, or their path's would be invalid, bail.
            if (!CheckIsStillAccessible() || !CheckIsStillPathable(map, tp))
            {
                MigcorpSkiptechMod.Message($"{pawn.Label} cannot reach exit or destination from exit. Cancelling plan.",
                    MigcorpSkiptechMod.LogLevel.Verbose);
                Notify_SkipNetPlanFailedOrCancelled();
                return;
            }

            // We are allowed to enter at this point, but the skipdoors may not be ready yet.
            // Check if we're waiting on anything.
            entry.IsEnterableNowBy(pawn, out int entryWaitTicks);
            exit.IsExitableNowBy(pawn, out int exitWaitTicks);
            int waitTicks = Mathf.Max(entryWaitTicks, exitWaitTicks);
            
            if (waitTicks > 0)
            {
                MigcorpSkiptechMod.Message($"{pawn.Label} is waiting for the skipdoors to be ready. waitTicks={waitTicks}, " +
                $"entryWaitTicks={entryWaitTicks}, " +
                $"exitWaitTicks={exitWaitTicks}", MigcorpSkiptechMod.LogLevel.Verbose);
                nextResolveTick = GenTicks.TicksGame + waitTicks;
                pawn.stances.SetStance(new Stance_Cooldown(waitTicks, pawn, null));
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
                if (pawn?.Map == null || pawn.pather == null)
                {
                    MigcorpSkiptechMod.Error($"Not sure how we got here, but skipnet plan failed because pawn't.");
                }
                else
                {
                    MigcorpSkiptechMod.Message($"{pawn.Label}'s skipnet plan failed.", MigcorpSkiptechMod.LogLevel.Verbose);
                    ResetPawnMoveState();
                    State = SkipNetPlanState.Disposed;

                    // Check if all the conditions needed to path are in place.
                    if (originalDest.IsValid && originalPeMode != PathEndMode.None)
                        pawn.pather.StartPath(originalDest, originalPeMode);
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
            MigcorpSkiptechMod.Message($"{pawn.Label}'s skipnet plan was disposed.", MigcorpSkiptechMod.LogLevel.Verbose);
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
        public bool CheckIsStillAccessible()
        {
            if (!entry.IsEnterableBy(pawn) || !exit.IsExitableBy(pawn))
            {
                return false;
            }
            return true;
        }

        public bool CheckIsStillPathable(Map map, TraverseParms tp)
        {
            if (!map.reachability.CanReach(pawn.Position, entry.Position, PathEndMode.OnCell, tp) ||
                !map.reachability.CanReach(exit.Position, originalDest, originalPeMode, tp) ||
                !map.reachability.CanReach(pawn.Position, originalDest, originalPeMode, tp))
            {
                return false;
            }
            return true;
        }
    }
}
