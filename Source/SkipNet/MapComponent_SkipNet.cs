using MigCorp.Skiptech.SkipNet.Comps;
using MigCorp.Skiptech.Utils;
using System.Collections.Generic;
using Verse;

namespace MigCorp.Skiptech.SkipNet
{
    public class MapComponent_SkipNet : MapComponent
    {
        // Skipdoors and Regions
        public List<CompSkipdoor> skipdoors;

        // SkipNetPlans
        public SkipNetProposer proposer;
        public SkipNetPlanner planner;
        public SkipNetPathSplicer splicer;

        public MapComponent_SkipNet(Map map) : base(map)
        {
            skipdoors = new List<CompSkipdoor>();

            proposer = new SkipNetProposer(this);
            splicer = new SkipNetPathSplicer(this);
            planner = new SkipNetPlanner(this);
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            proposer.ProcessQueue();
            splicer.Run(); // Should run before planner, otherwise we'll get churn from plans that definitely won't be ready.
            planner.Run();
        }

        public override void MapRemoved()
        {
            splicer.DropAll();
            base.MapRemoved();
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
            planner.MarkRegionDoorIndexDirty();
        }

        /// <summary>
        /// Unregisters a skipdoor from the SkipNet.
        /// </summary>
        public void UnregisterSkipdoor(CompSkipdoor skipdoor)
        {
            skipdoors.Remove(skipdoor);
            planner.MarkRegionDoorIndexDirty();
            planner.CancelPlansUsingSkipdoor(skipdoor);
        }
    }
}