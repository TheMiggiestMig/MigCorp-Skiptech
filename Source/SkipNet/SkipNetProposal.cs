using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public class SkipNetProposal
    {
        public Pawn pawn;
        public LocalTargetInfo dest;
        public PathEndMode peMode;
        public TraverseParms tp;
        public PathRequest originalPathRequest;
        public PathRequest dummyPathRequest; // We want to control the release timing of the path. Give the pawn a dummy that we will inject the path into once we know what we're doing.
        public int tickCreated; // Needed to kill on timeout. If a pawn can't reasonably resolve a proposal into a useable path in this time, kill it.
        public SkipNetPlan plan; // Set once the splicer takes the trip on. Dies with the proposal if it's released before the splice lands.

        // The pather only still wants this trip while it's holding our latest dummy.
        public bool IsStillRequired
        {
            get
            {
                return dummyPathRequest != null && pawn?.pather != null && pawn.pather.curPathRequest == dummyPathRequest;
            }
        }

        public SkipNetProposal(Pawn pawn, PathRequest originalPathRequest)
        {
            this.pawn = pawn;
            this.originalPathRequest = originalPathRequest;
            dest = originalPathRequest.Target;
            peMode = originalPathRequest.EndMode;
            tp = originalPathRequest.TraverseParms;
            tickCreated = GenTicks.TicksGame;
        }

        // Check if a PathRequest has the same details as this proposal's.
        public bool Matches(PathRequest request)
        {
            PathRequest o = originalPathRequest;
            return o != null
                && o.Start == request.Start
                && o.Target == request.Target
                && o.EndMode == request.EndMode
                && o.TraverseParms == request.TraverseParms;
        }

        // A fresh dummy for every GenerateNewPathRequest call. Vanilla may have already disposed the previous one by then.
        // Steals all the details from the original so anything reading pather.
        // Never PushRequest it... we don't need it to make a path, and we want to control when it Resolves().
        public PathRequest MakeDummyPathRequest()
        {
            PathRequest o = originalPathRequest;
            dummyPathRequest = o.map.pathFinder.CreateRequest(o.Start, o.Target, null, o.TraverseParms, o.Tuning, o.EndMode, pawn, o.customizer);
            return dummyPathRequest;
        }
    }
}
