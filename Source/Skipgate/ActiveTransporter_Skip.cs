using RimWorld;

namespace MigCorp.Skiptech.Skipgate
{
    // What MakeDropPodAt spawns for a skipgate arrival, in place of ActiveDropPod.
    //
    // Vanilla's ActiveTransporter waits out openDelay and then runs PodOpen, which finishes with
    // innerContainer.ClearAndDestroyContents() - the reason a drop pod that can't unload deletes its cargo.
    // We don't want to delete cargo... cargo is pawns... deleting pawns is bad.
    public class ActiveTransporter_Skip : ActiveTransporter
    {
        protected override void Tick()
        {
            if (!Spawned) { return; }

            ActiveTransporterInfo contents = Contents;
            if (contents == null)
            {
                Destroy();
                return;
            }

            SkipgateArrivalUtil.PlaceContents(contents, Position, Map);

            // Try placing again next tick if there are still pawns to place.
            if (contents.innerContainer.Count > 0) { return; }

            Destroy();
        }
    }
}