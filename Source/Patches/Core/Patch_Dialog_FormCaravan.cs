using HarmonyLib;
using MigCorp.Skiptech.Skipgate;
using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.Patches.Core
{
    [HarmonyPatch(typeof(Dialog_FormCaravan))]
    public static class Dialog_FormCaravan_Patch
    {
        private static MethodInfo checkForErrorsMethod = AccessTools.Method(typeof(Dialog_FormCaravan), "CheckForErrors");

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
            List<Pawn> pawns = TransferableUtility.GetPawnsFromTransferables(dialog.transferables);

            // Re-use vanilla's private Dialog_FormCaravan.CheckForErrors.
            if (!(bool)checkForErrorsMethod.Invoke(dialog, new object[] { pawns })) { return false; } // It's ugly, but it works.

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

            SkipgateCaravanUtil.StartFormingSkipgateCaravan(pawns.Where(x => !x.Downed).ToList(), pawns.Where(x => x.Downed).ToList(), dialog.transferables, gate);
            Messages.Message("CaravanFormationProcessStarted".Translate(), pawns[0], MessageTypeDefOf.PositiveEvent, historical: false);
            if (ModsConfig.BiotechActive && pawns.Any(p => p.RaceProps.IsMechanoid))
            {
                LessonAutoActivator.TeachOpportunity(ConceptDefOf.MechsInCaravans, OpportunityType.GoodToKnow);
            }
            return true;
        }
    }
}