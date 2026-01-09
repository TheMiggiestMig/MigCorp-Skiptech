using Verse;
using MigCorp.Skiptech.Systems.SkipNet;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;

namespace MigCorp.Skiptech.Comps
{
    public class CompProperties_Skipdoor : CompProperties
    {
        public CompProperties_Skipdoor() => this.compClass = typeof(CompSkipdoor);
    }

    public class CompSkipdoor : ThingComp, ISkipdoorAccessible
    {
        public CompProperties_Skipdoor Props => (CompProperties_Skipdoor)props;
        public IntVec3 Position => parent.Position;
        public List<ISkipdoorAccessible> accessibilityComps = new List<ISkipdoorAccessible>();
        public MapComponent_SkipNet SkipNet => parent.Map?.GetComponent<MapComponent_SkipNet>();

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            List<ThingComp> parentThingComps = parent.AllComps;
            base.PostSpawnSetup(respawningAfterLoad);
            
            foreach(ThingComp thingComp in parentThingComps)
            {
                if (thingComp is ISkipdoorAccessible accessible) { accessibilityComps.Add(accessible); }
            }

            SkipNet.RegisterSkipdoor(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode dMode)
        {
            map.GetComponent<MapComponent_SkipNet>().UnregisterSkipdoor(this);
            base.PostDeSpawn(map, dMode);
        }


        /// <summary>
        /// Aggregate check if a pawn can exit this skipdoor by checking the comps that define access rules.
        /// </summary>
        public bool IsEnterableBy(Pawn pawn)
        {
            foreach (ISkipdoorAccessible comp in accessibilityComps)
                if (!comp.CanEnter(pawn)) { return false; }

            return true;
        }

        /// <summary>
        /// Aggregate check if a pawn can exit this skipdoor by checking the comps that define access rules.
        /// </summary>
        public bool IsExitableBy(Pawn pawn)
        {
            foreach (ISkipdoorAccessible comp in accessibilityComps)
                if (!comp.CanExit(pawn)) { return false; }

            return true;
        }

        /// <summary>
        /// Aggregate check if a pawn can exit this skipdoor at this very moment by checking the comps that define access rules.
        /// </summary>
        /// <remarks>
        /// This differs from <c>IsEnterableBy</c> since a pawn may be a "allowed" access to enter a skipdoor,
        /// but it may not be ready yet (e.g. Requires charging up).
        /// </remarks>
        public bool IsEnterableNowBy(Pawn pawn, out int ticks)
        {
            ticks = 0;
            foreach (ISkipdoorAccessible comp in accessibilityComps)
            {
                int compTicks = comp.TicksUntilEnterable(pawn);
                ticks = (ticks == 0 && compTicks > 0) ? compTicks : compTicks > 0 ? Mathf.Min(ticks, compTicks) : ticks;
            }
            return ticks == 0;
        }

        /// <summary>
        /// Aggregate check if a pawn can exit this skipdoor at this very moment by checking the comps that define access rules.
        /// </summary>
        /// <remarks>
        /// This differs from <c>IsExitableBy</c> since a pawn may be a "allowed" access to exit a skipdoor,
        /// but it may not be ready yet (e.g. Requires charging up).
        /// </remarks>
        public bool IsExitableNowBy(Pawn pawn, out int ticks)
        {
            ticks = 0;
            foreach (ISkipdoorAccessible comp in accessibilityComps)
            {
                int compTicks = comp.TicksUntilExitable(pawn);
                ticks = (ticks == 0 && compTicks > 0) ? compTicks : compTicks > 0 ? Mathf.Min(ticks, compTicks) : ticks;
            }
            return ticks == 0;
        }

        public bool CanEnter(Pawn pawn)
        {
            bool allowed = true;

            // Check if it is forbidden (if it even has that comp)
            allowed = (pawn.IsColonist && (parent.GetComp<CompForbiddable>()?.Forbidden ?? false)) ? false : allowed;

            // Check if it is broken (if it even has that comp)
            allowed = (parent.GetComp<CompBreakdownable>()?.BrokenDown ?? false) ? false : allowed;

            return allowed;
        }
         
        public bool CanExit(Pawn pawn)
        {
            bool allowed = true;

            // Check if it is forbidden (if it even has that comp)
            allowed = (pawn.IsColonist && (parent.GetComp<CompForbiddable>()?.Forbidden ?? false)) ? false : allowed;

            // Check if it is broken (if it even has that comp)
            allowed = (parent.GetComp<CompBreakdownable>()?.BrokenDown ?? false) ? false : allowed;

            return allowed;
        }

        public int TicksUntilEnterable(Pawn pawn) { return 0; }
        public int TicksUntilExitable(Pawn pawn) { return 0; }

        public void Notify_PawnArrived(Pawn pawn, SkipNetPlan skipNetPlan, SkipdoorType type)
        {
            foreach(ISkipdoorAccessible comp in accessibilityComps)
            {
                if (comp == this) continue;
                comp.Notify_PawnArrived(pawn, skipNetPlan, type);
            }
        }

        public void Notify_PawnTeleported(Pawn pawn, SkipNetPlan skipNetPlan, SkipdoorType type)
        {
            foreach (ISkipdoorAccessible comp in accessibilityComps)
            {
                if (comp == this) continue;
                comp.Notify_PawnTeleported(pawn, skipNetPlan, type);
            }
        }
    }
}