using HarmonyLib;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public static class SkipNetPathSplicer
    {

        // The only PawnPath internals the splice needs. inUse is already set by
        // the pathfinder's EmitPath on both skip paths, so we never touch it.
        private static readonly AccessTools.FieldRef<PawnPath, float> _pathTotalCostRef =
            AccessTools.FieldRefAccess<PawnPath, float>("totalCostInt");
        private static readonly AccessTools.FieldRef<PawnPath, int> _pathCurNodeIndexRef =
            AccessTools.FieldRefAccess<PawnPath, int>("curNodeIndex");

        public enum SpliceResult
        {
            NotReady,   // Legs still being pathed.
            Failed,     // A leg came back with no path.
            SameSeam,   // Pawn is standing at a live seam through these same two skipdoors.
            NotWorth,   // Real leg costs don't beat the direct path.
            Built,      // Legs claimed and spliced. The caller owns the path.
        }

        // Pushes both legs of the splice (pawn -> entry skipdoor, exit skipdoor -> destination) onto the plan.
        // They live there until the Manager builds the splice or releases the plan.
        public static void BeginLegs(SkipNetPlan plan)
        {
            Pawn pawn = plan.pawn;
            PathFinder pathFinder = plan.map.pathFinder;
            PathFinderCostTuning? tuning = PathFinderCostTuning.For(pawn);

            // Get the exact cell the entry path should start on.
            IntVec3 start = plan.originalPathRequest.Start;

            plan.entryRequest = pathFinder.CreateRequest(start, new LocalTargetInfo(plan.entry.parent), null, pawn, tuning, PathEndMode.OnCell);
            plan.exitRequest = pathFinder.CreateRequest(plan.exit.Position, plan.dest, null, pawn, tuning, plan.peMode);

            pathFinder.PushRequest(plan.entryRequest);
            pathFinder.PushRequest(plan.exitRequest);
        }

        // Checks the plan's legs and, if they're ready and worth it, splices them. Only Built consumes the legs;
        // on any other result they stay on the plan for the Manager to wait on or release.
        public static SpliceResult TryBuildSplice(SkipNetPlan plan, SkipNetPlan livePlan, out PawnPath spliced)
        {
            spliced = null;

            if (!plan.entryRequest.ResultIsReady || !plan.exitRequest.ResultIsReady) { return SpliceResult.NotReady; }

            // Check if the requests finished with no path for either skip path.
            if (!HasUsablePath(plan.entryRequest, out PawnPath entryPath) || !HasUsablePath(plan.exitRequest, out PawnPath destPath)) { return SpliceResult.Failed; }

            // Standing at these same two skipdoors on its current splice (usually waiting on them to open)? Let that one finish.
            // The new splice would only make the pawn re-take the step it's already lined up for.
            if (IsStandingAtSameSeam(plan, livePlan)) { return SpliceResult.SameSeam; }

            // Will the spliced pair actually be shorter than the direct path?
            // We have actual path costs now, so this is the final hurdle.
            if (!IsWorthSplicing(plan.pawn, plan, entryPath, destPath)) { return SpliceResult.NotWorth; }

            // We're good! Claim the paths (so disposing the requests leaves them alone), then splice them.
            plan.entryRequest.ClaimCalculatedPath();
            plan.exitRequest.ClaimCalculatedPath();
            plan.DisposeLegs();

            spliced = BuildSplicedPath(entryPath, destPath);
            return SpliceResult.Built;
        }

        private static bool HasUsablePath(PathRequest request, out PawnPath path)
        {
            path = null;
            return request.Found == true && request.TryGetPath(out path) && path != null && path.Found;
        }

        // What the spliced path will cost. Both legs as the pathfinder costed them (this pawn's move ticks, terrain, doors, avoid grid etc.), plus the flat skip cost.
        private static float SplicedCost(PawnPath entryPath, PawnPath destPath)
        {
            return entryPath.TotalCost + destPath.TotalCost + MigcorpSkiptechMod.Settings.skipCost;
        }

        // Same check the searcher uses with real costs instead of octile estimates.
        // The direct path is still sitting unclaimed in the proposal's original request, so this only peeks at it.
        private static bool IsWorthSplicing(Pawn pawn, SkipNetPlan proposal, PawnPath entryPath, PawnPath destPath)
        {
            if (!proposal.originalPathRequest.TryGetPath(out PawnPath directPath) || directPath == null || !directPath.Found) { return false; }

            float splicedCost = SplicedCost(entryPath, destPath);
            float targetCost = directPath.TotalCost * MigcorpSkiptechMod.Settings.worthItFactor;
            if (splicedCost < targetCost) { return true; }

            return false;
        }

        /// <summary>
        /// Splices the entry skip path onto the dest skip path, disposes the entry skip path, leaving the dest (combined) skip path ready for install.
        /// </summary>
        private static PawnPath BuildSplicedPath(PawnPath entryPath, PawnPath destPath)
        {
            List<IntVec3> nodes = destPath.NodesReversed;
            nodes.AddRange(entryPath.NodesReversed);

            _pathTotalCostRef(destPath) = SplicedCost(entryPath, destPath);
            _pathCurNodeIndexRef(destPath) = nodes.Count - 1;

            entryPath.Dispose(); // Only ever call this once! PawnPathPool does not like duplicate Dispose calls on PawnPaths :|
            return destPath;
        }

        // Standing at a live seam that uses the same two skipdoors as this plan.
        private static bool IsStandingAtSameSeam(SkipNetPlan plan, SkipNetPlan livePlan)
        {
            return livePlan != null
                && livePlan.splicedPath == plan.pawn.pather?.curPath
                && livePlan.entryCell == plan.entryCell
                && livePlan.exitCell == plan.exitCell
                && livePlan.IsPawnAtSeam;
        }
    }
}