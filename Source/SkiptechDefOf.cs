using RimWorld;
using Verse;

namespace MigCorp.Skiptech
{
    [DefOf]
    public static class SkiptechDefOf
    {
        public static JobDef MigCorp_SkipLocally;

        static SkiptechDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SkiptechDefOf));
    }
}