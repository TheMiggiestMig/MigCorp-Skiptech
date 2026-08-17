using HarmonyLib;
using MigCorp.Skiptech.Skipgate;
using MigCorp.Skiptech.Skipgate.Comps;
using MigCorp.Skiptech.Skipgate.Operations;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MigCorp.Skiptech.Patches.Core
{
    [HarmonyPatch(typeof(Dialog_FormCaravan))]
    public static class Dialog_FormCaravan_Patch
    {
        // Use our TryFormAndSendSkipgateCaravan instead if we are using a skipgate.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Dialog_FormCaravan), "TryFormAndSendCaravan")]
        static bool TryFormAndSendCaravan_Prefix(Dialog_FormCaravan __instance, ref bool __result)
        {
            if (!(__instance is Dialog_FormSkipgateCaravan dialog)) { return true; }

            __result = TryFormAndSendSkipgateCaravan(dialog);
            return false;
        }

        // DEV "Send instantly" would form a vanilla world caravan toward the dummy startingTile.
        // The skipgate version needs the Send operation's Execute instead.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Dialog_FormCaravan), "DebugTryFormCaravanInstantly")]
        static bool DebugTryFormCaravanInstantly_Prefix(Dialog_FormCaravan __instance, ref bool __result)
        {
            if (!(__instance is Dialog_FormSkipgateCaravan)) { return true; }

            Messages.Message("DEV: instant send is not available for skipgates yet (needs the Send operation).", MessageTypeDefOf.RejectInput, historical: false);
            __result = false;
            return false;
        }

        // The gate version of Accept, replacing vanilla TryFormAndSendCaravan for our dialog.
        // We don't use an exit point (the Send operation does it all instead), and the gathering / waiting point
        // is the same place now (the Skipgate).
        private static bool TryFormAndSendSkipgateCaravan(Dialog_FormSkipgateCaravan dialog)
        {
            CompSkipgate gate = dialog.gate;

            // If the gate is doing something else, we can't use it to Send.
            if (gate.CurrentOperation != null)
            {
                Messages.Message("This skipgate is already busy.", gate.parent, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            // If the gate is cooling down, we can't use it to Send.
            if (gate.CoolingDown)
            {
                Messages.Message("This skipgate cannot be used while dispersing heat.", gate.parent, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            List<Pawn> pawns = TransferableUtility.GetPawnsFromTransferables(dialog.transferables);

            if (!CheckForErrors(dialog, pawns)) { return false; }

            // We don't do exit spots, so instead, check that all applicable pawns can reach the skipgate instead.
            Pawn unreachable = pawns.FirstOrDefault(x => x.IsColonist && !x.Downed && !x.CanReach(gate.parent, PathEndMode.Touch, Danger.Deadly));
            if (unreachable != null)
            {
                Messages.Message($"{unreachable.LabelShort} cannot reach the skipgate.", unreachable, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            // Vanilla roper check, except targeting the skipgate.
            IntVec3 gateSpot = gate.parent.Position;
            Pawn roamer = pawns.Find(ropee => AnimalPenUtility.NeedsToBeManagedByRope(ropee) && !pawns.Any(roper => roper.IsColonist && GatherAnimalsAndSlavesForCaravanUtility.CanRoperTakeAnimalToDest(roper, ropee, gateSpot)));
            if (roamer != null)
            {
                Messages.Message("CaravanRoamerCannotReachSpots".Translate(roamer.LabelShort, roamer), roamer, MessageTypeDefOf.CautionInput, historical: false);
                return false;
            }

            Lord lord = SkipgateCaravanUtil.StartFormingSkipgateCaravan(pawns.Where(x => !x.Downed).ToList(), pawns.Where(x => x.Downed).ToList(), dialog.transferables, gate);
            if (lord == null) { return false; } // Already logged by StartFormingSkipgateCaravan.

            // The Send operation drives the gate from here.
            // The lord only gathers and holds.
            SkipgateOperation_Send sendOp = new SkipgateOperation_Send(gate, lord);
            if (!gate.TryStartOperation(sendOp))
            {
                // Pre-checked above so this shouldn't fire, but just in case, never leave a op-less lord behind.
                CaravanFormingUtility.StopFormingCaravan(lord);
                return false;
            }

            Messages.Message("CaravanFormationProcessStarted".Translate(), pawns[0], MessageTypeDefOf.PositiveEvent, historical: false);

            // Don't skip out on vanilla lessons.
            if (ModsConfig.BiotechActive && pawns.Any(p => p.RaceProps.IsMechanoid))
            {
                LessonAutoActivator.TeachOpportunity(ConceptDefOf.MechsInCaravans, OpportunityType.GoodToKnow);
            }

            dialog.Close(doCloseSound: false);
            SkipgateTargetingUtil.BeginSendTargeting(gate, sendOp);

            return true;
        }

        // Modified copy of Dialog_FormCaravan.CheckForErrors.
        // Strips location checks (we do that later),
        // strips mass checks (just yeet em, if they skipped leg day and can't carry the load, that's their problem).
        private static bool CheckForErrors(Dialog_FormSkipgateCaravan dialog, List<Pawn> pawns)
        {
            if (!pawns.Any((Pawn x) => CaravanUtility.IsOwner(x, Faction.OfPlayer) && !x.Downed))
            {
                if (ModsConfig.IdeologyActive) { Messages.Message("CaravanMustHaveAtLeastOneNonSlaveColonist".Translate(), MessageTypeDefOf.RejectInput, historical: false); }
                else { Messages.Message("CaravanMustHaveAtLeastOneColonist".Translate(), MessageTypeDefOf.RejectInput, historical: false); }

                return false;
            }

            List<TransferableOneWay> transferables = dialog.transferables;
            for (int i = 0; i < transferables.Count; i++)
            {
                if (transferables[i].ThingDef.category != ThingCategory.Item) { continue; }

                int countToTransfer = transferables[i].CountToTransfer;
                int num = 0;
                if (countToTransfer <= 0) { continue; }

                for (int j = 0; j < transferables[i].things.Count; j++)
                {
                    Thing t = transferables[i].things[j];
                    if (!t.Spawned || pawns.Any((Pawn x) => x.IsColonist && x.CanReach(t, PathEndMode.Touch, Danger.Deadly)))
                    {
                        num += t.stackCount;
                        if (num >= countToTransfer) { break; }
                    }
                }

                if (num < countToTransfer)
                {
                    if (countToTransfer == 1) { Messages.Message("CaravanItemIsUnreachableSingle".Translate(transferables[i].ThingDef.label), MessageTypeDefOf.RejectInput, historical: false); }
                    else { Messages.Message("CaravanItemIsUnreachableMulti".Translate(countToTransfer, transferables[i].ThingDef.label), MessageTypeDefOf.RejectInput, historical: false); }
                    return false;
                }
            }

            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Dialog_FormCaravan), "CheckForErrors")]
        static bool CheckForErrors_Prefix(Dialog_FormCaravan __instance, List<Pawn> pawns, ref bool __result)
        {
            if (!(__instance is Dialog_FormSkipgateCaravan dialog)) { return true; }

            __result = CheckForErrors(dialog, pawns);
            return false;
        }
    }
}