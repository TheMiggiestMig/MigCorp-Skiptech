using RimWorld;
using Verse;
using HarmonyLib;
using Verse.AI;
using System;
using MigCorp.Skiptech.Systems.SkipNet;

namespace MigCorp.Skiptech.Comps
{
    [HarmonyPatch(typeof(Pawn_PathFollower))]
    static class Pawn_PathFollower_Patch
    {
        // Special accessors to dig into a given Pawn_PathFollower's private fields.
        static readonly AccessTools.FieldRef<Pawn_PathFollower, LocalTargetInfo>
        _patherDestRef = AccessTools.FieldRefAccess<Pawn_PathFollower, LocalTargetInfo>("destination");
        static readonly AccessTools.FieldRef<Pawn_PathFollower, PathEndMode>
        _patherPeModeRef = AccessTools.FieldRefAccess<Pawn_PathFollower, PathEndMode>("peMode");

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Pawn_PathFollower.StartPath))]
        static void StartPath_Prefix(
            Pawn_PathFollower __instance,
            ref LocalTargetInfo dest,
            ref PathEndMode peMode,
            Pawn ___pawn
            )
        {
            // Intercept the original StartPath request, and try to generate a SkipNetPlan if none exists for this pawn.
            // Otherwise, carry on.
            MapComponent_SkipNet skipNet = ___pawn?.Map?.GetComponent<MapComponent_SkipNet>();
            if(skipNet == null) { return; }

            // Not sure why someone would call StartPath to "Invalid" specifically, but here we are.
            if(!dest.IsValid || peMode == PathEndMode.None) { return; }

            if (skipNet.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan))
            {
                bool matchesOriginalDest =  dest == plan.originalDest && peMode == plan.originalPeMode;

                if (plan.State == SkipNetPlanState.ExecutingEntry)
                {
                    // For some reason, start path was called again to the same destination. Just keep using the current plan.
                    if (matchesOriginalDest)
                    {
                        dest = plan.entry.parent;
                        peMode = PathEndMode.OnCell;

                        MigcorpSkiptechMod.Message($"{___pawn.Label} already has a skipNet plan for this scenario. {plan.State}" +
                            $"entryPos={plan.entry.Position}, " +
                            $"exitPos={plan.exit.Position}, " +
                            $"originalDest={plan.originalDest}, " +
                            $"dest={dest}, " +
                            $"state={plan.State}",
                            MigcorpSkiptechMod.LogLevel.Verbose);
                        return;
                    }
                    else
                    {
                        plan.Dispose();
                    }
                }
                else if (plan.State == SkipNetPlanState.None)
                {
                    // We've already tried and failed this tick. Let vanilla handle it.
                    return;
                }
                else
                {
                    // This shouldn't happen. It means that StartPath was called while the plan was in an ExecutingExit state.
                    // ExecutingExit disposes the plan immediately once it hits that state, so it should never live long enough to live to this state.
                    MigcorpSkiptechMod.Warning($"{___pawn.Label} already had a skipNet plan for this scenario, but it was not in an ExecutingEntry state. Disposing plan.");
                    plan.ResetPawnMoveState();
                    plan.Dispose();
                    return;
                }
            }

            // There isn't an existing plan, try generating one.
            if (skipNet.planner.TryFindEligibleSkipNetPlan(___pawn, dest, peMode, out plan))
            {
                MigcorpSkiptechMod.Message($"{___pawn.Label} created a plan and hijacked the pathfinding.",
                    MigcorpSkiptechMod.LogLevel.Verbose);
                dest = plan.entry.parent;
                peMode = PathEndMode.OnCell;
            }
            else
            {
                MigcorpSkiptechMod.Message($"{___pawn.Label} is perfoming vanilla pathing.",
                    MigcorpSkiptechMod.LogLevel.Verbose);
            }
        }


        // We need to intercept and cancel if we arrived at an entry portal as part of a SkipNetPlan.
        // If we CanExit the exit, and the path from exit to dest is valid, ignore.
        // Otherwise, pass onto vanilla job.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Pawn_PathFollower), "PatherArrived")]
        static bool PatherArrived_Prefix(
            Pawn_PathFollower __instance,
            Pawn ___pawn
            )
        {
            MapComponent_SkipNet skipNet = ___pawn?.Map?.GetComponent<MapComponent_SkipNet>();
            if (skipNet == null) { return true; }

            if (!skipNet.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan) || plan.IsInvalid) { return true; }

            if (plan.State == SkipNetPlanState.ExecutingEntry && ___pawn.CanReachImmediate(new LocalTargetInfo(plan.entry.parent), PathEndMode.OnCell))
            {
                plan.Notify_SkipNetPlanEntryReached();
                return false;
            }
            else if (___pawn.CanReachImmediate(plan.originalDest, plan.originalPeMode))
            {
                plan.Notify_SkipNetPlanExitReached();
                return true;
            }
            else if(plan.State == SkipNetPlanState.ExecutingExit)
            {
                MigcorpSkiptechMod.Warning($"{___pawn.Label} notified PatherArrived while in ExecutingExit state. This shouldn't happen.");
                plan.Notify_SkipNetPlanExitReached();
                return true;
            }

            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Pawn_PathFollower), "PatherFailed")]
        static bool PatherFailed_Prefix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            MapComponent_SkipNet skipNet = ___pawn?.Map?.GetComponent<MapComponent_SkipNet>();
            if (skipNet == null) { return true; }

            // If we weren't running on a plan, let the PatherFailed notification pass.
            if (!skipNet.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan) || plan.IsInvalid) { return true; }

            // We had a plan and it failed. Let it try again or reset pathing rather than failing the original task.
            plan.Notify_SkipNetPlanFailedOrCancelled();
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("GenerateNewPathRequest")]
        static bool GenerateNewPathRequest_Prefix(
            Pawn_PathFollower __instance,
            ref PathRequest __result,
            Pawn ___pawn,
            LocalTargetInfo ___destination,
            PathEndMode ___peMode)
        {
            Map map = ___pawn.Map;
            MapComponent_SkipNet skipNet = ___pawn?.Map?.GetComponent<MapComponent_SkipNet>();
            if (skipNet == null) { return true; }

            ref LocalTargetInfo dest = ref _patherDestRef(___pawn.pather);
            ref PathEndMode peMode = ref _patherPeModeRef(___pawn.pather);

            if (skipNet.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan) && plan.State != SkipNetPlanState.None)
            {
                TraverseParms tp = TraverseParms.For(___pawn, mode: TraverseMode.ByPawn);

                if (plan.State == SkipNetPlanState.ExecutingEntry)
                {
                    MigcorpSkiptechMod.Message($"{___pawn.Label} is calling TryFindEligibleSkipNetPlan from GenerateNewPathRequest_Prefix. plan.state={plan?.State}.",
                        MigcorpSkiptechMod.LogLevel.Verbose);

                    if (!plan.CheckIsStillAccessible() ||
                        !plan.CheckIsStillPathable(map, tp))
                    {
                        MigcorpSkiptechMod.Message($"{___pawn.Label} tried reusing existing skipNet plan, however it is either inaccessible, or unpathable.",
                        MigcorpSkiptechMod.LogLevel.Verbose);
                        MigcorpSkiptechMod.Message($"{___pawn.Label} Acessibility entry={plan.entry}, (IsEnterable={plan.entry.IsEnterableBy(___pawn)}); exit={plan.exit}, (IsEnterable={plan.exit.IsExitableBy(___pawn)})",
                        MigcorpSkiptechMod.LogLevel.Verbose);
                        MigcorpSkiptechMod.Message($"{___pawn.Label} Reachability entry={plan.entry}, (CanReach={map.reachability.CanReach(___pawn.Position, plan.entry.Position, PathEndMode.OnCell, tp)}); exit={plan.exit}, (CanReach={map.reachability.CanReach(plan.originalDest.Cell, plan.exit.Position, plan.originalPeMode, tp)})",
                        MigcorpSkiptechMod.LogLevel.Verbose);

                        dest = plan.originalDestPostition;
                        peMode = plan.originalPeMode;

                        plan.Dispose();


                        // Try finding a new route.
                        if(skipNet.planner.TryFindEligibleSkipNetPlan(___pawn, ___destination, ___peMode, out plan))
                        {
                            dest = plan.entry.parent;
                            peMode = PathEndMode.OnCell;
                        };
                        return true;
                    }

                    // Re-apply the hijack.
                    dest = plan.entry.parent;
                    peMode = PathEndMode.OnCell;

                    MigcorpSkiptechMod.Message($"{___pawn.Label} reusing existing skipNet plan; re-hijacking StartPath to entry at {plan.entry.Position}.",
                        MigcorpSkiptechMod.LogLevel.Verbose);
                }
            }

            // Check if we've tried a skipnet plan this tick, if not, try that instead.
            // If there is a disposed of plan, then we may have already tried. If not,
            // Try one first.
            else if(!skipNet.TryGetSkipNetPlan(___pawn, out plan, true) || !plan.IsInvalid)
            {
                MigcorpSkiptechMod.Message($"{___pawn.Label} is calling TryFindEligibleSkipNetPlan from GenerateNewPathRequest_Prefix. plan.state={plan?.State}.",
                        MigcorpSkiptechMod.LogLevel.Verbose);
                if (skipNet.planner.TryFindEligibleSkipNetPlan(___pawn, ___destination, ___peMode, out plan))
                {
                    dest = plan.entry.parent;
                    peMode = PathEndMode.OnCell;
                }
            }

            // Fall-through: plan is still fine; allow vanilla to create a new request
            return true;
        }
        
        // Make sure the save data holds the original destination and peMode, not the SkipNetPlan replacement.
        public struct SwappedSaveState
        {
            public bool swapped;
            public LocalTargetInfo swappedDestination;
            public PathEndMode swappedPeMode;
            public bool swappedCurPathJobIsStale;
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Pawn_PathFollower.ExposeData))]
        static void ExposeData_Prefix(
            Pawn_PathFollower __instance,
            Pawn ___pawn,
            LocalTargetInfo ___destination,
            PathEndMode ___peMode,
            bool ___curPathJobIsStale,
            out SwappedSaveState __state
            )
        {
            __state = new SwappedSaveState();
            __state.swapped = false;
            __state.swappedDestination = ___destination;
            __state.swappedPeMode = ___peMode;
            __state.swappedCurPathJobIsStale = ___curPathJobIsStale;

            if (Scribe.mode != LoadSaveMode.Saving) { return; }

            Map map = ___pawn.Map;

            MapComponent_SkipNet skipNet = ___pawn?.Map?.GetComponent<MapComponent_SkipNet>();
            if (skipNet == null) { return; }

            if (skipNet.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan))
            {
                ref LocalTargetInfo dest = ref _patherDestRef(___pawn.pather);
                ref PathEndMode peMode = ref _patherPeModeRef(___pawn.pather);

                __state.swapped = true;

                dest = plan.originalDest;
                peMode = plan.originalPeMode;
                __instance.curPathJobIsStale = true; // Force the game to repath on load.
            }
        }

        [HarmonyFinalizer]
        [HarmonyPatch(nameof(Pawn_PathFollower.ExposeData))]
        static void ExposeData_Finalizer(
            Pawn_PathFollower __instance,
            Pawn ___pawn,
            ref SwappedSaveState __state
            )
        {
            if (__state.swapped)
            {
                ref LocalTargetInfo dest = ref _patherDestRef(___pawn.pather);
                ref PathEndMode peMode = ref _patherPeModeRef(___pawn.pather);

                dest = __state.swappedDestination;
                peMode = __state.swappedPeMode;
                __instance.curPathJobIsStale = __state.swappedCurPathJobIsStale;
            }
        }
    }
}
