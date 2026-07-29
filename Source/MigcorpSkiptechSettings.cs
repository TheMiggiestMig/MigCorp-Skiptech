using MigCorp.Skiptech.Utils;
using Verse;

namespace MigCorp.Skiptech
{
    public class MigcorpSkiptechSettings : ModSettings
    {
        // Access
        public const AccessMode DefaultAccessMode = AccessMode.Everyone;
        public const bool DefaultAnimalsCanUse = true;

        public AccessMode accessMode = DefaultAccessMode;
        public bool animalsCanUse = DefaultAnimalsCanUse;

        // Features
        public bool disableSkipShock = false;
        public bool enableSkipShockAvoidance = false;
        public bool disableUnpoweredSkipdoors = false;

        // Skipdoor power (for custom difficulty scaling)
        public int defaultSkipdoorPower = -1;

        // Accessibility
        public bool disableTeleportFlashEffect = false;

        // Debug
        public bool debugVerboseLogging = false;

        public override void ExposeData()
        {
            // Access
            Scribe_Values.Look(ref accessMode, "accessMode", DefaultAccessMode);
            Scribe_Values.Look(ref animalsCanUse, "animalsCanUse", DefaultAnimalsCanUse);

            // Features
            Scribe_Values.Look(ref disableSkipShock, "disableSkipShock", false);
            Scribe_Values.Look(ref enableSkipShockAvoidance, "enableSkipShockAvoidance", false);
            Scribe_Values.Look(ref disableUnpoweredSkipdoors, "disableUnpoweredSkipdoors", false);

            // Skipdoor power
            Scribe_Values.Look(ref defaultSkipdoorPower, "defaultSkipdoorPower", -1);

            // Accessibility
            Scribe_Values.Look(ref disableTeleportFlashEffect, "disableTeleportFlashEffect", false);

            // Debug
            Scribe_Values.Look(ref debugVerboseLogging, "debugVerboseLogging", false);
        }
    }
}
