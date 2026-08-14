using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.Skipgate
{
    // The bit that makes LordJob_FormSkipgateCaravan different.
    // If the caravan is ready before the gate has finished charging, make them wait.
    public class LordToil_PrepareSkipgateCaravan_Hold : LordToil_PrepareCaravan_Leave
    {
        public const int AnchorRadiusCells = 1;    // Pawns are anchored to the center cell...
        public const int AssembledRadiusCells = 5; // ...and count as assembled anywhere in an 11x11 footprint.

        private readonly IntVec3 gateCenter;
        //private CellRect assemblyRect;
        private CellRect anchorRect;
        private CellRect assembledRect;
        private bool allAssembled;

        public bool AllAssembled => allAssembled;

        // Let pawns handle their needs. It could be a long charge time.
        public override bool AllowSatisfyLongNeeds => true;

        public LordToil_PrepareSkipgateCaravan_Hold(IntVec3 gateCenter) : base(gateCenter)
        {
            this.gateCenter = gateCenter;

            anchorRect = CellRect.CenteredOn(gateCenter, AnchorRadiusCells);
            assembledRect = CellRect.CenteredOn(gateCenter, AssembledRadiusCells);
        }

        // Gotta force the pawn to wander close to the Skipgate's center, otherwise they won't count as assembled
        // (and the teleport effect will look weird if they're too far).
        public override void UpdateAllDuties()
        {
            CellRect rect = lord.ownedPawns.Count > anchorRect.Area
                ? CellRect.CenteredOn(gateCenter, AnchorRadiusCells + 1)
                : anchorRect;

            List<IntVec3> anchors = rect.Cells.OrderBy(c => c.DistanceToSquared(gateCenter)).ToList();

            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                pawn.mindState.duty = new PawnDuty(SkiptechDefOf.MigCorp_SkipgateHold, anchors[i % anchors.Count]);
                pawn.mindState.duty.locomotion = LocomotionUrgency.Jog;
                pawn.mindState.duty.wanderRadius = 2f;
            }
        }

        public override void LordToilTick()
        {
            // Do NOT call base.LordToilTick(), it fires the "ReadyToExitMap".
            // We handle our own exit.
            if (Find.TickManager.TicksGame % 60 == 0) { allAssembled = CheckAssembled(); }
        }

        private bool CheckAssembled()
        {
            if (lord.ownedPawns.Count == 0) { return false; }

            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (!pawn.Spawned || !assembledRect.Contains(pawn.Position)) { return false; }
            }

            // Make sure we count downed pawns.
            List<Pawn> downedPawns = ((LordJob_FormAndSendCaravan)lord.LordJob).downedPawns;
            for (int i = 0; i < downedPawns.Count; i++)
            {
                if (!JobGiver_PrepareCaravan_GatherDownedPawns.IsDownedPawnNearExitPoint(downedPawns[i], gateCenter))
                {
                    return false;
                }
            }

            return true;
        }
    }
}