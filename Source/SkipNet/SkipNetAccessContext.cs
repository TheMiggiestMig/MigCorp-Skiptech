using RimWorld;
using Verse;

namespace MigCorp.Skiptech.SkipNet
{
    // Read-only because "stupid defensive copies"
    // Struct (for now) since I can't think of behaviour it needs.
    // Treat it like TraverseParams, except, for using skipdoors instead.
    public readonly struct SkipNetAccessContext
    {
        public readonly Pawn pawn;
        public readonly Area allowedArea;
        public readonly bool isPlayerFaction;
        public readonly bool shockExempt; // pawn will use an unpowered door despite skip-shock

        // A lot of these checks are done many times during path searching.
        // A lot of these checks only need to be done once per search.
        public SkipNetAccessContext(Pawn pawn, bool forced = false)
        {
            this.pawn = pawn;
            allowedArea = PathUtility.GetAllowedArea(pawn);
            isPlayerFaction = pawn.Faction == Faction.OfPlayer;

            MigcorpSkiptechSettings settings = MigcorpSkiptechMod.Settings;
            shockExempt = !isPlayerFaction
                || settings.disableSkipShock
                || !settings.enableSkipShockAvoidance
                || pawn.Drafted
                || forced
                || (pawn.CurJob?.playerForced ?? false);
        }
    }
}