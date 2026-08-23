using HarmonyLib;
using Verse;

namespace MigCorp.Skiptech.Patches.Core
{
    [HarmonyPatch(typeof(Map))]
    public static class Patch_Map
    {
        // Stop space bases and other "temporary" maps from being nuked if there is a linked skipgate.
        // Keep that bastard running!
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Map.AnyBuildingBlockingMapRemoval), MethodType.Getter)]
        public static void AnyBuildingBlockingMapRemoval_PostFix(Map __instance, ref bool __result)
        {
            if (!__result && __instance.listerThings.AnyThingWithDef(SkiptechDefOf.MigCorp_SkipgatePortal))
            {
                __result = true;
            }
        }
    }
}
