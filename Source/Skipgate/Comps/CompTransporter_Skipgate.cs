using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MigCorp.Skiptech.Skipgate.Comps
{
    // Used to subclass CompTransporter while nuking all the interface.
    // We're using the Skipgate as the central control.
    public class CompTransporter_Skipgate : CompTransporter
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            yield break;
        }
        public override string CompInspectStringExtra()
        {
            return null;
        }

        // Borked my save somehow.
        // Used this to fix it, but it's probably a good guard too, so it can stay.
        public override void PostExposeData()
        {
            base.PostExposeData();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (innerContainer == null) { innerContainer = new ThingOwner<Thing>(this); }
                if (groupID >= 0 && leftToLoad.NullOrEmpty() && !innerContainer.Any) { groupID = -1; }
            }
        }
    }
}