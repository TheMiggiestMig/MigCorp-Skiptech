using HarmonyLib;
using MigCorp.Skiptech.SkipNet;
using Verse;
using Verse.AI;
using static MigCorp.Skiptech.SkipNet.SkipNetPathSplicer;

namespace MigCorp.Skiptech
{
    [HarmonyPatch(typeof(Pawn_PathFollower))]
    static class Pawn_PathFollower_Patch
    {
        // Nuke any current SkipNetPlan for the pawn since we're doing a whole new StartPath request.
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Pawn_PathFollower.StartPath))]
        static void StartPath_Prefix(
            LocalTargetInfo dest,
            PathEndMode peMode,
            Pawn_PathFollower __instance,
            Pawn ___pawn)
        {
            MapComponent_SkipNet skipNet =
                ___pawn?.Map?.GetComponent<MapComponent_SkipNet>();

            if (skipNet == null) { return; }

            // There are some real weird mod decisions out there that specifically try to path
            // to exacly nowhere. Let vanilla fail it.
            if (!dest.IsValid || peMode == PathEndMode.None) { return; }

            // If there isn't a current plan, carry on. GenerateNewPathRequest will make a new proposal for us.
            if (!skipNet.planner.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan)) { return; }

            // If a valid plan already exists, and it's going to the same location,
            // it's probably GetNewPathRequest refreshing the path. Skip proposing a new plan
            // and re-establish the hijack.
            if (plan.originalDest == dest && plan.originalPeMode == peMode &&
                __instance.Moving && __instance.curPath != null)
            {
                return;
            }

            // New StartPath request while there's an ongoing plan.
            // Dispose it as superseded so it doesn't fight being overwritten.
            plan.DisposeSuperseded();
        }

        /*
        // No longer needed since we only have one destination now (the original)
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
        */

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Pawn_PathFollower), "PatherFailed")]
        static void PatherFailed_Prefix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            MapComponent_SkipNet skipNet = ___pawn?.Map?.GetComponent<MapComponent_SkipNet>();
            if (skipNet == null) { return; }

            // If we were running on a plan, clean it up and let the PatherFailed notification pass.
            if (skipNet.planner.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan))
            {
                plan.DisposeCancelled();
            }
        }

        /*
        // No longer needed since we're no longer hijacking dest.
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

                        plan.DisposeSuperseded();
                        return;
                    }

                    // Re-apply the hijack.
                    dest = plan.entry.parent;
                    peMode = PathEndMode.OnCell;
                }
            }
        }
        */

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
            // if (skipNet.proposer.IsHijacking(___pawn)) { return; }

            // Already serving an active plan (hijack re-applied in the prefix), or attempted (and failed) a plan this tic.
            if (skipNet.planner.TryGetSkipNetPlan(___pawn, out SkipNetPlan plan)) { plan.DisposeSuperseded(); }

            skipNet.proposer.TryMakeSkipNetProposal(___pawn, ___destination, ___peMode, __result.TraverseParms);
        }

        public struct SkipNetPathSeamStepState
        {
            public SkipNetPathSplicer splicer;
            public SkipNetPathSplicer.SeamInfo seam;
            public bool approved;
        }

        [HarmonyPrefix]
        [HarmonyPatch("TryEnterNextPathCell")]
        static bool TryEnterNextPathCell_Prefix(
            Pawn_PathFollower __instance,
            Pawn ___pawn,
            out SkipNetPathSeamStepState __state)
        {
            __state = default;

            MapComponent_SkipNet skipNet = ___pawn?.Map?.GetComponent<MapComponent_SkipNet>();
            if (skipNet == null) { return true; }
            if (!skipNet.splicer.TryGetSeam(___pawn, out SkipNetPathSplicer.SeamInfo seam)) { return true; }

            __state.splicer = skipNet.splicer;
            __state.seam = seam;

            // Still walking the entry path. Ignore.
            if (___pawn.Position != seam.entryCell || __instance.nextCell != seam.exitCell) { return true; }

            // If the exit skipdoor has pawns standing on it blocking it, freeze the pawn's sprite at the spot (to stop tweening).
            if (__instance.WillCollideNextCell)
            {
                SkipNetPathSplicer.HoldAtSeam(__instance);
                return true;
            }

            switch (skipNet.splicer.DecideTeleportStep(___pawn, seam))
            {
                case TeleportStepDecision.Approved:
                    __state.approved = true;
                    return true; // Green to go!

                case TeleportStepDecision.Waiting:
                case TeleportStepDecision.BlockedDead:
                default:
                    return false; // no move this tick
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("TryEnterNextPathCell")]
        static void TryEnterNextPathCell_Postfix(Pawn ___pawn, SkipNetPathSeamStepState __state)
        {
            if (__state.seam == null) { return; }

            if (__state.approved)
            {
                __state.splicer.CompleteTeleportStep(___pawn, __state.seam);
                return;
            }

            // If the seam is still waiting, freeze the pawn's sprite at the spot (to stop tweening).
            Pawn_PathFollower pather = ___pawn.pather;
            if (___pawn.Position == __state.seam.entryCell && pather.nextCell == __state.seam.exitCell)
            {
                SkipNetPathSplicer.HoldAtSeam(pather);
            }
        }

        /*
        // Don't think this is needed anymore, since we aren't hijacking the dest / peMode any more.
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
        */
    }
}
