using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    // Exact-cell arrival for a skipgate Send.
    // We want more control in our landing spot than TransportersArrivalAction_LandInSpecificCell allows.
    public class TransportersArrivalAction_SkipInSpecificCell : TransportersArrivalAction
    {
        private MapParent mapParent;
        private IntVec3 cell;

        public override bool GeneratesMap => false;

        // Required by Scribe_Deep for loading.
        public TransportersArrivalAction_SkipInSpecificCell()
        {
        }

        public TransportersArrivalAction_SkipInSpecificCell(MapParent mapParent, IntVec3 cell)
        {
            this.mapParent = mapParent;
            this.cell = cell;
        }

        public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
        {
            FloatMenuAcceptanceReport report = base.StillValid(pods, destinationTile);
            if (!report) { return report; }

            if (mapParent != null && mapParent.Tile != destinationTile) { return false; }

            // Vanilla's own spawned / HasMap / enter-cooldown checks, so our option enables and disables
            // exactly like the "land in existing map" one it replaces.
            return TransportersArrivalAction_LandInSpecificCell.CanLandInSpecificCell(pods, mapParent);
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            Map map = mapParent.Map;
            Thing lookTarget = TransportersArrivalActionUtility.GetLookTarget(transporters);

            // First thing DropTravellingDropPods does. A no-op for a normal send (our pawns never became
            // world pawns), but a downed guest or similar could conceivably ride along as one.
            TransportersArrivalActionUtility.RemovePawnsFromWorldPawns(transporters);

            for (int i = 0; i < transporters.Count; i++)
            {
                SpawnContents(transporters[i], map);
            }

            Messages.Message(
                $"A skipgate send arrived at {mapParent.Label}.",
                lookTarget,
                MessageTypeDefOf.TaskCompletion,
                historical: false);
        }

        // A cutdown version of ActiveTransporter.PodOpen.
        private void SpawnContents(ActiveTransporterInfo info, Map map)
        {
            for (int i = info.innerContainer.Count - 1; i >= 0; i--)
            {
                Thing thing = info.innerContainer[i];

                Thing placed;
                if (!GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near, out placed))
                {
                    // If we can't place it nearby, just dump it on the ground. The container doesn't really
                    // exist, so we don't want pawns stuck in limbo.
                    GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Direct, out placed);
                }

                Pawn pawn = placed as Pawn;
                if (pawn == null) { continue; }

                // Keep behavior the same as drop pods, and draft out pawns if not on a home map.
                if (pawn.IsColonist && pawn.Spawned && !map.IsPlayerHome) { pawn.drafter.Drafted = true; }

                if (pawn.guest != null && pawn.guest.IsPrisoner) { pawn.guest.WaitInsteadOfEscapingForDefaultTicks(); }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Values.Look(ref cell, "cell");
        }
    }
}