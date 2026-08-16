using MigCorp.Skiptech.Skipgate.Comps;
using MigCorp.Skiptech.Skipgate.Operations;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MigCorp.Skiptech.Skipgate
{
    public class Command_SkipgateSendOrder : Command
    {
        private enum SendOrderState
        {
            Idle,
            SendOnce,
            Auto
        }

        private readonly SkipgateOperation_Send send;
        private readonly SendOrderState state;

        public Command_SkipgateSendOrder(CompSkipgate gate, SkipgateOperation_Send send)
        {
            this.send = send;

            state = gate.AutoSend ? SendOrderState.Auto
                : send.SendPressed ? SendOrderState.SendOnce
                : SendOrderState.Idle;

            defaultLabel = "Send";
            defaultDesc = DescFor(state);
            icon = IconFor(state);
        }

        public override void ProcessInput(Event ev)
        {
            // Deliberately not calling base.ProcessInput: it plays CurActivateSound without knowing
            // which button was used. Pick the checkbox sound from what this click is about to do.
            bool rightClick = ev != null && ev.button == 1;
            bool turningOn = rightClick ? state != SendOrderState.Auto : state != SendOrderState.SendOnce;

            (turningOn ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();

            if (rightClick) { send.ToggleAutoSend(); }
            else { send.ToggleSendOrder(); }
        }

        // Only merge multi-select interactions with gizmos in the same state (same rule as Command_Toggle).
        public override bool InheritInteractionsFrom(Gizmo other)
        {
            return other is Command_SkipgateSendOrder o && o.state == state;
        }

        // PLACEHOLDER icons until the art pass — one switch to swap.
        private static Texture2D IconFor(SendOrderState state)
        {
            switch (state)
            {
                case SendOrderState.SendOnce: return TexCommand.Attack;
                case SendOrderState.Auto: return TexCommand.ReleaseAnimals;
                default: return CompLaunchable.LaunchCommandTex;
            }
        }

        private static string DescFor(SendOrderState state)
        {
            string current;
            switch (state)
            {
                case SendOrderState.SendOnce:
                    current = "Send (when ready): caravan skips as soon as the gate is charged and everyone has gathered.";
                    break;

                case SendOrderState.Auto:
                    current = "Auto-send: gate automatically sends the once it's ready.";
                    break;

                default:
                    current = "Wait: caravan waits at the gate until you give an order (charging freezes when full).";
                    break;
            }

            return "Left-click: toggle a send order for this caravan.\n" +
                "Right-click: toggle auto-send for this gate.\n\n" + current;
        }
    }
}