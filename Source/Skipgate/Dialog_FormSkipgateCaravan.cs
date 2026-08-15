using HarmonyLib;
using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using System;
using System.Reflection;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    // Needed to tweak a couple of things, such as no route planning.
    public class Dialog_FormSkipgateCaravan : Dialog_FormCaravan
    {

        public static FieldInfo CanChooseRouteField = AccessTools.Field(typeof(Dialog_FormCaravan), "canChooseRoute");
        public static FieldInfo StartingTileField = AccessTools.Field(typeof(Dialog_FormCaravan), "startingTile");

        public readonly CompSkipgate gate;

        public Dialog_FormSkipgateCaravan(CompSkipgate gate, Action onClosed = null)
            : base(gate.parent.Map, false, onClosed)
        {
            this.gate = gate;

            // We don't want to choose a route. Our route is no route.
            Dialog_FormSkipgateCaravan.CanChooseRouteField.SetValue(this, false);

            // Needs a value, so we cheat and just give it any adjacent tile.
            Dialog_FormSkipgateCaravan.StartingTileField.SetValue(this, Find.WorldGrid.FindMostReasonableAdjacentTileForDisplayedPathCost(CurrentTile));
        }
    }
}