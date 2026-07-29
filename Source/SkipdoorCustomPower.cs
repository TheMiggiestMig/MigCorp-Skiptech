using HarmonyLib;
using MigCorp.Skiptech.SkipNet;
using MigCorp.Skiptech.SkipNet.Comps;
using MigCorp.Skiptech.Utils;
using RimWorld;
using Verse;

namespace MigCorp.Skiptech
{
    // For customizing how much power the MigCorp Skipdoors consume.
    // Mainly for balance and scenarios (builders may want them to be free, other players may want more of a challenge).
    public static class SkipdoorCustomPower
    {
        // Settings is getting too bloated.
        // Store this here.
        public const float MinWatts = 0f;
        public const float MaxWatts = 2000f;
        public const float StepWatts = 50f;
        public const int Unset = -1;

        private static float xmlWatts = 300f; // Default if for whatever reason we can't read the xml def ̄¯\_(ツ)_/¯

        // basePowerConsumption is private :(
        // Gotta use a FieldRef to crack it open.
        private static readonly AccessTools.FieldRef<CompProperties_Power, float> BasePowerRef =
            AccessTools.FieldRefAccess<CompProperties_Power, float>("basePowerConsumption");

        private static CompProperties_Power PowerProps =>
            ThingDef.Named("MigcorpSkipdoor")?.GetCompProperties<CompProperties_Power>();

        // TODO: Grab xml value via Bootstrap before it gets modified.
        public static void CaptureXmlDefault()
        {
            CompProperties_Power props = PowerProps;
            if (props != null) { xmlWatts = BasePowerRef(props); }
            else { SkiptechUtil.Warning($"Couldn't capture skipdoor XML power value. Falling back to {xmlWatts}."); }
        }

        public static float XmlWatts => xmlWatts;

        // Main Menu: If the default isn't customized yet, use the xml def value.
        public static float DefaultWatts =>
            MigcorpSkiptechMod.Settings.defaultSkipdoorPower >= 0
                ? MigcorpSkiptechMod.Settings.defaultSkipdoorPower
                : xmlWatts;

        // In-game: If the current save isn't customized yet, use the current default.
        public static float CurrentGameWatts
        {
            get
            {
                GameComponent_Skiptech comp = Current.Game?.GetComponent<GameComponent_Skiptech>();
                return comp != null && comp.skipdoorPowerOverride >= 0
                    ? comp.skipdoorPowerOverride
                    : DefaultWatts;
            }
        }

        // Updates the comp def, and also loops through all the spawned skipdoors (if in-game) to update them too.
        public static void ApplyToActiveGame()
        {
            if (Current.Game == null) { return; }

            CompProperties_Power props = PowerProps;
            if (props == null) { return; }

            BasePowerRef(props) = CurrentGameWatts;

            foreach (Map map in Find.Maps)
            {
                MapComponent_SkipNet skipNet = map.GetComponent<MapComponent_SkipNet>();
                if (skipNet == null) { continue; }
                foreach (CompSkipdoor skipdoor in skipNet.skipdoors)
                {
                    skipdoor?.parent?.TryGetComp<CompPowerTrader>()?.SetUpPowerVars();
                }
            }
        }
    }
}