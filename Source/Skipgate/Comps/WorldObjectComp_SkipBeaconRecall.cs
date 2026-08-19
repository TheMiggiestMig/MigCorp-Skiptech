using MigCorp.Skiptech.Comps;
using MigCorp.Skiptech.Skipgate.Operations;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    public class WorldObjectCompProperties_SkipBeaconRecall : WorldObjectCompProperties
    {
        public WorldObjectCompProperties_SkipBeaconRecall() => compClass = typeof(WorldObjectComp_SkipBeaconRecall);
    }

    [StaticConstructorOnStartup]
    public class WorldObjectComp_SkipBeaconRecall : WorldObjectComp
    {
        private static readonly Texture2D CancelIcon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");

        private static readonly List<Thing> tmpBeacons = new List<Thing>();
        private CompSkipBeacon ClaimedBeacon => FirstBeacon(true);
        private CompSkipBeacon AnyBeacon => FirstBeacon(false);
        private CompSkipBeacon FirstBeacon(bool claimedOnly)
        {
            Caravan caravan = parent as Caravan;
            if (caravan == null) { return null; }

            SkipBeaconUtil.BeaconsInCaravan(caravan, tmpBeacons);

            for (int i = 0; i < tmpBeacons.Count; i++)
            {
                CompSkipBeacon comp = SkipBeaconUtil.BeaconComp(tmpBeacons[i]);

                if (comp != null && (!claimedOnly || comp.IsClaimed)) { return comp; }
            }

            return null;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            CompSkipBeacon comp = ClaimedBeacon;
            SkipgateOperation_Recall recall = comp?.ActiveRecall;

            if (recall != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = recall.CancelLabel,
                    defaultDesc = $"{recall.CancelDesc}\n\nBeing recalled by {comp.RecallingGate.RenamableLabel}.",
                    icon = CancelIcon,
                    action = delegate { comp.ActiveRecall?.TryCancel(); }
                };

                yield break;
            }

            // Nothing claimed — offer to call home, if anyone aboard is actually wearing a beacon.
            CompSkipBeacon carried = AnyBeacon;
            if (carried == null) { yield break; }

            yield return new Command_RecallToSkipgate(carried.parent, SkipgateRecallMode.Emergency);
            yield return new Command_RecallToSkipgate(carried.parent, SkipgateRecallMode.Normal);
        }

        public override string CompInspectStringExtra()
        {
            CompSkipBeacon comp = ClaimedBeacon;
            SkipgateOperation_Recall recall = comp?.ActiveRecall;

            if (recall == null) { return null; }

            string gateLabel = comp.RecallingGate.RenamableLabel;

            return recall.Phase == SkipgateOperationPhase.Dialing
                ? $"Being recalled to {gateLabel} (dialing, {recall.DialingTicksLeft.ToStringTicksToPeriod()})"
                : $"Being recalled to {gateLabel} (charging, {comp.RecallingGateComp.Capacitor.ChargeEtaTicks().ToStringTicksToPeriod()})";
        }
    }
}