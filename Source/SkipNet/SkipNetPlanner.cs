using MigCorp.Skiptech.SkipNet.Comps;
using MigCorp.Skiptech.Utils;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public class SkipNetPlanner
    {
        public readonly MapComponent_SkipNet skipNet;
        private readonly SkipNetSearcher searcher;

        // Region --> Skipdoor mapping
        private bool regionSkipdoorsDirty = true;
        private readonly Dictionary<Region, List<CompSkipdoor>> regionSkipdoors = new Dictionary<Region, List<CompSkipdoor>>();

        public List<CompSkipdoor> skipdoors { get { return skipNet.skipdoors; } }
        public Map map { get { return skipNet.map; } }

        public SkipNetPlanner(MapComponent_SkipNet skipNet)
        {
            this.skipNet = skipNet;
            searcher = new SkipNetSearcherDijkstra(this);

            map.events.RegionsRoomsChanged += MarkRegionDoorIndexDirty;
            RebuildRegionDoorIndex();
        }

        public void Run()
        {
            RebuildRegionDoorIndex();
        }

        /// <summary>
        /// Rebuilds the Region --> Skipdoor lookup if marked as dirty.
        /// </summary>
        /// <remarks>
        /// The regionSkipdoors lookup is for quickly identifying which skipdoors are in a given region.
        /// </remarks>
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
        public bool TryInitializePlanner(Pawn pawn, LocalTargetInfo dest, TraverseParms tp, PawnPath directPath, out Region pawnRegion, out Region destRegion)
        {
            pawnRegion = null;
            destRegion = null;

            // We can only create a plan if there are actually 2 or more skipdoors present.
            if (skipNet.skipdoors.Count < 2) { return false; }

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
        /// Performs a search for a skipdoor pairing that reduces the pawn's current trip.
        /// </summary>
        /// <param name="proposal">The SkipNetProposal to convert into a SkipNetPlan</param>
        /// <param name="directPath">The pawn's current direct path to the destination</param>
        /// <returns></returns>
        public bool TryFindEligibleSkipNetPlan(SkipNetProposal proposal, PawnPath directPath, out SkipNetPlan plan)
        {
            Pawn pawn = proposal.pawn;
            LocalTargetInfo dest = proposal.dest;
            PathEndMode peMode = proposal.peMode;
            TraverseParms tp = proposal.tp;

            plan = null;

            // Make sure we meet the minimum requirements for a SkipNetPlan.
            if (!TryInitializePlanner(pawn, dest, tp, directPath, out Region pawnRegion, out Region destRegion)) { return false; }

            SkipNetAccessContext ac = new SkipNetAccessContext(pawn);

            bool found = searcher.TrySearchForSkipdoorPair(pawn, pawnRegion, destRegion, directPath, tp, ac, out CompSkipdoor entry, out CompSkipdoor exit, out int popCost);

            if (found)
            {
                plan = new SkipNetPlan(pawn, dest, peMode, tp);
                plan.Initialize(entry, exit);
            }

            skipNet.proposer.ConsumePopBudget(popCost);

            return found;
        }
    }
}
