using MigCorp.Skiptech.SkipNet.Comps;
using MigCorp.Skiptech.Utils;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using static MigCorp.Skiptech.SkipNet.SkipNetProposer;

namespace MigCorp.Skiptech.SkipNet
{
    public class SkipNetPlanner
    {
        public readonly MapComponent_SkipNet skipNet;
        private readonly SkipNetSearcher searcher;

        // Region --> Skipdoor mapping
        private bool regionSkipdoorsDirty = true;
        private int tickLastRegionSkipdoorRebuild;
        private readonly Dictionary<Region, List<CompSkipdoor>> regionSkipdoors = new Dictionary<Region, List<CompSkipdoor>>();

        // Plan management
        public readonly Dictionary<Pawn, SkipNetPlan> pawnSkipNetPlans = new Dictionary<Pawn, SkipNetPlan>();
        private readonly Deque<Pawn> plans = new Deque<Pawn>(); // Stealing this idea from the SkipNetProposer. Trust me, they're plans, not pawns.

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
            ResolveActivePlans();
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
            tickLastRegionSkipdoorRebuild = GenTicks.TicksGame;
        }

        public void MarkRegionDoorIndexDirty() => regionSkipdoorsDirty = true;
        public bool TryGetSkipdoorsInRegion(Region region, out List<CompSkipdoor> doors)
        {
            return regionSkipdoors.TryGetValue(region, out doors);
        }
        /// <summary>
        /// Returns a SkipNetPlan if it exists.
        /// </summary>
        /// <param name="force">Include "disposed" plans</param>
        /// <returns>Returns <see langword="true"/> if a plan existed or <see langword="false"/> otherwise.</returns>
        public bool TryGetSkipNetPlan(Pawn pawn, out SkipNetPlan plan, bool force = false)
        {
            if (pawn == null ||
                !pawnSkipNetPlans.TryGetValue(pawn, out plan) ||
                !force && plan.IsDisposed
                )
            {
                plan = default;
                return false;
            }
            return true;
        }

        public void RegisterPlan(Pawn pawn, SkipNetPlan plan)
        {
            pawnSkipNetPlans[pawn] = plan;
            plans.AddLast(pawn);
        }

        /// <summary>
        /// Loops through all active plans and attempts to resolve them.
        /// The plan itself handles the resolution; this method just tells it to try.
        /// </summary>
        public void ResolveActivePlans()
        {
            int numPlansToResolve = plans.Count;

            // Doing it like this to avoid the need for SnapshotPawnSkipNetPlans().
            // Hopefully a minor performance upgrade without breaking anything.
            while (numPlansToResolve-- > 0 && plans.Count > 0)
            {
                Pawn pawn = plans.PopFirst();

                // Check if this is a stale entry (i.e. the plan's been disposed of or cancelled)
                if (!pawnSkipNetPlans.TryGetValue(pawn, out SkipNetPlan plan)) { continue; }

                // Check if the plan is bad or invalid
                TryDisposeBadOrInvalidPlan(pawn, plan);

                // Check if the plan is still able to perform
                TryValidatePlan(pawn, plan);

                // Try to resolve the plans if they were waiting on something.
                if (!plan.IsDisposedOrInvalid && plan.Arrived)
                {
                    plan.Resolve();
                }

                // Check if the plan is disposed
                if (plan.IsDisposed)
                {
                    RecoverPawnIfNeeded(plan);
                    pawnSkipNetPlans.Remove(pawn);
                    continue;
                }

                // If we can't resolve the plan this tick, put the pawn back on the list to be tried again next tick.
                plans.AddLast(pawn);
            }
            //SkiptechUtil.Message($"Plans - Pawn Keys {plans.Count}, Pawn Plans {pawnSkipNetPlans.Count}");
        }

        // DEBUG This basically takes the role of the old SkipNetPlan.Notify_SkipNetPlanFailedOrCancelled.
        // Not sure if needed in the end, but for the mark-and-sweep refactor, I'll add it here instead.
        private void RecoverPawnIfNeeded(SkipNetPlan plan)
        {
            if (plan.DisposeState != SkipNetPlanDisposeState.Cancelled) { return; }

            // If pawn't, then plan't
            Pawn pawn = plan.pawn;
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != map || pawn.pather == null) { return; }

