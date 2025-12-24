using UnityEngine;
using Verse;
using MigCorp.Skiptech.Utils;

namespace MigCorp.Skiptech
{    
    public class SkiptechSettings : ModSettings
    {
        public int baseTeleportToll = 25;
        public AccessMode accessMode = AccessMode.Everyone;
        public bool animalsCanUse = true;
        public bool penAnimalsRequireLead = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref baseTeleportToll, "baseTeleportToll", 25);
            Scribe_Values.Look(ref accessMode, "accessMode", AccessMode.Everyone);
            Scribe_Values.Look(ref animalsCanUse, "animalsCanUse", true);
            Scribe_Values.Look(ref penAnimalsRequireLead, "penAnimalsRequireLead", true);

        }
    }

    public class MigCorpSkiptechMod : Mod
    {
        public static SkiptechSettings Settings;

        public MigCorpSkiptechMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SkiptechSettings>();
        }

        public override string SettingsCategory() => "MigCorp - Skiptech";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var ls = new Listing_Standard();
            ls.Begin(inRect);

            ls.GapLine();
            ls.Label("Access control:");
            if (Widgets.RadioButtonLabeled(ls.GetRect(24), "Everyone", Settings.accessMode == AccessMode.Everyone))
                Settings.accessMode = AccessMode.Everyone;
            if (Widgets.RadioButtonLabeled(ls.GetRect(24), "Friends only", Settings.accessMode == AccessMode.FriendsOnly))
                Settings.accessMode = AccessMode.FriendsOnly;
            if (Widgets.RadioButtonLabeled(ls.GetRect(24), "Colony only", Settings.accessMode == AccessMode.ColonyOnly))
                Settings.accessMode = AccessMode.ColonyOnly;

            ls.GapLine();
            ls.CheckboxLabeled("Animals can use skipdoors", ref Settings.animalsCanUse);
            ls.CheckboxLabeled("Pen animals require a colonist leading them", ref Settings.penAnimalsRequireLead);

            if (Prefs.DevMode)
            {
                ls.Label($"Debug: Base Teleport Toll: {Settings.baseTeleportToll}");
                // Slider row (0..100)
                var line = ls.GetRect(24f);
                Settings.baseTeleportToll = (int)Widgets.HorizontalSlider(
                    line, Settings.baseTeleportToll, 0f, 100f, true,
                    Settings.baseTeleportToll.ToString(), "0", "100");
            }
            else
            {
                ls.Label("Enable Dev Mode to access debug settings.");
            }

            ls.End();
        }
    }
}
