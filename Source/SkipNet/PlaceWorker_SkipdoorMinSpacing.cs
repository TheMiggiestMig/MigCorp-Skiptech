using MigCorp.Skiptech.SkipNet.Comps;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MigCorp.Skiptech.SkipNet
{
    public class PlaceWorker_SkipdoorMinSpacing : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(
            BuildableDef checkingDef, IntVec3 location, Rot4 rotation, Map map,
            Thing thingToIgnore = null, Thing thing = null)
        {
            // Check the 8 adjacent cells (1-tile spacing like traps)
            foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(new TargetInfo(location, map)))
            {
                if (!cell.InBounds(map)) { continue; }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t == thingToIgnore) { continue; }

                    // Existing built skipdoor?
                    if (t.TryGetComp<CompSkipdoor>() != null) { return "Skipdoors must be at least one cell apart."; }

                    // Existing blueprint that *will become* a skipdoor?
                    if (t is Blueprint blueprint)
                    {
                        if (blueprint.def.entityDefToBuild is ThingDef buildTd && buildTd.comps != null)
                        {
                            if (buildTd.comps.Any(cp => cp.compClass == typeof(CompSkipdoor))) { return "Skipdoors must be at least one cell apart."; }
                        }
                    }
                }
            }

            return true;
        }
    }
}
