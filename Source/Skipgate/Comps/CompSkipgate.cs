using MigCorp.Skiptech.Skipgate.Operations;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

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
    }
    public class CompSkipgate : ThingComp, IThingHolder, ISearchableContents
    {
        public CompProperties_Skipgate Props => (CompProperties_Skipgate)props;
        private CompSkipgateCapacitor capacitor;
        public CompSkipgateCapacitor Capacitor { get { return capacitor; } }

        private SkipgateOperation currentOperation;
        public SkipgateOperation CurrentOperation { get { return currentOperation; } }

        private ThingOwner innerContainer;
        public ThingOwner SearchableContents => innerContainer;

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
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_References.Look(ref portal, "portal");
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            capacitor = parent.GetComp<CompSkipgateCapacitor>();

            if (innerContainer == null) { innerContainer = new ThingOwner<Thing>(this); }
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

            return true;
        }

        public void EndOperation(SkipgateOperation operation, SkipgateOperationEnd result, float heatGenerated)
        {
            // Make sure we're only ending our own operation.
            if (currentOperation != operation) { return; }

            capacitor.ClearDemand();
            //if (currentOperation is SkipgateOperation_Link) { DespawnPortal(); }
            DespawnPortal();

            heatRemaining += heatGenerated;
            currentOperation = null;
        }
        public bool TrySpendCharge(float amount)
        {
            return capacitor.TrySpend(amount);
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
            }
            else
            {
                // Send
                yield return Gizmo_SendLoad();

                // Send
                yield return Gizmo_SendCaravan();

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
            return new Command_Action
            {
                defaultLabel = "Cancel Action",
                defaultDesc = "Cancel the current action.\n\n" +
                    "The current charge will remain but slowly drain.",
                icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                action = delegate
                {
                    if (currentOperation != null && currentOperation.TryCancel())
                    {
                        Messages.Message("Let me think about it.", MessageTypeDefOf.NeutralEvent);
                    }
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

        public Gizmo Gizmo_SendLoad()
        {
            Command_Action command = Skipgate_Command_Operation(
                defaultLabel: "Send Load",
                defaultDesc: "Send a load to a remote location in the world.",
                action: delegate
                {
                    TryStartOperation(new SkipgateOperation_SendLoad(this, "Sending a load!", 66f));
                }
                );

            return command;
        }

        public Gizmo Gizmo_SendCaravan()
        {
            Command_Action command = Skipgate_Command_Operation(
                defaultLabel: "Send Caravan",
                defaultDesc: "Send a caravan to a remote location in the world.",
                action: delegate
                {
                    TryStartOperation(new SkipgateOperation_SendCaravan(this, "Sending some pawns!", 42f));
                }
                );

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
            Command_Action command = Skipgate_Command_Operation(
                defaultLabel: "Link",
                defaultDesc: "Create a skip portal connecting two skipgates.",
                action: delegate
                {
                    // DEV Temp targeting. OR... I could reuse it for a right-click alternative with RightClickFloatMenuOptions :O
                    List<FloatMenuOption> options = new List<FloatMenuOption>();

                    foreach (Map map in Find.Maps)
                    {
                        foreach (Building_Skipgate target in map.listerBuildings.AllBuildingsColonistOfClass<Building_Skipgate>())
                        {
                            if (target == parent) { continue; }

                            float cost = SkipgateOperation_Link.CalculateLinkCost(this, target);
                            options.Add(new FloatMenuOption(
                                $"{target.RenamableLabel} ({target.Map.Parent.Label}) — cost {cost:F0}",
                                () => TryStartOperation(new SkipgateOperation_Link(this, target))));
                        }
                    }

                    if (options.Count == 0)
                    {
                        Messages.Message("No other skipgates to link to.", MessageTypeDefOf.RejectInput, historical: false);
                        return;
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                }
                );

            return command;
        }

        public override string CompInspectStringExtra()
        {
            string text = "";

            if (CoolingDown) { text += $"Cooling down ({CooldownTicksLeft().ToStringTicksToPeriod()})"; }

            return text + base.CompInspectStringExtra();
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }
    }
}