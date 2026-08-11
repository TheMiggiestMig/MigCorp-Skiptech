using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    // Left-click: world targeter.
    // Right-click: list skipgates by name. You did name them, right?
    public sealed class Command_LinkSkipgate : Command_Action
    {
        private readonly CompSkipgate source;

        public Command_LinkSkipgate(CompSkipgate source)
        {
            this.source = source;

            defaultLabel = "Link to...";
            defaultDesc = "Select another skipgate to establish a link.\n\n" +
                "Right-click to choose a skipgate by name.";
            icon = TexCommand.Install;

            if (source.CoolingDown)
            {
                Disabled = true;
                disabledReason = "Skipgate cannot be used while dispersing heat.";
            }

            action = () => SkipgateTargetingUtil.BeginLinkTargeting(source);
        }

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions =>
            SkipgateTargetingUtil.GetNamedTargetOptions(source);
    }
}