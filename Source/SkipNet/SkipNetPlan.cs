using MigCorp.Skiptech.SkipNet.Comps;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public enum SkipNetPlanState
    {
        PendingDirectPath,
        PendingCandidatePair,
        PendingSplicePair,
        PendingClaim,
        Active,
        Finished,
    }

    public class SkipNetPlan
    {
        public Pawn pawn;
        public Map map;
        public LocalTargetInfo dest;
        public PathEndMode peMode;
        public TraverseParms tp;
        public PathRequest originalPathRequest;
        public SkipNetPlanState state = SkipNetPlanState.PendingDirectPath;

        public CompSkipdoor entry, exit;
        public IntVec3 entryCell, exitCell;
        public bool compsNotified;

        public PathRequest entryRequest;
        public PathRequest exitRequest;
        public PawnPath splicedPath;

        public PathRequest dummyPathRequest; // We want to control the release timing of the path. Give the pawn a dummy that we will inject the path into once we know what we're doing.
        public int tickCreated; // Needed to kill on timeout. If a pawn can't reasonably resolve a SkipNetPlan into a useable path in this time, kill it.

        public bool IsDisposed { get; private set; } = false;

        public bool IsValid
        {
            get
            {
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != map || pawn.pather == null) { return false; }

                if (pawn.Downed && !pawn.health.CanCrawl) { return false; }

                if (!dest.IsValid || peMode == PathEndMode.None) { return false; }

                return true;
            }
        }

        // The pather only still wants this trip while it's holding our latest dummy.
        public bool IsStillRequired
        {
            get
            {
                return dummyPathRequest != null && pawn?.pather != null && pawn.pather.curPathRequest == dummyPathRequest;
            }
        }

        public SkipNetPlan(Pawn pawn, PathRequest originalPathRequest)
        {
            this.pawn = pawn;
            this.originalPathRequest = originalPathRequest;
            map = originalPathRequest.map;
            dest = originalPathRequest.Target;
            peMode = originalPathRequest.EndMode;
            tp = originalPathRequest.TraverseParms;
            tickCreated = GenTicks.TicksGame;
        }

        // Check if a PathRequest has the same details as this proposal's.
        public bool OriginalPathRequestMatches(PathRequest request)
        {
            PathRequest o = originalPathRequest;
            return o != null
                && o.Start == request.Start
                && o.Target == request.Target
                && o.EndMode == request.EndMode
                && o.TraverseParms == request.TraverseParms;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }

        public void DisposeLegs()
        {
            entryRequest?.Dispose();
            exitRequest?.Dispose();
            entryRequest = null;
            exitRequest = null;
        }

        // Teardown for a plan that still owns the direct path. If the pather is still holding our dummy, it gets the real
        // request back (vanilla claims it when ready; Found == false becomes a normal PatherFailed). If not, nobody wants it: dispose.
        // Once installed, the plan owns nothing and belongs to its seam, so this does nothing.
        public void Release()
        {
            if (originalPathRequest == null) { return; }

            // A skip pair still in flight dies with its proposal. The splicer disposes it on its next Run sweep.
            Dispose();
            DisposeLegs();

            if (IsStillRequired)
            {
                pawn.pather.curPathRequest = originalPathRequest;
            }
            else
            {
                originalPathRequest.Dispose();
            }

            originalPathRequest = null;
            dummyPathRequest = null;
        }

        // Hands the splice to the pather through our dummy (vanilla claims it on its next PatherTick, with all its usual claim
        // bookkeeping) and frees the direct path. Caller must check IsStillRequired first.
        public void Install(PawnPath path)
        {
            originalPathRequest.Dispose(); // Returns the unclaimed direct path to the pool.
            dummyPathRequest.Resolve(path); // The dummy owns the path now. If vanilla drops the dummy unclaimed, Dispose() pools it.
            splicedPath = path;             // Read-only. Only ever compared against pather.curPath, never disposed from here.

            originalPathRequest = null;
        }

        public void AssignCandidates(CompSkipdoor entry, CompSkipdoor exit)
        {
            this.entry = entry;
            this.exit = exit;
            entryCell = entry.Position;
            exitCell = exit.Position;
        }

        // On the entry cell, lined up to step into the exit.
        public bool IsPawnAtSeam { get { return pawn.Position == entryCell && pawn.pather?.nextCell == exitCell; } }

        // A fresh dummy for every GenerateNewPathRequest call. Vanilla may have already disposed the previous one by then.
        // Steals all the details from the original.
        // Never PushRequest it... we don't need it to make a path, and we want to control when it Resolves().
        public PathRequest GenerateDummyPathRequest()
        {
            PathRequest o = originalPathRequest;
            dummyPathRequest = o.map.pathFinder.CreateRequest(o.Start, o.Target, null, o.TraverseParms, o.Tuning, o.EndMode, pawn, o.customizer);
            return dummyPathRequest;
        }

        public bool IsCandidatePairStillAccessible()
        {
            if (!entry.parent.Spawned || !exit.parent.Spawned) { return false; }

            SkipNetAccessContext ac = new SkipNetAccessContext(pawn);
            return entry.IsEnterableBy(ac) && exit.IsExitableBy(ac);
        }

        public bool IsStillPathableFromExitToDest(Map map)
        {
            return map.reachability.CanReach(exit.Position, dest, peMode, tp);
        }
    }
}
