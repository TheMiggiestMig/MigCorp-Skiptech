using MigCorp.Skiptech.SkipNet;
using MigCorp.Skiptech.Utils;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech
{
    public class MigcorpSkiptechMod : Mod
    {
        public static MigcorpSkiptechSettings Settings;

        private const string keyPrefix = "MigCorp.Skiptech.Settings";

        public MigcorpSkiptechMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MigcorpSkiptechSettings>();
        }

        public override string SettingsCategory() => keyPrefix.Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard ls = new Listing_Standard();
            ls.Begin(inRect);

            DrawAccessSection(ls);
            DrawFeatureSection(ls);
            DrawPowerSection(ls);
            DrawSkipNetTuningSection(ls);
            DrawAccessibilitySection(ls);
            DrawDebugSection(ls);

            ls.End();
        }

        private static void DrawAccessSection(Listing_Standard ls)
        {
            ls.GapLine();
            ls.Label($"{keyPrefix}.Allowed".Translate());

            foreach (AccessMode mode in Enum.GetValues(typeof(AccessMode)))
            {
                if (ls.RadioButton($"{keyPrefix}.Allowed.{mode}".Translate(),
                        Settings.accessMode == mode, 20f,
                        $"{keyPrefix}.Allowed.{mode}.Tip".Translate()))
                {
                    Settings.accessMode = mode;
                }
            }

            Checkbox(ls, $"{keyPrefix}.Allowed.Animals", ref Settings.animalsCanUse);
        }

        private static void DrawFeatureSection(Listing_Standard ls)
        {
            ls.Gap();
            Checkbox(ls, $"{keyPrefix}.Features.Skipshock", ref Settings.disableSkipShock);
            Checkbox(ls, $"{keyPrefix}.Features.SkipshockAvoidance", ref Settings.enableSkipShockAvoidance);
            Checkbox(ls, $"{keyPrefix}.Features.DisableUnpoweredSkipdoors", ref Settings.disableUnpoweredSkipdoors);
        }

        private static void DrawPowerSection(Listing_Standard ls)
        {
            ls.GapLine();

            bool inGame = Current.ProgramState == ProgramState.Playing && Current.Game != null;
            GameComponent_Skiptech gameComp = inGame ? Current.Game.GetComponent<GameComponent_Skiptech>() : null;

            float shownWatts = inGame ? SkipdoorCustomPower.CurrentGameWatts
                                      : SkipdoorCustomPower.DefaultWatts;
            bool isCustom = inGame ? gameComp != null && gameComp.skipdoorPowerOverride >= 0
                                   : Settings.defaultSkipdoorPower >= 0;
            string key = inGame ? $"{keyPrefix}.Power.Game" : $"{keyPrefix}.Power.Default";
            string fallback = (inGame ? SkipdoorCustomPower.DefaultWatts
                                      : SkipdoorCustomPower.XmlWatts).ToString("F0");


            float powerSlider = SliderRow(ls,
                key.Translate(shownWatts.ToString("F0")),
                (key + ".Tip").Translate(fallback),
                shownWatts, SkipdoorCustomPower.MinWatts, SkipdoorCustomPower.MaxWatts,
                roundTo: SkipdoorCustomPower.StepWatts,
                showReset: isCustom,
                resetTo: SkipdoorCustomPower.Unset,
                resetTip: $"{keyPrefix}.Power.Reset".Translate());

            int watts = Mathf.RoundToInt(powerSlider);
            if (watts != Mathf.RoundToInt(shownWatts))
            {
                if (inGame)
                {
                    gameComp.skipdoorPowerOverride = watts;
                    SkipdoorCustomPower.ApplyToActiveGame();
                }
                else
                {
                    Settings.defaultSkipdoorPower = watts;
                }
            }
        }
        private void DrawSkipNetTuningSection(Listing_Standard ls)
        {
            ls.GapLine();
            ls.Label($"{keyPrefix}.Tuning".Translate());

            Settings.skipCost = TuningRow(ls, "SkipCost",
                Settings.skipCost, 0f, 300f, 5f,
                MigcorpSkiptechSettings.DefaultSkipCost,
                Settings.skipCost.ToString("F0"));

            Settings.worthItFactor = TuningRow(ls, "WorthItFactor",
                Settings.worthItFactor, 0.25f, 1f, 0.05f,
                MigcorpSkiptechSettings.DefaultWorthItFactor,
                Settings.worthItFactor.ToStringPercent());
        }

        private static void DrawAccessibilitySection(Listing_Standard ls)
        {
            ls.GapLine();
            Checkbox(ls, $"{keyPrefix}.Accessibility.FlashEffect", ref Settings.disableTeleportFlashEffect);
        }

        private static void DrawDebugSection(Listing_Standard ls)
        {
            ls.GapLine();
            if (Prefs.DevMode)
            {
                Checkbox(ls, $"{keyPrefix}.Debug.Verbose", ref Settings.debugVerboseLogging, hasTip: false);
            }
            else
            {
                ls.Label($"{keyPrefix}.Debug.Enable".Translate());
            }
        }

        // Row helpers (trying to make this a bit neater)
        private static void Checkbox(Listing_Standard ls, string key, ref bool value, bool hasTip = true)
        {
            ls.CheckboxLabeled(key.Translate(), ref value,
                hasTip ? (key + ".Tip").Translate().ToString() : null);
        }

        private static float SliderRow(Listing_Standard ls, string label, string tooltip,
            float value, float min, float max, float roundTo,
            bool showReset, float resetTo, string resetTip)
        {
            const float rowHeight = 30f;
            const float iconSize = 24f;
            const float pad = 8f;

            Rect row = ls.GetRect(rowHeight);
            Rect iconRect = new Rect(row.xMax - iconSize, row.y + (rowHeight - iconSize) / 2f, iconSize, iconSize);
            Rect labelRect = row.LeftPart(0.45f);
            Rect sliderRect = new Rect(labelRect.xMax + pad, row.y,
                iconRect.xMin - labelRect.xMax - 2f * pad, rowHeight);

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            if (!tooltip.NullOrEmpty())
            {
                Widgets.DrawHighlightIfMouseover(labelRect);
                TooltipHandler.TipRegion(labelRect, tooltip);
            }

            float result = Widgets.HorizontalSlider(sliderRect, value, min, max,
                middleAlignment: true, roundTo: roundTo);

            if (showReset && Widgets.ButtonImage(iconRect, TexButton.Reload, true, resetTip))
                result = resetTo;

            ls.Gap(ls.verticalSpacing);
            return result;
        }

        private static float TuningRow(Listing_Standard ls, string keySuffix,
            float value, float min, float max, float roundTo,
            float defaultValue, string valueText)
        {
            string key = $"{keyPrefix}.SkipNetTuning.{keySuffix}";
            return SliderRow(ls,
                key.Translate(valueText), (key + ".Tip").Translate(),
                value, min, max, roundTo,
                showReset: !Mathf.Approximately(value, defaultValue),
                resetTo: defaultValue,
                resetTip: $"{keyPrefix}.SkipNetTuning.Reset".Translate());
        }
    }
}