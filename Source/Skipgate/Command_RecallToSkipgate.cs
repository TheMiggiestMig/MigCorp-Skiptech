using MigCorp.Skiptech.Skipgate.Operations;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    public sealed class Command_RecallToSkipgate : Command_Action
    {
        private readonly Thing beacon;
        private readonly SkipgateRecallMode mode;

        public Command_RecallToSkipgate(Thing beacon, SkipgateRecallMode mode)
        {
            this.beacon = beacon;
            this.mode = mode;

            bool emergency = mode == SkipgateRecallMode.Emergency;

            defaultLabel = emergency ? "Emergency Recall" : "Recall";
            defaultDesc = (emergency
                ? "Call for an emergency skip out, consuming the skip beacon.\n\n" +
                  "WARNING: Will cause damage and breakdowns around the map!".Colorize(Color.yellow)
                : "Call for a skip back to one of your skipgates.")
                + "\n\nRight-click to choose a skipgate by name.";

            icon = CompLaunchable.LaunchCommandTex;

            action = () => SkipgateTargetingUtil.BeginRecallGateTargeting(beacon, mode);
        }

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions =>
            SkipgateTargetingUtil.GetRecallGateOptions(beacon, mode);
    }
}