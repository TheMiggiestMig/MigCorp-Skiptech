using MigCorp.Skiptech.Utils;
using RimWorld;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    public static class SkipgateArrivalUtil
    {
        // A cutdown version of ActiveTransporter.PodOpen, shared by TransportersArrivalAction_SkipInSpecificCell
        // and ActiveTransporter_Skip so every skipgate arrival puts things down the same way.
        //
        // Deliberately does NOT finish with innerContainer.ClearAndDestroyContents() the way PodOpen does.
        // That call is the reason a drop pod which can't unload deletes its cargo, and ours is full of colonists (probably don't want to delete them).
        public static void PlaceContents(ActiveTransporterInfo info, IntVec3 cell, Map map)
        {
            for (int i = info.innerContainer.Count - 1; i >= 0; i--)
            {
                Thing thing = info.innerContainer[i];

                Thing placed;
                if (!GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near, out placed))
                {
                    // If we can't place it nearby, just dump it on the ground. The container doesn't really
                    // exist, so we don't want pawns stuck in limbo.
                    if (!GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Direct, out placed))
                    {
                        // Both failed, so leave it held rather than losing it. Callers decide what to do next.
                        SkiptechUtil.Error($"Could not place {thing.LabelShortCap} at {cell}; still held.");
                        continue;
                    }
                }

                Pawn pawn = placed as Pawn;
                if (pawn == null) { continue; }

                // Keep behavior the same as drop pods, and draft out pawns if not on a home map.
                if (pawn.IsColonist && pawn.Spawned && !map.IsPlayerHome) { pawn.drafter.Drafted = true; }

                if (pawn.guest != null && pawn.guest.IsPrisoner) { pawn.guest.WaitInsteadOfEscapingForDefaultTicks(); }
            }
        }
    }
}