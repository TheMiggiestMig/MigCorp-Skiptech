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
    public static class SkipgateTargetingUtil
    {
        public static IEnumerable<Building_Skipgate> FindLinkCandidates(CompSkipgate source)
        {
            foreach (Map map in Find.Maps)
            {
                if (map.IsPocketMap) { continue; }

                foreach (Building_Skipgate gate in map.listerBuildings.AllBuildingsColonistOfClass<Building_Skipgate>())
                {
                    CompSkipgate comp = gate.skipgateComp;

                    if (gate == source.parent || comp == null) { continue; }
                    if (comp.CurrentOperation != null || comp.CoolingDown) { continue; }

                    yield return gate;
                }
            }
        }

        public static bool TryStartLink(CompSkipgate source, Building_Skipgate target)
        {
            if (!source.TryStartOperation(new SkipgateOperation_Link(source, target))) { return false; }

            Messages.Message(
                $"{(source.parent as Building_Skipgate).RenamableLabel} is charging to link with {target.RenamableLabel}.",
                new LookTargets(source.parent, target),
                MessageTypeDefOf.TaskCompletion,
                historical: false);

            return true;
        }

        private static string OptionLabel(CompSkipgate source, Building_Skipgate gate)
        {
            float cost = SkipgateOperation_Link.CalculateLinkCost(source, gate);

            if (gate.Map == source.parent.Map)
            {
                return $"{gate.RenamableLabel} (this map) — cost {cost:F0}";
            }

            string where = gate.Map.Parent?.LabelCap ?? "unknown location";
            float tiles = Find.WorldGrid.ApproxDistanceInTiles(source.parent.Map.Tile, gate.Map.Tile);

            return $"{gate.RenamableLabel} ({where}, {Mathf.RoundToInt(tiles)} tiles) — cost {cost:F0}";
        }

        // Right-click targeting.
        public static IEnumerable<FloatMenuOption> GetNamedTargetOptions(CompSkipgate source)
        {
            bool found = false;
            foreach (Building_Skipgate gate in FindLinkCandidates(source).OrderBy(g => g.RenamableLabel))
            {
                found = true;
                Building_Skipgate target = gate;
                yield return new FloatMenuOption(OptionLabel(source, target), () => TryStartLink(source, target));
            }

            if (!found) { yield return new FloatMenuOption("No other skipgates available", null); }
        }

        // World targeting (left-click).
        public static void BeginLinkTargeting(CompSkipgate source)
        {
            CameraJumper.TryJump(CameraJumper.GetWorldTarget(source.parent));

            List<PlanetTile> candidateTiles = FindLinkCandidates(source)
                .Select(g => g.Map.Tile).Distinct().ToList();

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo t) => TrySelectWorldTile(source, t),
                canTargetTiles: true,
                closeWorldTabWhenFinished: true,
                onUpdate: () => DrawCandidateHighlights(candidateTiles),
                extraLabelGetter: t => HoverLabel(source, t),
                canSelectTarget: t => SkipgatesAt(source, t).Any(),
                showCancelButton: true);
        }

        private static List<Building_Skipgate> SkipgatesAt(CompSkipgate source, GlobalTargetInfo target)
        {
            MapParent mapParent = target.WorldObject as MapParent
                ?? Find.WorldObjects.MapParentAt(target.Tile);

            if (mapParent == null || !mapParent.HasMap) { return new List<Building_Skipgate>(); }

            return FindLinkCandidates(source).Where(gate => gate.Map == mapParent.Map).ToList();
        }

        private static bool TrySelectWorldTile(CompSkipgate source, GlobalTargetInfo target)
        {
            List<Building_Skipgate> gates = SkipgatesAt(source, target);

            if (gates.Count == 0)
            {
                Messages.Message("No linkable skipgate there.", MessageTypeDefOf.RejectInput, historical: false);
                return false; // keep the targeter open
            }

            if (gates.Count == 1)
            {
                TryStartLink(source, gates[0]);
                return true; // close the targeter
            }

            // Multiple gates on that map — list them.
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Building_Skipgate gate in gates)
            {
                Building_Skipgate target2 = gate;
                options.Add(new FloatMenuOption(OptionLabel(source, target2), () =>
                {
                    if (TryStartLink(source, target2))
                    {
                        // The option must close the targeter itself.
                        // Returning true below would exit early on the first click otherwise.
                        Find.WorldTargeter.StopTargeting();
                    }
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options) { vanishIfMouseDistant = false });
            return false;
        }

        private static TaggedString HoverLabel(CompSkipgate source, GlobalTargetInfo target)
        {
            List<Building_Skipgate> gates = SkipgatesAt(source, target);

            if (gates.Count == 0) { return null; }
            if (gates.Count == 1) { return $"Link to {gates[0].RenamableLabel}"; }

            return $"{gates.Count} skipgates";
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