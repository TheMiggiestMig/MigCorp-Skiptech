using MigCorp.Skiptech.SkipNet.Comps;
using MigCorp.Skiptech.Utils;
using System.Collections.Generic;
using Verse;

namespace MigCorp.Skiptech.SkipNet
{
    public class MapComponent_SkipNet : MapComponent
    {
        // Caching lookup, since we're operating in a hot-path now i.e. TryEnterNextPathCell
        private static Map cachedMap;
        private static MapComponent_SkipNet cachedComp;

        public static MapComponent_SkipNet For(Map map)
        {
            if (map == null) { return null; }
            if (map != cachedMap)
            {
                cachedMap = map;
                cachedComp = map.GetComponent<MapComponent_SkipNet>();
            }
            return cachedComp;
        }

        // Skipdoors and Regions
        public List<CompSkipdoor> skipdoors;

        // SkipNetPlans
        public SkipNetManager manager;

        public MapComponent_SkipNet(Map map) : base(map)
        {
            skipdoors = new List<CompSkipdoor>();

            manager = new SkipNetManager(this);
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            manager.Tick();
        }

        public override void MapRemoved()
        {
            if (cachedMap == map)
            {
                cachedMap = null;
                cachedComp = null;
            }
            manager.DropAll();
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
            manager.Notify_SkipdoorRegistered(skipdoor);
        }

        /// <summary>
        /// Unregisters a skipdoor from the SkipNet.
        /// </summary>
        public void UnregisterSkipdoor(CompSkipdoor skipdoor)
        {
            skipdoors.Remove(skipdoor);
            manager.Notify_SkipdoorUnregistered(skipdoor);
        }
    }
}