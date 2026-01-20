using RimWorld;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.SkipNet.Comps
{
    public class CompProperties_Skipdoor_Powered : CompProperties_Skipdoor_Delayed
    {
        public float unpoweredSkipshockChance { get; set; } = 0.4f;

        public CompProperties_Skipdoor_Powered() => compClass = typeof(CompSkipdoor_Powered);
    }
    public class CompSkipdoor_Powered : CompSkipdoor_Delayed
    {

        public CompPowerTrader powerTrader;
        public new CompProperties_Skipdoor_Powered Props => (CompProperties_Skipdoor_Powered)props;

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

        public override bool CanEnter(Pawn pawn)
        {
            return WantsToAvoidSkipShock(pawn);
        }

        public override bool CanExit(Pawn pawn)
        {
            return WantsToAvoidSkipShock(pawn);
        }

        public override void Notify_PawnTeleported(Pawn pawn, SkipNetPlan skipNetPlan, SkipdoorType type)
        {
            // If the skipdoor is not powered and it should be, they'll have a 40% chance of getting sick (for each unpowered skipdoor in the jump, so ~64% chance if both are unpowered).
            if (powerTrader.Off)
            {
                if (ShouldApplySkipShock(pawn) && Rand.Chance(Props.unpoweredSkipshockChance))
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
            bool skipShockAdded = false;
            if (skipShock == null)
            {
                skipShock = HediffMaker.MakeHediff(skipShockDef, pawn);
                pawn.health.AddHediff(skipShock);
                skipShockAdded = true;
            }
            else
            {
                skipShock.Severity = Mathf.Max(skipShock.Severity, skipShockDef.initialSeverity);
            }

            HediffComp_Disappears disappearsComp = skipShock.TryGetComp<HediffComp_Disappears>();
            if (disappearsComp != null)
            {
                if (skipShockAdded || disappearsComp.ticksToDisappear != disappearsComp.Props.disappearsAfterTicks.max)
                {
                    DoSkipshockTextMote(pawn);
                }
                disappearsComp.ticksToDisappear = disappearsComp.Props.disappearsAfterTicks.max;
            }
        }

        // We don't want skipshock to hit mechanoids or anomalies.
        private static bool ShouldApplySkipShock(Pawn pawn)
        {
            if (MigcorpSkiptechMod.Settings.disableSkipShock) { return false; }
            if (pawn?.RaceProps == null) return false;
            if (pawn.RaceProps.IsMechanoid) return false;
            if (pawn.RaceProps.IsAnomalyEntity) return false;

            // If it can't eat a simple meal, it can't throw up.
            if (!pawn.RaceProps.CanEverEat(ThingDefOf.MealSimple)) return false;

            // weird edge case, huh
            if (pawn.Dead) return false;

            return true;
        }

        private static void DoSkipshockTextMote(Pawn pawn)
        {
            MoteMaker.ThrowText(pawn.PositionHeld.ToVector3Shifted(), text: (string)"MigCorp.Skiptech.Text.Skipshock".Translate(), map: pawn.Map, color: Color.yellow);
        }

        // Checks if the skipdoor would cause skip-shock and the pawn wants to avoid it.
        public bool WantsToAvoidSkipShock(Pawn pawn)
        {
            if (pawn.Faction != Faction.OfPlayer) return true;
            if (!powerTrader.Off) return true;
            if (MigcorpSkiptechMod.Settings.disableSkipShock) return true;
            if (!MigcorpSkiptechMod.Settings.enableSkipShockAvoidance) return true;
            if (pawn.Drafted || pawn.CurJob.playerForced) return true;

            return false;
        }
    }
}
