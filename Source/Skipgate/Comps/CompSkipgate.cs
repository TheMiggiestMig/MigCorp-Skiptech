using MigCorp.Skiptech.Skipgate.Actions;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using static System.Net.Mime.MediaTypeNames;

namespace MigCorp.Skiptech.Skipgate.Comps
{
    public enum SkipgateState
    {
        Idle,
        Charging,
        Pending, // Fully charged, but pawns still gathering. May be skipped if pawns are ready before charging is done.
        Dialing,
        LinkActive,
        Cooldown
    }

    public class CompProperties_Skipgate : CompProperties
    {
        public CompProperties_Skipgate() => compClass = typeof(CompSkipgate);
        public int heatDissipationRate = 1;
    }
    public class CompSkipgate : ThingComp
    {
        public CompProperties_Skipgate Props => (CompProperties_Skipgate)props;
        public CompSkipgateCapacitor capacitor;

        public SkipgateAction_Send sendAction;
        public SkipgateAction_Recall recallAction;
        public SkipgateAction_Link linkAction;

        public SkipgateAction action;
        private SkipgateState state = SkipgateState.Idle;
        public SkipgateState State
        {
            get
            {
                if (heatRemaining > 0) { return SkipgateState.Cooldown; }
                return state;
            }
        }

        private int heatRemaining;
        public int HeatRemaining;
        private int CooldownTicksLeft() =>
            Mathf.CeilToInt(heatRemaining / Props.heatDissipationRate);


        //public bool recallResearchFinished = DefDatabase<ResearchProjectDef>.GetNamed("MigCorpSkipTech_SkipgateRecall").IsFinished;
        //public bool linkResearchFinished = DefDatabase<ResearchProjectDef>.GetNamed("MigCorpSkipTech_SkipgateLink").IsFinished;
        public bool recallResearchFinished = true; // true for testing
        public bool linkResearchFinished = true; // true for testing

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref heatRemaining, "heatRemaining", defaultValue: 0);
            Scribe_Values.Look(ref state, "state", defaultValue: SkipgateState.Idle);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            capacitor = parent.GetComp<CompSkipgateCapacitor>();

            sendAction = new SkipgateAction_Send(this);
            recallAction = new SkipgateAction_Recall(this);
            linkAction = new SkipgateAction_Link(this);

        }

        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);

            if (State == SkipgateState.Cooldown) {
                heatRemaining -= delta * Props.heatDissipationRate;

                if (heatRemaining <= 0)
                {
                    heatRemaining = 0;
                    Messages.Message($"Skipgate {(parent as Building_Skipgate).RenamableLabel} is ready to be used again.", MessageTypeDefOf.NeutralEvent);
                }
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo item in base.CompGetGizmosExtra())
            {
                yield return item;
            }

            // Cancel Action
            if (State != SkipgateState.Idle && State != SkipgateState.Cooldown)
            {
                yield return Gizmo_Cancel();
            }
            else
            {
                // Send
                if (recallResearchFinished)
                {
                    yield return Gizmo_Send();
                }

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
                    defaultLabel = "DEV: Add +100 heat",
                    action = delegate
                    {
                        heatRemaining += 100;
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = "DEV: Reset heat",
                    action = delegate
                    {
                        heatRemaining = 0;
                    }
                };

                // Test switching state
                /*
                List<FloatMenuOption> list = new List<FloatMenuOption>();
                Command_Action command = new Command_Action();
                command.defaultLabel = $"DEV: Switch state (current: {State})";
                command.action = delegate
                {
                    foreach (SkipgateState st in Enum.GetValues(typeof(SkipgateState)).Cast<SkipgateState>().ToList())
                    {
                        list.Add(new FloatMenuOption($"Set state to {st}.", delegate
                        {
                            state = st;
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(list));
                };
                yield return command;
                */
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
                    if (action == null || action.TryCancelAction())
                    {
                        state = SkipgateState.Idle;
                    }
                    Messages.Message("Let me think about it.", MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        private Command_Action Skipgate_Command_Action(string defaultLabel, string defaultDesc, System.Action action)
        {
            Command_Action command = new Command_Action();
            if (State == SkipgateState.Cooldown)
            {
                command = new Command_Action();
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
            Command_Action command = Skipgate_Command_Action(
                defaultLabel: "Send",
                defaultDesc: "Send a load to a remote location in the world.",
                action: delegate
                {
                    Messages.Message("It was never about the journey.", MessageTypeDefOf.PositiveEvent);
                }
                );

            return command;
        }

        public Gizmo Gizmo_EmergencyRecall()
        {
            Command_Action command = Skipgate_Command_Action(
                defaultLabel: "Emergency Recall",
                defaultDesc: "Target a pawn or caravan equipped with a skip beacon and teleport them to this skipgate, consuming the skip beacon.\n\n" +
                    "WARNING: Will cause damage and breakdowns around the map!".Colorize(Color.yellow),
                action: delegate
                {
                    Messages.Message("Get me outta here!", MessageTypeDefOf.NegativeEvent);
                }
                );

            return command;
        }

        public Gizmo Gizmo_Recall()
        {
            Command_Action command = Skipgate_Command_Action(
                defaultLabel: "Recall",
                defaultDesc: $"Targets a pawn or caravan equipped with a skip beacon and teleports them to this skipgate{(!linkResearchFinished ? ", consuming the skip beacon" : null)}.",
                action: delegate
                {
                    Messages.Message("Home sweet home.", MessageTypeDefOf.PositiveEvent);
                }
                );

            return command;
        }

        public Gizmo Gizmo_Link()
        {
            Command_Action command = Skipgate_Command_Action(
                defaultLabel: "Link",
                defaultDesc: "Create a skip portal connecting two skipgates.",
                action: delegate
                {
                    Messages.Message("I know a shortcut.", MessageTypeDefOf.PositiveEvent);
                }
                );

            return command;
        }

        public override string CompInspectStringExtra()
        {
            string text = "";

            if (State == SkipgateState.Cooldown) { text += $"Cooling down ({CooldownTicksLeft().ToStringTicksToPeriod()})"; }

            return text + base.CompInspectStringExtra();
        }
    }
}
