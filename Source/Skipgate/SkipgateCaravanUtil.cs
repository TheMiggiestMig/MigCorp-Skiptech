using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MigCorp.Skiptech.Skipgate
{
    public static class SkipgateCaravanUtil
    {
        // Blatant copy of CaravanFormingUtility.StartFormingCaravan, minus the route tiles and with our LordJob_FormSkipgateCaravan.
        public static Lord StartFormingSkipgateCaravan(List<Pawn> pawns, List<Pawn> downedPawns, List<TransferableOneWay> transferables, CompSkipgate gate)
        {
            if (!pawns.Any())
            {
                Log.Error("Can't start forming skipgate caravan with 0 pawns.");
                return null;
            }

            if (pawns.Any((Pawn x) => x.Downed))
            {
                Log.Warning("Forming a skipgate caravan with a downed pawn. This shouldn't happen because we have to create a Lord.");
            }

            List<TransferableOneWay> list = transferables?.ToList() ?? new List<TransferableOneWay>();
            list.RemoveAll((TransferableOneWay x) => x.CountToTransfer <= 0 || !x.HasAnyThing || x.AnyThing is Pawn);
            for (int i = 0; i < pawns.Count; i++)
            {
                pawns[i].GetLord()?.Notify_PawnLost(pawns[i], PawnLostCondition.ForcedToJoinOtherLord);
            }

            LordJob_FormSkipgateCaravan lordJob = new LordJob_FormSkipgateCaravan(list, downedPawns ?? new List<Pawn>(), gate.parent.Position, gate.parent.Map.Tile);
            Lord lord = LordMaker.MakeNewLord(Faction.OfPlayer, lordJob, gate.parent.Map, pawns);
            for (int j = 0; j < pawns.Count; j++)
            {
                Pawn pawn = pawns[j];
                if (pawn.Spawned)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }

            return lord;
        }
    }
}