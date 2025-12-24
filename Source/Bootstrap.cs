using HarmonyLib;
using Verse;

namespace MigCorp.Skiptech
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            var id = "migcorp.skiptech";
            new Harmony(id).PatchAll();
            Log.Message("[MigCorp.Skiptech] Loaded.");
        }
    }
}