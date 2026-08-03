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

        public enum TeleportStepDecision
        {
            Approved,
            Waiting,     // used if the skipdoor is delayed
            BlockedDead
        }

        private class SkipPathPair
        {
            public Pawn pawn;
            public SkipNetPlan plan;
            public PathRequest pathToEntry;
            public PathRequest pathToDest;
            public int tickStarted;
        }

        private readonly List<SkipPathPair> pendingPairs = new List<SkipPathPair>();

        public class SeamInfo
        {
            public SkipNetPlan plan;
            public PawnPath installedPath; // Keep a record of the path we installed (in case it goes missing >.>). Don't do ANYTHING with it, just use it to check agains the pawn's curPath.
            public IntVec3 entryCell;
            public IntVec3 exitCell;
            public bool compsNotified;     // Make sure we only Notify_PawnArrived once
        }

        private readonly Dictionary<Pawn, SeamInfo> seams = new Dictionary<Pawn, SeamInfo>();
        private static readonly List<Pawn> tmpSeamCleanup = new List<Pawn>();

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

        public bool TryBeginSkipPaths(SkipNetPlan plan)
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
                tickStarted = GenTicks.TicksGame
            });

            // The splicer owns this trip now.
            plan.State = SkipNetPlanState.SkipPathsPending;
            return true;
        }

        public void Run()
        {
            RunPendingPairs();
            RunSeamMaintenance();
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
                    AbortPair(i, pair);
                    continue;
                }

                // Mark and sweep style, just like granny used to make.
                if (plan == null || plan.IsDisposedOrInvalid)
                {
                    AbortPair(i, pair);
                    continue;
                }

                Pawn_PathFollower pather = pawn.pather;

                // If there's already another path request for the pawn, the pawn isn't moving or doesn't have a path,
                // then the plan is already invalid (since we assume it's supposed to be moving on the direct path for now).
                if (pather.curPathRequest != null || !pather.Moving || pather.curPath == null)
                {
                    AbortPair(i, pair);
                    continue;
                }

                // If for whatever reason the pawn isn't going to the same destination after we requested the skip paths,
                // then the plan is invalid.
                if (SkipNetUtils.PatherDest(pather) != plan.originalDest ||
                    SkipNetUtils.PatherPeMode(pather) != plan.originalPeMode)
                {
                    AbortPair(i, pair);
                    continue;
                }

                if (!pair.pathToEntry.ResultIsReady || !pair.pathToDest.ResultIsReady)
                {
                    if (GenTicks.TicksGame - pair.tickStarted > skipPathTimeoutTicks)
                    {
                        SkiptechUtil.Warning($"[Splicer] skip paths for {pawn.LabelShort} timed out after {skipPathTimeoutTicks} ticks.");
                        AbortPair(i, pair);
                    }
                    continue;
                }

                // Check if the requests finished with no path for either skip path.
                if (!HasUsablePath(pair.pathToEntry, out PawnPath entryPath)
                    || !HasUsablePath(pair.pathToDest, out PawnPath destPath))
                {
                    AbortPair(i, pair);
                    continue;
                }

                // We're good! Claim the paths and dispose the requests.
                pair.pathToEntry.ClaimCalculatedPath();
                pair.pathToDest.ClaimCalculatedPath();
                DisposeRequests(pair);
                pendingPairs.RemoveAt(i);

                PawnPath spliced = BuildSplicedPath(entryPath, destPath);

                // Swap the pawn's direct path with our spliced one.
                Install(pawn, plan, spliced);
            }
        }
        private static bool HasUsablePath(PathRequest request, out PawnPath path)
        {
            path = null;
            return request.Found == true && request.TryGetPath(out path) && path != null && path.Found;
        }

        private void AbortPair(int index, SkipPathPair pair)
        {
            pair.plan?.DisposeSuperseded();

            DisposeRequests(pair);
            pendingPairs.RemoveAt(index);
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

        public void DropAll()
        {
            for (int i = pendingPairs.Count - 1; i >= 0; i--) { DisposeRequests(pendingPairs[i]); }
            pendingPairs.Clear();
            seams.Clear();
        }
        private void Install(Pawn pawn, SkipNetPlan plan, PawnPath spliced)
        {
            Pawn_PathFollower pather = pawn.pather;

            // Blatantly copied from vanilla's path claiming (PatherTick)
            pather.DisposeAndClearCurPath();
            pather.curPath = spliced;          // vanilla owns it from here, let it handle disposal. NEVER dispose it here again.
            pather.curPathJobIsStale = false;  // whoops

            plan.State = SkipNetPlanState.Installed;
            seams[pawn] = new SeamInfo
            {
                plan = plan,
                installedPath = spliced,
                entryCell = plan.entry.Position,
                exitCell = plan.exit.Position,
            };
        }

        public bool TryGetSeam(Pawn pawn, out SeamInfo seam)
        {
            return seams.TryGetValue(pawn, out seam);
        }

        public static void HoldAtSeam(Pawn_PathFollower pather)
        {
            pather.nextCellCostLeft = 1f;
            pather.nextCellCostTotal = 1f;
        }

        public TeleportStepDecision DecideTeleportStep(Pawn pawn, SkipNetPathSplicer.SeamInfo seam)
        {
            Pawn_PathFollower pather = pawn.pather;
            SkipNetPlan plan = seam.plan;

            // MAke sure the plan and path are still good this tick, otherwise it's an invalid teleport.
            if (plan == null || plan.IsDisposedOrInvalid || pather.curPath != seam.installedPath)
            {
                pather.ResetToCurrentPosition();
                seams.Remove(pawn);
                return TeleportStepDecision.BlockedDead;
            }

            // I totally didn't forget to tell the doors to start charging >.>;
            if (!seam.compsNotified)
            {
                seam.compsNotified = true;
                plan.entry.Notify_PawnArrived(pawn, plan, SkipdoorType.Entry);
                plan.exit.Notify_PawnArrived(pawn, plan, SkipdoorType.Exit);
            }

            // Doors still spinning up? Park the pawn in a cooldown stance.
            // Blatantly stolen from the door check in Pawn_PathFollower.TryEnterNextPathCell
            plan.entry.IsEnterableNowBy(pawn, out int entryWait);
            plan.exit.IsExitableNowBy(pawn, out int exitWait);
            int waitTicks = Mathf.Max(entryWait, exitWait);
            if (waitTicks > 0)
            {
                Stance_Cooldown stance = new Stance_Cooldown(waitTicks, new LocalTargetInfo(plan.entry.parent), null)
                {
                    neverAimWeapon = true,
                };
                pawn.stances.SetStance(stance);
                HoldAtSeam(pather);
                return TeleportStepDecision.Waiting;
            }

            // Final check before teleporting the pawn.
            if (!plan.IsStillAccessible() ||
                !plan.IsStillPathableFromExitToDest(map, SkipNetUtils.JankyTraverseParmsFor(pawn)))
            {
                plan.DisposeCancelled();
                pather.ResetToCurrentPosition();
                seams.Remove(pawn);
                return TeleportStepDecision.BlockedDead;
            }

            return TeleportStepDecision.Approved;
        }

        public void CompleteTeleportStep(Pawn pawn, SeamInfo seam)
        {
            // Move was probably blocked this tick. Let the next TryEnterNextPathCell prefix attempt.
            if (pawn.Position != seam.exitCell) { return; }

            SkipNetPlan plan = seam.plan;

            // Do the thing.
            // Teleport, cancel the tween, fire the effects, and notify the skipdoors that the pawn teleported.
            pawn.Drawer.tweener.Notify_Teleported();

            FxUtil.PlaySkip(seam.entryCell, map, false);
            FxUtil.PlaySkip(seam.exitCell, map, false);

            plan.entry.Notify_PawnTeleported(pawn, plan, SkipdoorType.Entry);
            plan.exit.Notify_PawnTeleported(pawn, plan, SkipdoorType.Exit);

            // Everything from this point is just vanilla walking to the destination.
            // Tear down the plan and seams.
            plan.DisposeCompleted();
            seams.Remove(pawn);
        }

        // Cleanup active spliced paths.
        private void RunSeamMaintenance()
        {
            if (seams.Count == 0) { return; }
            tmpSeamCleanup.Clear();

            // Pawn owned the path, so would have handled disposing the path.
            foreach (KeyValuePair<Pawn, SeamInfo> kv in seams)
            {
                Pawn pawn = kv.Key;
                SeamInfo seam = kv.Value;
                Pawn_PathFollower pather = pawn?.pather;


                // Pawn is pawn't. Clean up the records.
                if (pawn == null || !pawn.Spawned || pawn.Map != map || pather == null)
                {
                    seam.plan?.DisposeSuperseded();
                    tmpSeamCleanup.Add(pawn);
                    continue;
                }

                // Pawn got a new path from somewhere and replaced our spliced one.
                if (pather.curPath != seam.installedPath)
                {
                    seam.plan?.DisposeSuperseded();
                    tmpSeamCleanup.Add(pawn);
                    continue;
                }

                // If we don't have a plan anymore, we shouldn't be teleporting.
                // Ditch the seam and get the pawn to resume normal pathing.
                if (seam.plan == null || seam.plan.IsDisposedOrInvalid)
                {
                    pather.ResetToCurrentPosition();
                    tmpSeamCleanup.Add(pawn);
                    continue;
                }
            }

            for (int i = 0; i < tmpSeamCleanup.Count; i++)
            {
                seams.Remove(tmpSeamCleanup[i]);
            }
            tmpSeamCleanup.Clear();
        }
    }
}