using System.Collections.Generic;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    // A read-only helper class for the Send operation to translate our caravan pawn roster into a pseudo ThingHolder to allow us to re-use
    // vanilla targeting and mass calculation methods (I'm lazy like that).
    //
    // NEVER scribe this and NEVER call a mutating ThingOwner method on it, or yer gonnaaa have a bad tiiiime.
    public class SkipgateRosterHolder : IThingHolder
    {
        private readonly ThingOwner<Thing> contents;

        public SkipgateRosterHolder(List<Thing> roster)
        {
            contents = new ThingOwner<Thing>(this);

            // Treat our roster like a group of things being held. I mean, technically they are... in a metaphysical sense.
            contents.InnerListForReading.AddRange(roster);
        }

        public IThingHolder ParentHolder => null;

        public ThingOwner GetDirectlyHeldThings() => contents;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }
    }
}