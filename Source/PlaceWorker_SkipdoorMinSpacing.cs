using System.Linq;
using RimWorld;
using Verse;
using MigCorp.Skiptech.Comps;

namespace MigCorp.Skiptech
{
    public class PlaceWorker_SkipdoorMinSpacing : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(
            BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
            Thing thingToIgnore = null, Thing thing = null)
        {
            // Check the 8 adjacent cells (1-tile spacing like traps)
            foreach (var c in GenAdj.CellsAdjacent8Way(new TargetInfo(loc, map)))
            {
                if (!c.InBounds(map)) continue;

                var things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var t = things[i];
                    if (t == thingToIgnore) continue;

                    // Existing built skipdoor?
                    if (t.TryGetComp<CompSkipdoor>() != null)
                        return "Skipdoors must be at least one cell apart.";

                    // Existing blueprint that *will become* a skipdoor?
                    if (t is Blueprint bp)
                    {
                        if (bp.def.entityDefToBuild is ThingDef buildTd && buildTd.comps != null)
                        {
                            // Avoid LINQ allocs if you want; this is fine for dev:
                            if (buildTd.comps.Any(cp => cp.compClass == typeof(CompSkipdoor)))
                                return "Skipdoors must be at least one cell apart.";
                        }
                    }
                }
            }

            return true;
        }
    }
}
