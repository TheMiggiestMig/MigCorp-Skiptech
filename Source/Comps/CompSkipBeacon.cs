using MigCorp.Skiptech.Skipgate;
using MigCorp.Skiptech.Skipgate.Comps;
using Verse;

namespace MigCorp.Skiptech.Comps
{
    public class CompProperties_SkipBeacon : CompProperties
    {
        public CompProperties_SkipBeacon() => compClass = typeof(CompSkipBeacon);

        public float recallRadius = 4.9f;
    }

    // Marks a thing as a recall anchor for skip gates.
    public class CompSkipBeacon : ThingComp
    {
        public CompProperties_SkipBeacon Props => (CompProperties_SkipBeacon)props;

        private Building_Skipgate recallingGate;

        public Building_Skipgate RecallingGate => recallingGate;
        public CompSkipgate RecallingGateComp => recallingGate?.GetComp<CompSkipgate>();
        public bool IsClaimed => recallingGate != null;
        public float Radius => Props.recallRadius;

        public void Claim(Building_Skipgate gate) => recallingGate = gate;
        public void ClearClaim() => recallingGate = null;

        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_References.Look(ref recallingGate, "recallingGate");
        }
    }
}