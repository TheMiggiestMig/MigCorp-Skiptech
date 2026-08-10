using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    public class Building_SkipgatePortal : MapPortal
    {
        private Building_Skipgate owningGate;

        public CompSkipgate OwningSkipgate => owningGate?.skipgateComp;
        private CompSkipgate FarGate => OwningSkipgate?.LinkedFarGate;

        public void SetOwningSkipgate(CompSkipgate skipgate) =>
            owningGate = (Building_Skipgate)skipgate.parent;

        public override string EnterString => "Travel through skipgate";

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            containerProxy = new SkipgatePortalContainerProxy { portal = this };
        }

        public override bool IsEnterable(out string reason)
        {
            CompSkipgate own = OwningSkipgate;
            CompSkipgate far = FarGate;

            if (own == null || far == null) { reason = "Skipgate is not linked."; return false; }
            if (!far.parent.Position.Standable(far.parent.Map)) { reason = "The far skipgate is obstructed."; return false; }

            return base.IsEnterable(out reason);
        }

        public override Map GetOtherMap() => FarGate?.parent.Map;
        public override IntVec3 GetDestinationLocation() => FarGate?.parent.Position ?? IntVec3.Invalid;

        public override void OnEntered(Pawn pawn)
        {
            // Do NOT call base.OnEntered! It assumes a pocket-map exit.
            Notify_ThingAdded(pawn);

            // TODO Teleport FX later (probably just use the skipdoor ones tbh).
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref owningGate, "owningGate");
        }

        public override string GetInspectString()
        {
            Building_Skipgate far = FarGate?.parent as Building_Skipgate;
            string mine = far != null ? $"Leads to: {far.RenamableLabel}" : "Link unstable.";

            string baseStr = base.GetInspectString();
            return baseStr.NullOrEmpty() ? mine : mine + "\n" + baseStr;
        }
    }

    // If the link died with a delivery in flight, drop the item beside the gate instead of onto a null map.
    public class SkipgatePortalContainerProxy : PortalContainerProxy
    {
        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
        {
            if (portal is Building_SkipgatePortal p && p.OwningSkipgate?.LinkedFarGate == null)
            {
                portal.Notify_ThingAdded(item);
                GenDrop.TryDropSpawn(item, portal.Position, portal.Map, ThingPlaceMode.Near, out _);
                return true;
            }

            return base.TryAdd(item, canMergeWithExistingStacks);
        }
    }
}