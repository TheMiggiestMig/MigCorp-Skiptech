using MigCorp.Skiptech.SkipNet.Comps;
using MigCorp.Skiptech.Utils;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public class SkipNetCandidateFinder
    {
        public readonly MapComponent_SkipNet skipNet;
        private readonly SkipNetSearcher searcher;

        // Region --> Skipdoor mapping
        private bool regionSkipdoorsDirty = true;
        private readonly Dictionary<Region, List<CompSkipdoor>> regionSkipdoors = new Dictionary<Region, List<CompSkipdoor>>();

        public List<CompSkipdoor> skipdoors { get { return skipNet.skipdoors; } }
        public Map map { get { return skipNet.map; } }

        public SkipNetCandidateFinder(MapComponent_SkipNet skipNet)
        {
            this.skipNet = skipNet;
            searcher = new SkipNetSearcherDijkstra(this);

            map.events.RegionsRoomsChanged += MarkRegionDoorIndexDirty;
            RebuildRegionDoorIndex();
        }

        private void RebuildRegionDoorIndex()
        {
            if (!regionSkipdoorsDirty) { return; }

            regionSkipdoors.Clear();

            foreach (CompSkipdoor skipdoor in skipdoors)
            {
                if (skipdoor == null || !skipdoor.parent.Spawned) { continue; }
                if (skipdoor.parent?.Map != skipNet.map || !skipdoor.Position.InBounds(map)) { continue; }

                Region region = map.regionGrid.GetValidRegionAt_NoRebuild(skipdoor.Position);
                if (region == null || !region.valid || region.type == RegionType.None) { continue; }

                if (!regionSkipdoors.TryGetValue(region, out List<CompSkipdoor> skipdoorsInRegion))
                {
                    regionSkipdoors[region] = skipdoorsInRegion = new List<CompSkipdoor>();
                }

                skipdoorsInRegion.Add(skipdoor);
            }

            SkiptechUtil.Message("RegionDoorIndex rebuilt.", LogLevel.Verbose);
            regionSkipdoorsDirty = false;
        }

        public void MarkRegionDoorIndexDirty() => regionSkipdoorsDirty = true;
        public bool TryGetSkipdoorsInRegion(Region region, out List<CompSkipdoor> doors)
        {
            return regionSkipdoors.TryGetValue(region, out doors);
        }

        /// <summary>
        /// Performs some initial validation to make sure the plan can be generated, then initializes the
        /// search.
        /// </summary>
        public bool TryGetSearchRegions(Pawn pawn, LocalTargetInfo dest, TraverseParms tp, PawnPath directPath, out Region pawnRegion, out Region destRegion)
        {
            pawnRegion = null;
            destRegion = null;

            // Make sure the pawn is actually in a valid region.
            // Thing.Spawned and Map.InBounds(Thing) are both covered by this.
            pawnRegion = pawn.GetRegion();
            if (pawnRegion == null) { return false; }

            // Are they on the same map?
            if (dest.HasThing && dest.Thing.MapHeld != pawn.Map) { return false; }

            // Get the final cell.
            IntVec3 destCell = directPath.LastNode;
            destRegion = map.regionGrid.GetValidRegionAt_NoRebuild(destCell);

            if (destRegion == null || !destRegion.Allows(tp, isDestination: true)) { return false; }

            RebuildRegionDoorIndex();

            return true;
        }

        /// <summary>
        /// Searches for a skipdoor pair that shortens the plan's trip. Read-only on the plan: the caller decides what to do with the answer.
        /// </summary>
        /// <param name="plan">The plan to search for (pawn, dest and traverse parms are read from it)</param>
        /// <param name="directPath">The pawn's current direct path to the destination</param>
        /// <param name="popCost">Search effort spent (region pops), for the caller's per-tick budget. Zero if the search never started.</param>
        public bool TryFindSkipdoorPair(SkipNetPlan plan, PawnPath directPath, out CompSkipdoor entry, out CompSkipdoor exit, out int popCost)
        {
            Pawn pawn = plan.pawn;
            LocalTargetInfo dest = plan.dest;
            TraverseParms tp = plan.tp;

            entry = null;
            exit = null;
            popCost = 0;

            // Make sure we meet the minimum requirements for a SkipNetPlan.
            if (!TryGetSearchRegions(pawn, dest, tp, directPath, out Region pawnRegion, out Region destRegion)) { return false; }

            SkipNetAccessContext ac = new SkipNetAccessContext(pawn);

            return searcher.TrySearchForSkipdoorPair(pawn, pawnRegion, destRegion, directPath, tp, ac, out entry, out exit, out popCost);
        }
    }
}
