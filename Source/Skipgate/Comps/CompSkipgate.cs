using MigCorp.Skiptech.Skipgate.Operations;
using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.Skipgate.Comps
{
    public class CompProperties_Skipgate : CompProperties
    {
        public CompProperties_Skipgate() => compClass = typeof(CompSkipgate);

        public float heatPerCost = 2.5f;
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

        public int emergencyChargeTicks = 180;
    }

    [StaticConstructorOnStartup]
    public class CompSkipgate : ThingComp
    {
        private static readonly Texture2D CancelIcon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");
        public CompProperties_Skipgate Props => (CompProperties_Skipgate)props;
        private CompSkipgateCapacitor capacitor;
        public CompSkipgateCapacitor Capacitor => capacitor ?? (capacitor = parent.GetComp<CompSkipgateCapacitor>());

        private SkipgateOperation currentOperation;
        public SkipgateOperation CurrentOperation => currentOperation;
        public Building_Skipgate Gate => (Building_Skipgate)parent;
        public string GateLabel => Gate.RenamableLabel;

        private Building_SkipgatePortal portal;
        public Building_SkipgatePortal Portal => portal;


        private float heatRemaining;
        public float HeatRemaining => heatRemaining;
        public bool CoolingDown => heatRemaining > 0f;
        public int CooldownTicksFor(float heat) => Props.heatDissipationPerSecond <= 0f ? 0 : Mathf.CeilToInt(heat / Props.heatDissipationPerSecond * 60f);
        private int CooldownTicksLeft() => CooldownTicksFor(heatRemaining);

        public CompSkipgate LinkedFarGate => CurrentOperation is SkipgateOperation_Link link
                                && link.Phase == SkipgateOperationPhase.Active
                                ? link.OtherGate
                                : null;


        //public bool recallResearchFinished = DefDatabase<ResearchProjectDef>.GetNamed("MigCorpSkipTech_SkipgateRecall").IsFinished;
        //public bool linkResearchFinished = DefDatabase<ResearchProjectDef>.GetNamed("MigCorpSkipTech_SkipgateLink").IsFinished;
        public bool recallResearchFinished = true; // true for testing
        public bool linkResearchFinished = true; // true for testing

        private bool postLoadValidationPending;
        private bool autoSend;
        public bool AutoSend => autoSend;
        public void SetAutoSend(bool value) => autoSend = value;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref heatRemaining, "heatRemaining", defaultValue: 0f);
            Scribe_Deep.Look(ref currentOperation, "currentOperation", this);
            Scribe_References.Look(ref portal, "portal");
            Scribe_Values.Look(ref autoSend, "autoSend", defaultValue: false);
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
                    Messages.Message($"Skipgate {GateLabel} is ready to be used again.", MessageTypeDefOf.NeutralEvent);
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

                foreach (Gizmo gizmo in CurrentOperation.GetGizmos())
                {
                    yield return gizmo;
                }
            }
            else
            {
                // Send
                yield return Gizmo_Send();

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
            }
        }

        public Gizmo Gizmo_Cancel()
        {
            SkipgateOperation operation = CurrentOperation;

            return new Command_Action
            {
                defaultLabel = operation.CancelLabel,
                defaultDesc = operation.CancelDesc,
                icon = CancelIcon,
                action = delegate
                {
                    string confirmation = operation.CancelConfirmation;

                    if (confirmation != null)
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            confirmation,
                            () => currentOperation?.TryCancel(),
                            destructive: true));
                        return;
                    }

                    currentOperation?.TryCancel();
                }
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
            Command_Action command = Skipgate_Command_Operation(
                defaultLabel: "Send",
                defaultDesc: "Form a caravan at this skipgate and skip it somewhere else.\n\n" +
                    "Pawns gather at the gate exactly as they would for an ordinary caravan, and the gate charges while they do.",
                action: delegate { Find.WindowStack.Add(new Dialog_FormSkipgateCaravan(this)); }
                );

            command.icon = CompLaunchable.LaunchCommandTex;

            return command;
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
            return new Command_LinkSkipgate(this);
        }

        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();

            CurrentOperation?.AppendInspectLines(sb);

            if (CoolingDown) { sb.AppendLine($"Cooling down ({CooldownTicksLeft().ToStringTicksToPeriod()})"); }

            return sb.ToString().TrimEndNewlines();
        }
    }
}