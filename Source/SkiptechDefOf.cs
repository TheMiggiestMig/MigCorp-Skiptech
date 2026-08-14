using RimWorld;
using Verse;

namespace MigCorp.Skiptech
{
    [DefOf]
    public static class SkiptechDefOf
    {
        public static ThingDef MigCorp_SkipgatePortal;
        public static RulePackDef MigCorp_SkipgateNameMaker;

        static SkiptechDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SkiptechDefOf));
    }
}