using MigCorp.Skiptech.Utils;
using RimWorld;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet.Comps
{
    public class CompProperties_Skipdoor : CompProperties
    {
        public CompProperties_Skipdoor() => compClass = typeof(CompSkipdoor);
    }

    public class CompSkipdoor : ThingComp, ISkipdoorAccessible, IRenameable
    {
        public CompProperties_Skipdoor Props => (CompProperties_Skipdoor)props;
        public IntVec3 Position => parent.Position;
        public List<ISkipdoorAccessible> accessibilityComps = new List<ISkipdoorAccessible>();
        public MapComponent_SkipNet SkipNet => parent.Map?.GetComponent<MapComponent_SkipNet>();

        private CompForbiddable forbiddableComp;
        private CompBreakdownable breakdownableComp;
        private CompFlickable flickableComp;

        private string skipdoorName;
        public bool IsNamed => skipdoorName != null;

        public string RenamableLabel
        {
            get { return skipdoorName ?? string.Empty; }
            set
            {
                string trimmed = value?.Trim();
                skipdoorName = trimmed.NullOrEmpty() ? null : trimmed;
            }
        }
        public string BaseLabel => parent.def.LabelCap;
        public string InspectLabel => BaseLabel;

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

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref skipdoorName, "skipdoorName");
        }

        public override string CompInspectStringExtra()
        {
            return IsNamed ? "MigCorp.Skiptech.Text.ID".Translate(skipdoorName).Resolve() : null;
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

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) { yield return gizmo; }

            // Unowned doors are allowed since VPE skipdoors are spawned without a faction.
            if ((parent.Faction != null && parent.Faction != Faction.OfPlayer) || Find.Selector.SingleSelectedThing != parent) { yield break; }

            yield return new Command_Action
            {
                defaultLabel = "MigCorp.Skiptech.Skipdoor.SetID".Translate(),
                defaultDesc = "MigCorp.Skiptech.Skipdoor.Rename.Desc".Translate(),
                icon = TexButton.Rename,
                action = () => Find.WindowStack.Add(new Dialog_RenameSkipdoor(this))
            };
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.CompFloatMenuOptions(selPawn)) { yield return option; }
            foreach (FloatMenuOption option in SkipLocallyOptions(Gen.YieldSingle(selPawn))) { yield return option; }
        }

        public override IEnumerable<FloatMenuOption> CompMultiSelectFloatMenuOptions(IEnumerable<Pawn> selPawns)
        {
            foreach (FloatMenuOption option in base.CompMultiSelectFloatMenuOptions(selPawns)) { yield return option; }
            foreach (FloatMenuOption option in SkipLocallyOptions(selPawns)) { yield return option; }
        }

        private IEnumerable<FloatMenuOption> SkipLocallyOptions(IEnumerable<Pawn> selPawns)
        {
            MapComponent_SkipNet skipNet = MapComponent_SkipNet.For(parent.Map);
            if (skipNet == null) { yield break; }

            //List<Pawn> pawns = new List<Pawn>();
            bool anyPawnCanEnter = false;
            List<string> pawnsRejected = new List<string>();
            List<CompSkipdoor> exits = new List<CompSkipdoor>();
            Dictionary<CompSkipdoor, List<Pawn>> exitPawns = new Dictionary<CompSkipdoor, List<Pawn>>();
            Dictionary<CompSkipdoor, List<string>> exitPawnsRejected = new Dictionary<CompSkipdoor, List<string>>();

            // Cycle through each pawn to see if they can receive a job.
            foreach (Pawn pawn in selPawns)
            {
                SkipNetAccessContext ac = new SkipNetAccessContext(pawn, forced: true);
                if (!(skipNet.manager.TryFilterSettings(pawn) && IsEnterableBy(ac) && pawn.CanReach(parent, PathEndMode.OnCell, Danger.Deadly)))
                {
                    pawnsRejected.AddDistinct("MigCorp.Skiptech.Skipdoor.PawnCannotSkip".Translate(pawn.LabelShort, "MigCorp.Skiptech.Skipdoor.CannotSkipNoEntry".Translate()));
                    continue;
                }
                anyPawnCanEnter = true;

                //bool exitable = false;
                foreach (CompSkipdoor skipdoor in skipNet.skipdoors)
                {
                    if (skipdoor == this || !skipdoor.IsNamed) continue;

                    // This doesn't mean it will be successful, it just means there *is* an exit it can use.
                    if (skipdoor.IsExitableBy(ac))
                    {
                        //pawns.Add(pawn);
                        if (!exitPawns.TryGetValue(skipdoor, out List<Pawn> accepted))
                        {
                            accepted = new List<Pawn>();
                            exitPawns[skipdoor] = accepted;
                        }
                        accepted.Add(pawn);
                        exits.AddDistinct(skipdoor);
                        //exitable = true;
                    }
                    else
                    {
                        if (!exitPawnsRejected.TryGetValue(skipdoor, out List<string> rejected))
                        {
                            rejected = new List<string>();
                            exitPawnsRejected[skipdoor] = rejected;
                        }
                        rejected.Add("MigCorp.Skiptech.Skipdoor.PawnCannotSkip".Translate(pawn.LabelShort, "MigCorp.Skiptech.Skipdoor.CannotSkipNoExit".Translate()));
                    }
                }

                //if (!exitable)
                //{
                //    pawnsRejected.AddDistinct("MigCorp.Skiptech.Skipdoor.PawnCannotSkip".Translate(pawn.LabelShort, "MigCorp.Skiptech.Skipdoor.CannotSkipNoExits".Translate()));
                //    continue;
                //}
            }

            // Check that at least one pawn can use the entry, and take an exit.
            //if(pawns.Count == 0)
            if (!anyPawnCanEnter)
            {
                yield return new FloatMenuOption("MigCorp.Skiptech.Skipdoor.CannotSkip".Translate("MigCorp.Skiptech.Skipdoor.CannotSkipNoEntry".Translate()), null);
                yield break;
            }

            if (exits.Count == 0)
            {
                yield return new FloatMenuOption("MigCorp.Skiptech.Skipdoor.CannotSkip".Translate("MigCorp.Skiptech.Skipdoor.CannotSkipNoExits".Translate()), null);
                yield break;
            }

            // Looks good. Sort the exits and list the options.
            exits.Sort((a, b) => string.Compare(a.RenamableLabel, b.RenamableLabel, System.StringComparison.OrdinalIgnoreCase));

            foreach (CompSkipdoor exit in exits)
            {
                yield return new FloatMenuOption("MigCorp.Skiptech.Skipdoor.SkipLocallyTo".Translate(exit.RenamableLabel), () =>
                {
                    //foreach (Pawn pawn in pawns)
                    foreach (Pawn pawn in exitPawns[exit])
                    {
                        // The menu can outlive a pawn's state; the job's own fail conditions cover the rest.
                        if (!pawn.Spawned || pawn.Downed) { continue; }

                        // Make a job for the pawn.
                        Job job = JobMaker.MakeJob(SkiptechDefOf.MigCorp_SkipLocally, parent, exit.parent);
                        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }

                    // Show errors for the pawns that can't receive a job.
                    foreach (string reason in pawnsRejected)
                    {
                        Messages.Message(reason, MessageTypeDefOf.RejectInput, historical: false);
                    }
                    if (exitPawnsRejected.ContainsKey(exit))
                    {
                        foreach (string reason in exitPawnsRejected[exit])
                        {
                            Messages.Message(reason, MessageTypeDefOf.RejectInput, historical: false);
                        }
                    }

                }, revalidateClickTarget: parent);
            }
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


        public void Notify_PawnArrived(Pawn pawn, SkipdoorType type)
        {
            foreach (ISkipdoorAccessible comp in accessibilityComps)
            {
                if (comp == this) continue;
                //comp.Notify_PawnArrived(pawn, skipNetPlan, type);
                comp.Notify_PawnArrived(pawn, type);
            }
        }

        public void Notify_PawnTeleported(Pawn pawn, SkipdoorType type)
        {
            foreach (ISkipdoorAccessible comp in accessibilityComps)
            {
                if (comp == this) continue;
                //comp.Notify_PawnTeleported(pawn, skipNetPlan, type);
                comp.Notify_PawnTeleported(pawn, type);
            }
        }
    }
}