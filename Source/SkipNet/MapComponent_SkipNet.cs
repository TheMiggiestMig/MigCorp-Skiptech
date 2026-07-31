using MigCorp.Skiptech.SkipNet.Comps;
using MigCorp.Skiptech.Utils;
using System.Collections.Generic;
using Verse;

namespace MigCorp.Skiptech.SkipNet
{
    public enum SkipdoorType
    {
        None,
        Entry,
        Exit
    }

    public class MapComponent_SkipNet : MapComponent
    {
        // Skipdoors and Regions
        public List<CompSkipdoor> skipdoors;

        // SkipNetPlans
        public SkipNetProposer proposer;
        public SkipNetPlanner planner;
        public Dictionary<Pawn, SkipNetPlan> pawnSkipNetPlans;
        private readonly List<KeyValuePair<Pawn, SkipNetPlan>> _tempPawnSkipNetPlans = new List<KeyValuePair<Pawn, SkipNetPlan>>(); // Snapshot for the pawnSkipNetPlans to prevent mutating the table mid loop.

        public List<Pawn> disposedPawnSkipNetPlans;
        public int lastSkipNetPlanDeepCleanTick;


        public MapComponent_SkipNet(Map map) : base(map)
        {
            skipdoors = new List<CompSkipdoor>();

            pawnSkipNetPlans = new Dictionary<Pawn, SkipNetPlan>();
            disposedPawnSkipNetPlans = new List<Pawn>();

            proposer = new SkipNetProposer(this);
            planner = new SkipNetPlanner(this);
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            ResolveActivePlans();
            proposer.ProcessQueue();
            Cleanup();
        }

        /// <summary>
        /// Registers a skipdoor to the SkipNet.
        /// </summary>
        public void RegisterSkipdoor(CompSkipdoor skipdoor)
        {
            if (skipdoors.Contains(skipdoor))
            {
                SkiptechUtil.Warning($"Attempted to register already registered skipdoor at {skipdoor.Position}.");
                return;
            }
            skipdoors.Add(skipdoor);
            planner.RebuildRegionDoorIndex();
        }

        /// <summary>
        /// Unregisters a skipdoor from the SkipNet.
        /// </summary>
        public void UnregisterSkipdoor(CompSkipdoor skipdoor)
        {
            skipdoors.Remove(skipdoor);
            planner.RebuildRegionDoorIndex();

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
    }
}