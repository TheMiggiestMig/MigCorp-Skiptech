using MigCorp.Skiptech.SkipNet;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech
{
    public class GameComponent_Skiptech : GameComponent
    {
        public int skipdoorPowerOverride = -1;

        public GameComponent_Skiptech(Game game) { }

        public override void StartedNewGame()
        {
            LockPowerSettingsFromDefault();
            SkipdoorCustomPower.ApplyToActiveGame();
        }

        public override void LoadedGame()
        {
            LockPowerSettingsFromDefault();
            SkipdoorCustomPower.ApplyToActiveGame();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref skipdoorPowerOverride, "skipdoorPowerOverride", -1);
        }

        // If the new game or loaded game hasn't locked in it's power setting, grab the current default and lock it in.
        private void LockPowerSettingsFromDefault()
        {
            if (skipdoorPowerOverride < 0) { skipdoorPowerOverride = Mathf.RoundToInt(SkipdoorCustomPower.DefaultWatts); }
        }
    }
}