using MigCorp.Skiptech.Skipgate.Comps;
using MigCorp.Skiptech.Skipgate.Operations;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    public static class SkipgateTargetingUtil
    {

        // LINK

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
                $"{source.GateLabel} is charging to link with {target.RenamableLabel}.",
                new LookTargets(source.parent, target),
                MessageTypeDefOf.TaskCompletion,
                historical: false);

            return true;
        }

        private static string OptionLabel(CompSkipgate source, Building_Skipgate gate)
        {
            float cost = SkipgateCostUtil.CalculateLinkCost(source, gate);

            if (gate.Map == source.parent.Map)
            {
                return $"{gate.RenamableLabel} (this map) — cost {cost:F0}";
            }

            string where = gate.Map.Parent?.LabelCap ?? "unknown location";
            float tiles = SkipgateCostUtil.TilesBetween(source, gate.Map.Tile);

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

        // SEND

        // Basically CompLaunchable.GetTransportersFloatMenuOptionsAt, except we give it our "definitely-not-fake-pods" to check against.
        // Also, don't give the "invalid tile -> contents will be lost" option.
        public static IEnumerable<FloatMenuOption> GetSendFloatMenuOptionsAt(SkipgateOperation_Send send, PlanetTile tile, Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            // bool anything = false; // Used by vanilla to determine if contents will be lost. We will always be sending pawns, so we don't even want that as an option.
            IEnumerable<IThingHolder> definitely_not_fake_pods = send.RosterHolders();

            if (TransportersArrivalAction_FormCaravan.CanFormCaravanAt(definitely_not_fake_pods, tile) && !Find.WorldObjects.AnySettlementBaseAt(tile) && !Find.WorldObjects.AnySiteAt(tile))
            {
                PlanetTile tileLocal = tile;
                yield return new FloatMenuOption("FormCaravanHere".Translate(), () => launchAction(tileLocal, new TransportersArrivalAction_FormCaravan()));
            }

            List<WorldObject> worldObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i].Tile != tile) { continue; }

                foreach (FloatMenuOption option in SkipCellOptionsFor(worldObjects[i], definitely_not_fake_pods, launchAction))
                {
                    yield return option;
                }
            }

            /*
            // Yeah... don't do this.
            if (!anything && !Find.World.Impassable(tile))
            {
                yield return new FloatMenuOption("TransportPodsContentsWillBeLost".Translate(), delegate
                {
                    launchAction(tile, null);
                });
            }
            */
        }

        // Quick filter to strip out cell targeting options and replace them with our own,
        // since Vanilla's TargetingParameters.ForDropPodsDestination() and DropCellFinder.CanPhysicallyDropInto dont let us choose
        // overhead mountain or other "undrop-podable" tiles.
        private static IEnumerable<FloatMenuOption> SkipCellOptionsFor(WorldObject worldObject, IEnumerable<IThingHolder> pods, Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            MapParent mapParent = worldObject as MapParent;
            bool canLandInCell = mapParent != null && TransportersArrivalAction_LandInSpecificCell.CanLandInSpecificCell(pods, mapParent);

            // Return all the options, but skip out on the "LandInExistingMap" ones (ones that target the cells in a map).
            // Messy way to do it with "string matching" on the option Label, but not sure how else to do it without funky patches.
            string vanillaLabel = canLandInCell ? ((string)"LandInExistingMap".Translate(mapParent.Label)).TrimEnd() : null;

            // Also skip gifting options. Skipgate caravans always have colony pawns. Just use drop pods if you want to get rid of Timmy.
            Settlement settlement = worldObject as Settlement;
            string giftLabel = settlement != null && settlement.Faction != null && settlement.Faction != Faction.OfPlayer
                ? ((string)"GiveGiftViaTransportPods".Translate(settlement.Faction.Name, FactionGiftUtility.GetGoodwillChange(pods, settlement).ToStringWithSign())).TrimEnd()
                : null;

            foreach (FloatMenuOption option in worldObject.GetTransportersFloatMenuOptions(pods, launchAction))
            {
                if (vanillaLabel != null && option.Label == vanillaLabel) { continue; }
                if (giftLabel != null && option.Label == giftLabel) { continue; }

                yield return option;
            }

            if (!canLandInCell) { yield break; }

            // Since we've stripped out the "cell landing" options, replace them with our own.
            MapParent mapParentLocal = mapParent;
            yield return new FloatMenuOption(vanillaLabel, () => BeginSkipCellTargeting(mapParentLocal, launchAction));
        }

        // Basically the same option setup as MapParent.GetTransportersFloatMenuOptions', but with a less restrictive TargetingParameters.validator.
        // Only checks if the cell is in bounds, standable, not fogged. More importantly, doesn't care about the (overhead mountain) roof.
        // The arrival action is ours too — vanilla's does the same roof check again on the way in.
        private static void BeginSkipCellTargeting(MapParent mapParent, Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            Current.Game.CurrentMap = mapParent.Map;
            CameraJumper.TryHideWorld();

            TargetingParameters parameters = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetSelf = false,
                canTargetPawns = false,
                canTargetFires = false,
                canTargetBuildings = false,
                canTargetItems = false,
                validator = (TargetInfo t) => t.Map != null && t.Cell.InBounds(t.Map) && t.Cell.Standable(t.Map) && !t.Cell.Fogged(t.Map)
            };

            MapParent mapParentLocal = mapParent;
            Find.Targeter.BeginTargeting(parameters, delegate (LocalTargetInfo x)
            {
                launchAction(mapParentLocal.Tile, new TransportersArrivalAction_SkipInSpecificCell(mapParentLocal, x.Cell));
            }, null, null, CompLaunchable.TargeterMouseAttachment);
        }

        private static Action<PlanetTile, TransportersArrivalAction> SendLaunchAction(CompSkipgate source, SkipgateOperation_Send send)
        {
            return delegate (PlanetTile tile, TransportersArrivalAction action)
            {
                // We've committed by this point, so the targeting session is over either way.
                Find.WorldTargeter.StopTargeting();

                AcceptanceReport report = send.TrySetDestination(new GlobalTargetInfo(tile), action);
                if (!report.Accepted)
                {
                    Messages.Message(report.Reason, source.parent, MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }

                Messages.Message(
                     $"{source.GateLabel} is charging to send to {send.DestinationLabel} — cost {send.RequiredCharge:F0}.",
                    source.parent,
                    MessageTypeDefOf.TaskCompletion,
                    historical: false);
            };
        }

        public static void BeginSendTargeting(CompSkipgate source, SkipgateOperation_Send send)
        {
            PlanetTile origin = source.parent.Map.Tile;

            CameraJumper.TryJump(CameraJumper.GetWorldTarget(source.parent));
            Find.WorldSelector.ClearSelection();

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo t) => ChoseSendTarget(source, send, t),
                canTargetTiles: true,
                mouseAttachment: CompLaunchable.TargeterMouseAttachment,
                closeWorldTabWhenFinished: true,
                onUpdate: null,
                extraLabelGetter: t => SendHoverLabel(source, send, t),
                canSelectTarget: t => CanSelectSendTarget(source, send, t),
                originForClosest: origin,
                showCancelButton: true);
        }

        private static bool CanSelectSendTarget(CompSkipgate source, SkipgateOperation_Send send, GlobalTargetInfo target)
        {
            if (!target.IsValid) { return false; }
            if (target.HasWorldObject && !target.WorldObject.def.validLaunchTarget) { return false; }

            return GetSendFloatMenuOptionsAt(send, target.Tile, SendLaunchAction(source, send)).Any();
        }

        // Much simpler than CompLaunchable.ChoseWorldTarget
        private static bool ChoseSendTarget(CompSkipgate source, SkipgateOperation_Send send, GlobalTargetInfo target)
        {
            if (!target.IsValid)
            {
                Messages.Message("MessageTransportPodsDestinationIsInvalid".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            if (target.HasWorldObject && !target.WorldObject.def.validLaunchTarget)
            {
                Messages.Message("MessageWorldObjectIsInvalid".Translate(target.WorldObject.Named("OBJECT")), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            List<FloatMenuOption> options = GetSendFloatMenuOptionsAt(send, target.Tile, SendLaunchAction(source, send)).ToList();

            if (options.Count == 0)
            {
                Messages.Message("MessageTransportPodsDestinationIsInvalid".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            if (options.Count == 1)
            {
                if (options[0].Disabled) { return false; }

                // The option itself stops the targeter (see SendLaunchAction),
                // so don't also return true here or a cell-picking option would be closed out from under us.
                options[0].action();
                return false;
            }

            Find.WindowStack.Add(new FloatMenu(options) { vanishIfMouseDistant = false });
            return false;
        }

        private static TaggedString SendHoverLabel(CompSkipgate source, SkipgateOperation_Send send, GlobalTargetInfo target)
        {
            if (!target.IsValid) { return null; }

            List<FloatMenuOption> options = GetSendFloatMenuOptionsAt(send, target.Tile, SendLaunchAction(source, send)).ToList();
            if (options.Count == 0) { return null; }

            float cost = send.EstimateCostTo(target);

            string chargeTime = source.Capacitor.EstimateChargeTicks(cost).ToStringTicksToPeriod();
            float watts = source.Capacitor.WattsForCost(cost);
            float cooldownSeconds = source.CooldownTicksFor(cost * source.Props.heatPerCost) / 60f;

            string header = options.Count == 1 ? options[0].Label : "Click to see available orders";

            // return $"{header}\ncost {cost:F0} — {watts:F0}W for {chargeSeconds:F0}s — cooldown {cooldownSeconds:F0}s";
            return $"{header}\ncost {cost:F0} — {watts:F0}W for {chargeTime} — cooldown {cooldownSeconds:F0}s";
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

        private static float EstimateRecallCost(CompSkipgate source, Thing beacon, SkipgateRecallMode mode)
        {
            return mode == SkipgateRecallMode.Emergency
                ? source.Props.emergencyRequiredCharge
                : SkipgateCostUtil.CalculateRecallCost(source, beacon);
        }

        private static string RecallOptionLabel(CompSkipgate source, Thing beacon, SkipgateRecallMode mode)
        {
            Pawn holder = SkipBeaconUtil.HolderOf(beacon);
            Caravan caravan = SkipBeaconUtil.CaravanOf(beacon);

            string where = caravan != null
                ? caravan.LabelCap
                : SkipBeaconUtil.MapOf(beacon)?.Parent?.LabelCap ?? "unknown location";

            float cost = EstimateRecallCost(source, beacon, mode);
            int count = SkipBeaconUtil.YoinkSet(beacon).Count;

            return $"{holder.LabelShortCap} ({where}) — {count} coming through, cost {cost:F0}";
        }

        private static TaggedString RecallHoverLabel(CompSkipgate source, List<Thing> candidates, SkipgateRecallMode mode, GlobalTargetInfo target)
        {
            List<Thing> beacons = BeaconsAt(candidates, target);

            if (beacons.Count == 0) { return null; }
            if (beacons.Count > 1) { return $"{beacons.Count} skip beacons"; }

            Thing beacon = beacons[0];
            bool emergency = mode == SkipgateRecallMode.Emergency;
            float cost = EstimateRecallCost(source, beacon, mode);

            // Emergency's wind-up is fixed, so its wattage comes off emergencyChargeTicks, not the usual rate.
            string chargeTime = (emergency ? source.Props.emergencyChargeTicks : source.Capacitor.EstimateChargeTicks(cost)).ToStringTicksToPeriod();
            float watts = emergency
                ? source.Capacitor.WattsForCost(cost, source.Props.emergencyChargeTicks)
                : source.Capacitor.WattsForCost(cost);
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
    }
}