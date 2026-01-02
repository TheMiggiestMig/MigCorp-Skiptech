using UnityEngine;
using Verse;
using MigCorp.Skiptech.Utils;
using System;
using RimWorld;

namespace MigCorp.Skiptech
{
    // Mod Settings
    public class MigcorpSkiptechSettings : ModSettings
    {
        public AccessMode accessMode = AccessMode.Everyone;
        public bool animalsCanUse = true;
        public bool disableSkipShock = false;
        public bool disableTeleportFlashEffect = false;
        public bool debugVerboseLogging = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref accessMode, "accessMode", AccessMode.Everyone);
            Scribe_Values.Look(ref animalsCanUse, "animalsCanUse", true);
            Scribe_Values.Look(ref disableSkipShock, "disableSkipShock", false);
            Scribe_Values.Look(ref disableTeleportFlashEffect, "disableTeleportFlashEffect", false);
            Scribe_Values.Look(ref debugVerboseLogging, "debugVerboseLogging", false);
        }
    }

    // Mod Options and helper functions
    public class MigcorpSkiptechMod : Mod
    {
        public static MigcorpSkiptechSettings Settings;

        public enum LogLevel { Normal, Debug, Verbose }
        public enum LogType { Info, Warning, Error }

        // UI Helpers
        private static readonly string modTagString = "[MigCorp-Skiptech]";

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

        // Logging and messages
        private static string LogString(string message, LogLevel level = LogLevel.Normal)
        {
            switch (level)
            {
                case LogLevel.Debug:
                    return "[DEBUG] " + message;
                case LogLevel.Verbose:
                    return "[INFO] " + message;
            }

            return " " + message;
        }

        public static void Log(string message, LogLevel level = LogLevel.Normal, LogType type = LogType.Info)
        {
            if ((level == LogLevel.Debug || level == LogLevel.Verbose) && !Prefs.DevMode) { return; }
            if (level == LogLevel.Verbose && !MigcorpSkiptechMod.Settings.debugVerboseLogging) { return; }

            switch (type)
            {
                case LogType.Info:
                    Verse.Log.Message(modTagString + LogString(message));
                    return;
                case LogType.Warning:
                    Verse.Log.Warning(modTagString + LogString(message));
                    return;
                case LogType.Error:
                    Verse.Log.Error(modTagString + LogString(message));
                    return;
            }
        }

        public static void Log(object obj, LogLevel level = LogLevel.Normal, LogType type = LogType.Info)
        {
            Log(obj.ToString(), level, type);
        }

        public static void Message(string message, LogLevel level = LogLevel.Normal)
        { MigcorpSkiptechMod.Log(message, level); }
        public static void Message(object obj, LogLevel level = LogLevel.Normal)
        { MigcorpSkiptechMod.Log(obj, level); }
        public static void Warning(string message,LogLevel level = LogLevel.Normal)
        { MigcorpSkiptechMod.Log(message, level, LogType.Warning); }
        public static void Warning(object obj, LogLevel level = LogLevel.Normal)
        { MigcorpSkiptechMod.Log(obj, level, LogType.Warning); }
        public static void Error(string message, LogLevel level = LogLevel.Normal)
        { MigcorpSkiptechMod.Log(message, level, LogType.Error); }
        public static void Error(object obj, LogLevel level = LogLevel.Normal)
        { MigcorpSkiptechMod.Log(obj, level, LogType.Error); }
    }
}
