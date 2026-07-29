using Verse;

namespace MigCorp.Skiptech
{
    public class GameComponent_Skiptech : GameComponent
    {
        // Set to -1 if the default isn't customized.
        // Otherwise, it holds the value of the default skipdoor power for new games
        // and saves that haven't customized their own individually.
        public int skipdoorPowerOverride = -1;

        public GameComponent_Skiptech(Game game) { }

        public override void StartedNewGame()
        {
            SkipdoorCustomPower.ApplyToActiveGame();
        }

        public override void LoadedGame()
        {
            SkipdoorCustomPower.ApplyToActiveGame();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref skipdoorPowerOverride, "skipdoorPowerOverride", -1);
        }
    }
}