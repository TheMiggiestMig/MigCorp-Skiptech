using MigCorp.Skiptech.Skipgate.Operations;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MigCorp.Skiptech.Skipgate.Comps
{
    public class CompProperties_Skipgate : CompProperties
    {
        public CompProperties_Skipgate() => compClass = typeof(CompSkipgate);

        public float heatPerCost = 1f;
        public float heatDissipationPerSecond = 1f;

        // Dialing time (10/64 glyph dialing phase after charging completes and pawns / load is ready).
        public int dialingTicks = 300; //5s

        public float linkCostBase = 25f;
        public float linkCostPerTile = 1f;
        public float linkBufferSeconds = 250f;
        public float linkRebuildWatts = 200f;

        public float sendCostBase = 10f;
        public float sendCostPerKg = 0.05f;
        public float sendCostPerKgPerTile = 0.002f;
    }

    [StaticConstructorOnStartup]
    public class CompSkipgate : ThingComp
    {
        private static readonly Texture2D ViewLinkedGateIcon = ContentFinder<Texture2D>.Get("UI/Commands/ViewCave");
        private static readonly Texture2D CancelIcon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");
        public CompProperties_Skipgate Props => (CompProperties_Skipgate)props;
        private CompSkipgateCapacitor capacitor;
        public CompSkipgateCapacitor Capacitor => capacitor ?? (capacitor = parent.GetComp<CompSkipgateCapacitor>());

        private SkipgateOperation currentOperation;
        public SkipgateOperation CurrentOperation => currentOperation;

        private Building_SkipgatePortal portal;
        public Building_SkipgatePortal Portal => portal;


        private float heatRemaining;
        public float HeatRemaining => heatRemaining;
        public bool CoolingDown => heatRemaining > 0f;
        private int CooldownTicksLeft() => Mathf.CeilToInt(heatRemaining / Props.heatDissipationPerSecond * 60);

        public CompSkipgate LinkedFarGate => CurrentOperation is SkipgateOperation_Link link
                                && link.Phase == SkipgateOperationPhase.Active
                                ? link.OtherGate
                                : null;


        //public bool recallResearchFinished = DefDatabase<ResearchProjectDef>.GetNamed("MigCorpSkipTech_SkipgateRecall").IsFinished;
        //public bool linkResearchFinished = DefDatabase<ResearchProjectDef>.GetNamed("MigCorpSkipTech_SkipgateLink").IsFinished;
        public bool recallResearchFinished = true; // true for testing
        public bool linkResearchFinished = true; // true for testing

        private bool postLoadValidationPending;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref heatRemaining, "heatRemaining", defaultValue: 0f);
            Scribe_Deep.Look(ref currentOperation, "currentOperation", this);
            Scribe_References.Look(ref portal, "portal");
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (currentOperation != null) { currentOperation.RestoreAfterLoad(); }

            // Need to perform gate-to-gate linking checks after *everything* is spawned... which means, on the next tick.
            postLoadValidationPending = respawningAfterLoad;
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            currentOperation?.TryCancel();
            DespawnPortal(); // Just in case the message wasn't clear <.<
        }

        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);

            // Might move this to a GameComponent to do during FinalizeInit.
            if (postLoadValidationPending)
            {
                postLoadValidationPending = false;
                currentOperation?.ResumeAfterLoad();
                if (LinkedFarGate == null) { DespawnPortal(); }
            }

            currentOperation?.Tick(delta);

            TickCooldown(delta);
        }

        private void TickCooldown(int delta)
        {
            if (CoolingDown)
            {
                heatRemaining -= delta * Props.heatDissipationPerSecond / 60f;

                if (!CoolingDown)
                {
                    heatRemaining = 0f;
                    Messages.Message($"Skipgate {(parent as Building_Skipgate).RenamableLabel} is ready to be used again.", MessageTypeDefOf.NeutralEvent);
                }
            }
        }

        public bool TryStartOperation(SkipgateOperation operation)
        {
            if (operation == null || currentOperation != null) { return false; }
            if (HeatRemaining > 0f) { return false; }

            AcceptanceReport canStart = operation.CanStart();
            if (!canStart.Accepted)
            {
                Messages.Message(canStart.Reason, parent, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            currentOperation = operation;
            currentOperation.Start();

            // Link reservation can actually fail to start, so success depends on if it could actually start (if it's still the currentOperation).
            // Everything else should be fine :)
            return currentOperation == operation;
        }

        public void EndOperation(SkipgateOperation operation, SkipgateOperationEnd result, float heatGenerated)
        {
            // Make sure we're only ending our own operation.
            if (currentOperation != operation) { return; }

            Capacitor.ClearDemand();
            DespawnPortal();

            heatRemaining += heatGenerated * Props.heatPerCost;
            currentOperation = null;
        }
        public bool TrySpendCharge(float amount)
        {
            return Capacitor.TrySpend(amount);
        }
        public void SpawnPortal()
        {
            if (portal != null && portal.Spawned) { return; }

            portal = (Building_SkipgatePortal)ThingMaker.MakeThing(SkiptechDefOf.MigCorp_SkipgatePortal);
            portal.SetOwningSkipgate(this);
            GenSpawn.Spawn(portal, parent.Position, parent.Map);
        }

        public void DespawnPortal()
        {
            if (portal == null) { return; }

            if (portal.Spawned)
            {
                if (portal.LoadInProgress) { portal.CancelLoad(); }
                portal.Destroy(DestroyMode.Vanish);
            }

            portal = null;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo item in base.CompGetGizmosExtra())
            {
                yield return item;
            }

            // Cancel Action
            if (CurrentOperation != null)
            {
                yield return Gizmo_Cancel();
                if (CurrentOperation is SkipgateOperation_Link) { yield return Gizmo_ViewLinkedGate(); }
            }
            else
            {
                // Send
                //yield return Gizmo_Send();

                // Emergency Recall
                if (recallResearchFinished)
                {
                    yield return Gizmo_EmergencyRecall();
                }

                // Recall
                if (linkResearchFinished)
                {
                    yield return Gizmo_Recall();
                    yield return Gizmo_Link();
                }
            }

            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Add +10s heat",
                    action = delegate { heatRemaining += 10; }
                };

                yield return new Command_Action
                {
                    defaultLabel = "DEV: Remove -10s heat",
                    action = delegate { heatRemaining -= 10; }
                };

                yield return new Command_Action
                {
                    defaultLabel = "DEV: Reset heat",
                    action = delegate { heatRemaining = 0; }
                };

                if (CurrentOperation == null)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "DEV: Form skipgate caravan",
                        defaultDesc = "Starts the forming lord with the selected free colonists (all free colonists if none are selected) and up to 10 simple meals as cargo.",
                        action = delegate
                        {
                            List<Pawn> pawns = Find.Selector.SelectedPawns
                                .Where(p => p.IsFreeColonist && p.Spawned && p.Map == parent.Map && !p.Downed && !p.InMentalState)
                                .ToList();

                            if (pawns.Count == 0)
                            {
                                pawns = parent.Map.mapPawns.FreeColonistsSpawned
                                    .Where(p => !p.Downed && !p.InMentalState)
                                    .ToList();
                            }

                            if (pawns.Count == 0)
                            {
                                Messages.Message("DEV: no free colonists available.", MessageTypeDefOf.RejectInput, historical: false);
                                return;
                            }

                            SkipgateCaravanUtil.StartFormingSkipgateCaravan(pawns, new List<Pawn>(), DEV_MealTransferables(), this);
                            Messages.Message($"DEV: forming skipgate caravan with {pawns.Count} colonist(s).", parent, MessageTypeDefOf.NeutralEvent, historical: false);
                        }
                    };
                }
            }
        }

        public Gizmo Gizmo_Cancel()
        {
            bool isLiveLink = CurrentOperation is SkipgateOperation_Link && CurrentOperation.Phase == SkipgateOperationPhase.Active;
            bool isIncomingLink = !isLiveLink && CurrentOperation is SkipgateOperation_Link incomingLink && incomingLink.Role == LinkRole.Responder;

            string label;
            if (isLiveLink) { label = "Unlink"; }
            else if (isIncomingLink) { label = "Cancel Link"; }
            else if (CurrentOperation.Phase == SkipgateOperationPhase.Dialing) { label = "Cancel Dialing"; }
            else { label = "Cancel Charging"; }

            return new Command_Action
            {
                defaultLabel = label,
                defaultDesc = isLiveLink
                    ? "Close the link.\n\nWARNING: Both skipgates will generate heat and must cool down."
                    : "Cancel the current action.\n\nThe current charge will remain but slowly drain.",
                icon = CancelIcon,
                action = delegate
                {
                    if (isLiveLink)
                    {
                        Building_Skipgate far = LinkedFarGate?.parent as Building_Skipgate;
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            $"Unlink from {far?.RenamableLabel}?\n\nBoth skipgates will take heat and must cool down before they can be used again.",
                            () => currentOperation?.TryCancel(),
                            destructive: true));
                        return;
                    }

                    currentOperation?.TryCancel();
                }
            };
        }

        public Gizmo Gizmo_ViewLinkedGate()
        {
            Building_Skipgate far = (Building_Skipgate)((SkipgateOperation_Link)CurrentOperation).OtherGate.parent;

            return new Command_Action
            {
                defaultLabel = CurrentOperation.Phase == SkipgateOperationPhase.Active
                    ? "View linked skipgate"
                    : "View linking skipgate",
                defaultDesc = $"Jump the camera to {far.RenamableLabel}.",
                icon = ViewLinkedGateIcon,
                action = () => CameraJumper.TryJumpAndSelect(far)
            };
        }

        private Command_Action Skipgate_Command_Operation(string defaultLabel, string defaultDesc, System.Action action)
        {
            Command_Action command = new Command_Action();
            if (CoolingDown)
            {
                command.Disabled = true;
                command.disabledReason = "Skipgate cannot be used while dispersing heat.";
            }
            command.defaultLabel = defaultLabel;
            command.defaultDesc = defaultDesc;
            command.action = action;

            return command;
        }

        public Gizmo Gizmo_Send()
        {
            return new Command_Action { };
        }

        public Gizmo Gizmo_EmergencyRecall()
        {
            Command_Action command = Skipgate_Command_Operation(
                defaultLabel: "Emergency Recall",
                defaultDesc: "Target a pawn or caravan equipped with a skip beacon and teleport them to this skipgate, consuming the skip beacon.\n\n" +
                    "WARNING: Will cause damage and breakdowns around the map!".Colorize(Color.yellow),
                action: delegate
                {
                    TryStartOperation(new SkipgateOperation_Recall(this, "Emergency Recalling some poor schmucks!", 69f, SkipgateRecallMode.Emergency));
                }
                );

            return command;
        }

        public Gizmo Gizmo_Recall()
        {
            Command_Action command = Skipgate_Command_Operation(
                defaultLabel: "Recall",
                defaultDesc: $"Targets a pawn or caravan equipped with a skip beacon and teleports them to this skipgate{(!linkResearchFinished ? ", consuming the skip beacon" : null)}.",
                action: delegate
                {
                    TryStartOperation(new SkipgateOperation_Recall(this, "Bring home the pawns!", 37f));
                }
                );

            return command;
        }

        public Gizmo Gizmo_Link()
        {
            // Custom Command (so that the right-click could be overridden to produce a list while left click opens world map targeting).
            return new Command_LinkSkipgate(this);
        }

        // DEV Just gather (up to) 10 simple meals for the DEV Form skipgate caravan test.
        private List<TransferableOneWay> DEV_MealTransferables()
        {
            List<Thing> meals = parent.Map.listerThings.ThingsOfDef(ThingDefOf.MealSimple)
                .Where(t => t.Spawned && !t.IsForbidden(Faction.OfPlayer))
                .ToList();

            if (meals.Count == 0) { return new List<TransferableOneWay>(); }

            TransferableOneWay transferable = new TransferableOneWay();
            foreach (Thing meal in meals)
            {
                transferable.things.Add(meal);
            }
            transferable.AdjustTo(Mathf.Min(10, transferable.MaxCount));

            return new List<TransferableOneWay> { transferable };
        }

        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();

            if (CurrentOperation is SkipgateOperation_Link activeLink
                && activeLink.Phase == SkipgateOperationPhase.Active)
            {
                sb.AppendLine($"Linked to: {(LinkedFarGate?.parent as Building_Skipgate)?.RenamableLabel ?? "unknown"}");

                if (!Capacitor.Powered && Capacitor.LoadPerSecond > 0f)
                {
                    int collapseTicks = Mathf.CeilToInt(Capacitor.Charge / Capacitor.LoadPerSecond * 60f);
                    sb.AppendLine($"WARNING: no power — link collapse in {collapseTicks.ToStringTicksToPeriod()}");
                }
            }
            else if (CurrentOperation is SkipgateOperation_Link incoming && incoming.Role == LinkRole.Responder)
            {
                sb.AppendLine($"Incoming link from: {(incoming.OtherGate?.parent as Building_Skipgate)?.RenamableLabel ?? "unknown"}");
            }
            else if (CurrentOperation != null)
            {
                switch (CurrentOperation.Phase)
                {
                    case SkipgateOperationPhase.Preparing:
                        sb.AppendLine($"Preparing: {CurrentOperation.Type}");
                        break;

                    case SkipgateOperationPhase.Dialing:
                        sb.AppendLine($"Dialing: {CurrentOperation.Type} ({CurrentOperation.DialingTicksLeft.ToStringTicksToPeriod()})");
                        break;
                }
            }

            // DEV visibility for cycle-A testing; the real Send inspect line arrives with the operation.
            if (DebugSettings.ShowDevGizmos)
            {
                Lord formingLord = parent.Map?.lordManager.lords
                    .FirstOrDefault(l => l.LordJob is LordJob_FormSkipgateCaravan);

                if (formingLord?.LordJob is LordJob_FormSkipgateCaravan formingJob)
                {
                    sb.AppendLine($"DEV caravan: {formingJob.Status} — holding: {formingJob.Holding}, assembled: {formingJob.AllAssembled}");
                }
            }


            if (CoolingDown) { sb.AppendLine($"Cooling down ({CooldownTicksLeft().ToStringTicksToPeriod()})"); }

            return sb.ToString().TrimEndNewlines();
        }
    }
}