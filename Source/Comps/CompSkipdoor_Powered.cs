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

        public override void Notify_PawnTeleported(Pawn pawn, SkipNetPlan skipNetPlan, SkipdoorType type)
        {
            // If the skipdoor is not powered and it should be, they'll have a 40% chance of getting sick (for each unpowered skipdoor in the jump, so ~64% chance if both are unpowered).
            if (powerTrader.Off)
            {
                if (ShouldApplySkipShock(pawn) && Rand.Chance(0.40f))
                    ApplySkipShock(pawn);
            }
            return;
        }

        private void ApplySkipShock(Pawn pawn)
        {
            HediffDef skipShockDef = DefDatabase<HediffDef>.GetNamed("MigcorpSkipShock");
            Hediff skipShock = pawn.health.hediffSet.GetFirstHediffOfDef(skipShockDef);

            // Not sure how a pawn with no health stat did this, but hey.
            if (pawn?.health == null) return;

            // Add if not there, otherwise reset.
            if (skipShock == null)
            {
                skipShock = HediffMaker.MakeHediff(skipShockDef, pawn);
                pawn.health.AddHediff(skipShock);
            }
            else
            {
                skipShock.Severity = Mathf.Max(skipShock.Severity, skipShockDef.initialSeverity);
            }

            HediffComp_Disappears disappearsComp = skipShock.TryGetComp<HediffComp_Disappears>();
            if (disappearsComp != null)
            {
                disappearsComp.ticksToDisappear = disappearsComp.Props.disappearsAfterTicks.max;
            }

            // If the pawn doesn't eat or have a stomach, it doesn't make sense to vomit.

        }

        // We don't want skipshock to hit mechanoids or anomalies.
        private static bool ShouldApplySkipShock(Pawn pawn)
        {
            if (pawn?.RaceProps == null) return false;
            if (pawn.RaceProps.IsMechanoid) return false;
            if (pawn.RaceProps.IsAnomalyEntity) return false;

            // If it can't eat a simple meal, it can't throw up.
            if (!pawn.RaceProps.CanEverEat(ThingDefOf.MealSimple)) return false;

            // weird edge case, huh
            if (pawn.Dead) return false;

            return true;
        }
    }
}
