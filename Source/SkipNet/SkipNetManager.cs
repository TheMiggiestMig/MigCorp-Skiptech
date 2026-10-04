using LudeonTK;
using MigCorp.Skiptech.SkipNet.Comps;
using MigCorp.Skiptech.Utils;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public class SkipNetManager
    {
        [TweakValue("MigCorp Performance Test", 0, 1)]
        public static int DEBUG_AlwaysInstallDirectPath = 0;

        private const int PendingPlanMaxLifetimeTicks = 20;

        private readonly MapComponent_SkipNet skipNet;
        private readonly SkipNetCandidateFinder finder;
        public int TickPopCap = 150; // Might make this an advanced option in settings.
        private int tickPopCount = 0;
        public bool PopCapReached { get { return tickPopCount >= TickPopCap; } }

        // PawnPathPool throws ErrorOnce once total paths created exceeds 2 * spawnedPawns + 5 (2N + 5).
        // Each pending pair temporarily takes 3 paths on top of the pawn's own (2 splice legs + 1 direct).
        // Assuming worst case scenario, we can process (N - plans + 5) / 2.
        // May still throw the PathPool leak OnceOff error if every animal and every pawn had a path and tried pathing again in the same tick.
        // ...Veeery unlikely though.
        private int MaxPendingPairs { get { return (skipNet.map.mapPawns.AllPawnsSpawnedCount - plansByPawn.Count + 5) / 2; } }
        private bool AtPairCapacity { get { return pendingPairCount >= MaxPendingPairs; } }
        private int pendingPairCount = 0; // Plans in PendingSplicePair. Kept by SetState.

        private readonly Dictionary<Pawn, SkipNetPlan> plansByPawn = new Dictionary<Pawn, SkipNetPlan>();
        private readonly Dictionary<Pawn, SkipNetPlan> live = new Dictionary<Pawn, SkipNetPlan>(); // Active: the splice the pawn is walking.
        private readonly Deque<SkipNetPlan> plans = new Deque<SkipNetPlan>(); // Queue the plan itself, so a stale entry can't be mistaken for the pawn's current one.
        private static readonly List<Pawn> tmpFinished = new List<Pawn>();

        public SkipNetManager(MapComponent_SkipNet skipNet)
        {
            this.skipNet = skipNet;
            finder = new SkipNetCandidateFinder(skipNet);
        }

        public void Notify_SkipdoorRegistered(CompSkipdoor skipdoor)
        {
            finder.MarkRegionDoorIndexDirty();
        }

        public void Notify_SkipdoorUnregistered(CompSkipdoor skipdoor)
        {
            finder.MarkRegionDoorIndexDirty();
            CancelPlansUsingSkipdoor(skipdoor);
        }

        public void Tick()
        {
            ProcessQueue();
            TickLive();
        }

        private void ProcessQueue()
        {
            // Visit each queued plan at most once per tick, even if waiting ones get re-appended.
            int numPlansToProcess = plans.Count;

            while (numPlansToProcess-- > 0 && plans.Count > 0)
            {
                SkipNetPlan plan = plans.PopFirst();

                if (!IsCurrent(plan)) { continue; }

                if (plan.state == SkipNetPlanState.PendingClaim)
                {
                    TickPendingClaim(plan);
                    continue;
                }

                if (plan.IsDisposed || !(PawnCanUseSkipNet(plan.pawn) && plan.IsValid && plan.IsStillRequired) || IsTimedOut(plan))
                {
                    Release(plan);
                    continue;
                }

                if (plan.state == SkipNetPlanState.PendingDirectPath)
                {
                    if (!plan.originalPathRequest.ResultIsReady) { plans.AddLast(plan); continue; }

                    if (plan.originalPathRequest.Found != true) { Release(plan); continue; }

                    SetState(plan, SkipNetPlanState.PendingCandidatePair);
                }

                if (plan.state == SkipNetPlanState.PendingCandidatePair)
                {
                    if ((PopCapReached && !plan.pawn.Drafted) || AtPairCapacity) { plans.AddLast(plan); continue; }

                    if (!TryStartSplice(plan)) { Release(plan); continue; }

                    SetState(plan, SkipNetPlanState.PendingSplicePair);
                }

                if (plan.state == SkipNetPlanState.PendingSplicePair)
                {
                    live.TryGetValue(plan.pawn, out SkipNetPlan livePlan);

                    switch (SkipNetPathSplicer.TryBuildSplice(plan, livePlan, out PawnPath spliced))
                    {
                        case SkipNetPathSplicer.SpliceResult.NotReady:
                            plans.AddLast(plan);
                            break;

                        case SkipNetPathSplicer.SpliceResult.Built:
                            InstallSplice(plan, spliced);
                            break;

                        case SkipNetPathSplicer.SpliceResult.SameSeam:
                            plan.pawn.pather.DisposeAndClearCurPathRequest();
                            Release(plan);
                            break;

                        default: // Failed / NotWorth... the direct path is the answer.
                            Release(plan);
                            break;
                    }
                }
            }

            ResetPopBudget();
        }

        public bool PawnCanUseSkipNet(Pawn pawn)
        {
            if (skipNet.skipdoors.Count < 2) { return false; }
            if (!TryFilterSettings(pawn)) { return false; }

            return true;
        }

        /// <summary>
        /// Called from the GenerateNewPathRequest postfix. Takes ownership of <paramref name="request"/> and hands
        /// back a dummy for the pather to hold, or returns false and leaves vanilla's request alone.
        /// </summary>
        public bool TryCapturePathRequest(Pawn pawn, PathRequest request, out PathRequest dummy)
        {
            dummy = null;

            // A new request while walking a splice. A different trip, or a re-path from the seam itself (it starts at the exit cell),
            // ends that splice. Mark only: this runs inside ResetToCurrentPosition too, so it must never write to live.
            if (TryPeekLivePlan(pawn, out SkipNetPlan livePlan))
            {
                bool sameTrip = livePlan.dest == request.Target && livePlan.peMode == request.EndMode;
                if (!sameTrip || request.Start == livePlan.exitCell) { livePlan.Dispose(); }
            }

            plansByPawn.TryGetValue(pawn, out SkipNetPlan existingPlan);

            // Check if this is a request for the same path parameters e.g. NeedNewPath and its 30 tick interval.
            // If it's the same parameters, keep working on the proposal we have and drop the new request.
            if (existingPlan != null && existingPlan.OriginalPathRequestMatches(request))
            {
                request.Dispose(); // Cancelled, so the pathfinder drops it before computing it.
                dummy = existingPlan.GenerateDummyPathRequest();
                return true;
            }

            // If there's an existing proposal, but it has different path paramenters, hand the pawn the old request. We'll make a fresh one.
            if (existingPlan != null) { Release(existingPlan); }

            // Is this pawn allowed to use the SkipNet?
            if (!PawnCanUseSkipNet(pawn)) { return false; }

            SkipNetPlan plan = new SkipNetPlan(pawn, request);
            plansByPawn[pawn] = plan;

            if (pawn.Drafted) { plans.AddFirst(plan); } // Drafted pawns have priority.
            else { plans.AddLast(plan); }

            dummy = plan.GenerateDummyPathRequest();
            return true;
        }

        /// <summary>
        /// The single exit for every plan. If the pather still holds our dummy, it gets the real request back
        /// (vanilla claims it when ready; Found == false becomes a normal PatherFailed). If not, nobody wants it: dispose.
        /// Either way we never touch the original again.
        /// </summary>
        public void Release(SkipNetPlan plan)
        {
            Unregister(plan);

            plan.Release();
            SetState(plan, SkipNetPlanState.Finished);
        }

        /// <summary>
        /// Resolves the pather's dummy with <paramref name="path"/> (vanilla claims it on its next PatherTick,
        /// with all its usual claim bookkeeping) and disposes the direct path we were holding.
        /// Returns false if the pather no longer wants it, in which case the caller still owns <paramref name="path"/>.
        /// </summary>
        private bool TryInstallPath(SkipNetPlan plan, PawnPath path)
        {
            if (plan.originalPathRequest == null) { return false; }

            if (!plan.IsStillRequired || DEBUG_AlwaysInstallDirectPath > 0)
            {
                Release(plan);
                return false;
            }

            plan.Install(path);
            SetState(plan, SkipNetPlanState.PendingClaim); // The splicer's seams own it from here.
            return true;
        }

        // Finds a skipdoor pair for the plan and hands it to the splicer to path both legs.
        //private bool TryConvertSkipNetProposalIntoSkipNetPlan(Pawn pawn, SkipNetPlan plan)
        private bool TryStartSplice(SkipNetPlan plan)
        {
            if (!plan.originalPathRequest.TryGetPath(out PawnPath directPath) || directPath == null) { return false; }

            bool found = finder.TryFindSkipdoorPair(plan, directPath, out CompSkipdoor entry, out CompSkipdoor exit, out int popCost);
            ConsumePopBudget(popCost);
            if (!found) { return false; }

            plan.AssignCandidates(entry, exit);

            SkipNetPathSplicer.BeginLegs(plan);
            return true;
        }

        // Hands a built splice to the pawn and gives its seam to the splicer. If the pather has moved on, the splice is still ours to dispose.
        private void InstallSplice(SkipNetPlan plan, PawnPath spliced)
        {
            if (!TryInstallPath(plan, spliced))
            {
                spliced.Dispose();
                return;
            }

            plans.AddLast(plan); // Waits in the queue for the pather's claim (see TickPendingClaim).
        }

        // Marks every plan that uses skipdoor (typically destroyed, minified, or despawned): pending and live.
        // Only marks them; the queue and the live sweep tear them down.
        private void CancelPlansUsingSkipdoor(CompSkipdoor skipdoor)
        {
            foreach (SkipNetPlan plan in plansByPawn.Values)
            {
                if (plan.entry == skipdoor || plan.exit == skipdoor) { plan.Dispose(); }
            }

            foreach (SkipNetPlan plan in live.Values)
            {
                if (plan.entry == skipdoor || plan.exit == skipdoor) { plan.Dispose(); }
            }
        }

        // Map is going away. Free the legs still being pathed and forget everything.
        public void DropAll()
        {
            foreach (SkipNetPlan plan in plansByPawn.Values) { plan.DisposeLegs(); }
            plansByPawn.Clear();
            plans.Clear();
            pendingPairCount = 0;

            live.Clear();
        }

        // Installed and waiting for the pather to claim the splice.
        // Parity with the old pending seams: a cancelled (IsDisposed) plan still gets claimed, and the live sweep resets the pawn on the next tick.
        private void TickPendingClaim(SkipNetPlan plan)
        {
            Pawn pawn = plan.pawn;
            Pawn_PathFollower pather = pawn?.pather;
            bool pawnOk = pawn != null && pawn.Spawned && pawn.Map == plan.map && pather != null;

            // Claimed. It's live now. (Usually already promoted by TryGetLivePlan the moment the pather set up its first step.)
            if (pawnOk && pather.curPath == plan.splicedPath) { Promote(plan); return; }

            // Pawn is pawn't, or the dummy was dropped before it was claimed (vanilla disposed the splice along with it).
            if (!pawnOk || pather.curPathRequest != plan.dummyPathRequest)
            {
                Unregister(plan);
                Finish(plan);
                return;
            }

            plans.AddLast(plan); // Still waiting on the claim.
        }

        /// <summary>
        /// Called from the pather patches. The pawn's live splice, if it's still walking it. Promotes a just-claimed splice first,
        /// and retires a live one the pawn has left.
        /// </summary>
        public bool TryGetLivePlan(Pawn pawn, out SkipNetPlan plan)
        {
            plan = null;
            if (plansByPawn.Count == 0 && live.Count == 0) { return false; } // Hot path (every cell step, every pawn): nothing to promote or check.

            PawnPath curPath = pawn?.pather?.curPath;
            if (curPath == null) { return false; }

            if (plansByPawn.TryGetValue(pawn, out SkipNetPlan pending) && pending.state == SkipNetPlanState.PendingClaim && pending.splicedPath == curPath)
            {
                Promote(pending);
            }

            if (!live.TryGetValue(pawn, out plan)) { return false; }

            if (plan.splicedPath != curPath)
            {
                Retire(plan);
                live.Remove(pawn);
                plan = null;
                return false;
            }
            return true;
        }

        // Read-only look at the live splice. Safe to call from inside the live sweep (it re-enters through ResetToCurrentPosition).
        private bool TryPeekLivePlan(Pawn pawn, out SkipNetPlan plan)
        {
            return live.TryGetValue(pawn, out plan) && plan.splicedPath == pawn.pather?.curPath;
        }

        // The pather has claimed a pending splice. It becomes the live one, and the old live one (if any) is retired.
        private void Promote(SkipNetPlan plan)
        {
            Unregister(plan);
            if (live.TryGetValue(plan.pawn, out SkipNetPlan old)) { Retire(old); }

            plan.dummyPathRequest = null; // Vanilla disposed it when it claimed the path.
            live[plan.pawn] = plan;
            SetState(plan, SkipNetPlanState.Active);
        }

        // Ends a live splice the pawn is no longer walking, stepping it back off the seam if it was standing there. The caller removes it from live.
        private void Retire(SkipNetPlan plan)
        {
            SkipNetSeam.StepBackIfAtSeam(plan);
            Finish(plan);
        }

        // Ends a plan that owns nothing any more (installed or live).
        private void Finish(SkipNetPlan plan)
        {
            plan.Dispose();
            SetState(plan, SkipNetPlanState.Finished);
        }

        private void EndLive(SkipNetPlan plan)
        {
            if (live.TryGetValue(plan.pawn, out SkipNetPlan current) && current == plan) { live.Remove(plan.pawn); }
            Finish(plan);
        }

        // Upkeep for the splices being walked. Removals are collected and applied after the loop.
        // ResetToCurrentPosition re-enters TryCapturePathRequest, which only ever reads live.
        private void TickLive()
        {
            if (live.Count == 0) { return; }
            tmpFinished.Clear();

            foreach (KeyValuePair<Pawn, SkipNetPlan> kv in live)
            {
                Pawn pawn = kv.Key;
                SkipNetPlan plan = kv.Value;
                Pawn_PathFollower pather = pawn.pather;

                // Pawn is pawn't. Clean up the records.
                if (!pawn.Spawned || pawn.Map != skipNet.map || pather == null)
                {
                    Finish(plan);
                    tmpFinished.Add(pawn);
                    continue;
                }

                // Pawn got a new path from somewhere and replaced our spliced one.
                if (pather.curPath != plan.splicedPath)
                {
                    Retire(plan); // Also steps the pawn back off the seam if it was standing there.
                    tmpFinished.Add(pawn);
                    continue;
                }

                // If we don't have a plan anymore, we shouldn't be teleporting.
                // Ditch the seam and get the pawn to resume normal pathing.
                if (plan.IsDisposed)
                {
                    pather.ResetToCurrentPosition();
                    Finish(plan);
                    tmpFinished.Add(pawn);
                    continue;
                }

                // Skipdoor rules (power, forbidden, allowed area, faction) can change while the pawn walks to the entry, and vanilla
                // knows nothing about them. Check now and then, and turn the pawn around early rather than let it walk up to a door
                // it can't use. (DecideTeleportStep checks again at the door.)
                if (pawn.IsHashIntervalTick(180) && !plan.IsCandidatePairStillAccessible())
                {
                    plan.Dispose();
                    pather.ResetToCurrentPosition();
                    Finish(plan);
                    tmpFinished.Add(pawn);
                    continue;
                }
            }

            for (int i = 0; i < tmpFinished.Count; i++) { live.Remove(tmpFinished[i]); }
            tmpFinished.Clear();
        }

        // Called from the TryEnterNextPathCell prefix, with the pawn at the seam. On BlockedDead the pawn has already been reset.
        public SkipNetSeam.TeleportStepDecision DecideTeleportStep(Pawn pawn, SkipNetPlan plan)
        {
            SkipNetSeam.TeleportStepDecision decision = SkipNetSeam.DecideTeleportStep(pawn, plan);
            if (decision == SkipNetSeam.TeleportStepDecision.BlockedDead) { EndLive(plan); }
            return decision;
        }

        // Called from the TryEnterNextPathCell postfix after an approved step.
        public void CompleteTeleportStep(Pawn pawn, SkipNetPlan plan)
        {
            if (!SkipNetSeam.CompleteTeleportStep(pawn, plan)) { return; }

            EndLive(plan);

            if (pawn.Spawned && pawn.pather != null && ShouldRepathAfterTeleport(pawn, plan))
            {
                pawn.pather.ResetToCurrentPosition();
            }
        }

        // Should the pawn repath after teleporting?
        // In a separate method so you other modders can patch in your own reasons :)
        public bool ShouldRepathAfterTeleport(Pawn pawn, SkipNetPlan plan)
        {
            if(pawn.roping?.IsRopingOthers == true) {  return true; }

            return false;
        }

        public bool TryFilterSettings(Pawn pawn)
        {
            if (MigcorpSkiptechMod.Settings.accessMode == AccessMode.Colonists && pawn.Faction != Faction.OfPlayer) { return false; }

            if (MigcorpSkiptechMod.Settings.accessMode != AccessMode.Everyone && pawn.HostileTo(Faction.OfPlayer)) { return false; }

            // Possible fix for roped animal thrashing.
            // The FollowRoper job tries to follow the roper's path, but a few cells back. This causes issues with the gaps SkipNet creates.
            // Instead, just yeet them when the roper is ready to teleport. idc.
            if (pawn.roping?.IsRopedByPawn == true || pawn.jobs?.curJob?.def == JobDefOf.FollowRoper) { return false; }

            if (!MigcorpSkiptechMod.Settings.animalsCanUse && pawn.IsAnimal) { return false; }

            return true;
        }

        private bool IsCurrent(SkipNetPlan plan) { return plansByPawn.TryGetValue(plan.pawn, out SkipNetPlan current) && current == plan; }

        private void Unregister(SkipNetPlan plan) { if (IsCurrent(plan)) { plansByPawn.Remove(plan.pawn); } }

        private static bool IsTimedOut(SkipNetPlan plan) { return GenTicks.TicksGame - plan.tickCreated > PendingPlanMaxLifetimeTicks; }

        private void SetState(SkipNetPlan plan, SkipNetPlanState state)
        {
            if (plan.state == SkipNetPlanState.PendingSplicePair) { pendingPairCount--; }
            if (state == SkipNetPlanState.PendingSplicePair) { pendingPairCount++; }
            plan.state = state;
        }

        private void ConsumePopBudget(int popCount) { tickPopCount += popCount; }

        private void ResetPopBudget() { tickPopCount = 0; }
    }
}
