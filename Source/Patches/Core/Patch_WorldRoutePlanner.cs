using HarmonyLib;
using MigCorp.Skiptech.Skipgate;
using RimWorld;
using RimWorld.Planet;
using System;

namespace MigCorp.Skiptech.Patches.Core
{
    [HarmonyPatch(typeof(WorldRoutePlanner))]
    public static class WorldRoutePlanner_Patch
    {
        // Where we're going, our Skipgate caravans don't need roads.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(WorldRoutePlanner), "Start", new Type[] { typeof(Dialog_FormCaravan) })]
        static bool WorldRoutePlanner_Start_Prefix(Dialog_FormCaravan formCaravanDialog)
        {
            return !(formCaravanDialog is Dialog_FormSkipgateCaravan);
        }
    }
}