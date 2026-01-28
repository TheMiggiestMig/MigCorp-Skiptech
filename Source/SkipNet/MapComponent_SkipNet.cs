using System.Collections.Generic;
using Verse;
using MigCorp.Skiptech.SkipNet.Comps;

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
        public SkipNetPlanner planner;
        public Dictionary<Pawn, SkipNetPlan> pawnSkipNetPlans;
        private readonly List<KeyValuePair<Pawn, SkipNetPlan>> _tempPawnSkipNetPlans = new List<KeyValuePair<Pawn, SkipNetPlan>>(); // Snapshot for the pawnSkipNetPlans to prevent mutating the table mid loop.

        public List<Pawn> disposedPawnSkipNetPlans;
        public int lastSkipNetPlanDeepCleanTick;

        // Buffers
        private List<CompSkipdoor> tmpEnterableSkipdoors;
        private List<CompSkipdoor> tmpExitableSkipdoors;


        public MapComponent_SkipNet(Map map) : base(map)
        {
            skipdoors = new List<CompSkipdoor>();

            pawnSkipNetPlans = new Dictionary<Pawn, SkipNetPlan>();
            disposedPawnSkipNetPlans = new List<Pawn>();

            tmpEnterableSkipdoors = new List<CompSkipdoor>();
            tmpExitableSkipdoors = new List<CompSkipdoor>();

            planner = new SkipNetPlanner(this);
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            ResolveActivePlans();
            Cleanup();
        }

        /// <summary>
        /// Registers a skipdoor to the SkipNet.
        /// </summary>
        public void RegisterSkipdoor(CompSkipdoor skipdoor)
        {
            if (skipdoors.Contains(skipdoor))
            {
                MigcorpSkiptechMod.Warning($"Attempted to register already registered skipdoor at {skipdoor.Position}.");
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

        //

        /// <summary>
        /// Returns <c>true</c> if there is one or more skipdoors the <c>pawn</c> can enter.
        /// </summary>
        /// <remarks>
        /// If <c>skipdoorListToFilter</c> is provided, that list is filtered instead of the whole SkipNet registered set of skipdoors.
        /// </remarks>
        /// <param name="pawn">Pawn to check</param>
        /// <param name="enterableSkipdoors">Found list of skipdoors</param>
        /// <param name="skipdoorListToFilter">(Optional) List of skipdoors to filter.</param>
        /// <returns></returns>
        public bool TryGetEnterableSkipdoors(Pawn pawn, out List<CompSkipdoor> enterableSkipdoors, List<CompSkipdoor> skipdoorListToFilter = null)
        {
            enterableSkipdoors = tmpEnterableSkipdoors;
            enterableSkipdoors.Clear();

            if (pawn == null) { return false; }

            List<CompSkipdoor> skipdoors = skipdoorListToFilter ?? this.skipdoors;

            foreach (CompSkipdoor skipdoor in skipdoors)
            {
                if (skipdoor.IsEnterableBy(pawn)) { enterableSkipdoors.Add(skipdoor); }
            }

            return enterableSkipdoors.Count > 0;
        }

        /// <summary>
        /// Returns <c>true</c> if there is one or more skipdoors the <c>pawn</c> can exit.
        /// </summary>
        /// <remarks>
        /// If <c>skipdoorListToFilter</c> is provided, that list is filtered instead of the whole SkipNet registered set of skipdoors.
        /// </remarks>
        /// <param name="pawn">Pawn to check</param>
        /// <param name="exitableSkipdoors">Found list of skipdoors</param>
        /// <param name="skipdoorListToFilter">(Optional) List of skipdoors to filter.</param>
        /// <returns></returns>
        public bool TryGetExitableSkipdoors(Pawn pawn, out List<CompSkipdoor> exitableSkipdoors, List<CompSkipdoor> skipdoorListToFilter = null)
        {
            exitableSkipdoors = tmpExitableSkipdoors;
            exitableSkipdoors.Clear();

            if (pawn == null) { return false; }

            List<CompSkipdoor> skipdoors = skipdoorListToFilter ?? this.skipdoors;

            foreach (CompSkipdoor skipdoor in skipdoors)
            {
                if (skipdoor.IsExitableBy(pawn)) { exitableSkipdoors.Add(skipdoor); }
            }

            return exitableSkipdoors.Count > 0;
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