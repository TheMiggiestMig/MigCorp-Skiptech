using MigCorp.Skiptech.Utils;
using System.Collections.Generic;
using Verse.AI;
using Verse;
using RimWorld;
using System;
using MigCorp.Skiptech.SkipNet.Comps;

namespace MigCorp.Skiptech.SkipNet
{
    public class SkipNetPlanner
    {
        public MapComponent_SkipNet skipNet;

        // BFS
        private Deque<Region> openPawnRegions = new Deque<Region>();
        private Deque<Region> openDestRegions = new Deque<Region>();
        private Dictionary<Region, int> closedPawnRegions = new Dictionary<Region, int>();
        private Dictionary<Region, int> closedDestRegions = new Dictionary<Region, int>();
        private Dictionary<Region, List<CompSkipdoor>> regionSkipdoors = new Dictionary<Region, List<CompSkipdoor>>();
        private HashSet<(IntVec3 dest, Region region)> proxyDestinations = new HashSet<(IntVec3 dest, Region region)>();

        public List<CompSkipdoor> skipdoors { get { return skipNet.skipdoors; } }
        public Map map { get { return skipNet.map; } }

        public SkipNetPlanner(MapComponent_SkipNet skipNet)
        {
            this.skipNet = skipNet;

            map.events.RegionsRoomsChanged += RebuildRegionDoorIndex;
            RebuildRegionDoorIndex();
        }

        public void RebuildRegionDoorIndex()
        {
            MigcorpSkiptechMod.Message("RegionDoorIndex rebuilt.", MigcorpSkiptechMod.LogLevel.Verbose);
            regionSkipdoors.Clear();

            foreach (CompSkipdoor skipdoor in skipdoors)
            {
                if (skipdoor == null || !skipdoor.parent.Spawned)
                {
                    continue;
                }

                if (skipdoor.parent?.Map != skipNet.map || !skipdoor.Position.InBounds(map))
                {
                    continue;
                }

                Region region = map.regionGrid.GetValidRegionAt_NoRebuild(skipdoor.Position);
                if (region == null || !region.valid || region.type == RegionType.None)
                {
                    continue;
                }

                if (!regionSkipdoors.TryGetValue(region, out List<CompSkipdoor> skipdoorsInRegion))
                {
                    regionSkipdoors[region] = skipdoorsInRegion = new List<CompSkipdoor>();
                }

                skipdoorsInRegion.Add(skipdoor);
            }
        }

        /// <summary>
        /// Performs some initial validation to make sure the plan can be generated, then initializes the
        /// search.
        /// </summary>
        public bool TryInitializePlanner(Pawn pawn, LocalTargetInfo dest, PathEndMode peMode, TraverseParms tp, out Region pawnReg, out List<Region> destRegs)
        {
            pawnReg = null;
            destRegs = new List<Region>();

            // We can only create a plan if there are actually 2 or more skipdoors present.
            if (skipNet.skipdoors.Count < 2)
            {
                return false;
            }

            // No pawn? No dest? No plan.
            if (pawn?.Map == null || pawn.Map != skipNet.map || dest == null || !dest.IsValid)
            {
                return false;
            }

            // I don't care how advanced the science is: If the pawn can't crawl to their destination, they can't crawl to a skipdoor neither.
            if (pawn.Downed && !pawn.health.CanCrawl)
            {
                return false;
            }

            // Make sure the pawn is actually in a valid region.
            //Thing.Spawned and Map.InBounds(Thing) are both covered by this.
            pawnReg = pawn.GetRegion();
            if (pawnReg == null)
            {
                return false;
            }

            // Are they on the same map?
            if (dest.HasThing && dest.Thing.MapHeld != pawn.Map)
            {
                return false;
            }

            if (!dest.Cell.InBounds(pawn.Map))
            {
                return false;
            }

            // Quick vanilla check. For performance reasons, we only want plans for paths
            // we could reach normally. Only for performance reasons, and not because I
            // can't be arsed fighting CanReach in future >.>
            if (!skipNet.map.reachability.CanReach(pawn.Position, dest, peMode, tp))
            {
                return false;
            }

            // Normalize the dest and peMode, and see what regions we can search from for dest.
            PathEndMode normalizedPeMode = peMode;
            LocalTargetInfo normalizedDest = (LocalTargetInfo)GenPath.ResolvePathMode(pawn, dest.ToTargetInfo(map), ref normalizedPeMode);

            if (normalizedPeMode == PathEndMode.OnCell)
            {
                Region region = map.regionGrid.GetValidRegionAt_NoRebuild(normalizedDest.Cell);
                if (region != null && region.Allows(tp, isDestination: true))
                {
                    destRegs.Add(region);
                }
            }
            // This should fix it for mining, shuttle loading, and pit gates.
            else if (normalizedPeMode == PathEndMode.Touch)
            {
                TouchPathEndModeUtility.AddAllowedAdjacentRegions(normalizedDest, tp, map, destRegs);

                // Clean up any null regions from AddAllowedAdjacentRegions (just in case it hadn't finished rebuilding).
                destRegs.RemoveAll(r => r == null || !r.valid || !r.Allows(tp, isDestination: false));
            }

            if (destRegs.Count == 0)
            {
                return false;
            }

            // Looks good, clear the buffers before searching.
            openPawnRegions.Clear();
            openDestRegions.Clear();
            closedPawnRegions.Clear();
            closedDestRegions.Clear();
            proxyDestinations.Clear();

            return true;
        }

