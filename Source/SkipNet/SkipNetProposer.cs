using MigCorp.Skiptech.Utils;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public class SkipNetProposer
    {
        public MapComponent_SkipNet skipNet;
        public int TickPopCap = 150; // Might make this an advanced option in settings.
        private int tickPopCount = 0;
        public bool PopCapReached { get { return tickPopCount >= TickPopCap; } }

        private readonly Dictionary<Pawn, SkipNetProposal> proposalsByPawn = new Dictionary<Pawn, SkipNetProposal>();
        private readonly Deque<Pawn> proposals = new Deque<Pawn>(); // Actually a list of pawns used as keys for proposals, but whatever.

        private Pawn pawnHijacking = null;
        public bool IsHijacking(Pawn pawn) => pawnHijacking == pawn;

        public struct SkipNetProposal
        {
            public Pawn pawn;
            public LocalTargetInfo dest;
            public PathEndMode peMode;
            public TraverseParms tp;

            public SkipNetProposal(Pawn pawn, LocalTargetInfo dest, PathEndMode peMode, TraverseParms tp)
            {
                this.pawn = pawn;
                this.dest = dest;
                this.peMode = peMode;
                this.tp = tp;
            }

            public void Update(LocalTargetInfo dest, PathEndMode peMode, TraverseParms tp)
            {
                this.dest = dest;
                this.peMode = peMode;
                this.tp = tp;
            }
        }

        public SkipNetProposer(MapComponent_SkipNet skipNet)
        {
            this.skipNet = skipNet;
        }

        public void ProcessQueue()
        {
            // Visit each queued pawn at most once per tick, even if skipped items get re-appended.
            int numProposalsToProcess = proposals.Count;

            while (numProposalsToProcess-- > 0 && proposals.Count > 0)
            {
                Pawn pawn = proposals.PopFirst();

                // Check for a stale order entry (proposal was culled, replaced, or already handled).
                if (!proposalsByPawn.TryGetValue(pawn, out SkipNetProposal proposal)) { continue; }

                // Check if the plan still describes what the pawn is doing.
                if (!IsProposalPlannable(proposal))
                {
                    proposalsByPawn.Remove(pawn);
                    continue;
                }

                Pawn_PathFollower pather = pawn.pather;

                // If the pawn has no path and they're not waiting on one,
                // why are they here?
                if (pather.curPath == null && pather.curPathRequest == null)
                {
                    proposalsByPawn.Remove(pawn);
                    continue;
                }

                // Check of the pawn is still waiting on the direct path to resolve.
                // Push it to the back of the queue if so.
                if (pather.curPathRequest != null)
                {
                    proposals.AddLast(pawn);
                    continue;
                }

                // If we've reached the budgeted pop cap, put it back at the start of the
                // queue to be processed next tick, and stop processing this tick.
                if (PopCapReached && !pawn.Drafted)
                {
                    proposals.AddFirst(pawn);
                    break;
                }

                TryConvertSkipNetProposalIntoSkipNetPlan(pawn, proposal);
            }

            ResetPopBudget();
        }

        // Since we're no longer running plans at exactly the same time as a pawn requested a path, conditions can change.
        // Make sure the proposal still matches what the pawn is trying to achieve.
        private bool IsProposalPlannable(SkipNetProposal proposal)
        {
            Pawn pawn = proposal.pawn;

            if(!IsValidProposal(proposal)) { return false; }
            if (pawn.Dead || !pawn.Spawned || pawn.Map != skipNet.map || pawn.pather == null) { return false; }
            if (pawn.Downed && !pawn.health.CanCrawl) { return false; }
            if (!TryFilterSettings(pawn)) { return false; }

            if (SkipNetUtils.PatherDest(pawn.pather) != proposal.dest ||
                SkipNetUtils.PatherPeMode(pawn.pather) != proposal.peMode)
            {
                return false;
            }

            return true;
        }

        public bool TryMakeSkipNetProposal(Pawn pawn, LocalTargetInfo dest, PathEndMode peMode, TraverseParms tp)
        {
            if (!IsValidProposal(pawn, ref dest, peMode)) {  return false; }

            if(proposalsByPawn.TryGetValue(pawn, out SkipNetProposal existingProposal))
            {
                existingProposal.Update(dest, peMode, tp);
                return true;
            }

            proposalsByPawn[pawn] = new SkipNetProposal(pawn, dest, peMode, tp);
            if (pawn.Drafted) // Drafted pawns have priority.
            {
                proposals.AddFirst(pawn);
            }
            else
            {
                proposals.AddLast(pawn);
            }

            return true;
        }
        public bool IsValidProposal(Pawn pawn, ref LocalTargetInfo dest, PathEndMode peMode)
        {
            if (pawn == null || !dest.IsValid || peMode == PathEndMode.None) { return false; }

            if (skipNet.skipdoors.Count < 2) { return false; }

            return true;
        }

        public bool IsValidProposal(SkipNetProposal proposal)
        {
            return IsValidProposal(proposal.pawn, ref proposal.dest, proposal.peMode);
        }

        public bool TryProcessSkipNetProposalNow(Pawn pawn)
        {
            if (pawn == null || !proposalsByPawn.TryGetValue(pawn, out SkipNetProposal proposal)) { return false; }
            if (PopCapReached && !pawn.Drafted) { return false; }
            if (!IsValidProposal(proposal))
            {
                proposalsByPawn.Remove(pawn);
                return false;
            }

            return TryConvertSkipNetProposalIntoSkipNetPlan(pawn, proposal);
        }

        private bool TryConvertSkipNetProposalIntoSkipNetPlan(Pawn pawn, SkipNetProposal proposal)
        {
            proposalsByPawn.Remove(pawn);

            // This will be needed for the Dijkstra implementation.
            if (!TryExtractPawnPath(pawn, out PawnPath directPath)) { return false; }

            if (skipNet.planner.TryFindEligibleSkipNetPlan(proposal.pawn, proposal.dest, proposal.peMode, out SkipNetPlan plan))
            {
                StartPathToEntry(plan);
                return true;
            }
            return false;
        }

        private bool TryExtractPawnPath(Pawn pawn, out PawnPath directPath)
        {
            directPath = null;

            if (!(pawn?.pather?.curPathRequest?.TryGetPath(out directPath) ?? false) || directPath?.Found != true)
            {
                directPath = pawn?.pather?.curPath;
            }

            return directPath != null;
        }

        private void StartPathToEntry(SkipNetPlan plan)
        {
            try
            {
                pawnHijacking = plan.pawn;
                plan.pawn.pather.StartPath(new LocalTargetInfo(plan.entry.parent), PathEndMode.OnCell);
            }
            finally
            {
                pawnHijacking = null;
            }
        }

        // Moved from SkipNetPlanner.
        public bool TryFilterSettings(Pawn pawn)
        {
            if (MigcorpSkiptechMod.Settings.accessMode == AccessMode.Colonists && pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (MigcorpSkiptechMod.Settings.accessMode != AccessMode.Everyone && pawn.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            if (!MigcorpSkiptechMod.Settings.animalsCanUse && pawn.IsAnimal)
            {
                if (!(pawn.jobs?.curJob?.def == JobDefOf.FollowRoper))
                {
                    return false;
                }
            }

            return true;
        }
        private void ResetPopBudget()
        {
            tickPopCount = 0;
        }
    }
}
