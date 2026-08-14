using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    // The bit that makes LordJob_FormSkipgateCaravan different.
    // If the caravan is ready before the gate has finished charging, make them wait.
    public class LordToil_PrepareSkipgateCaravan_Hold : LordToil_PrepareCaravan_Leave
    {
        public const int AssemblyRadiusCells = 2;

        private readonly IntVec3 gateCenter;
        private CellRect assemblyRect;
        private bool allAssembled;

        public bool AllAssembled => allAssembled;

        // Let pawns handle their needs. It could be a long charge time.
        public override bool AllowSatisfyLongNeeds => true;

        public LordToil_PrepareSkipgateCaravan_Hold(IntVec3 gateCenter) : base(gateCenter)
        {
            this.gateCenter = gateCenter;
            assemblyRect = CellRect.CenteredOn(gateCenter, AssemblyRadiusCells);
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
                if (!pawn.Spawned || !assemblyRect.Contains(pawn.Position)) { return false; }
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