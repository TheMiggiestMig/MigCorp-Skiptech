using MigCorp.Skiptech.Utils;
using Verse;

namespace MigCorp.Skiptech
{
    public class MigcorpSkiptechSettings : ModSettings
    {
        public AccessMode accessMode = AccessMode.Everyone;
        public bool animalsCanUse = true;
        public bool disableUnpoweredSkipdoors = false;
        public bool disableSkipShock = false;
        public bool enableSkipShockAvoidance = false;
        public bool disableTeleportFlashEffect = false;
        public bool debugVerboseLogging = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref accessMode, "accessMode", AccessMode.Everyone);
            Scribe_Values.Look(ref animalsCanUse, "animalsCanUse", true);
            Scribe_Values.Look(ref disableUnpoweredSkipdoors, "disableUnpoweredSkipdoors", false);
            Scribe_Values.Look(ref disableSkipShock, "disableSkipShock", false);
            Scribe_Values.Look(ref enableSkipShockAvoidance, "enableSkipShockAvoidance", false);
            Scribe_Values.Look(ref disableTeleportFlashEffect, "disableTeleportFlashEffect", false);
            Scribe_Values.Look(ref debugVerboseLogging, "debugVerboseLogging", false);
        }
    }
}
