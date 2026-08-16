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
                SkipgateArrivalUtil.PlaceContents(transporters[i], cell, map);
            }

            Messages.Message(
                $"A skipgate send arrived at {mapParent.Label}.",
                lookTarget,
                MessageTypeDefOf.TaskCompletion,
                historical: false);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Values.Look(ref cell, "cell");
        }
    }
}