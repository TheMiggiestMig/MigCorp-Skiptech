using HarmonyLib;
using MigCorp.Skiptech.SkipNet;
using Verse;

namespace MigCorp.Skiptech
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            SkipdoorCustomPower.CaptureXmlDefault(); // Grab the xml power setting for skipdoors as the game loads.

            string id = "migcorp.skiptech";
            new Harmony(id).PatchAll();
            Log.Message("[MigCorp.Skiptech] Loaded.");
        }
    }
}