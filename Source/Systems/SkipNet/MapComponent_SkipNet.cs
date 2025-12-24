using System.Collections.Generic;
using Verse;
using MigCorp.Skiptech.Comps;
using System;
using Verse.AI;

namespace MigCorp.Skiptech.Systems.SkipNet
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
        public List<Pawn> disposedPawnSkipNetPlans;
        public List<Pawn> invalidPawnSkipNetPlans;
        public int lastSkipNetPlanDeepCleanTick;
        private int ticksBetweenSkipNetPlanDeepClean = 180;

        // Buffers
        private List<CompSkipdoor> tmpEnterableSkipdoors;
        private List<CompSkipdoor> tmpExitableSkipdoors;


        public MapComponent_SkipNet(Map map) : base(map)
        {
            skipdoors = new List<CompSkipdoor>();

            pawnSkipNetPlans = new Dictionary<Pawn, SkipNetPlan>();
            disposedPawnSkipNetPlans = new List<Pawn>();
            invalidPawnSkipNetPlans = new List<Pawn>();

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
        }

        /// <summary>
        /// Unregisters a skipdoor from the SkipNet.
        /// </summary>
        public void UnregisterSkipdoor(CompSkipdoor skipdoor)
        {
            skipdoors.Remove(skipdoor);
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
            enterableSkipdoors = this.tmpEnterableSkipdoors;
            enterableSkipdoors.Clear();

            if (pawn == null) { return false; }

            List<CompSkipdoor> skipdoors = skipdoorListToFilter ?? this.skipdoors;

            foreach (CompSkipdoor skipdoor in skipdoors)
            {
                if (skipdoor.CanEnter(pawn)) { enterableSkipdoors.Add(skipdoor); }
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
            exitableSkipdoors = this.tmpExitableSkipdoors;
            exitableSkipdoors.Clear();

            if (pawn == null) { return false; }

            List<CompSkipdoor> skipdoors = skipdoorListToFilter ?? this.skipdoors;

            foreach (CompSkipdoor skipdoor in skipdoors)
            {
                if (skipdoor.CanExit(pawn)) { exitableSkipdoors.Add(skipdoor); }
            }

            return exitableSkipdoors.Count > 0;
        }

        public bool TryGetSkipNetPlan(Pawn pawn, out SkipNetPlan plan, bool force = false)
        {
            if (pawn == null ||
                !pawnSkipNetPlans.TryGetValue(pawn, out plan) ||
                (!force && plan.State == SkipNetPlanState.Disposed)
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
            foreach ((Pawn pawn, SkipNetPlan skipNetPlan) in pawnSkipNetPlans)
            {
                if (skipNetPlan.State != SkipNetPlanState.Disposed && skipNetPlan.arrived)
                    skipNetPlan.Resolve();
            }
        }

        public void Cleanup()
        {
            CleanupInvalid();
            AttemptRepathOnInvalidSkipNetPlans();

            if (GenTicks.TicksGame > lastSkipNetPlanDeepCleanTick + ticksBetweenSkipNetPlanDeepClean)
            {
                DeepCleanup();
                lastSkipNetPlanDeepCleanTick = GenTicks.TicksGame;
            }
            RemoveDisposedSkipNetPlans();

            invalidPawnSkipNetPlans.Clear();
            disposedPawnSkipNetPlans.Clear();
        }

        public void CleanupInvalid()
        {
            foreach ((Pawn pawn, SkipNetPlan plan) in pawnSkipNetPlans)
            {
                if (plan.State == SkipNetPlanState.Invalid)
                {
                    plan.Dispose();
                    continue;
                }

                if (plan.State != SkipNetPlanState.Disposed &&
                !plan.CheckIsStillAccessible())
                {
                    plan.Dispose();
                    invalidPawnSkipNetPlans.Add(pawn);
                };
            }
        }

        public void AttemptRepathOnInvalidSkipNetPlans()
        {
            foreach (Pawn pawn in invalidPawnSkipNetPlans)
            {
                if (pawn != null && pawn.Spawned && pawnSkipNetPlans.TryGetValue(pawn, out SkipNetPlan plan) &&
                    plan?.originalDest != null && plan.originalPeMode != PathEndMode.None)
                {
                    pawn.pather.StartPath(plan.originalDest, plan.originalPeMode);
                }
            }
        }

        public void RemoveDisposedSkipNetPlans()
        {
            foreach (Pawn pawn in disposedPawnSkipNetPlans)
            {
                if (pawnSkipNetPlans.TryGetValue(pawn, out SkipNetPlan plan) &&
                    plan.State == SkipNetPlanState.Disposed
                    )
                {
                    pawnSkipNetPlans.Remove(pawn);
                }
            }
        }

        public void DeepCleanup()
        {
            foreach ((Pawn pawn, SkipNetPlan plan) in pawnSkipNetPlans)
            {
                if (pawn?.Map != map ||
                    plan.State == SkipNetPlanState.Disposed
                    )
                {
                    disposedPawnSkipNetPlans.Add(pawn);
                }
            }
        }
    }
}