using HarmonyLib;
using MigCorp.Skiptech.SkipNet;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech
{
    [HarmonyPatch(typeof(Pawn_PathFollower))]
    static class Pawn_PathFollower_Patch
    {
        // Nuke any current SkipNetPlan for the pawn since we're doing a whole new StartPath request.
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Pawn_PathFollower.StartPath))]
        static bool StartPath_Prefix(
            LocalTargetInfo dest,
            PathEndMode peMode,
            Pawn ___pawn)
        {
            MapComponent_SkipNet skipNet =
                ___pawn?.Map?.GetComponent<MapComponent_SkipNet>();

            if (skipNet == null) { return true; }

            // There are some real weird mod decisions out there that specifically try to path
            // to exacly nowhere.
            if (!dest.IsValid || peMode == PathEndMode.None) { return true; }

            // Preserve StartPath calls made by proposer.
            if (skipNet.proposer.IsHijacking(___pawn)) { return true; }

            // If a valid plan already exists, and it's going to the same location,
            // it's probably GetNewPathRequest refreshing the path. Skip proposing a new plan
            // and re-establish the hijack.
            if (skipNet.planner.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan))
            {
                if (plan.State == SkipNetPlanState.ExecutingEntry &&
                plan.originalDest == dest && plan.originalPeMode == peMode &&
                ___pawn.pather.Moving)
                {
                    return false;
                }

                plan.DisposeSuperseded();
            }

            return true;
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

            if (!skipNet.planner.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan) || plan.IsInvalid) { return true; }

            if (plan.State == SkipNetPlanState.ExecutingEntry && ___pawn.CanReachImmediate(new LocalTargetInfo(plan.entry.parent), PathEndMode.OnCell))
            {
                plan.Notify_SkipNetPlanEntryReached();
                return false;
            }
            else if (___pawn.CanReachImmediate(plan.originalDest, plan.originalPeMode))
            {
                plan.DisposeSuperseded();
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
            if (!skipNet.planner.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan) || plan.IsInvalid) { return true; }

            // We had a plan and it failed. Let it try again or reset pathing rather than failing the original task.
            plan.DisposeCancelled();
            return false;
        }

        // Usually called from StartPath (occasionally from the PatherTick).
        // Re-affirms good plans, nuke's bad ones.
        [HarmonyPrefix]
        [HarmonyPatch("GenerateNewPathRequest")]
        static void GenerateNewPathRequest_Prefix(
            Pawn_PathFollower __instance,
            Pawn ___pawn)
        {
            Map map = ___pawn?.Map;
            MapComponent_SkipNet skipNet = map?.GetComponent<MapComponent_SkipNet>();
            if (skipNet == null) { return; }

            ref LocalTargetInfo dest = ref SkipNetUtils._patherDestRef(___pawn.pather);
            ref PathEndMode peMode = ref SkipNetUtils._patherPeModeRef(___pawn.pather);

            TraverseParms tp = SkipNetUtils.JankyTraverseParmsFor(___pawn);

            if (skipNet.planner.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan))
            {
                // This path request is for a plan that was just created. Don't bother doing the checks, it's good... trust.
                if (skipNet.proposer.IsHijacking(___pawn)) { return; }

                // Check if we need to nuke the plan or just reapply the hijack.
                if (plan.State == SkipNetPlanState.ExecutingEntry)
                {

                    if (!plan.IsStillAccessible() ||
                        !plan.IsStillPathableFromEntryToExit(map, tp))
                    {

                        dest = plan.originalDestPostition;
                        peMode = plan.originalPeMode;

                        plan.DisposeCancelled();
                        return;
                    }

                    // Re-apply the hijack.
                    dest = plan.entry.parent;
                    peMode = PathEndMode.OnCell;
                }
            }
        }

        // Now handles the proposing of new plans.
        [HarmonyPostfix]
        [HarmonyPatch("GenerateNewPathRequest")]
        static void GenerateNewPathRequest_Postfix(
            PathRequest __result,
            Pawn ___pawn,
            LocalTargetInfo ___destination,
            PathEndMode ___peMode)
        {
            // Just in case another mod kills GenerateNewPathRequest.
            if (__result == null) { return; }

            MapComponent_SkipNet skipNet = ___pawn?.Map?.GetComponent<MapComponent_SkipNet>();
            if (skipNet == null) { return; }
            if (skipNet.proposer.IsHijacking(___pawn)) { return; }

            // Already serving an active plan (hijack re-applied in the prefix), or attempted (and failed) a plan this tic.
            if (skipNet.planner.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan)) { return; }

            skipNet.proposer.TryMakeSkipNetProposal(___pawn, ___destination, ___peMode, __result.TraverseParms);
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

            if (skipNet.planner.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan))
            {
                ref LocalTargetInfo dest = ref SkipNetUtils._patherDestRef(___pawn.pather);
                ref PathEndMode peMode = ref SkipNetUtils._patherPeModeRef(___pawn.pather);

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
                ref LocalTargetInfo dest = ref SkipNetUtils._patherDestRef(___pawn.pather);
                ref PathEndMode peMode = ref SkipNetUtils._patherPeModeRef(___pawn.pather);

                dest = __state.swappedDestination;
                peMode = __state.swappedPeMode;
                __instance.curPathJobIsStale = __state.swappedCurPathJobIsStale;
            }
        }
    }
}
