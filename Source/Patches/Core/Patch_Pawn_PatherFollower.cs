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

            // If a valid plan already exists, and it's going to the same location, let it.
            if (plan.originalDest == dest && plan.originalPeMode == peMode &&
                __instance.Moving && __instance.curPath != null)
            {
                return;
            }

            // New StartPath request while there's an ongoing plan.
            // Dispose or the current plan as superseded so it doesn't fight being overwritten.
            plan.DisposeSuperseded();
        }

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

            // Assuming StartPath was just re-executing an existing plan, now's the time to dispose of it and try again.
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
    }
}