        public bool TryExtractIntVec3Dest(LocalTargetInfo dest, out IntVec3 destCell)
        {
            destCell = IntVec3.Invalid;

            if (dest.Cell.IsValid)
            {
                destCell = dest.Cell;
                return true;
            }

            if (dest.HasThing)
            {
                // Make sure it hasn't been destroyed (shakes fist at despawning filth)
                if (dest.ThingDestroyed)
                    return false;

                // Check if the thing is not spawned and has no owner (not being held or in a container).
                if (!dest.Thing.Spawned && dest.Thing.holdingOwner == null) { return false; }

                destCell = dest.Thing.PositionHeld;
                return true;
            }

            return false;
        }

        public bool TryFilterSettings(Pawn pawn)
        {
            if (MigcorpSkiptechMod.Settings.accessMode == AccessMode.Colonists && pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }
            
            if (MigcorpSkiptechMod.Settings.accessMode != AccessMode.Everyone && pawn.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            if (!MigcorpSkiptechMod.Settings.animalsCanUse && pawn.IsAnimal)
            {
                if (!(pawn.jobs?.curJob?.def == JobDefOf.FollowRoper))
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryFindEligibleSkipNetPlan(Pawn pawn, LocalTargetInfo dest, PathEndMode peMode, out SkipNetPlan plan)
        {
            Pawn DEBUG = (Find.Selector.SingleSelectedThing as Pawn) == pawn ? pawn : null;

            // A blank plan lets us know we at least attempted one.
            plan = new SkipNetPlan(skipNet, pawn, dest, peMode);

            if (!TryFilterSettings(pawn))
            {
                return false;
            }

            // Make sure we meet the minimum requirements for a SkipNetPlan.
            TraverseParms tp = TraverseParms.For(pawn, mode: TraverseMode.ByPawn);

            if (!TryInitializePlanner(pawn, dest, peMode, tp, out Region regPawn, out List<Region> regDests))
            {
                return false;
            }
            LocalTargetInfo originalDest = dest;

            if (!TryExtractIntVec3Dest(originalDest, out IntVec3 destCell))
            {
                return false;
            }
            dest = destCell;

            // Prepare the search from both ends (pawn / dest).
            int entrySkipdoorRange = -1;
            int exitSkipdoorRange = -1;
            int entryRegionCost = int.MaxValue;
            int exitRegionCost = int.MaxValue;
            int estimateDirectRegionCost = -1;
            int linkCost;
            CompSkipdoor bestEntry = null;
            CompSkipdoor bestExit = null;
            int bestEntryHeuristicCost = int.MaxValue;
            int bestExitHeuristicCost = int.MaxValue;
            bool pawnSideReachedDest = false;

            openPawnRegions.AddLast(regPawn); closedPawnRegions[regPawn] = 0;
            foreach (Region regDest in regDests)
            {
                pawnSideReachedDest = pawnSideReachedDest || regPawn == regDest;
                openDestRegions.AddLast(regDest); closedDestRegions[regDest] = 0;
            }

            estimateDirectRegionCost = pawnSideReachedDest ? 0 : estimateDirectRegionCost;

            // Helper function: Scan's a given region for skipdoors the pawn can enter.
            bool TryScanForEntry(Region region, int regionCost)
            {
                if (!regionSkipdoors.TryGetValue(region, out List<CompSkipdoor> candidateSkipDoors)) return false;
                if (!skipNet.TryGetEnterableSkipdoors(pawn, out List<CompSkipdoor> enterable, skipdoorListToFilter: candidateSkipDoors)) return false;
                foreach (CompSkipdoor skipdoor in enterable)
                {
                    int heuristicCost = SkipNetUtils.OctileDistance(pawn.Position, skipdoor.Position);
                    if (heuristicCost < bestEntryHeuristicCost && map.reachability.CanReach(pawn.Position, skipdoor.parent, PathEndMode.OnCell, tp))
                    {
                        bestEntryHeuristicCost = heuristicCost; bestEntry = skipdoor; entryRegionCost = regionCost;
                    }
                }
                return true;
            }

            // Helper function: Scan's a given region for skipdoors the pawn can exit.
            bool TryScanForExit(Region region, int regionCost)
            {
                if (!regionSkipdoors.TryGetValue(region, out List<CompSkipdoor> candidateSkipdoors)) return false;
                if (!skipNet.TryGetExitableSkipdoors(pawn, out List<CompSkipdoor> exitable, skipdoorListToFilter: candidateSkipdoors)) return false;
                foreach (CompSkipdoor skipdoor in exitable)
                {
                    int heuristicCost = SkipNetUtils.OctileDistance(skipdoor.Position, dest.Cell);
                    if (heuristicCost < bestExitHeuristicCost && map.reachability.CanReach(region.AnyCell, skipdoor.parent, PathEndMode.OnCell, tp))
                    {
                        bestExitHeuristicCost = heuristicCost; bestExit = skipdoor; exitRegionCost = regionCost;
                    }
                }
                return true;
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
                        if (TryScanForEntry(region, regionCost) && entrySkipdoorRange == -1)
                        {
                            entrySkipdoorRange = Math.Max(regionCost + 1, 2);
                        }
                    }

                    // Check if we've made contact with the dest search.
                    if (!pawnSideReachedDest)
                    {
                        if (regDests.Contains(region) || closedDestRegions.ContainsKey(region))
                        {
                            // We have a direct path, which is a prerequisite for a SkipNetPlan.
                            pawnSideReachedDest = true;
                            estimateDirectRegionCost = closedDestRegions[region] + regionCost;
                        }
                    }
                    else if(entrySkipdoorRange == -1)
                    {
                        entrySkipdoorRange = Math.Max(regionCost + 1, 2);
                    }


                    // Add the linked regions to the pawn search.
                    foreach (RegionLink link in region.links)
                    {
                        Region nextRegion = link.GetOtherRegion(region);
                        if (nextRegion == null || !nextRegion.valid || closedPawnRegions.ContainsKey(nextRegion) || !nextRegion.Allows(tp, false)) continue;

                        // We don't want pathable doors to cost extra.
                        linkCost = nextRegion.IsDoorway ? 0 : 1;

                        // If we've already established a direct path,
                        // only add regions within our skipdoor search range.
                        if ((pawnSideReachedDest || exitSkipdoorRange != -1) && entrySkipdoorRange != -1 && regionCost > entrySkipdoorRange) continue;

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
                        if (TryScanForExit(region, regionCost) && exitSkipdoorRange == -1)
                        {
                            exitSkipdoorRange = Math.Max(regionCost + 1, 2);
                        }
                    }

                    if (!pawnSideReachedDest)
                    {
                        if (region == regPawn || closedPawnRegions.ContainsKey(region))
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
                        if (nextRegion == null || !nextRegion.valid || closedDestRegions.ContainsKey(nextRegion) || !nextRegion.Allows(tp, false)) continue;

                        linkCost = nextRegion.IsDoorway ? 0 : 1;

                        if ((pawnSideReachedDest || entrySkipdoorRange != -1) && exitSkipdoorRange != -1 && regionCost > exitSkipdoorRange) continue;

                        closedDestRegions[nextRegion] = regionCost + linkCost;
                        if (linkCost == 0) { openDestRegions.AddFirst(nextRegion); }
                        else { openDestRegions.AddLast(nextRegion); }
                    }
                }
            }

            if (bestEntry == null || bestExit == null)
            {
                return false;
            }

            if (bestEntry == bestExit)
            {
                return false;
            }

            // Check if our skipplan is shorter than direct travel by region.
            if (estimateDirectRegionCost != -1)
            {
                int skipplanTotalCost = entryRegionCost + exitRegionCost;
                if (skipplanTotalCost > estimateDirectRegionCost + 2)
                {
                    return false;
                }

                // Same diff, except comparing octile heuristics (in case of tie break).
                int directH = SkipNetUtils.OctileDistance(pawn.Position, dest.Cell);
                int entryH = SkipNetUtils.OctileDistance(pawn.Position, bestEntry.Position);
                int exitH = SkipNetUtils.OctileDistance(bestExit.Position, dest.Cell);

                if (directH <= entryH + exitH)
                {
                   return false;
                }
            }

            plan.Initialize(bestEntry, bestExit);
            return true;
        }
    }
}
