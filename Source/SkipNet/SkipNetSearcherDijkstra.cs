using MigCorp.Skiptech.SkipNet.Comps;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public class SkipNetSearcherDijkstra : SkipNetSearcher
    {
        private static float skipCost => MigcorpSkiptechMod.Settings.skipCost; // TODO Make Mod Settinsg for it.
        private static float worthItFactor => MigcorpSkiptechMod.Settings.worthItFactor; // TODO Make Mod Settinsg for it.
        //private static float skipCost => 60;
        //private static float worthItFactor => 0.85f;

        // Stores info about a skipdoor being considered by the search.
        // Access checks can be fairly expensive.
        private struct CandidateSkipdoor
        {
            public CompSkipdoor skipdoor;
            public bool canEnter, canExit;
            public bool accessResolved;
            public float entryLowerBound, exitLowerBound; // Naive octile lower bounds.
        }

        // Tracks the two cheapest skipdoors offered to it.
        // Two, because if the same door tops both sides, the legal pair has to route through a runner-up.
        private struct BestTwoSkipdoors
        {
            public CompSkipdoor firstSkipdoor, secondSkipdoor;
            public float firstCost, secondCost;

            public static BestTwoSkipdoors Empty
            {
                get
                {
                    BestTwoSkipdoors bestTwo;
                    bestTwo.firstSkipdoor = null; bestTwo.secondSkipdoor = null;
                    bestTwo.firstCost = float.MaxValue; bestTwo.secondCost = float.MaxValue;
                    return bestTwo;
                }
            }

            // Check the skipdoor against the current best(s), and keep the best 2 out of all of them (while ranking them).
            public void Try(CompSkipdoor skipdoor, float cost)
            {
                if (skipdoor == firstSkipdoor)
                {
                    firstCost = Mathf.Min(firstCost, cost);
                    return;
                }

                if (skipdoor == secondSkipdoor) { secondCost = Mathf.Min(secondCost, cost); }
                else if (cost < secondCost) { secondSkipdoor = skipdoor; secondCost = cost; }

                // Second place may have just dethroned first.
                if (secondCost < firstCost)
                {
                    CompSkipdoor tmpSkipdoor = firstSkipdoor; firstSkipdoor = secondSkipdoor; secondSkipdoor = tmpSkipdoor;
                    float tmpCost = firstCost; firstCost = secondCost; secondCost = tmpCost;
                }
            }

            // Cheapest entry + exit combo that uses two DIFFERENT skipdoors.
            public static bool TryBestPair(in BestTwoSkipdoors entries, in BestTwoSkipdoors exits,
                               out CompSkipdoor entry, out CompSkipdoor exit, out float cost)
            {
                entry = null; exit = null; cost = float.MaxValue;
                if (entries.firstSkipdoor == null || exits.firstSkipdoor == null) { return false; }

                if (entries.firstSkipdoor != exits.firstSkipdoor)
                {
                    entry = entries.firstSkipdoor; exit = exits.firstSkipdoor;
                    cost = entries.firstCost + exits.firstCost;
                    return true;
                }

                // The same skipdoor tops both sides. Check the secondaries for a path that still works.
                float viaSecondExit = exits.secondSkipdoor != null ? entries.firstCost + exits.secondCost : float.MaxValue;
                float viaSecondEntry = entries.secondSkipdoor != null ? entries.secondCost + exits.firstCost : float.MaxValue;
                if (viaSecondExit == float.MaxValue && viaSecondEntry == float.MaxValue) { return false; }

                if (viaSecondExit <= viaSecondEntry) { entry = entries.firstSkipdoor; exit = exits.secondSkipdoor; cost = viaSecondExit; }
                else { entry = entries.secondSkipdoor; exit = exits.firstSkipdoor; cost = viaSecondEntry; }
                return true;
            }
        }

        // Candidate skipdoors worth checking this search, and a fast door -> index lookup for scoring.
        private CandidateSkipdoor[] candidates = new CandidateSkipdoor[16];
        private int candidateCount;
        private readonly Dictionary<CompSkipdoor, int> candidateIndex = new Dictionary<CompSkipdoor, int>();

        // One search out from the pawn (entries), one out from the destination (exits).
        private readonly DijkstraSearchStepper pawnStepper = new DijkstraSearchStepper();
        private readonly DijkstraSearchStepper destStepper = new DijkstraSearchStepper();
        private readonly Action<Region, IntVec3, float> scoreEntryDoors;
        private readonly Action<Region, IntVec3, float> scoreExitDoors;

        private BestTwoSkipdoors bestEntries, bestExits;

        private float maxRouteCost; // (directPath * worthItFactor - skipCost): if pawn->entry + exit->dest costs more than this, skipping isn't worth it.
        private float entryCostFloor, exitCostFloor;
        private SkipNetAccessContext ac;

        public SkipNetSearcherDijkstra(SkipNetPlanner planner) : base(planner)
        {
            scoreEntryDoors = (region, from, g) => ScoreDoorsInRegion(region, from, g, entrySide: true);
            scoreExitDoors = (region, from, g) => ScoreDoorsInRegion(region, from, g, entrySide: false);
        }
        public override bool TrySearchForSkipdoorPair(Pawn pawn, Region pawnRegion, Region destRegion, PawnPath directPath, TraverseParms tp, SkipNetAccessContext ac, out CompSkipdoor entrySkipdoor, out CompSkipdoor exitSkipdoor, out int popCost)
        {
            bool found = SearchForSkipdoorPair(pawn, pawnRegion, destRegion, directPath, tp, ac, out entrySkipdoor, out exitSkipdoor);
            popCost = pawnStepper.PopCount + destStepper.PopCount;
            return found;
        }

        private bool SearchForSkipdoorPair(Pawn pawn, Region pawnRegion, Region destRegion, PawnPath directPath, TraverseParms tp, SkipNetAccessContext ac, out CompSkipdoor entry, out CompSkipdoor exit)
        {
            entry = null;
            exit = null;

            Reset();

            // Is the route too short to bother? Easy bail.
            maxRouteCost = EstimateDirectPathCost(directPath) * worthItFactor - skipCost;
            if (maxRouteCost <= 0f) { return false; }

            this.ac = ac;
            IntVec3 destCell = directPath.LastNode;

            // If none of the candidates can produce a shorter pair, bail.
            if (!TryBuildCandidates(pawn.Position, destCell)) { return false; }

            //ScoreDoorsInRegion(pawnRegion, pawn.Position, 0f, entrySide: true);
            //ScoreDoorsInRegion(destRegion, destCell, 0f, entrySide: false);
            pawnStepper.Initialize(pawnRegion, pawn.Position, maxRouteCost - exitCostFloor, tp, scoreEntryDoors);
            destStepper.Initialize(destRegion, destCell, maxRouteCost - entryCostFloor, tp, scoreExitDoors);


            while (true)
            {
                GetSideCostLimit(out float entryCostLimit, out float exitCostLimit);

                bool entryDone = pawnStepper.CheapestCost >= entryCostLimit;
                bool exitDone = destStepper.CheapestCost >= exitCostLimit;
                if (entryDone && exitDone) { break; }

                // Bail... even the most optimistic pair can't beat walking.
                float entryLowerBound = Mathf.Max(entryCostFloor, Mathf.Min(bestEntries.firstCost, pawnStepper.CheapestCost));
                float exitLowerBound = Mathf.Max(exitCostFloor, Mathf.Min(bestExits.firstCost, destStepper.CheapestCost));
                if (entryLowerBound + exitLowerBound >= maxRouteCost) { return false; }

                // Step the lowest costing stepper (pawn vs dest).
                if (!entryDone && (exitDone || pawnStepper.CheapestCost <= destStepper.CheapestCost))
                {
                    pawnStepper.StepOnce();
                }
                else
                {
                    destStepper.StepOnce();
                }
            }

            // Take the cheapest pair that uses two different doors (if there even is one).
            // Also, make sure the detour is actually worth it.
            return BestTwoSkipdoors.TryBestPair(in bestEntries, in bestExits, out entry, out exit, out float routeCost)
                && routeCost < maxRouteCost;
        }

        // The frontier cost at which each side stops searching. Past its limit, nothing a side
        // finds can improve the best legal pair anymore.
        private void GetSideCostLimit(out float entryCostLimit, out float exitCostLimit)
        {
            bool sameDoorTopsBoth = bestEntries.firstSkipdoor != null && bestEntries.firstSkipdoor == bestExits.firstSkipdoor;

            if (!sameDoorTopsBoth)
            {
                entryCostLimit = bestEntries.firstCost;
                exitCostLimit = bestExits.firstCost;
                return;
            }

            // Both sides have the same best skipdoor, and just one door is not a shortcut.
            // Keep digging for each side's runner-up, but no deeper than the budget left over after the other side's best.
            entryCostLimit = Mathf.Min(bestEntries.secondCost, maxRouteCost - bestExits.firstCost);
            exitCostLimit = Mathf.Min(bestExits.secondCost, maxRouteCost - bestEntries.firstCost);
        }
        public override void Reset()
        {
            candidateCount = 0;
            candidateIndex.Clear();
            bestEntries = BestTwoSkipdoors.Empty;
            bestExits = BestTwoSkipdoors.Empty;
            pawnStepper.Reset();
            destStepper.Reset();
        }

        // Estimate the walking cost of the remaining direct path.
        // directPath.TotalCost also bakes in avoid grid and allowed area penalties... no good for this estimate.
        private float EstimateDirectPathCost(PawnPath directPath)
        {
            // Work backwards.
            List<IntVec3> nodes = directPath.NodesReversed;
            int cells = directPath.NodesLeftCount;

            float cost = 0f;
            for (int i = 1; i < cells; i++)
            {
                IntVec3 delta = nodes[i] - nodes[i - 1];
                cost += (delta.x != 0 && delta.z != 0) ? 14 : 10;
            }
            return cost;
        }

        // Builds the list of skipdoors worth considering, and proves the search is worth starting at all.
        private bool TryBuildCandidates(IntVec3 pawnPos, IntVec3 destCell)
        {
            BestTwoSkipdoors optimisticEntries = BestTwoSkipdoors.Empty;
            BestTwoSkipdoors optimisticExits = BestTwoSkipdoors.Empty;

            // Check the optimistic (direct octile) distance from every skipdoor on the map.
            foreach (CompSkipdoor door in planner.skipdoors)
            {
                if (door == null || !door.parent.Spawned || door.parent.Map != planner.map) { continue; }

                if (candidateCount == candidates.Length) { Array.Resize(ref candidates, candidates.Length * 2); }
                ref CandidateSkipdoor candidate = ref candidates[candidateCount++];
                candidate.skipdoor = door;
                candidate.canEnter = true;
                candidate.canExit = true;
                candidate.accessResolved = false;
                candidate.entryLowerBound = SkipNetUtils.OctileDistance(pawnPos, door.Position);
                candidate.exitLowerBound = SkipNetUtils.OctileDistance(destCell, door.Position);

                optimisticEntries.Try(door, candidate.entryLowerBound);
                optimisticExits.Try(door, candidate.exitLowerBound);
            }

            // If the best two from the absolute best case scenario can't beat the target, the search isn't worth doing.
            if (!BestTwoSkipdoors.TryBestPair(in optimisticEntries, in optimisticExits, out CompSkipdoor pairEntry, out CompSkipdoor pairExit, out float bestImaginable)
                || bestImaginable >= maxRouteCost)
            { return false; }

            // Make sure our best two for each of entry and exit are ones we can actually use for entering / exiting.
            ConfirmBestTwo(entrySide: true, out BestTwoSkipdoors confirmedEntries);
            ConfirmBestTwo(entrySide: false, out BestTwoSkipdoors confirmedExits);

            // If the best two "usable" skipdoors from each of entry / exit cannot beat the target even with best case scenario, its a bust search.
            if (!BestTwoSkipdoors.TryBestPair(in confirmedEntries, in confirmedExits, out pairEntry, out pairExit, out float bestUsable)
                || bestUsable >= maxRouteCost)
            { return false; }

            // These floors bound the whole search.
            // They're optimistic, so they only tighten things that could never have worked anyway.
            entryCostFloor = confirmedEntries.firstCost;
            exitCostFloor = confirmedExits.firstCost;

            // Cull the candidates that are beyond their opposing cost floors.
            // Even with the best case opposite pair, these will never beat the target cost.
            for (int i = candidateCount - 1; i >= 0; i--)
            {
                ref CandidateSkipdoor candidate = ref candidates[i];
                if (candidate.entryLowerBound + exitCostFloor >= maxRouteCost) { candidate.canEnter = false; }
                if (candidate.exitLowerBound + entryCostFloor >= maxRouteCost) { candidate.canExit = false; }

                if (!candidate.canEnter && !candidate.canExit)
                {
                    candidates[i] = candidates[--candidateCount]; // swap-remove (nifty little trick!)
                }
            }

            for (int i = 0; i < candidateCount; i++)
            {
                candidateIndex[candidates[i].skipdoor] = i;
            }

            return true;
        }

        // Finds the top two usable doors for one side while doing as few access checks as possible (they be expensive sometimes).
        private void ConfirmBestTwo(bool entrySide, out BestTwoSkipdoors topTwo)
        {
            topTwo = BestTwoSkipdoors.Empty;

            for (int i = 0; i < candidateCount; i++)
            {
                ref CandidateSkipdoor candidate = ref candidates[i];
                if (!candidate.accessResolved) { continue; }
                if (entrySide ? candidate.canEnter : candidate.canExit)
                {
                    topTwo.Try(candidate.skipdoor, entrySide ? candidate.entryLowerBound : candidate.exitLowerBound);
                }
            }

            while (true)
            {
                // Cheapest candidate that hasn't had its access checked yet.
                int bestIndex = -1;
                float bestLowerBound = float.MaxValue;
                for (int i = 0; i < candidateCount; i++)
                {
                    if (candidates[i].accessResolved) { continue; }

                    float lowerBound = entrySide ? candidates[i].entryLowerBound : candidates[i].exitLowerBound;
                    if (lowerBound < bestLowerBound) { bestLowerBound = lowerBound; bestIndex = i; }
                }

                // Nothing unresolved can displace the confirmed second place. TopTwo is final.
                if (bestIndex < 0 || bestLowerBound >= topTwo.secondCost) { break; }

                ref CandidateSkipdoor candidate = ref candidates[bestIndex];
                ResolveSkipdoorAccess(ref candidate);

                if (entrySide ? candidate.canEnter : candidate.canExit)
                {
                    topTwo.Try(candidate.skipdoor, bestLowerBound);
                }
            }
        }
        private void ResolveSkipdoorAccess(ref CandidateSkipdoor candidate)
        {
            candidate.skipdoor.IsUsableBy(in ac, out bool allowEnter, out bool allowExit);
            candidate.canEnter = candidate.canEnter && allowEnter;
            candidate.canExit = candidate.canExit && allowExit;
            candidate.accessResolved = true;
        }

        private void ScoreDoorsInRegion(Region region, IntVec3 from, float g, bool entrySide)
        {
            if (!planner.TryGetSkipdoorsInRegion(region, out List<CompSkipdoor> doors)) { return; }

            foreach (CompSkipdoor door in doors)
            {
                if (!candidateIndex.TryGetValue(door, out int i)) { continue; } // Not a candidate (culled during setup).

                ref CandidateSkipdoor candidate = ref candidates[i];
                if (!candidate.accessResolved) { ResolveSkipdoorAccess(ref candidate); }
                if (!(entrySide ? candidate.canEnter : candidate.canExit)) { continue; }

                float cost = g + SkipNetUtils.OctileDistance(from, door.Position);
                if (entrySide) { bestEntries.Try(door, cost); }
                else { bestExits.Try(door, cost); }
            }
        }
    }
}