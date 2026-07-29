using Verse;

namespace MigCorp.Skiptech.Utils
{
    public enum AccessMode { Everyone, NonHostile, Colonists }
    public enum LogLevel { Normal, Debug, Verbose }
    public enum LogType { Info, Warning, Error }

    internal class SkiptechUtil
    {


        // UI Helpers
        private static readonly string modTagString = "[MigCorp-Skiptech]";
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
        { Log(message, level); }
        public static void Message(object obj, LogLevel level = LogLevel.Normal)
        { Log(obj, level); }
        public static void Warning(string message, LogLevel level = LogLevel.Normal)
        { Log(message, level, LogType.Warning); }
        public static void Warning(object obj, LogLevel level = LogLevel.Normal)
        { Log(obj, level, LogType.Warning); }
        public static void Error(string message, LogLevel level = LogLevel.Normal)
        { Log(message, level, LogType.Error); }
        public static void Error(object obj, LogLevel level = LogLevel.Normal)
        { Log(obj, level, LogType.Error); }
    }
}
