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
                var eff = def.Spawn();
                var tgt = new TargetInfo(cell, map);
                eff.Trigger(tgt, tgt);
                eff.Cleanup();
            }
            else
            {
                FleckMaker.ThrowMicroSparks(cell.ToVector3Shifted(), map);
            }
        }

        /// <summary>
        /// Choose the correct EMP "disabled" EffecterDef (small/large) based on footprint.
        /// </summary>
        private static EffecterDef EmpDefFor(Thing t)
        {
            if (t?.def == null) return EffecterDefOf.DisabledByEMP; // safe default
            var sz = t.def.Size; // IntVec2
            return (sz.Area >= 4) ? EffecterDefOf.DisabledByEMPLarge : EffecterDefOf.DisabledByEMP;
        }

        /// <summary>
        /// Start a sustained EMP-disabled effect on this thing. Returns the spawned Effecter (store it).
        /// Also triggers once immediately so audio/first burst starts.
        /// </summary>
        public static Effecter StartEmpDisabled(Thing t)
        {
            if (t?.Map == null) return null;
            var def = EmpDefFor(t);
            var eff = def?.Spawn();
            if (eff != null)
            {
                var tgt = new TargetInfo(t.Position, t.Map);
                eff.Trigger(tgt, tgt);
            }
            return eff;
        }

        /// <summary>
        /// Tick a sustained Effecter on this thing (visual + audio).
        /// </summary>
        public static void TickEffecter(Effecter eff, Thing t)
        {
            if (eff == null || t?.Map == null) return;
            var tgt = new TargetInfo(t.Position, t.Map);
            eff.EffectTick(tgt, tgt);
        }

        /// <summary>
        /// Stop and null an Effecter safely.
        /// </summary>
        public static void Stop(ref Effecter eff)
        {
            if (eff != null)
            {
                eff.Cleanup();
                eff = null;
            }
        }
    }
}
