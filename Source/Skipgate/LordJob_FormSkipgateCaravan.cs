using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI.Group;

namespace MigCorp.Skiptech.Skipgate
{
    // Like the LordJob_FormAndSendCaravan, except instead of exiting the map when ready, it waits at the skipgate.
    // The skipgate's own Send Operation will handle caravan creation.
    public class LordJob_FormSkipgateCaravan : LordJob_FormAndSendCaravan
    {
        private LordToil_PrepareSkipgateCaravan_Hold holdToil;

        public bool Holding => lord.CurLordToil == holdToil;
        public bool AllAssembled => Holding && holdToil.AllAssembled;

        public LordJob_FormSkipgateCaravan()
        {
        }

        public LordJob_FormSkipgateCaravan(List<TransferableOneWay> transferables, List<Pawn> downedPawns, IntVec3 gateCenter, PlanetTile tile)
            : base(transferables, downedPawns, gateCenter, gateCenter, tile, tile)
        {
        }

        // Swap all instances of the Leave toil with the custom Hold (at the skipgate) toil.
        // I don't want the pawns to leave, I want them to wait for the skipgate to make them leave.
        public override StateGraph CreateGraph()
        {
            StateGraph graph = base.CreateGraph();
            LordToil leave = graph.lordToils.First(toil => toil is LordToil_PrepareCaravan_Leave);
            holdToil = new LordToil_PrepareSkipgateCaravan_Hold(ExitSpot);
            graph.lordToils[graph.lordToils.IndexOf(leave)] = holdToil;

            foreach (Transition transition in graph.transitions)
            {
                for (int i = 0; i < transition.sources.Count; i++)
                {
                    if (transition.sources[i] == leave) { transition.sources[i] = holdToil; }
                }

                if (transition.target == leave) { transition.target = holdToil; }
            }

            graph.transitions.RemoveAll(transition => transition.target is LordToil_End);

            return graph;
        }
    }
}