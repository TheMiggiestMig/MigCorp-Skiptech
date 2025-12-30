using MigCorp.Skiptech.Comps;
using MigCorp.Skiptech.Utils;
using MigCorp.Skiptech;
using System.Collections.Generic;
using Verse.AI;
using Verse;
using RimWorld;
using System;

namespace MigCorp.Skiptech.Systems.SkipNet
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
        int lastRegionDoorIndexRebuildTick;

        public List<CompSkipdoor> skipdoors { get { return skipNet.skipdoors; } }
        public Map map { get { return skipNet.map; } }

        public SkipNetPlanner(MapComponent_SkipNet skipNet)
        {
            this.skipNet = skipNet;
        }

        void RebuildRegionDoorIndex(bool force = false)
        {
            int curGameTicks = GenTicks.TicksGame;
            if (lastRegionDoorIndexRebuildTick == curGameTicks && !force) { return; }

            lastRegionDoorIndexRebuildTick = curGameTicks;
            regionSkipdoors.Clear();

            foreach (CompSkipdoor skipdoor in skipdoors)
            {
                if (skipdoor == null || skipdoor.parent?.Map != skipNet.map) continue;

                Region region = skipdoor.Position.GetRegion(skipNet.map);
                if (region == null || !region.valid) continue;

                if (!regionSkipdoors.TryGetValue(region, out var set))
                    regionSkipdoors[region] = set = new List<CompSkipdoor>();

                set.Add(skipdoor);
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

            // MigcorpSkiptechMod.Message($"{pawn?.Label} looking: peMode={peMode} + dest.HasThing={dest.HasThing} + dest.Thing.InteractionCell={dest.Thing.InteractionCell} + dest.Cell={dest.Cell}", MigcorpSkiptechMod.LogLevel.Verbose);

            // No pawn? No dest? No plan.
            if (pawn?.Map == null || pawn.Map != skipNet.map || dest == null || !dest.IsValid)
            {
                MigcorpSkiptechMod.Warning($"{pawn?.Label} is not on the map, or dest({dest}) is not valid.",
                    MigcorpSkiptechMod.LogLevel.Verbose);
                return false;
            }
            if(!pawn.Position.InBounds(skipNet.map))
            {
                MigcorpSkiptechMod.Warning($"{pawn?.Label} is not in bounds on the map.",
                    MigcorpSkiptechMod.LogLevel.Verbose);
            }
            pawnReg = map.regionGrid.GetValidRegionAt_NoRebuild(pawn.Position);

            // Are they on the same map?
            if (dest.HasThing && dest.Thing.MapHeld != pawn.Map)
            {
                // The thing might be in a container. If not, it's not valid.
                MigcorpSkiptechMod.Warning($"{pawn?.Label} and dest({dest}) are not on the same map.",
                    MigcorpSkiptechMod.LogLevel.Verbose);
                return false;
            }

            if (!dest.Cell.InBounds(pawn.Map))
            {
                MigcorpSkiptechMod.Warning($"{pawn?.Label}'s plan shows dest({dest}) is out of bounds.",
                    MigcorpSkiptechMod.LogLevel.Verbose);
                return false;
            }

            // Quick vanilla check. For performance reasons, we only want plans for paths
            // we could reach normally. Only for performance reasons, and not because I
            // can't be arsed fighting CanReach in future >.>
            if (!skipNet.map.reachability.CanReach(pawn.Position, dest, peMode, tp))
            {

                MigcorpSkiptechMod.Message($"{pawn?.Label} cannot reach the destination on foot.",
                    MigcorpSkiptechMod.LogLevel.Verbose);
                return false;
            }

            // Normalize the dest and peMode, and see what regions we can search from for dest.
            PathEndMode normalizedPeMode = peMode;
            LocalTargetInfo normalizedDest = (LocalTargetInfo)GenPath.ResolvePathMode(pawn, dest.ToTargetInfo(map), ref normalizedPeMode);

            MigcorpSkiptechMod.Message($"{pawn?.Label}'s plan has normalized PeMode={normalizedPeMode}, dest={dest}, normalizedDest={normalizedDest}.",
                    MigcorpSkiptechMod.LogLevel.Verbose);
            if (normalizedPeMode == PathEndMode.OnCell)
            {
                MigcorpSkiptechMod.Message($"{pawn?.Label}'s plan involves PathEndMode.OnCell at {normalizedDest}).",
                    MigcorpSkiptechMod.LogLevel.Verbose);
                Region region = map.regionGrid.GetValidRegionAt_NoRebuild(normalizedDest.Cell);
                if (region != null && region.Allows(tp, isDestination: true))
                {
                    destRegs.Add(region);
                }
            }
            // This should fix it for mining, shuttle loading, and pit gates.
            else if(normalizedPeMode ==  PathEndMode.Touch)
            {
                MigcorpSkiptechMod.Message($"{pawn?.Label}'s plan involves PathEndMode.Touch at {normalizedDest}).",
                    MigcorpSkiptechMod.LogLevel.Verbose);
                TouchPathEndModeUtility.AddAllowedAdjacentRegions(normalizedDest, tp, map, destRegs);
            }

            if (destRegs.Count == 0)
            {
                MigcorpSkiptechMod.Warning($"{pawn?.Label}'s plan could not find a valid region for dest({dest}).",
                    MigcorpSkiptechMod.LogLevel.Verbose);
                return false;
            }

            // Looks good, clear the buffers before searching.
            RebuildRegionDoorIndex();
            openPawnRegions.Clear();
            openDestRegions.Clear();
            closedPawnRegions.Clear();
            closedDestRegions.Clear();
            proxyDestinations.Clear();

            return true;
        }

        public bool TryExtractIntVec3Dest(LocalTargetInfo dest, out IntVec3 proxyDest)
        {
            proxyDest = IntVec3.Invalid;

            if (dest.Cell.IsValid)
            {
                proxyDest = dest.Cell;
                return true;
            }
            
            if(dest.HasThing)
            {
                // Make sure it hasn't been destroyed (shakes fist at despawning filth)
                if (dest.Thing == null || dest.Thing.Destroyed)
                    return false;

                // Check if the thing is not spawned and has no owner (not being held or in a container).
                if (!dest.Thing.Spawned && dest.Thing.holdingOwner == null) { return false; }

                proxyDest = dest.Thing.PositionHeld;
                return true;
            }

            return false;
        }

        public bool TryFilterSettings(Pawn pawn)
        {
            if (pawn.Faction != null)
            {
                if (MigcorpSkiptechMod.Settings.accessMode == AccessMode.Colonists && pawn.Faction != Faction.OfPlayer) { return false; }
                if (MigcorpSkiptechMod.Settings.accessMode != AccessMode.Everyone && pawn.Faction.HostileTo(Faction.OfPlayer)) { return false; }
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
            // A blank plan lets us know we at least attempted one.
            plan = new SkipNetPlan(skipNet, pawn);

            if(!TryFilterSettings(pawn)) { return false; }

            // Make sure we meet the minimum requirements for a SkipNetPlan.
            TraverseParms tp = TraverseParms.For(pawn, mode: TraverseMode.ByPawn);

            if (!TryInitializePlanner(pawn, dest, peMode, tp, out Region regPawn, out List<Region> regDests)) { return false; }
            LocalTargetInfo originalDest = dest;

            if(!TryExtractIntVec3Dest(originalDest, out IntVec3 proxyDest)) { return false; }
            dest = proxyDest;

            // Prepare the search from both ends (pawn / dest).
            int entrySkipdoorRange = -1;
            int exitSkipdoorRange = -1;
            int bestEntryG = int.MaxValue;
            int bestExitG = int.MaxValue;
            int estimateTotalG = 0;
            int linkCost;
            CompSkipdoor bestEntry = null;
            CompSkipdoor bestExit = null;
            int bestEntryH = int.MaxValue;
            int bestExitH = int.MaxValue;
            bool pawnSideReachedDest = false;

            openPawnRegions.AddLast(regPawn); closedPawnRegions[regPawn] = 0;
            foreach (Region regDest in regDests)
            {
                pawnSideReachedDest = (regPawn == regDest || pawnSideReachedDest) ? true : false;
                openDestRegions.AddLast(regDest); closedDestRegions[regDest] = 0;
            }

            // We're good to go.
            MigcorpSkiptechMod.Message($"{pawn.Label} is trying to find a skipNet path. (position={pawn.PositionHeld}, dest={dest.Cell}, pawnRegion={regPawn}, destRegions={regDests.ToStringSafeEnumerable()})",
                MigcorpSkiptechMod.LogLevel.Verbose);

            // Helper function: Scan's a given region for skipdoors the pawn can enter.
            bool TryScanForEntry(Region region, int gCost)
            {
                if (!regionSkipdoors.TryGetValue(region, out List<CompSkipdoor> set)) return false;
                if (!skipNet.TryGetEnterableSkipdoors(pawn, out List<CompSkipdoor> enterable, skipdoorListToFilter: new List<CompSkipdoor>(set))) return false;
                foreach (CompSkipdoor skipdoor in enterable)
                {
                    string DEBUG_VERBOSE = $"Considering entry={skipdoor}, g={gCost}, maxG={entrySkipdoorRange}";
                    int h = SkipNetUtils.OctileDistance(pawn.Position, skipdoor.Position);
                    if (h < bestEntryH && map.reachability.CanReach(pawn.Position, skipdoor.parent, PathEndMode.OnCell, tp))
                    {
                        bestEntryH = h; bestEntry = skipdoor; bestEntryG = gCost;
                        DEBUG_VERBOSE += ", best so far!";
                    }
                    MigcorpSkiptechMod.Message(DEBUG_VERBOSE, MigcorpSkiptechMod.LogLevel.Verbose);
                }
                return true;
            }

            // Helper function: Scan's a given region for skipdoors the pawn can exit.
            bool TryScanForExit(Region region, int gCost)
            {
                if (!regionSkipdoors.TryGetValue(region, out List<CompSkipdoor> set)) return false;
                if (!skipNet.TryGetExitableSkipdoors(pawn, out List<CompSkipdoor> exitable, skipdoorListToFilter: new List<CompSkipdoor>(set))) return false;
                foreach (CompSkipdoor skipdoor in exitable)
                {
                    string DEBUG_VERBOSE = $"Considering exit={skipdoor}, g={gCost}, maxG={exitSkipdoorRange}";
                    int h = SkipNetUtils.OctileDistance(skipdoor.Position, dest.Cell);
                    if (h < bestExitH && map.reachability.CanReach(region.AnyCell, skipdoor.parent, PathEndMode.OnCell, tp))
                    {
                        bestExitH = h; bestExit = skipdoor; bestExitG = gCost;
                        DEBUG_VERBOSE += ", best so far!";
                    }
                    MigcorpSkiptechMod.Message(DEBUG_VERBOSE, MigcorpSkiptechMod.LogLevel.Verbose);
                }
                return true;
            }

            // The actual BFS search.
            while ((openPawnRegions.Count > 0 || openDestRegions.Count > 0))
            {
                // Expand pawn search
                if (openPawnRegions.Count > 0)
                {
                    Region region = openPawnRegions.PopFirst();
                    int g = closedPawnRegions[region];

                    // Search for an entry skipdoor if we either haven't discovered one,
                    // or are still within the search range for them.
                    if (entrySkipdoorRange == -1 || g <= entrySkipdoorRange)
                    {
                        if (TryScanForEntry(region, g) && entrySkipdoorRange == -1)
                        {
                            entrySkipdoorRange = Math.Max(g + 1, 2);
                            MigcorpSkiptechMod.Message($"Setting entrySkipdoorRange={entrySkipdoorRange}",
                                MigcorpSkiptechMod.LogLevel.Verbose);
                        }
                    }

                    // Check if we've made contact with the dest search.
                    if (!pawnSideReachedDest && (regDests.Contains(region) || closedDestRegions.ContainsKey(region)))
                    {
                        // We have a direct path, which is a prerequisite for a SkipNetPlan.
                        pawnSideReachedDest = true;
                        estimateTotalG = closedDestRegions[region] + g;
                    }

                    // Add the linked regions to the pawn search.
                    foreach (RegionLink link in region.links)
                    {
                        Region nextRegion = link.GetOtherRegion(region);
                        if (nextRegion == null || !nextRegion.valid || closedPawnRegions.ContainsKey(nextRegion) || !nextRegion.Allows(tp, false)) continue;

                        // We don't want pathable doors to cost extra.
                        linkCost = ((nextRegion.IsDoorway) ? 0 : 1);

                        // If we've already established a direct path,
                        // only add regions within our skipdoor search range.
                        if (pawnSideReachedDest && entrySkipdoorRange != -1 && g > entrySkipdoorRange) continue;

                        closedPawnRegions[nextRegion] = g + linkCost;
                        if (linkCost == 0) { openPawnRegions.AddFirst(nextRegion); }
                        else { openPawnRegions.AddLast(nextRegion); }
                    }
                }

                // Expand destination search (same thing as the pawn search)
                for (int i = 0; i < regDests.Count; i++)
                {
                    if (openDestRegions.Count > 0)
                    {
                        Region region = openDestRegions.PopFirst();
                        int g = closedDestRegions[region];

                        if (exitSkipdoorRange == -1 || g <= exitSkipdoorRange)
                        {
                            if (TryScanForExit(region, g) && exitSkipdoorRange == -1)
                            {
                                exitSkipdoorRange = Math.Max(g + 1, 2);
                                MigcorpSkiptechMod.Message($"Setting exitSkipdoorRange={exitSkipdoorRange}",
                                    MigcorpSkiptechMod.LogLevel.Verbose);
                            }
                        }

                        if (!pawnSideReachedDest && (region == regPawn || closedPawnRegions.ContainsKey(region)))
                        {
                            pawnSideReachedDest = true;
                            estimateTotalG = closedPawnRegions[region] + g;
                        }

                        foreach (RegionLink link in region.links)
                        {
                            Region nextRegion = link.GetOtherRegion(region);
                            if (nextRegion == null || !nextRegion.valid || closedDestRegions.ContainsKey(nextRegion) || !nextRegion.Allows(tp, false)) continue;

                            linkCost = ((nextRegion.IsDoorway) ? 0 : 1);

                            if (pawnSideReachedDest && exitSkipdoorRange != -1 && g > exitSkipdoorRange) continue;

                            closedDestRegions[nextRegion] = g + linkCost;
                            if (linkCost == 0) { openDestRegions.AddFirst(nextRegion); }
                            else { openDestRegions.AddLast(nextRegion); }
                        }
                    }
                }
            }

            if (bestEntry == null || bestExit == null)
            {
                MigcorpSkiptechMod.Message($"{pawn.Label} couldn't find a valid pair of skipdoors.",
                    MigcorpSkiptechMod.LogLevel.Verbose);
                return false;
            }

            if (bestEntry == bestExit)
            {
                MigcorpSkiptechMod.Message($"{pawn.Label} best pair was the same skipdoor ({bestEntry}); skipping skipNet plan.",
                    MigcorpSkiptechMod.LogLevel.Verbose);
                return false;
            }

            // Check if our skipplan is shorter than direct travel by region.
            int skipPlanG = bestEntryG + bestExitG;
            if (skipPlanG > estimateTotalG)
            {
                MigcorpSkiptechMod.Message($"{pawn.Label} found direct path faster than skipNet. " +
                    $"entry={bestEntryG} {bestEntry}, " +
                    $"exit={bestExitG} {bestExit}, " +
                    $"actual={estimateTotalG}",
                    MigcorpSkiptechMod.LogLevel.Verbose);
                return false;
            }

            // Same diff, except comparing octile heuristics (in case of tie break).
            if (skipPlanG == estimateTotalG)
            {
                int directH = SkipNetUtils.OctileDistance(pawn.Position, dest.Cell);
                int entryH = SkipNetUtils.OctileDistance(pawn.Position, bestEntry.Position);
                int exitH = SkipNetUtils.OctileDistance(bestExit.Position, dest.Cell);

                if (directH <= entryH)
                {
                    MigcorpSkiptechMod.Message($"{pawn.Label} found direct path faster than skipNet (just). " +
                        $"entry={bestEntryG} {bestEntry}, " +
                        $"exit={bestExitG} {bestExit}, " +
                        $"actual={estimateTotalG}",
                        MigcorpSkiptechMod.LogLevel.Verbose);
                    return false;
                }
            }

            plan = new SkipNetPlan(skipNet, pawn, bestEntry, bestExit, originalDest, peMode);
            MigcorpSkiptechMod.Message($"{pawn.Label} created a SkipNetPlan using {bestEntry} and {bestExit}.",
                MigcorpSkiptechMod.LogLevel.Verbose);
            return true;
        }
    }
}
