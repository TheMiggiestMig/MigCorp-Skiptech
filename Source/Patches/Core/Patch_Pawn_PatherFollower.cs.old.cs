/// <summary>
/// Patches the PatherTick to capture the original pathing request once it is calculated, then calculate
/// a few skipnet alternatives before either hijacking the original path, or abandoning the skipnet plan.
/// </summary>
//[HarmonyPatch(typeof(Pawn_PathFollower))]
//public static class Patch_Pawn_PathFollower_PatherTick
//{
//    [HarmonyPostfix]
//    [HarmonyPatch(nameof(Pawn_PathFollower.PatherTick),
//        new Type[] { })]
//    public static void Postfix(
//        Pawn_PathFollower __instance,
//        ref Pawn ___pawn,
//        ref float ___cachedMovePercentage,
//        ref bool ___cachedWillCollideNextCell,
//        ref bool ___moving,
//        ref int ___lastMovedTick,
//        ref LocalTargetInfo ___destination,
//        ref PathEndMode ___peMode
//        )
//    {
//        Pawn pawn = ___pawn;
//        SkipNetworkPlanner planner = ___pawn.Map.GetComponent<MapComponent_SkipNetwork>().planner;
//        if (!(planner?.TryGetPlan(pawn, out SkipPlan plan) == true)) return;
//        PawnPath curPath = __instance.curPath;
//        PathRequest curPathRequest = __instance.curPathRequest;

//        // Wait for the original path to load.
//        // We want to use it to know if it's worth checking for skipnetwork alternatives.
//        if (plan.pendingOriginalPath)
//        {
//            if (!(curPathRequest == null && curPath != null)) return;

//            plan.pendingOriginalPath = false;
//            plan.originalPathCost = curPath.TotalCost;
//            plan.originalNodesCount = curPath.NodesLeftCount;

//            // Now that we have the path, lets cull any nodes that are horrible.
//            List<CompSkipdoor> filteredEnterSkipdoors = new List<CompSkipdoor>();
//            List<CompSkipdoor> filteredExitSkipdoors = new List<CompSkipdoor>();

//            foreach ((CompSkipdoor skipdoor, int hCost) in plan.entrySkipdoorCandidates)
//            {
//                if (!pawn.CanReach(skipdoor.parent, PathEndMode.OnCell, skipdoor.parent.Position.GetDangerFor(pawn, pawn.Map))) continue;
//                if (hCost >= plan.originalNodesCount * (1 - MigcorpSkiptechMod.Settings.pathImprovementMargin)) continue;
//                if (!skipdoor.IsEnterableBy(pawn)) continue;
//                filteredEnterSkipdoors.Add(skipdoor);
//            }

//            foreach ((CompSkipdoor skipdoor, int hCost) in plan.exitSkipdoorCandidates)
//            {
//                if (!pawn.CanReach(skipdoor.parent, PathEndMode.OnCell, skipdoor.parent.Position.GetDangerFor(pawn, pawn.Map))) continue;
//                if (hCost >= plan.originalNodesCount * (1 - MigcorpSkiptechMod.Settings.pathImprovementMargin)) continue;
//                if (!skipdoor.IsExitableBy(pawn)) continue;

//                filteredExitSkipdoors.Add(skipdoor);
//            }

//            // If we have insufficient useable skipdoors, nuke the plan.
//            if (!(filteredEnterSkipdoors.Count > 0 && filteredExitSkipdoors.Count > 0) ||   // There must be at least one entry and one exit skipdoor
//                (filteredEnterSkipdoors.Count == 1 && filteredExitSkipdoors.Count == 1 && filteredEnterSkipdoors[0] == filteredExitSkipdoors[0]))   // If only 1 each, they must be different.
//            {
//                planner.DisposePlan(pawn);
//                return;
//            }

//            if (pawn.needs?.mood != null)
//            // Queue up the PathRequests to generate paths to the.
//            plan.entryPathCandidateReqs = new List<(CompSkipdoor sd, PathRequest pr)>();
//            plan.exitPathCandidateReqs = new List<(CompSkipdoor sd, PathRequest pr)>();

//            foreach (CompSkipdoor skipdoor in filteredEnterSkipdoors)
//            {
//                PathRequest req = pawn.Map.pathFinder.CreateRequest(pawn.Position, skipdoor.parent, null, pawn, null, PathEndMode.OnCell);
//                plan.entryPathCandidateReqs.Add((skipdoor, req));

//                pawn.Map.pathFinder.PushRequest(req);
//            }

//            foreach (CompSkipdoor skipdoor in filteredExitSkipdoors)
//            {
//                plan.exitPathCandidateReqs.Add((skipdoor, pawn.Map.pathFinder.CreateRequest(skipdoor.parent.Position, plan.dest, null, pawn, null, plan.peMode)));
//            }
//        }

