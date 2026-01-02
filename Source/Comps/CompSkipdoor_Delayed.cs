using MigCorp.Skiptech.Systems.SkipNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using static HarmonyLib.Code;

namespace MigCorp.Skiptech.Comps
{
    public class CompProperties_Skipdoor_Delayed : CompProperties_Skipdoor_RestrictedBase
    {
        public bool restrictEntry = true; // Does this comp apply on entry?
        public bool restrictExit = true; // Does this comp apply on exit?
        public int delayTicks = 60;
        public int remainOpenTicks = 180;

        public CompProperties_Skipdoor_Delayed() => this.compClass = typeof(CompSkipdoor_Delayed);
    }
    public class CompSkipdoor_Delayed : CompSkipdoor_RestrictedBase
    {
        public int curDelayTick = 0;
        public int curRemainOpenTick = 0;

        public bool open;
        public bool opening;
        public bool Open => open;
        public bool Opening => opening;

        public virtual CompProperties_Skipdoor_Delayed Props => (CompProperties_Skipdoor_Delayed)props;

        public override void CompTick()
        {
            base.CompTick();
            if (!open)
            {
                //if (!opening && curDelayTick > 0) { curDelayTick--; return; }
                if (opening)
                {
                    if (curDelayTick == 0)
                    {
                        parent.BroadcastCompSignal("SkipdoorOpening");
                    }
                    curDelayTick++;

                    if (curDelayTick >= Props.delayTicks)
                    {
                        open = true;
                        opening = false;
                        curDelayTick = 0;
                        parent.BroadcastCompSignal("SkipdoorOpened");
                    }
                    return;
                }
            }

            if (curRemainOpenTick < Props.remainOpenTicks && open) { curRemainOpenTick++; }
            if (curRemainOpenTick >= Props.remainOpenTicks)
            {
                open = false;
                curRemainOpenTick = 0;
                parent.BroadcastCompSignal("SkipdoorClosed");
            }
        }
        public override bool CanEnter(Pawn pawn)
        {
            return true;
        }

        public override bool CanExit(Pawn pawn)
        {
            return true;
        }

        public override void Notify_PawnArrived(Pawn pawn, SkipNetPlan skipNetPlan, SkipdoorType type)
        {
            if (open) { curRemainOpenTick = 0; }
            if (Props.restrictEntry && type == SkipdoorType.Entry && !open) { opening = true; }
            if (Props.restrictExit && type == SkipdoorType.Exit && !open) { opening = true; }
        }

        public override void Notify_PawnTeleported(Pawn pawn, SkipNetPlan skipNetPlan, SkipdoorType type)
        {
            return;
        }

        public override int TicksUntilEnterable(Pawn pawn)
        {
            if(Props.restrictEntry && opening) { return Props.delayTicks - curDelayTick;  }
            return 0;
        }

        public override int TicksUntilExitable(Pawn pawn)
        {
            if (Props.restrictExit && opening) { return Props.delayTicks - curDelayTick; }
            return 0;
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref curDelayTick, "curDelayTick", 0);
            Scribe_Values.Look(ref curRemainOpenTick, "curRemainOpenTick", 0);
            Scribe_Values.Look(ref open, "open", false);
            Scribe_Values.Look(ref opening, "opening", false);
        }
    }
}
