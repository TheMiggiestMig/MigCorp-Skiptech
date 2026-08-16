using RimWorld;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech
{
    [DefOf]
    public static class SkiptechDefOf
    {
        public static ThingDef MigCorp_SkipgatePortal;
        public static ThingDef MigCorp_SkipgateSent;
        public static RulePackDef MigCorp_SkipgateNameMaker;
        public static DutyDef MigCorp_SkipgateHold;

        static SkiptechDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SkiptechDefOf));
    }
}