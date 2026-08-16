using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld.Planet;
using System;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    // One home for the skipgate distance/cost formulas.
    // Send, Link, Recall and the targeting labels all quote these.
    public static class SkipgateCostUtil
    {
        public static float TilesBetween(CompSkipgate gate, PlanetTile tile)
        {
            if (!tile.Valid || gate.parent.Map == null) { return 0f; }

            return Find.WorldGrid.ApproxDistanceInTiles(gate.parent.Map.Tile, tile);
        }

        public static float TilesBetween(CompSkipgate gate, GlobalTargetInfo target)
        {
            return target.IsValid ? TilesBetween(gate, target.Tile) : 0f;
        }

        public static float CalculateSendCost(CompSkipgate gate, float mass, float tiles)
        {
            CompProperties_Skipgate props = gate.Props;

            return props.sendCostBase + mass * (props.sendCostPerKg + props.sendCostPerKgPerTile * tiles);
        }

        public static void CalculateRecallCost(CompSkipgate gate, float mass, float tiles)
        {
            throw new NotImplementedException();
        }

        public static float CalculateLinkCost(CompSkipgate gate, Building_Skipgate target)
        {
            return gate.Props.linkCostBase + gate.Props.linkCostPerTile * TilesBetween(gate, target.Map.Tile);
        }
    }
}