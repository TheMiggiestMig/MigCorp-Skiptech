using LudeonTK;
using MigCorp.Skiptech.Utils;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    /// <summary>
    /// The teleport step itself: what happens when a pawn reaches the seam of its splice (entry cell, lined up to step into the exit).
    /// Stateless. It works on the plan and the pawn's pather, and never touches the Manager's registries; the Manager wraps these
    /// calls with its own bookkeeping.
    /// </summary>
    public static class SkipNetSeam
    {
        [TweakValue("MigCorp Performance Test", 0, 1)]
        public static int DEBUG_TurnFXOff = 0;
        public enum TeleportStepDecision
        {
            Approved,
            Waiting,     // used if the skipdoor is delayed
            BlockedDead
        }

        // Freezes the pawn's step at 0%, so the tweener keeps it drawn on the entry skipdoor.
        public static void HoldAtSeam(Pawn_PathFollower pather)
        {
            pather.nextCellCostLeft = 1f;
            pather.nextCellCostTotal = 1f;
        }

        // If the pawn is standing at this plan's seam, step it back onto its own cell. Otherwise vanilla finishes that "step" into
        // the exit cell: a teleport with no plan, no power and no FX. The pawn's new path carries on from where it stands, the same
        // way vanilla handles a new path that starts a cell behind.
        public static void StepBackIfAtSeam(SkipNetPlan plan)
        {
            Pawn_PathFollower pather = plan.pawn?.pather;
            if (pather == null || !plan.IsPawnAtSeam) { return; }

            pather.nextCell = plan.pawn.Position;
            pather.nextCellCostLeft = 0f;
            pather.nextCellCostTotal = 1f;
        }

        // Called with the pawn at the seam, about to step into the exit. BlockedDead has already reset the pawn's pather.
        public static TeleportStepDecision DecideTeleportStep(Pawn pawn, SkipNetPlan plan)
        {
            Pawn_PathFollower pather = pawn.pather;

            // Make sure the plan and path are still good this tick, otherwise it's an invalid teleport.
            if (plan.IsDisposed || pather.curPath != plan.splicedPath)
            {
                pather.ResetToCurrentPosition();
                return TeleportStepDecision.BlockedDead;
            }

            // Pawn was sent somewhere else since this splice was built, and the new path hasn't landed yet. Don't teleport toward the old destination.
            if (SkipNetUtils.PatherDest(pather) != plan.dest || SkipNetUtils.PatherPeMode(pather) != plan.peMode)
            {
                plan.Dispose();
                pather.ResetToCurrentPosition();
                return TeleportStepDecision.BlockedDead;
            }

            // I totally didn't forget to tell the doors to start charging >.>;
            if (!plan.compsNotified)
            {
                plan.compsNotified = true;
                plan.entry.Notify_PawnArrived(pawn, SkipdoorType.Entry);
                plan.exit.Notify_PawnArrived(pawn, SkipdoorType.Exit);
            }

            // Doors still spinning up? Park the pawn in a cooldown stance.
            // Blatantly stolen from the door check in Pawn_PathFollower.TryEnterNextPathCell
            plan.entry.IsEnterableNowBy(pawn, out int entryWait);
            plan.exit.IsExitableNowBy(pawn, out int exitWait);
            int waitTicks = Mathf.Max(entryWait, exitWait);
            if (waitTicks > 0)
            {
                Stance_Cooldown stance = new Stance_Cooldown(waitTicks, new LocalTargetInfo(plan.entry.parent), null)
                {
                    neverAimWeapon = true,
                };
                pawn.stances.SetStance(stance);
                HoldAtSeam(pather);
                return TeleportStepDecision.Waiting;
            }

            // Final check before teleporting the pawn.
            if (!plan.IsCandidatePairStillAccessible() ||
                !plan.IsStillPathableFromExitToDest(plan.map))
            {
                plan.Dispose();
                pather.ResetToCurrentPosition();
                return TeleportStepDecision.BlockedDead;
            }

            return TeleportStepDecision.Approved;
        }

        // Called after an Approved step. Returns false if vanilla didn't move the pawn this tick (the next TryEnterNextPathCell
        // prefix tries again). On true the teleport is done: everything from here is vanilla walking the exit leg.
        public static bool CompleteTeleportStep(Pawn pawn, SkipNetPlan plan)
        {
            // Move was probably blocked this tick.
            if (pawn.Position != plan.exitCell) { return false; }

            // Fix for roped animals. Teleport them first.
            TeleportRopees(pawn, plan);

            // Do the thing.
            // Teleport, cancel the tween, fire the effects, and notify the skipdoors that the pawn teleported.
            pawn.Drawer.tweener.Notify_Teleported();

            if (DEBUG_TurnFXOff <= 0)
            {
                FxUtil.PlaySkip(plan.entryCell, plan.map, false);
                FxUtil.PlaySkip(plan.exitCell, plan.map, false);
            }

            plan.entry.Notify_PawnTeleported(pawn, SkipdoorType.Entry);
            plan.exit.Notify_PawnTeleported(pawn, SkipdoorType.Exit);

            // A re-path still pending was asked for from the entry side (one made at the seam starts at the exit cell, and tears the
            // plan down instead, so it never gets here). The pawn's on the far side now, so drop it and carry on along the splice's
            // exit leg. If it was one of our dummies, its plan is released on the Manager's next tick.
            Pawn_PathFollower pather = pawn.pather;
            if (pather.curPathRequest != null && pather.curPathRequest.Start != plan.exitCell) { pather.DisposeAndClearCurPathRequest(); }

            return true;
        }

        // If the pawn has roped animals following them, teleport the animals first and reset their pathing.
        // The FollowRoper job causes them to try and follow their roper's path, but a few cells back... which can be
        // far enough away to trigger them to teleport back. Which makes them far enough away to trigger them to come close... repeat...
        private static void TeleportRopees(Pawn roper, SkipNetPlan plan)
        {
            if (roper.roping?.IsRopingOthers != true) { return; }

            var ropees = roper.roping.Ropees;

            for (int i = 0; i < ropees.Count; i++)
            {
                Pawn ropee = ropees[i];

                if (ropee == null || !ropee.Spawned || ropee.Map != plan.map) { continue; }

                ropee.Position = plan.exitCell; // Pile them all up on the skipdoor. I know some of you crazies put these in 1x1 cell rooms.

                ropee.Notify_Teleported(endCurrentJob: false);

                // The ropee physically passed through both Skipdoors, so let Skipdoor
                // comps apply teleport side effects such as Skipshock to it too.
                plan.entry.Notify_PawnTeleported(ropee, SkipdoorType.Entry);
                plan.exit.Notify_PawnTeleported(ropee, SkipdoorType.Exit);
            }
        }
    }
}