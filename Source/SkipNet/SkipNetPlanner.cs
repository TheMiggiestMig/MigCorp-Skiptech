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
        private readonly Dictionary<Region, List<CompSkipdoor>> regionSkipdoors = new Dictionary<Region, List<CompSkipdoor>>();

        // Plan management
        public readonly Dictionary<Pawn, SkipNetPlan> pawnSkipNetPlans = new Dictionary<Pawn, SkipNetPlan>();
        private readonly List<KeyValuePair<Pawn, SkipNetPlan>> _tempPawnSkipNetPlans = new List<KeyValuePair<Pawn, SkipNetPlan>>(); // Snapshot for the pawnSkipNetPlans to prevent mutating the table mid loop.
        public readonly List<Pawn> disposedPawnSkipNetPlans = new List<Pawn>();
        public int lastSkipNetPlanDeepCleanTick;

        public List<CompSkipdoor> skipdoors { get { return skipNet.skipdoors; } }
        public Map map { get { return skipNet.map; } }

        public SkipNetPlanner(MapComponent_SkipNet skipNet)
        {
            this.skipNet = skipNet;
            //searcher = new SkipNetSearcherBFS(this);
            searcher = new SkipNetSearcherDijkstra(this);

            map.events.RegionsRoomsChanged += MarkRegionDoorIndexDirty;
            RebuildRegionDoorIndex();
        }

        public void Run()
        {
            ResolveActivePlans();
            Cleanup();
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
        }

        /// <summary>
        /// Loops through all active plans and attempts to resolve them.
        /// The plan itself handles the resolution; this method just tells it to try.
        /// </summary>
        public void ResolveActivePlans()
        {
            List<KeyValuePair<Pawn, SkipNetPlan>> activeSkipnetPlans = SnapshotPawnSkipNetPlans();
            SkipNetPlan plan;

            for (int i = 0; i < activeSkipnetPlans.Count; i++)
            {
                plan = activeSkipnetPlans[i].Value;

                if (!plan.IsDisposedOrInvalid && plan.Arrived)
                    plan.Resolve();
            }
        }

        /// <summary>
        /// Cancels all active SkipNetPlans that use <see langword="skipdoor"/>
        /// </summary>
        /// <param name="skipdoor">The affected skipdoor (typically destroyed, minified, or despawned).</param>
        public void CancelPlansUsingSkipdoor(CompSkipdoor skipdoor)
        {
            // Notify all plans using this skipdoor to cancel
            List<KeyValuePair<Pawn, SkipNetPlan>> activeSkipnetPlans = SnapshotPawnSkipNetPlans();
            SkipNetPlan plan;

            for (int i = 0; i < activeSkipnetPlans.Count; i++)
            {
                plan = activeSkipnetPlans[i].Value;

                if (plan.entry == skipdoor || plan.exit == skipdoor)
                    plan.Notify_SkipNetPlanFailedOrCancelled();
            }
        }

        public void Cleanup()
        {
            CleanupInvalidAndBadPlans();
            RemoveDisposedSkipNetPlans();
        }

        public void CleanupInvalidAndBadPlans()
        {
            List<KeyValuePair<Pawn, SkipNetPlan>> activeSkipnetPlans = SnapshotPawnSkipNetPlans();
            Pawn pawn;
            SkipNetPlan plan;

            for (int i = 0; i < activeSkipnetPlans.Count; i++)
            {
                pawn = activeSkipnetPlans[i].Key;
                plan = activeSkipnetPlans[i].Value;

                if (plan.IsInvalid || pawn?.Map != map || !pawn.Spawned)
                {
                    plan.Dispose();
                    continue;
                }

                // Check if the skipdoors are still useable.
                if (!plan.IsDisposedOrInvalid)
                {
                    if (pawn.IsHashIntervalTick(60) && !plan.IsStillAccessible())
                    {
                        plan.Notify_SkipNetPlanFailedOrCancelled();
                    }

                    // Do a DeepClean on the plan.
                    if (pawn.IsHashIntervalTick(180))
                    {
                        DeepClean(pawn, plan);
                    }
                }
            }
        }

        public void RemoveDisposedSkipNetPlans()
        {
            foreach (Pawn pawn in disposedPawnSkipNetPlans)
            {
                if (pawnSkipNetPlans.TryGetValue(pawn, out SkipNetPlan plan) && plan.IsDisposed)
                {
                    pawnSkipNetPlans.Remove(pawn);
                }
            }

            disposedPawnSkipNetPlans.Clear();
        }

        public void DeepClean(Pawn pawn, SkipNetPlan plan)
        {
            // Check if the paths are still valid.
            TraverseParms tp = TraverseParms.For(pawn, mode: TraverseMode.ByPawn);

            if (!plan.IsDisposedOrInvalid &&
            (!plan.IsStillPathableFromEntryToExit(map, tp) || !plan.IsStillPathableFromExitToDest(map, tp)))
            {
                plan.Notify_SkipNetPlanFailedOrCancelled();
            };
        }

        private List<KeyValuePair<Pawn, SkipNetPlan>> SnapshotPawnSkipNetPlans()
        {
            _tempPawnSkipNetPlans.Clear();
            _tempPawnSkipNetPlans.AddRange(pawnSkipNetPlans);

            return _tempPawnSkipNetPlans;
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
                plan = new SkipNetPlan(skipNet, pawn, dest, peMode);
                plan.Initialize(entry, exit);
            };

            return found;
        }
    }
}
