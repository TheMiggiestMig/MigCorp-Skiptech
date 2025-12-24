using MigCorp.Skiptech.Systems.SkipNet;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.Comps
{
    public class CompProperties_Skipdoor_Powered : CompProperties_Skipdoor_Delayed
    {
        public float IdlePercent { get; set; } = 0.5f;
        public float DesiredIdlePercent { get; set; } = 0f;

        public CompProperties_Skipdoor_Powered() => this.compClass = typeof(CompSkipdoor_Powered);
    }
    public class CompSkipdoor_Powered : CompSkipdoor_Delayed
    {

        public CompPowerTrader powerTrader;
        public override CompProperties_Skipdoor_Delayed Props => (CompProperties_Skipdoor_Delayed)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerTrader = parent.TryGetComp<CompPowerTrader>();
            if (powerTrader == null) { MigcorpSkiptechMod.Warning("CompProperties_Skipdoor_Powered was instantiated without a CompPowerTrader attached to parent."); }
        }

        public override int TicksUntilEnterable(Pawn pawn)
        {
            return !powerTrader.Off ? 0 : base.TicksUntilEnterable(pawn);
        }

        public override int TicksUntilExitable(Pawn pawn)
        {
            return !powerTrader.Off ? 0 : base.TicksUntilEnterable(pawn);
        }
    }
}
