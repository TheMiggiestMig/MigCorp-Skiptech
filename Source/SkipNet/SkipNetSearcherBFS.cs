using MigCorp.Skiptech.SkipNet.Comps;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public class SkipNetSearcherBFS : SkipNetSearcher
    {
        // BFS
        private readonly Deque<Region> openPawnRegions = new Deque<Region>();
        private readonly Deque<Region> openDestRegions = new Deque<Region>();
        private readonly Dictionary<Region, int> closedPawnRegions = new Dictionary<Region, int>();
        private readonly Dictionary<Region, int> closedDestRegions = new Dictionary<Region, int>();
        private readonly Dictionary<CompSkipdoor, SkipdoorAccessRecord> accessCheckedSkipdoors = new Dictionary<CompSkipdoor, SkipdoorAccessRecord>();
        private readonly HashSet<(IntVec3 dest, Region region)> proxyDestinations = new HashSet<(IntVec3 dest, Region region)>();

        private int entrySkipdoorRange;
        private int exitSkipdoorRange;
        private int entryRegionCost;
        private int exitRegionCost;
        private int estimateDirectRegionCost;
        private int linkCost;
        private int bestEntryHeuristicCost;
        private int bestExitHeuristicCost;
        private bool pawnSideReachedDest;

        private struct SkipdoorAccessRecord
        {
            public bool canEnter;
            public bool canExit;
        }
        public SkipNetSearcherBFS(SkipNetPlanner planner) : base(planner) { }

        public override void Reset()
        {
            openPawnRegions.Clear();
            openDestRegions.Clear();
            closedPawnRegions.Clear();
            closedDestRegions.Clear();
            proxyDestinations.Clear();
            accessCheckedSkipdoors.Clear();

            entrySkipdoorRange = -1;
            exitSkipdoorRange = -1;
            entryRegionCost = int.MaxValue;
            exitRegionCost = int.MaxValue;
            estimateDirectRegionCost = -1;
            bestEntryHeuristicCost = int.MaxValue;
            bestExitHeuristicCost = int.MaxValue;
            pawnSideReachedDest = false;
        }

        public override bool TrySearchForSkipdoorPair(Pawn pawn, Region pawnRegion, Region destRegion, PawnPath directPath, TraverseParms tp, SkipNetAccessContext ac, out CompSkipdoor entry, out CompSkipdoor exit, out int popCost)
        {
            entry = null;
            exit = null;
            popCost = 0;

            Reset();
            if (SearchForSkipdoorPair(pawn, pawnRegion, destRegion, directPath, tp, ac, out entry, out exit)) { return true; }

            return false;
        }

        private bool SearchForSkipdoorPair(Pawn pawn, Region pawnRegion, Region destRegion, PawnPath directPath, TraverseParms tp, SkipNetAccessContext ac, out CompSkipdoor entry, out CompSkipdoor exit)
        {
            entry = null;
            exit = null;

            CompSkipdoor bestEntry = null;
            CompSkipdoor bestExit = null;

            openPawnRegions.AddLast(pawnRegion); closedPawnRegions[pawnRegion] = 0;
            openDestRegions.AddLast(destRegion); closedDestRegions[destRegion] = 0;

            estimateDirectRegionCost = pawnSideReachedDest ? 0 : estimateDirectRegionCost;

            // Checks a region for skipdoors it can use, and sets the best if found.
            bool CheckSkipdoorAccess(Region region, int regionCost, bool entering = true)
            {
                if (!planner.TryGetSkipdoorsInRegion(region, out List<CompSkipdoor> candidateSkipdoors)) { return false; }

                bool usableSkipdoorFound = false;
                IntVec3 targetCell = entering ? pawn.Position : directPath.LastNode;
                int currentBestHeuristic = entering ? bestEntryHeuristicCost : bestExitHeuristicCost;

                foreach (CompSkipdoor skipdoor in candidateSkipdoors)
                {
                    if (!accessCheckedSkipdoors.TryGetValue(skipdoor, out SkipdoorAccessRecord accessRecord))
                    {
                        accessRecord = new SkipdoorAccessRecord();
                        skipdoor.IsUsableBy(ac, out accessRecord.canEnter, out accessRecord.canExit);
                    }

                    if (entering ? !accessRecord.canEnter : !accessRecord.canExit) { continue; }

                    int heuristicCost = SkipNetUtils.OctileDistance(skipdoor.Position, targetCell);
                    if (heuristicCost < currentBestHeuristic &&
                        skipNet.map.reachability.CanReach(targetCell, skipdoor.parent, PathEndMode.OnCell, tp))
                    {
                        if (entering)
                        {
                            bestEntryHeuristicCost = heuristicCost;
                            bestEntry = skipdoor;
                            entryRegionCost = regionCost;
                        }
                        else
                        {
                            bestExitHeuristicCost = heuristicCost;
                            bestExit = skipdoor;
                            exitRegionCost = regionCost;
                        }

                        usableSkipdoorFound = true;
                    }
                }

                return usableSkipdoorFound;
            }


            // We're gonna do 2 BFS searches at the same time; one from the pawn for the entry, and one from the destination for the exit.
            // (note to self: turns out this is called a 'bi-directional BFS', I learned something new!)

            // Early exit conditions: entry + exit found before both searches overlap, or both searches overlap before entry + exit is found.
            while (openPawnRegions.Count > 0 || openDestRegions.Count > 0)
            {
                // Expand pawn search
                if (openPawnRegions.Count > 0)
                {
                    Region region = openPawnRegions.PopFirst();
                    int regionCost = closedPawnRegions[region];

                    // Search for an entry skipdoor if we either haven't discovered one,
                    // or are still within the search range for them.
                    if (entrySkipdoorRange == -1 || regionCost <= entrySkipdoorRange)
                    {
                        if (CheckSkipdoorAccess(region, regionCost, entering: true) && entrySkipdoorRange == -1)
                        {
                            entrySkipdoorRange = Math.Max(regionCost + 1, 2);
                        }
                    }

                    // Check if we've made contact with the dest search.
                    if (!pawnSideReachedDest)
                    {
                        if (region == destRegion || closedDestRegions.ContainsKey(region))
                        {
                            // We have a direct path, which is a prerequisite for a SkipNetPlan.
                            pawnSideReachedDest = true;
                            estimateDirectRegionCost = closedDestRegions[region] + regionCost;
                        }
                    }
                    else if (entrySkipdoorRange == -1)
                    {
                        entrySkipdoorRange = Math.Max(regionCost + 1, 2);
                    }


                    // Add the linked regions to the pawn search.
                    foreach (RegionLink link in region.links)
                    {
                        Region nextRegion = link.GetOtherRegion(region);
                        if (nextRegion == null || !nextRegion.valid || closedPawnRegions.ContainsKey(nextRegion) || !nextRegion.Allows(tp, false)) { continue; }

                        // We don't want pathable doors to cost extra.
                        linkCost = nextRegion.IsDoorway ? 0 : 1;

                        // If we've already established a direct path,
                        // only add regions within our skipdoor search range.
                        if ((pawnSideReachedDest || exitSkipdoorRange != -1) && entrySkipdoorRange != -1 && regionCost > entrySkipdoorRange) { continue; }

                        closedPawnRegions[nextRegion] = regionCost + linkCost;
                        if (linkCost == 0) { openPawnRegions.AddFirst(nextRegion); }
                        else { openPawnRegions.AddLast(nextRegion); }
                    }
                }

                // Expand destination search (same thing as the pawn search)
                if (openDestRegions.Count > 0)
                {
                    Region region = openDestRegions.PopFirst();
                    int regionCost = closedDestRegions[region];

                    if (exitSkipdoorRange == -1 || regionCost <= exitSkipdoorRange)
                    {
                        if (CheckSkipdoorAccess(region, regionCost, entering: false) && exitSkipdoorRange == -1)
                        {
                            exitSkipdoorRange = Math.Max(regionCost + 1, 2);
                        }
                    }

                    if (!pawnSideReachedDest)
                    {
                        if (region == pawnRegion || closedPawnRegions.ContainsKey(region))
                        {
                            pawnSideReachedDest = true;
                            estimateDirectRegionCost = closedPawnRegions[region] + regionCost;
                        }
                    }
                    else if (exitSkipdoorRange == -1)
                    {
                        exitSkipdoorRange = Math.Max(regionCost + 1, 2);
                    }

                    foreach (RegionLink link in region.links)
                    {
                        Region nextRegion = link.GetOtherRegion(region);
                        if (nextRegion == null || !nextRegion.valid || closedDestRegions.ContainsKey(nextRegion) || !nextRegion.Allows(tp, false)) { continue; }

                        linkCost = nextRegion.IsDoorway ? 0 : 1;

                        if ((pawnSideReachedDest || entrySkipdoorRange != -1) && exitSkipdoorRange != -1 && regionCost > exitSkipdoorRange) { continue; }

                        closedDestRegions[nextRegion] = regionCost + linkCost;
                        if (linkCost == 0) { openDestRegions.AddFirst(nextRegion); }
                        else { openDestRegions.AddLast(nextRegion); }
                    }
                }
            }

            entry = bestEntry;
            exit = bestExit;

            if (entry == null || exit == null) { return false; }
            if (entry == exit) { return false; }

            // Check if our skipplan is shorter than direct travel by region.
            if (estimateDirectRegionCost != -1)
            {
                int skipplanTotalCost = entryRegionCost + exitRegionCost;
                if (skipplanTotalCost > estimateDirectRegionCost + 2)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
