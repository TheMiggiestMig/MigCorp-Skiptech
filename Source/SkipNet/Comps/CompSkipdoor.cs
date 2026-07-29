using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.SkipNet.Comps
{
    public class CompProperties_Skipdoor : CompProperties
    {
        public CompProperties_Skipdoor() => compClass = typeof(CompSkipdoor);
    }

    public class CompSkipdoor : ThingComp, ISkipdoorAccessible
    {
        public CompProperties_Skipdoor Props => (CompProperties_Skipdoor)props;
        public IntVec3 Position => parent.Position;
        public List<ISkipdoorAccessible> accessibilityComps = new List<ISkipdoorAccessible>();
        public MapComponent_SkipNet SkipNet => parent.Map?.GetComponent<MapComponent_SkipNet>();

        private CompForbiddable forbiddableComp;
        private CompBreakdownable breakdownableComp;
        private CompFlickable flickableComp;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            List<ThingComp> parentThingComps = parent.AllComps;
            base.PostSpawnSetup(respawningAfterLoad);

            foreach (ThingComp thingComp in parentThingComps)
            {
                if (thingComp is ISkipdoorAccessible accessible) { accessibilityComps.Add(accessible); }
            }

            forbiddableComp = parent.GetComp<CompForbiddable>();
            breakdownableComp = parent.GetComp<CompBreakdownable>();
            flickableComp = parent.GetComp<CompFlickable>();

            SkipNet.RegisterSkipdoor(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode dMode)
        {
            map.GetComponent<MapComponent_SkipNet>().UnregisterSkipdoor(this);
            base.PostDeSpawn(map, dMode);
        }

        private bool GetAccess(in SkipNetAccessContext ac)
        {
            if (ac.isPlayerFaction && (forbiddableComp?.Forbidden ?? false)) { return false; }
            if (breakdownableComp?.BrokenDown ?? false) { return false; }
            if (!(flickableComp?.SwitchIsOn ?? true)) { return false; }

            // Should fix pawns pathing to skipdoors outside their allowed zones... I think.
            if (ac.allowedArea != null && !ac.allowedArea[parent.Position]) { return false; }

            return true;
        }

        public void IsUsableBy(in SkipNetAccessContext ac, out bool canEnter, out bool canExit)
        {
            canEnter = canExit = false;
            if (!GetAccess(in ac)) { return; }
            canEnter = canExit = true;
            foreach (ISkipdoorAccessible comp in accessibilityComps)
            {
                if (comp == this) { continue; }
                if (canEnter && !comp.CanEnter(in ac)) { canEnter = false; }
                if (canExit && !comp.CanExit(in ac)) { canExit = false; }
                if (!canEnter && !canExit) { return; }
            }
        }

        /// <summary>
        /// Aggregate check if a pawn can enter this skipdoor by checking the comps that define access rules.
        /// </summary>
        public bool IsEnterableBy(in SkipNetAccessContext ac)
        {
            foreach (ISkipdoorAccessible comp in accessibilityComps)
                if (!comp.CanEnter(in ac)) { return false; }

            return true;
        }

        /// <summary>
        /// Aggregate check if a pawn can exit this skipdoor by checking the comps that define access rules.
        /// </summary>
        public bool IsExitableBy(in SkipNetAccessContext ac)
        {
            foreach (ISkipdoorAccessible comp in accessibilityComps)
                if (!comp.CanExit(in ac)) { return false; }

            return true;
        }

        /// <summary>
        /// Aggregate check if a pawn can enter this skipdoor at this very moment by checking the comps that define access rules.
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
                ticks = ticks == 0 && compTicks > 0 ? compTicks : compTicks > 0 ? Mathf.Min(ticks, compTicks) : ticks;
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
                ticks = ticks == 0 && compTicks > 0 ? compTicks : compTicks > 0 ? Mathf.Min(ticks, compTicks) : ticks;
            }
            return ticks == 0;
        }

        public bool CanEnter(in SkipNetAccessContext ac)
        {
            return GetAccess(in ac);
        }
        public bool CanExit(in SkipNetAccessContext ac)
        {
            return GetAccess(in ac);
        }

        public int TicksUntilEnterable(Pawn pawn) { return 0; }
        public int TicksUntilExitable(Pawn pawn) { return 0; }

        public void Notify_PawnArrived(Pawn pawn, SkipNetPlan skipNetPlan, SkipdoorType type)
        {
            foreach (ISkipdoorAccessible comp in accessibilityComps)
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