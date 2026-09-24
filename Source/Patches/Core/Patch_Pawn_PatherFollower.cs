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
        // Now handles the proposing of new plans.
        [HarmonyPostfix]
        [HarmonyPatch("GenerateNewPathRequest")]
        static void GenerateNewPathRequest_Postfix(
            ref PathRequest __result,
            Pawn ___pawn)
        {
            // Just in case another mod kills GenerateNewPathRequest.
            if (__result == null) { return; }

            MapComponent_SkipNet skipNet = MapComponent_SkipNet.For(___pawn?.Map);
            if (skipNet == null) { return; }

            if (skipNet.splicer.TryPeekLiveSeam(___pawn, out SkipNetPathSplicer.SeamInfo seam) && seam.plan != null)
            {
                bool sameTrip = seam.plan.originalDest == __result.Target && seam.plan.originalPeMode == __result.EndMode;
                if (!sameTrip || __result.Start == seam.exitCell) { seam.plan.DisposeSuperseded(); }
            }

            if (skipNet.proposer.TryCaptureRequest(___pawn, __result, out PathRequest dummy))
            {
                __result = dummy;
            }
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

            MapComponent_SkipNet skipNet = MapComponent_SkipNet.For(___pawn?.Map);
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
