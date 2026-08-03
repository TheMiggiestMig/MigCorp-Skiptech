using HarmonyLib;
using MigCorp.Skiptech.Utils;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public class SkipNetPathSplicer
    {
        public readonly MapComponent_SkipNet skipNet;

        // The only PawnPath internals the splice needs. inUse is already set by
        // the pathfinder's EmitPath on both skip paths, so we never touch it.
        private static readonly AccessTools.FieldRef<PawnPath, float> _pathTotalCostRef =
            AccessTools.FieldRefAccess<PawnPath, float>("totalCostInt");
        private static readonly AccessTools.FieldRef<PawnPath, int> _pathCurNodeIndexRef =
            AccessTools.FieldRefAccess<PawnPath, int>("curNodeIndex");

        // Safety trigger, if something in maintenance isn't working.
        private const int skipPathTimeoutTicks = 600;
        private const int MaxConcurrentSplicesHard = 8;

        private class SkipPathPair
        {
            public Pawn pawn;
            public SkipNetPlan plan;
            public PathRequest pathToEntry;
            public PathRequest pathToDest;
            public int tickStarted;

            // Snapshots for the comparison log (never hold the direct path itself).
            public float directCost;
            public int directNodes;
        }

        private readonly List<SkipPathPair> pendingPairs = new List<SkipPathPair>();

        private int debugBegunCount;
        private int debugMergedCount;
        private int debugAbortedCount;

        public Map map { get { return skipNet.map; } }

        public SkipNetPathSplicer(MapComponent_SkipNet skipNet)
        {
            this.skipNet = skipNet;
        }

        // PawnPathPool throws ErrorOnce once total paths created exceeds 2 * spawnedPawns + 5.
        // Each pending pair temporarily takes 2 paths on top of the pawn's own, so usage: ~N + 2P must stay under 2N + 5.
        // Which means processing splices must be <= (N + 5) / 2.
        // ... Or I can just let it error once and let the pool grow, but I'd rather not have errors.
        public int MaxPendingPairs
        {
            get
            {
                int poolSafe = (map.mapPawns.AllPawnsSpawnedCount + 5) / 2;
                return Mathf.Min(MaxConcurrentSplicesHard, poolSafe);
            }
        }

        public bool AtCapacity { get { return pendingPairs.Count >= MaxPendingPairs; } }

        public bool TryBeginDryRunSkipPaths(SkipNetPlan plan, PawnPath directPath)
        {
            if (AtCapacity) { return false; }

            Pawn pawn = plan.pawn;
            if (pawn?.pather == null || !pawn.Spawned || pawn.Map != map) { return false; }

            PathFinder pathFinder = map.pathFinder;
            PathFinderCostTuning? tuning = PathFinderCostTuning.For(pawn);

            // Get the exact cell the entry path should start on.
            IntVec3 start = pawn.pather.nextCell.IsValid ? pawn.pather.nextCell : pawn.Position;

            PathRequest skipPathToEntry = pathFinder.CreateRequest(start, new LocalTargetInfo(plan.entry.parent), null, pawn, tuning, PathEndMode.OnCell);
            PathRequest skipPathToDest = pathFinder.CreateRequest(plan.exit.Position, plan.originalDest, null, pawn, tuning, plan.originalPeMode);

            pathFinder.PushRequest(skipPathToEntry);
            pathFinder.PushRequest(skipPathToDest);

            pendingPairs.Add(new SkipPathPair
            {
                pawn = pawn,
                plan = plan,
                pathToEntry = skipPathToEntry,
                pathToDest = skipPathToDest,
                tickStarted = GenTicks.TicksGame,
                directCost = directPath?.TotalCost ?? -1f,
                directNodes = directPath?.NodesLeftCount ?? -1,
            });
            debugBegunCount++;

            return true;
        }

        public void Run()
        {
            RunPendingPairs();
        }

        private void RunPendingPairs()
        {
            for (int i = pendingPairs.Count - 1; i >= 0; i--)
            {
                SkipPathPair pair = pendingPairs[i];
                Pawn pawn = pair.pawn;
                SkipNetPlan plan = pair.plan;

                // Check for pawn't first.
                // Paths for "no longer pawn" never finalize or set ResultIsReady, so the path lingers forever unless
                // we nuke it ourselves.
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != map || pawn.pather == null)
                {
                    AbortPair(i, pair, "pawn gone :(");
                    continue;
                }

                // Mark and sweep style, just like granny used to make.
                if (plan == null || plan.IsDisposedOrInvalid)
                {
                    AbortPair(i, pair, "plan retired");
                    continue;
                }

                if (!pair.pathToEntry.ResultIsReady || !pair.pathToDest.ResultIsReady)
                {
                    if (GenTicks.TicksGame - pair.tickStarted > skipPathTimeoutTicks)
                    {
                        SkiptechUtil.Warning($"[Splicer] skip paths for {pawn.LabelShort} timed out after {skipPathTimeoutTicks} ticks.");
                        AbortPair(i, pair, "timeout");
                    }
                    continue;
                }

                // Check if the requests finished with no path for either skip path.
                if (pair.pathToEntry.Found != true || pair.pathToDest.Found != true ||
                    !pair.pathToEntry.TryGetPath(out PawnPath entryPath) || entryPath == null || !entryPath.Found ||
                    !pair.pathToDest.TryGetPath(out PawnPath destPath) || destPath == null || !destPath.Found)
                {
                    AbortPair(i, pair, "no path");
                    continue;
                }

                // We're good! Claim the paths and dispose the requests.
                pair.pathToEntry.ClaimCalculatedPath();
                pair.pathToDest.ClaimCalculatedPath();
                DisposeRequests(pair);
                pendingPairs.RemoveAt(i);

                float entryCost = entryPath.TotalCost;
                float destCost = destPath.TotalCost;
                PawnPath spliced = BuildSplicedPath(entryPath, destPath);
                debugMergedCount++;

                SkiptechUtil.Message($"[Splicer] {pawn.LabelShort}: {plan.entry.Position}→{plan.exit.Position}," +
                    $"skip paths {entryCost}+{destCost}+skip {MigcorpSkiptechMod.Settings.skipCost} = spliced {spliced.TotalCost} ({spliced.NodesLeftCount} nodes) vs direct {pair.directCost} ({pair.directNodes} nodes). {DebugTally()}",
                    LogLevel.Verbose);

                // DEBUG Only need to dispose here for testing, but keep in mind the PawnPath must be handled VERY carefully during it's lifecycle.
                // That's what kept breaking the Aug 2025 prototypes :/
                spliced.Dispose();
            }
        }

        private void AbortPair(int index, SkipPathPair pair, string reason)
        {
            DisposeRequests(pair);
            pendingPairs.RemoveAt(index);
            debugAbortedCount++;
            SkiptechUtil.Message($"[Splicer] pair for {pair.pawn?.LabelShort ?? "???"} aborted ({reason}). {DebugTally()}", LogLevel.Verbose);
        }

        private static void DisposeRequests(SkipPathPair pair)
        {
            pair.pathToEntry?.Dispose();
            pair.pathToDest?.Dispose();
            pair.pathToEntry = null;
            pair.pathToDest = null;
        }

        /// <summary>
        /// Splices the entry skip path onto the dest skip path, disposes the entry skip path, leaving the dest (combined) skip path ready for install.
        /// </summary>
        private static PawnPath BuildSplicedPath(PawnPath entryPath, PawnPath destPath)
        {
            List<IntVec3> nodes = destPath.NodesReversed;
            nodes.AddRange(entryPath.NodesReversed);

            _pathTotalCostRef(destPath) = destPath.TotalCost + entryPath.TotalCost
                                          + MigcorpSkiptechMod.Settings.skipCost;
            _pathCurNodeIndexRef(destPath) = nodes.Count - 1;

            entryPath.Dispose(); // Only ever call this once! PawnPathPool does not like duplicate Dispose calls on PawnPaths :|
            return destPath;
        }

        private string DebugTally()
        {
            return $"[begun:{debugBegunCount} merged:{debugMergedCount} aborted:{debugAbortedCount} pending:{pendingPairs.Count}]";
        }
        public void DropAll()
        {
            for (int i = pendingPairs.Count - 1; i >= 0; i--) { DisposeRequests(pendingPairs[i]); }
            pendingPairs.Clear();
        }
    }
}