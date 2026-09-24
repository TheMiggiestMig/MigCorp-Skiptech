using MigCorp.Skiptech.Utils;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public class SkipNetProposer
    {
        public const int UnprocessedProposalMaxLifetimeTicks = 20;

        public MapComponent_SkipNet skipNet;
        public int TickPopCap = 150; // Might make this an advanced option in settings.
        private int tickPopCount = 0;
        public bool PopCapReached { get { return tickPopCount >= TickPopCap; } }

        public int ProposalCount { get { return proposalsByPawn.Count; } }

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
                if (GenTicks.TicksGame - proposal.tickCreated > UnprocessedProposalMaxLifetimeTicks)
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

                // No direct path at all. Hand it back so vanilla fails it the normal way (PatherFailed).
                if (proposal.originalPathRequest.Found != true)
                {
                    Release(proposal);
                    continue;
                }

                // Over this tick's search budget, or the splicer is full? Wait a tick (the deadline still applies).
                if ((PopCapReached && !proposal.pawn.Drafted) || skipNet.splicer.AtCapacity)
                {
                    proposals.AddLast(proposal);
                    continue;
                }

                // No eligible skipdoor pair (or the splicer refused it): the direct path is the answer.
                // Otherwise the splicer drives the trip from here, and the proposal stays registered until it's fulfilled or released.
                if (!TryConvertSkipNetProposalIntoSkipNetPlan(proposal.pawn, proposal))
                {
                    Release(proposal);
                }
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
        public void Release(SkipNetProposal proposal)
        {
            if (proposalsByPawn.TryGetValue(proposal.pawn, out SkipNetProposal current) && current == proposal)
            {
                proposalsByPawn.Remove(proposal.pawn);
            }

            if (proposal.originalPathRequest == null) { return; }

            // A skip pair still in flight dies with its proposal. The splicer disposes it on its next Run sweep.
            proposal.plan?.DisposeSuperseded();

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

        /// <summary>
        /// Resolves the pather's dummy with <paramref name="path"/> (vanilla claims it on its next PatherTick,
        /// with all its usual claim bookkeeping) and disposes the direct path we were holding.
        /// Returns false if the pather no longer wants it, in which case the caller still owns <paramref name="path"/>.
        /// </summary>
        public bool TryInstallPath(SkipNetProposal proposal, PawnPath path)
        {
            if (proposal.originalPathRequest == null) { return false; }

            if (!proposal.IsStillRequired)
            {
                Release(proposal);
                return false;
            }

            if (proposalsByPawn.TryGetValue(proposal.pawn, out SkipNetProposal current) && current == proposal)
            {
                proposalsByPawn.Remove(proposal.pawn);
            }

            proposal.originalPathRequest.Dispose(); // Returns the unclaimed direct path to the pool.
            proposal.dummyPathRequest.Resolve(path); // The dummy owns the path now. If vanilla drops the dummy unclaimed, Dispose() pools it.

            proposal.originalPathRequest = null;
            proposal.dummyPathRequest = null;
            return true;
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

        private bool TryConvertSkipNetProposalIntoSkipNetPlan(Pawn pawn, SkipNetProposal proposal)
        {
            //proposalsByPawn.Remove(pawn);

            //if (!TryExtractPawnPath(pawn, out PawnPath directPath)) { return false; }
            // Read-only peek at the direct path. The original request keeps ownership of it (no ClaimCalculatedPath).
            if (!proposal.originalPathRequest.TryGetPath(out PawnPath directPath) || directPath == null) { return false; }

            if (skipNet.planner.TryFindEligibleSkipNetPlan(proposal, directPath, out SkipNetPlan plan))
            {
                //if (skipNet.splicer.TryBeginSkipPaths(plan)) { return true; }
                if (skipNet.splicer.TryBeginSkipPaths(plan, proposal))
                {
                    proposal.plan = plan;
                    return true;
                }

                plan.DisposeSuperseded(); // Make sure the (failed) generated plan isn't accidentally used.
            }
            return false;
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
