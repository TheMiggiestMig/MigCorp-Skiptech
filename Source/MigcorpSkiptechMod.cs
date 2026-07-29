using MigCorp.Skiptech.Utils;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech
{
    // Mod Options and helper functions
    public class MigcorpSkiptechMod : Mod
    {
        public static MigcorpSkiptechSettings Settings;

        public MigcorpSkiptechMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MigcorpSkiptechSettings>();
        }

        public override string SettingsCategory() => "MigCorp.Skiptech.Settings".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Gameplay Settings
            var ls = new Listing_Standard();
            ls.Begin(inRect);
            ls.GapLine();
            ls.Label("MigCorp.Skiptech.Settings.Allowed".Translate());

            foreach (AccessMode accessMode in Enum.GetValues(typeof(AccessMode)))
                AccessMode_RadioButton(ls, accessMode);

            ls.CheckboxLabeled("MigCorp.Skiptech.Settings.Allowed.Animals".Translate(),
                ref Settings.animalsCanUse,
                "MigCorp.Skiptech.Settings.Allowed.Animals.Tip".Translate());
            ls.Gap();
            ls.CheckboxLabeled("MigCorp.Skiptech.Settings.Features.Skipshock".Translate(),
                ref Settings.disableSkipShock,
                "MigCorp.Skiptech.Settings.Features.Skipshock.Tip".Translate());
            ls.CheckboxLabeled("MigCorp.Skiptech.Settings.Features.SkipshockAvoidance".Translate(),
                ref Settings.enableSkipShockAvoidance,
                "MigCorp.Skiptech.Settings.Features.SkipshockAvoidance.Tip".Translate());
            ls.CheckboxLabeled("MigCorp.Skiptech.Settings.Features.DisableUnpoweredSkipdoors".Translate(),
                ref Settings.disableUnpoweredSkipdoors,
                "MigCorp.Skiptech.Settings.Features.DisableUnpoweredSkipdoors.Tip".Translate());

            // Accessibility Settings
            ls.GapLine();
            ls.CheckboxLabeled("MigCorp.Skiptech.Settings.Accessibility.FlashEffect".Translate(),
                ref Settings.disableTeleportFlashEffect,
                "MigCorp.Skiptech.Settings.Accessibility.FlashEffect.Tip".Translate());

            // Dev Settings
            ls.GapLine();
            if (Prefs.DevMode)
            {
                ls.Gap();
                ls.CheckboxLabeled("MigCorp.Skiptech.Settings.Debug.Verbose".Translate(),
                    ref Settings.debugVerboseLogging);
            }
            else
            {
                ls.Label("MigCorp.Skiptech.Settings.Debug.Enable".Translate());
            }

            ls.End();
        }

        private void AccessMode_RadioButton(Listing_Standard ls, AccessMode accessMode)
        {
            if (ls.RadioButton($"MigCorp.Skiptech.Settings.Allowed.{accessMode}".Translate(),
                Settings.accessMode == accessMode,
                20,
                $"MigCorp.Skiptech.Settings.Allowed.{accessMode}.Tip".Translate()))
            {
                Settings.accessMode = accessMode;
            }
        }
    }
}