            Thing entryThing = plan.entry?.parent;
            if (entryThing == null) { return; }

            // Trying to remove things from the harmony patches where possible.
            LocalTargetInfo patherDest = SkipNetUtils.PatherDest(pawn.pather);
            bool stillOnHijackedLeg = patherDest.HasThing
                ? patherDest.Thing == entryThing
                : patherDest.Cell == entryThing.Position;
            if (!stillOnHijackedLeg) { return; }

            plan.ResetPawnMoveState();

            if (plan.originalDest.IsValid && plan.originalPeMode != PathEndMode.None &&
                !plan.originalDest.ThingDestroyed)
            {
                pawn.pather.StartPath(plan.originalDest, plan.originalPeMode);
            }
        }

        /// <summary>
        /// Cancels all active SkipNetPlans that use <see langword="skipdoor"/>
        /// </summary>
        /// <param name="skipdoor">The affected skipdoor (typically destroyed, minified, or despawned).</param>
        public void CancelPlansUsingSkipdoor(CompSkipdoor skipdoor)
        {
            // DEBUG In theory, no risk of mutating lists anymore since we're only marking disposals :)
            foreach (SkipNetPlan plan in pawnSkipNetPlans.Values)
            {
                if (plan.entry == skipdoor || plan.exit == skipdoor)
                {
                    plan.DisposeCancelled();
                }
            }
        }
        /*
        private bool TryRemovePlan(Pawn pawn, SkipNetPlan plan)
        {
            if (pawnSkipNetPlans.TryGetValue(pawn, out var current) && current == plan)
            {
                pawnSkipNetPlans.Remove(pawn);
                return true;
            }
            return false;
        }
        */

        public bool TryDisposeBadOrInvalidPlan(Pawn pawn, SkipNetPlan plan)
        {
            if (plan.IsInvalid || pawn?.Map != map || !pawn.Spawned)
            {
                plan.DisposeSuperseded(); // Janky, but Superseded should prevent it from retrying.
                return true;
            }
            return false;
        }

        public bool TryValidatePlan(Pawn pawn, SkipNetPlan plan)
        {
            if (!plan.IsDisposedOrInvalid)
            {

                // Check if the paths are still valid since the regionSkipdoor mapping was dirtied (i.e. The RegionGrid rebuilt).
                if (pawn.IsHashIntervalTick(60) && plan.tickLastRegionSkipdoorRebuild != tickLastRegionSkipdoorRebuild)
                {
                    plan.tickLastRegionSkipdoorRebuild = tickLastRegionSkipdoorRebuild;
                    TraverseParms tp = SkipNetUtils.JankyTraverseParmsFor(pawn);

                    if (!plan.IsStillPathableFromEntryToExit(map, tp) || !plan.IsStillPathableFromExitToDest(map, tp))
                    {
                        plan.DisposeCancelled();
                        return true;
                    }
                }


                // Check if the skipdoors are still usable.
                if (pawn.IsHashIntervalTick(180) && !plan.IsStillAccessible())
                {
                    plan.DisposeCancelled();
                    return true;
                }
            }
            return false;
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
            // Keeping this here for performance testing once I make the search harnesses.
            // Stopwatch stopwatch = Stopwatch.StartNew();
            // stopwatch.ElapsedTicks;

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
                plan = new SkipNetPlan(skipNet, pawn, dest, peMode, tickLastRegionSkipdoorRebuild);
                plan.Initialize(entry, exit);
            };

            //SkiptechUtil.Message($"{pawn.LabelShort} performed a search (bfs_time:{(bfs_time * 1_000_000.0) / Stopwatch.Frequency}us, dijkstra_time:{(dijkstra_time * 1_000_000.0) / Stopwatch.Frequency}us, diff:{((dijkstra_time * 1_000_000.0) / Stopwatch.Frequency) - ((bfs_time * 1_000_000.0) / Stopwatch.Frequency)}us)");
            skipNet.proposer.ConsumePopBudget(popCost);

            return found;
        }
    }
}
