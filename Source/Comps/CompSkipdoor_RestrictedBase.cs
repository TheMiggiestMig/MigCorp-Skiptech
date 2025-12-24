using MigCorp.Skiptech.Systems.SkipNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace MigCorp.Skiptech.Comps
{
    public abstract class CompProperties_Skipdoor_RestrictedBase : CompProperties
    {
        public CompProperties_Skipdoor_RestrictedBase() => this.compClass = typeof(CompSkipdoor_RestrictedBase);
    }
    public abstract class CompSkipdoor_RestrictedBase : ThingComp, ISkipdoorAccessible
    {
        public abstract bool CanEnter(Pawn pawn);
        public abstract bool CanExit(Pawn pawn);
        public abstract void Notify_PawnArrived(Pawn pawn, SkipNetPlan skipNetPlan, SkipdoorType type = SkipdoorType.Entry);
        public abstract int TicksUntilEnterable(Pawn pawn);
        public abstract int TicksUntilExitable(Pawn pawn);
    }
}