//        // The original path has loaded.
//        // Lets try populating the skip plan. Start by getting remaining candidate entry points.
//        // For each of them, request a path from the pawn to them. Then keep the shortest.

//        if (!plan.offloadedStartPath)
//        {
//            if (plan.entryPath == null)
//            {
//                // Check if all of the entry paths have come back. We want the shortest.
//                int count = 0;
//                foreach ((CompSkipdoor sd, PathRequest pr) in plan.entryPathCandidateReqs)
//                {
//                    if (pr.ResultIsReady)
//                    {
//                        count++;
//                    }
//                }

//                //If we're still waiting on some, wait for them.
//                if (count != plan.entryPathCandidateReqs.Count) return;

//                float lowestCost = float.PositiveInfinity;
//                PawnPath outPath;

//                foreach ((CompSkipdoor sd, PathRequest pr) in plan.entryPathCandidateReqs)
//                {
//                    if (pr.TryGetPath(out outPath) &&
//                        outPath.Found &&
//                        outPath.TotalCost < lowestCost &&
//                        outPath.TotalCost < plan.originalPathCost)
//                    {
//                        lowestCost = outPath.TotalCost;
//                        plan.entryPath?.Dispose();
//                        plan.entryPath = outPath;
//                        plan.entry = sd;
//                        pr.ClaimCalculatedPath();
//                    }
//                    pr.Dispose();
//                }

//                // If there were no valid entryPaths, terminate the plan.
//                if (plan.entryPath == null)
//                {
//                    planner.DisposePlan(pawn);
//                    return;
//                }

//                // We've found the shorted entryPath. Lets check the exitPaths.
//                foreach ((CompSkipdoor sd, PathRequest pr) in plan.exitPathCandidateReqs)
//                {
//                    pawn.Map.pathFinder.PushRequest(pr);
//                }

//            }

//            // Same deal as the entryPath above, find the shorted outpath from the available candidates.
//            // We will also be checking if the total path length exceeds the improvement margin.
//            else
//            {
//                int count = 0;
//                foreach ((CompSkipdoor sd, PathRequest pr) in plan.exitPathCandidateReqs)
//                {
//                    if (pr.ResultIsReady)
//                        count++;
//                }

//                // If we're still waiting on some, wait for them.
//                if (count != plan.exitPathCandidateReqs.Count) return;

//                float lowestCost = float.PositiveInfinity;
//                PawnPath outPath;

//                foreach ((CompSkipdoor sd, PathRequest pr) in plan.exitPathCandidateReqs)
//                {
//                    if (pr.TryGetPath(out outPath) &&
//                        outPath.Found &&
//                        outPath.TotalCost < lowestCost &&
//                        outPath.TotalCost + plan.entryPath.TotalCost < plan.originalPathCost)
//                    {
//                        lowestCost = outPath.TotalCost;
//                        plan.exitPath?.Dispose();
//                        plan.exitPath = outPath;
//                        plan.exit = sd;
//                        pr.ClaimCalculatedPath();
//                    }
//                    pr.Dispose();
//                }

//                // Do we have a valid path combination?
//                if (!(plan.entryPath != null && plan.exitPath != null))
//                {
//                    planner.DisposePlan(pawn);
//                    return;
//                }

//                // Hijack the pawns original path with the entryPath. We will leave the rest to TryEnterNextPathCell.
             
//                __instance.ResetToCurrentPosition();
//                __instance.DisposeAndClearCurPath();
//                __instance.DisposeAndClearCurPathRequest();

//                __instance.curPath = plan.entryPath;
//                plan.entryPath = null;
//                plan.offloadedStartPath = true;
//            }
//        }
//        // We've finished the first leg of the journey. Just override with the second leg, and ditch the plan.
//        if (plan.offloadedStartPath && pawn.Position == plan.entry.parent.Position)
//        {
//            if (plan.entry.IsEnterableBy(pawn) && plan.exit.IsExitableBy(pawn))
//            {
//                pawn.Position = plan.entry.parent.Position; // Teleport!
//                pawn.Drawer.tweener.Notify_Teleported(); // Make it snappy!
//                MigCorp.Skiptech.Utils.FxUtil.PlaySkip(pawn.Position, pawn.Map, true);
//                pawn.Position = plan.exit.parent.Position;
//                pawn.Drawer.tweener.Notify_Teleported();
//                MigCorp.Skiptech.Utils.FxUtil.PlaySkip(pawn.Position, pawn.Map, false);

//                __instance.ResetToCurrentPosition();
//                __instance.DisposeAndClearCurPath();
//                __instance.DisposeAndClearCurPathRequest();
//                /*
//                __instance.curPath = plan.exitPath;
//                plan.exitPath = null;
//                */
//                planner.DisposePlan(pawn);
//            }
//        }
//    }
//}