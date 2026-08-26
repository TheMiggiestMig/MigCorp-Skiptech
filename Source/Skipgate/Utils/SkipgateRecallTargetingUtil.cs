using MigCorp.Skiptech.Skipgate.Comps;
using MigCorp.Skiptech.Skipgate.Operations;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    public static class SkipgateRecallTargetingUtil
    {
        private static readonly List<PlanetTile> tmpBeaconTiles = new List<PlanetTile>();

        public static void BeginRecallTargeting(CompSkipgate source, SkipgateRecallMode mode)
        {
            List<Thing> candidates = SkipBeaconUtil.TargetableBeacons();

            if (candidates.Count == 0)
            {
                Messages.Message("Nobody is carrying a skip beacon.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            CameraJumper.TryJump(CameraJumper.GetWorldTarget(source.parent));
            Find.WorldSelector.ClearSelection();

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo t) => ChoseRecallTarget(source, candidates, mode, t),
                canTargetTiles: true,
                mouseAttachment: CompLaunchable.TargeterMouseAttachment,
                closeWorldTabWhenFinished: true,
                onUpdate: () => DrawBeaconHighlights(candidates),
                extraLabelGetter: t => RecallHoverLabel(source, candidates, mode, t),
                canSelectTarget: t => AnyBeaconAt(candidates, t),
                originForClosest: source.parent.Map.Tile,
                showCancelButton: true);
        }

        private static bool AnyBeaconAt(List<Thing> candidates, GlobalTargetInfo target)
        {
            if (!target.IsValid) { return false; }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (SkipBeaconUtil.TileOf(candidates[i]) == target.Tile) { return true; }
            }

            return false;
        }

        private static List<Thing> BeaconsAt(List<Thing> candidates, GlobalTargetInfo target)
        {
            List<Thing> here = new List<Thing>();
            if (!target.IsValid) { return here; }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (SkipBeaconUtil.TileOf(candidates[i]) == target.Tile) { here.Add(candidates[i]); }
            }

            return here;
        }

        private static void DrawBeaconHighlights(List<Thing> candidates)
        {
            tmpBeaconTiles.Clear();

            for (int i = 0; i < candidates.Count; i++)
            {
                PlanetTile tile = SkipBeaconUtil.TileOf(candidates[i]);

                if (tile.Valid && !tmpBeaconTiles.Contains(tile)) { tmpBeaconTiles.Add(tile); }
            }

            DrawCandidateHighlights(tmpBeaconTiles);
        }

        private static string RecallOptionLabel(CompSkipgate source, Thing beacon, SkipgateRecallMode mode)
        {
            Pawn holder = SkipBeaconUtil.HolderOf(beacon);
            Caravan caravan = SkipBeaconUtil.CaravanOf(beacon);

            string where = caravan != null
                ? caravan.LabelCap
                : SkipBeaconUtil.MapOf(beacon)?.Parent?.LabelCap ?? "unknown location";

            int count = SkipBeaconUtil.YoinkSet(beacon).Count;

            return mode == SkipgateRecallMode.Emergency
                ? $"{holder.LabelShortCap} ({where}) — {count} coming through"
                : $"{holder.LabelShortCap} ({where}) — {count} coming through, cost {SkipgateCostUtil.CalculateRecallCost(source, beacon):F0}";
        }

        private static TaggedString RecallHoverLabel(CompSkipgate source, List<Thing> candidates, SkipgateRecallMode mode, GlobalTargetInfo target)
        {
            List<Thing> beacons = BeaconsAt(candidates, target);

            if (beacons.Count == 0) { return null; }
            if (beacons.Count > 1) { return $"{beacons.Count} skip beacons"; }

            Thing beacon = beacons[0];

            if (mode == SkipgateRecallMode.Emergency)
            {
                return $"{RecallOptionLabel(source, beacon, mode)}\nNo charge required — dials in {source.Props.dialingTicks.ToStringTicksToPeriod()}";
            }

            float cost = SkipgateCostUtil.CalculateRecallCost(source, beacon);
            string chargeTime = source.Capacitor.EstimateChargeTicks(cost).ToStringTicksToPeriod();
            float watts = source.Capacitor.WattsForCost(cost);
            float cooldownSeconds = source.CooldownTicksFor(cost * source.Props.heatPerCost) / 60f;

            return $"{RecallOptionLabel(source, beacon, mode)}\n{watts:F0}W for {chargeTime} — cooldown {cooldownSeconds:F0}s";
        }

        private static bool ChoseRecallTarget(CompSkipgate source, List<Thing> candidates, SkipgateRecallMode mode, GlobalTargetInfo target)
        {
            List<Thing> beacons = BeaconsAt(candidates, target);

            if (beacons.Count == 0)
            {
                Messages.Message("No skip beacon there.", MessageTypeDefOf.RejectInput, historical: false);
                return false; // keep the targeter open
            }

            if (beacons.Count == 1)
            {
                TryStartRecall(source, beacons[0], mode);
                return true; // close the targeter
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            for (int i = 0; i < beacons.Count; i++)
            {
                Thing beacon = beacons[i];

                options.Add(new FloatMenuOption(RecallOptionLabel(source, beacon, mode), () =>
                {
                    // Same as Link, the option closes the targeter itself because it runs long after this returns.
                    if (TryStartRecall(source, beacon, mode)) { Find.WorldTargeter.StopTargeting(); }
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options) { vanishIfMouseDistant = false });

            return false;
        }

        public static bool TryStartRecall(CompSkipgate source, Thing beacon, SkipgateRecallMode mode)
        {
            SkipgateOperation_Recall recall = new SkipgateOperation_Recall(source, beacon, mode);

            // TryStartOperation runs CanStart and messages any refusal for us.
            if (!source.TryStartOperation(recall)) { return false; }

            Messages.Message(
                $"{source.GateLabel} is charging to recall {recall.TargetLabel} — cost {recall.RequiredCharge:F0}.",
                source.parent,
                MessageTypeDefOf.TaskCompletion,
                historical: false);

            return true;
        }

        public static IEnumerable<CompSkipgate> FindRecallGateCandidates(SkipgateRecallMode mode)
        {
            foreach (Map map in Find.Maps)
            {
                if (map.IsPocketMap) { continue; }

                foreach (Building_Skipgate gate in map.listerBuildings.AllBuildingsColonistOfClass<Building_Skipgate>())
                {
                    CompSkipgate comp = gate.skipgateComp;

                    if (comp == null || comp.CurrentOperation != null) { continue; }
                    if (comp.CoolingDown && mode != SkipgateRecallMode.Emergency) { continue; }

                    if (!(mode == SkipgateRecallMode.Emergency ? comp.EmergencyRecallUnlocked : comp.RecallUnlocked)) { continue; }

                    yield return comp;
                }
            }
        }

        private static string RecallGateOptionLabel(CompSkipgate gate, Thing beacon, SkipgateRecallMode mode)
        {
            float tiles = SkipgateCostUtil.TilesBetween(gate, SkipBeaconUtil.TileOf(beacon));

            return mode == SkipgateRecallMode.Emergency
                ? $"{gate.GateLabel} ({Mathf.RoundToInt(tiles)} tiles)"
                : $"{gate.GateLabel} ({Mathf.RoundToInt(tiles)} tiles) — cost {SkipgateCostUtil.CalculateRecallCost(gate, beacon):F0}";
        }

        // Right-click on the far-side gizmo.
        public static IEnumerable<FloatMenuOption> GetRecallGateOptions(Thing beacon, SkipgateRecallMode mode)
        {
            bool found = false;

            foreach (CompSkipgate gate in FindRecallGateCandidates(mode).OrderBy(g => g.GateLabel))
            {
                found = true;
                CompSkipgate target = gate;

                yield return new FloatMenuOption(RecallGateOptionLabel(target, beacon, mode), () => TryStartRecall(target, beacon, mode));
            }

            if (!found) { yield return new FloatMenuOption("No skipgate is available", null); }
        }

        // Left-click on the far-side gizmo.
        public static void BeginRecallGateTargeting(Thing beacon, SkipgateRecallMode mode)
        {
            List<CompSkipgate> candidates = FindRecallGateCandidates(mode).ToList();

            if (candidates.Count == 0)
            {
                Messages.Message("No skipgate is available.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            List<PlanetTile> candidateTiles = candidates.Select(g => g.parent.Map.Tile).Distinct().ToList();

            CameraJumper.TryJump(SkipBeaconUtil.TileOf(beacon));
            Find.WorldSelector.ClearSelection();

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo t) => ChoseRecallGate(beacon, candidates, mode, t),
                canTargetTiles: true,
                mouseAttachment: CompLaunchable.TargeterMouseAttachment,
                closeWorldTabWhenFinished: true,
                onUpdate: () => DrawCandidateHighlights(candidateTiles),
                extraLabelGetter: t => RecallGateHoverLabel(beacon, candidates, mode, t),
                canSelectTarget: t => GatesAt(candidates, t).Any(),
                originForClosest: SkipBeaconUtil.TileOf(beacon),
                showCancelButton: true);
        }

        private static List<CompSkipgate> GatesAt(List<CompSkipgate> candidates, GlobalTargetInfo target)
        {
            List<CompSkipgate> here = new List<CompSkipgate>();
            if (!target.IsValid) { return here; }

            for (int i = 0; i < candidates.Count; i++)
            {
                Map map = candidates[i].parent.Map;

                if (map != null && map.Tile == target.Tile) { here.Add(candidates[i]); }
            }

            return here;
        }

        private static TaggedString RecallGateHoverLabel(Thing beacon, List<CompSkipgate> candidates, SkipgateRecallMode mode, GlobalTargetInfo target)
        {
            List<CompSkipgate> gates = GatesAt(candidates, target);

            if (gates.Count == 0) { return null; }
            if (gates.Count > 1) { return $"{gates.Count} skipgates"; }

            return RecallGateOptionLabel(gates[0], beacon, mode);
        }

        private static bool ChoseRecallGate(Thing beacon, List<CompSkipgate> candidates, SkipgateRecallMode mode, GlobalTargetInfo target)
        {
            List<CompSkipgate> gates = GatesAt(candidates, target);

            if (gates.Count == 0)
            {
                Messages.Message("No available skipgate there.", MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            if (gates.Count == 1)
            {
                TryStartRecall(gates[0], beacon, mode);
                return true;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            for (int i = 0; i < gates.Count; i++)
            {
                CompSkipgate gate = gates[i];

                options.Add(new FloatMenuOption(RecallGateOptionLabel(gate, beacon, mode), () =>
                {
                    if (TryStartRecall(gate, beacon, mode)) { Find.WorldTargeter.StopTargeting(); }
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options) { vanishIfMouseDistant = false });

            return false;
        }
        private static void DrawCandidateHighlights(List<PlanetTile> tiles)
        {
            foreach (PlanetTile tile in tiles)
            {
                // Other-layer tiles are in that layer's coordinate space — skip them for now.
                // TODO make this work for cross layers (looking at you, "Odyssey" =.=)
                if (tile.Layer != PlanetLayer.Selected) { continue; }

                WorldRendererUtility.DrawQuadTangentialToPlanet(
                    Find.WorldGrid.GetTileCenter(tile),
                    0.8f * Find.WorldGrid.AverageTileSize,
                    0.018f, // below the hover marker's 0.05 so hover draws on top
                    WorldMaterials.CurTargetingMat);
            }
        }
    }
}