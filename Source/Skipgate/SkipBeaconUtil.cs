using MigCorp.Skiptech.Comps;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    // Finding skip beacons, and working out what is standing next to one.
    public static class SkipBeaconUtil
    {
        private static List<ThingDef> beaconDefs;

        public static List<ThingDef> BeaconDefs // In case someone mods more beacons in.
        {
            get
            {
                if (beaconDefs == null)
                {
                    beaconDefs = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(def => def.HasComp<CompSkipBeacon>())
                        .ToList();
                }

                return beaconDefs;
            }
        }
        public static CompSkipBeacon BeaconComp(Thing thing) => thing?.TryGetComp<CompSkipBeacon>();
        public static float RadiusOf(Thing beacon) => BeaconComp(beacon)?.Radius ?? 0f;
        public static Caravan CaravanOf(Thing beacon) => ThingOwnerUtility.GetAnyParent<Caravan>(beacon);
        public static Pawn HolderOf(Thing beacon) => ThingOwnerUtility.GetAnyParent<Pawn>(beacon);
        public static Pawn WearerOf(Thing beacon) => (beacon as Apparel)?.Wearer;
        public static Map MapOf(Thing beacon) => beacon.MapHeld;

        public static PlanetTile TileOf(Thing beacon)
        {
            Caravan caravan = CaravanOf(beacon);
            if (caravan != null) { return caravan.Tile; }

            Map map = MapOf(beacon);

            return map != null ? map.Tile : PlanetTile.Invalid;
        }

        public static bool IsTargetable(Thing beacon)
        {
            if (BeaconComp(beacon) == null) { return false; }
            if (!TileOf(beacon).Valid) { return false; }

            Pawn wearer = (beacon as Apparel)?.Wearer;

            return wearer != null && wearer.Faction == Faction.OfPlayer;
        }

        public static void BeaconsInCaravan(Caravan caravan, List<Thing> outBeacons)
        {
            outBeacons.Clear();

            List<Pawn> pawns = caravan.PawnsListForReading;
            for (int i = 0; i < pawns.Count; i++)
            {
                List<Apparel> worn = pawns[i].apparel?.WornApparel;
                if (worn == null) { continue; }

                for (int j = 0; j < worn.Count; j++)
                {
                    if (BeaconComp(worn[j]) != null) { outBeacons.Add(worn[j]); }
                }
            }
        }

        public static void BeaconsOnMap(Map map, List<Thing> outBeacons)
        {
            outBeacons.Clear();

            List<ThingDef> defs = BeaconDefs;
            List<Thing> found = new List<Thing>();

            for (int i = 0; i < defs.Count; i++)
            {
                ThingOwnerUtility.GetAllThingsRecursively<Thing>(map, ThingRequest.ForDef(defs[i]), found);
                outBeacons.AddRange(found);
            }
        }

        public static List<Thing> TargetableBeacons()
        {
            List<Thing> beacons = new List<Thing>();
            List<Thing> found = new List<Thing>();

            List<Caravan> caravans = Find.WorldObjects.Caravans;
            for (int i = 0; i < caravans.Count; i++)
            {
                if (!caravans[i].IsPlayerControlled) { continue; }

                BeaconsInCaravan(caravans[i], found);
                beacons.AddRange(found);
            }

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                BeaconsOnMap(maps[i], found);
                beacons.AddRange(found);
            }

            beacons.RemoveAll(beacon => !IsTargetable(beacon));

            return beacons;
        }

        public static bool IsYoinkable(Thing thing)
        {
            if (thing.def.category != ThingCategory.Pawn && thing.def.category != ThingCategory.Item) { return false; }

            // Just like Farskip, don't strand a quest's lodger somewhere the quest can't follow them.
            // It makes the quest system sad.
            return !(thing is Pawn pawn) || !pawn.IsQuestLodger();
        }

        public static List<Thing> YoinkSet(Thing beacon)
        {
            // Yoink the whole caravan. Ez.
            Caravan caravan = CaravanOf(beacon);
            if (caravan != null) { return new List<Thing>(caravan.PawnsListForReading); }

            Map map = MapOf(beacon);
            if (map == null) { return new List<Thing>(); }

            // Take stock of everything in range and add it to the yoink list.
            List<Thing> things = new List<Thing>();

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(beacon.PositionHeld, map, RadiusOf(beacon), useCenter: true))
            {
                if (IsYoinkable(thing)) { things.Add(thing); }
            }

            return things;
        }

        public static float YoinkSetMass(Thing beacon)
        {
            return CollectionsMassCalculator.MassUsage(YoinkSet(beacon), IgnorePawnsInventoryMode.DontIgnore, includePawnsMass: true);
        }
    }
}