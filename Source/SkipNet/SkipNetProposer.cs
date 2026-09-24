using MigCorp.Skiptech.Utils;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public class SkipNetProposer
    {
        public const int ProposalMaxLifetimeTicks = 20;

        public MapComponent_SkipNet skipNet;
        public int TickPopCap = 150; // Might make this an advanced option in settings.
        private int tickPopCount = 0;
        public bool PopCapReached { get { return tickPopCount >= TickPopCap; } }

        private readonly Dictionary<Pawn, SkipNetProposal> proposalsByPawn = new Dictionary<Pawn, SkipNetProposal>();
        private readonly Deque<SkipNetProposal> proposals = new Deque<SkipNetProposal>(); // Queue the proposal itself, so a stale entry can't be mistaken for the pawn's current one.

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
                SkipNetProposal proposal = proposals.PopFirst();

                // Check for a stale order entry.
                // Stale entry: released, or superseded by a newer proposal for the same pawn.
                if (!proposalsByPawn.TryGetValue(proposal.pawn, out SkipNetProposal current) || current != proposal) { continue; }


                if (!proposal.IsStillRequired || !IsProposalPlannable(proposal))
                {
                    Release(proposal);
                    continue;
                }

                // Out of time. Give the pawn its direct path.
                if (GenTicks.TicksGame - proposal.tickCreated > ProposalMaxLifetimeTicks)
                {
                    Release(proposal);
                    continue;
                }

                // Still waiting on the direct path. Back of the queue.
                if (!proposal.originalPathRequest.ResultIsReady)
                {
                    proposals.AddLast(proposal);
                    continue;
                }

                // DEBUG - Just give it the original back for now, to make sure I didn't break anything.
                Release(proposal);

                // If we've reached the budgeted pop cap, put it back at the start of the
                // queue to be processed next tick, and stop processing this tick.
                /*
                if (PopCapReached && !pawn.Drafted)
                {
                    proposals.AddFirst(pawn);
                    break;
                }

                // If the splicer is at capacity, give it another tick to free up.
                if (skipNet.splicer.AtCapacity)
                {
                    proposals.AddFirst(pawn);
                    break;
                }

                TryConvertSkipNetProposalIntoSkipNetPlan(pawn, proposal);*/
            }

            ResetPopBudget();
        }

        // Since we're no longer running plans at exactly the same time as a pawn requested a path, conditions can change.
        // Make sure the proposal still matches what the pawn is trying to achieve.
        private bool IsProposalPlannable(SkipNetProposal proposal)
        {
            Pawn pawn = proposal.pawn;

            if (!IsValidProposal(proposal)) { return false; }
            if (pawn.Dead || !pawn.Spawned || pawn.Map != skipNet.map || pawn.pather == null) { return false; }
            if (pawn.Downed && !pawn.health.CanCrawl) { return false; }

            if (SkipNetUtils.PatherDest(pawn.pather) != proposal.dest ||
                SkipNetUtils.PatherPeMode(pawn.pather) != proposal.peMode)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Called from the GenerateNewPathRequest postfix. Takes ownership of <paramref name="request"/> and hands
        /// back a dummy for the pather to hold, or returns false and leaves vanilla's request alone.
        /// </summary>
        public bool TryCaptureRequest(Pawn pawn, PathRequest request, out PathRequest dummy)
        {
            dummy = null;
            proposalsByPawn.TryGetValue(pawn, out SkipNetProposal existing);

            // Check if this is a request for the same path parameters e.g. NeedNewPath and it's 30 tick interval.
            // If it's the same parameters, keep working on the proposal we have and drop the new request.
            if (existing != null && existing.Matches(request))
            {
                request.Dispose(); // Cancelled, so the pathfinder drops it before computing it.
                dummy = existing.MakeDummyPathRequest();
                return true;
            }

            // If there's an existing proposal, but it has different path paramenters, nuke it. We'll make a fresh one.
            if (existing != null) { Release(existing); }

            LocalTargetInfo dest = request.Target;
            if (!IsValidProposal(pawn, ref dest, request.EndMode)) { return false; } // Not ours. Vanilla keeps its request.

            SkipNetProposal proposal = new SkipNetProposal(pawn, request);
            proposalsByPawn[pawn] = proposal;

            if (pawn.Drafted) { proposals.AddFirst(proposal); } // Drafted pawns have priority.
            else { proposals.AddLast(proposal); }

            dummy = proposal.MakeDummyPathRequest();
            return true;
        }

        /// <summary>
        /// The single exit for every proposal. If the pather still holds our dummy, it gets the real request back
        /// (vanilla claims it when ready; Found == false becomes a normal PatherFailed). If not, nobody wants it: dispose.
        /// Either way we never touch the original again.
        /// </summary>
        private void Release(SkipNetProposal proposal)
        {
            if (proposalsByPawn.TryGetValue(proposal.pawn, out SkipNetProposal current) && current == proposal)
            {
                proposalsByPawn.Remove(proposal.pawn);
            }

            if (proposal.originalPathRequest == null) { return; }

            if (proposal.IsStillRequired)
            {
                proposal.pawn.pather.curPathRequest = proposal.originalPathRequest;
            }
            else
            {
                proposal.originalPathRequest.Dispose();
            }

            proposal.originalPathRequest = null;
            proposal.dummyPathRequest = null;
        }

        public bool IsValidProposal(Pawn pawn, ref LocalTargetInfo dest, PathEndMode peMode)
        {
            if (pawn == null || !dest.IsValid || peMode == PathEndMode.None) { return false; }

            if (skipNet.skipdoors.Count < 2) { return false; }
            if (!TryFilterSettings(pawn)) { return false; }

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
            if (skipNet.splicer.AtCapacity) { return false; }
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

            if (!TryExtractPawnPath(pawn, out PawnPath directPath)) { return false; }

            if (skipNet.planner.TryFindEligibleSkipNetPlan(proposal, directPath, out SkipNetPlan plan))
            {
                if (skipNet.splicer.TryBeginSkipPaths(plan)) { return true; }

                plan.DisposeSuperseded(); // Make sure the (failed) generated plan isn't accidentally used.
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

        public void ConsumePopBudget(int popCount)
        {
            tickPopCount += popCount;
        }
        private void ResetPopBudget()
        {
            tickPopCount = 0;
        }
    }
}
