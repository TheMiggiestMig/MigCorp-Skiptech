using RimWorld;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.SkipNet.Comps
{
    public class CompProperties_Skipdoor_IndicatorOverlay : CompProperties
    {
        public GraphicData powered;
        public GraphicData charging;
        public GraphicData charged;
        public GraphicData unpowered;
        public CompProperties_Skipdoor_IndicatorOverlay()
        {
            compClass = typeof(CompSkipdoor_IndicatorOverlay);
        }
    }

    public class CompSkipdoor_IndicatorOverlay : ThingComp
    {
        private enum SkipdoorState
        {
            None,
            Powered,
            Charging,
            Charged,
            Unpowered
        }

        SkipdoorState lastState;
        SkipdoorState currentState;

        private CompSkipdoor_Powered skipdoorPower;
        private CompSkipdoor_Delayed skipdoorDelayer;

        private CompProperties_Skipdoor_IndicatorOverlay Props =>
            (CompProperties_Skipdoor_IndicatorOverlay)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            skipdoorPower = parent.TryGetComp<CompSkipdoor_Powered>();
            skipdoorDelayer = parent.TryGetComp<CompSkipdoor_Delayed>();

            lastState = SkipdoorState.None;
            CheckForStateChange();
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
        }

        public override void ReceiveCompSignal(string signal)
        {
            base.ReceiveCompSignal(signal);

            CheckForStateChange();
        }

        private void DirtyMesh()
        {
            parent?.Map?.mapDrawer?.MapMeshDirty(parent.Position, MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Buildings);
        }

        private void CheckForStateChange()
        {
            currentState = CurrentState();
            if (currentState != lastState)
            {
                lastState = currentState;
                DirtyMesh();
            }
        }

        private SkipdoorState CurrentState()
        {
            if (!skipdoorPower.powerTrader.Off) { return SkipdoorState.Powered; }
            if (skipdoorDelayer.Opening) { return SkipdoorState.Charging; }
            if (skipdoorDelayer.Open) { return SkipdoorState.Charged; }
            return SkipdoorState.Unpowered;
        }

        private GraphicData GetGraphicData()
        {
            switch (currentState)
            {
                case SkipdoorState.Powered: return Props.powered;
                case SkipdoorState.Charging: return Props.charging;
                case SkipdoorState.Charged: return Props.charged;
                default: return Props.unpowered;
            }
        }

        public override void PostPrintOnto(SectionLayer layer)
        {
            base.PostPrintOnto(layer);

            GraphicData graphicData = GetGraphicData();
            Material material = graphicData.Graphic.MatAt(parent.Rotation);
            Vector3 position = parent.DrawPos;
            position.y += 0.01f;

            Printer_Plane.PrintPlane(layer, position, parent.DrawSize, material);
        }
    }
}