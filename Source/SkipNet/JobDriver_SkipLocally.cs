using MigCorp.Skiptech.SkipNet.Comps;
using MigCorp.Skiptech.Utils;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    /// <summary>
    /// Player-ordered local skip: walk onto the entry skipdoor (A), wait for both doors to be ready, then skip onto the exit skipdoor (B).
    /// Same access rules and charge delay as automatic SkipNet travel. The exit is never pathed to, so isolated exits are fine.
    /// A carried pawn stays carried: the JobDef keeps it across the job boundary, and the teleport doesn't touch the carry tracker.
    /// </summary>
    public class JobDriver_SkipLocally : JobDriver
    {
        private const TargetIndex EntryInd = TargetIndex.A;
        private const TargetIndex ExitInd = TargetIndex.B;

        private CompSkipdoor Entry => job.GetTarget(EntryInd).Thing?.TryGetComp<CompSkipdoor>();
        private CompSkipdoor Exit => job.GetTarget(ExitInd).Thing?.TryGetComp<CompSkipdoor>();

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override string GetReport()
        {
            CompSkipdoor exit = Exit;
            if (exit == null) { return base.GetReport(); }

            string exitLabel = exit.IsNamed ? exit.RenamableLabel : exit.parent.LabelShort;
            return "MigCorp.Skiptech.Skipdoor.SkipLocallyReport".Translate(exitLabel).Resolve();
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(EntryInd);
            this.FailOnDespawnedOrNull(ExitInd);
            this.FailOn(() => !CanStillSkip());

            yield return Toils_Goto.GotoThing(EntryInd, PathEndMode.OnCell);

            // Wake both doors (starts the charge on delayed doors), then hold on the entry until both are ready.
            Toil charge = ToilMaker.MakeToil("SkipLocally_Charge");
            charge.initAction = () =>
            {
                Entry.Notify_PawnArrived(pawn, SkipdoorType.Entry);
                Exit.Notify_PawnArrived(pawn, SkipdoorType.Exit);
            };
            charge.tickAction = () =>
            {
                Entry.IsEnterableNowBy(pawn, out int entryWait);
                Exit.IsExitableNowBy(pawn, out int exitWait);
                int waitTicks = Mathf.Max(entryWait, exitWait);
                if (waitTicks <= 0)
                {
                    ReadyForNextToil();
                    return;
                }

                // Same cooldown pie automatic travel shows at the seam.
                if (!pawn.stances.FullBodyBusy)
                {
                    pawn.stances.SetStance(new Stance_Cooldown(waitTicks, job.GetTarget(EntryInd), null) { neverAimWeapon = true });
                }
            };
            charge.defaultCompleteMode = ToilCompleteMode.Never;
            yield return charge;

            Toil skip = ToilMaker.MakeToil("SkipLocally_Skip");
            skip.initAction = DoSkip;
            skip.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return skip;
        }

        // Same rules the menu used, re-checked every tick (automatic travel does its final check at the seam).
        // Might make this happen on a hashed tick rather than every tick if performance is bad.
        private bool CanStillSkip()
        {
            CompSkipdoor entry = Entry;
            CompSkipdoor exit = Exit;
            if (entry == null || exit == null) { return false; }

            SkipNetAccessContext ac = new SkipNetAccessContext(pawn, forced: true);
            return entry.IsEnterableBy(ac) && exit.IsExitableBy(ac);
        }

        private void DoSkip()
        {
            CompSkipdoor entry = Entry;
            CompSkipdoor exit = Exit;
            Map map = pawn.Map;
            IntVec3 entryCell = entry.Position;
            IntVec3 exitCell = exit.Position;

            // The charge cooldown can outlast the doors by a tick. Don't carry it to the far side.
            pawn.stances.CancelBusyStanceHard();

            // Same move as vanilla's skip psycast (CompAbilityEffect_Teleport), including unfogging an unseen destination.
            pawn.Position = exitCell;
            if ((pawn.Faction == Faction.OfPlayer || pawn.IsPlayerControlled) && exitCell.Fogged(map)) { FloodFillerFog.FloodUnfog(exitCell, map); }
            pawn.Notify_Teleported(endCurrentJob: false);

            FxUtil.PlaySkip(entryCell, map, false);
            FxUtil.PlaySkip(exitCell, map, false);

            entry.Notify_PawnTeleported(pawn, SkipdoorType.Entry);
            exit.Notify_PawnTeleported(pawn, SkipdoorType.Exit);
        }
    }
}