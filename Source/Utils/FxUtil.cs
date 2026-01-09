using RimWorld;
using Verse;

namespace MigCorp.Skiptech.Utils
{
    public static class FxUtil
    {
        public static void PlaySkip(IntVec3 cell, Map map, bool entry, bool noDelay = true)
        {
            EffecterDef def = null;
            try
            {
                def = entry
                    ? (noDelay ? EffecterDefOf.Skip_EntryNoDelay : EffecterDefOf.Skip_Entry)
                    : (noDelay ? EffecterDefOf.Skip_ExitNoDelay : EffecterDefOf.Skip_Exit);
            }
            catch { }

            if (def != null && !MigcorpSkiptechMod.Settings.disableTeleportFlashEffect)
            {
                Effecter eff = def.Spawn();
                TargetInfo tgt = new TargetInfo(cell, map);
                eff.Trigger(tgt, tgt);
                eff.Cleanup();
            }
            else
            {
                FleckMaker.ThrowMicroSparks(cell.ToVector3Shifted(), map);
            }
        }
    }
}
