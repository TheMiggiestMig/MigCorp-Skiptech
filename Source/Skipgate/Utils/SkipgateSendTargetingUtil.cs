using MigCorp.Skiptech.Skipgate.Comps;
using MigCorp.Skiptech.Skipgate.Operations;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    public static class SkipgateSendTargetingUtil
    {
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
    }
}
