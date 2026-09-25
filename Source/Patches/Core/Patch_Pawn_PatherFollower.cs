using HarmonyLib;
using MigCorp.Skiptech.SkipNet;
using Verse;
using Verse.AI;
using static MigCorp.Skiptech.SkipNet.SkipNetSeam;

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

            if (skipNet.manager.TryCapturePathRequest(___pawn, __result, out PathRequest dummy))
            {
                __result = dummy;
            }
        }

        public struct SkipNetPathSeamStepState
        {
            public SkipNetManager manager;
            public SkipNetPlan plan;
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
            if (!skipNet.manager.TryGetLivePlan(___pawn, out SkipNetPlan plan)) { return true; }

            __state.manager = skipNet.manager;
            __state.plan = plan;

            // Still walking the entry path. Ignore.
            if (!plan.IsPawnAtSeam) { return true; }

            // If the exit skipdoor has pawns standing on it blocking it, freeze the pawn's sprite at the spot (to stop tweening).
            if (__instance.WillCollideNextCell)
            {
                HoldAtSeam(__instance);
                return true;
            }

            switch (skipNet.manager.DecideTeleportStep(___pawn, plan))
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
            if (__state.approved) { __state.manager.CompleteTeleportStep(___pawn, __state.plan); }
        }

        // Holds the pawn at the entry while waiting for the teleporter to be ready.
        // Patched here rather than TryEnterNextPathCell because it gets called even if the pawn is already standing on the entry.
        [HarmonyPostfix]
        [HarmonyPatch("SetupMoveIntoNextCell")]
        static void SetupMoveIntoNextCell_Postfix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            MapComponent_SkipNet skipNet = MapComponent_SkipNet.For(___pawn?.Map);
            if (skipNet == null) { return; }
            if (!skipNet.manager.TryGetLivePlan(___pawn, out SkipNetPlan plan)) { return; } // Also promotes a just-claimed splice to live.

            if (plan.IsPawnAtSeam)
            {
                HoldAtSeam(__instance);
            }
        }
    }
}
