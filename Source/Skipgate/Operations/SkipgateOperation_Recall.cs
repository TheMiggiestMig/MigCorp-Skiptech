using MigCorp.Skiptech.Comps;
using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Text;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.Skipgate.Operations
{
    public enum SkipgateRecallMode
    {
        Normal,
        Emergency
    }

    // Handles teleporting pawns to the Skipgate.
    // Can only teleport pawns (or caravans) that have a Skip Beacon with them.
    // Can be initialized from either the pawn/caravan or the skipgate, but the skipgate handles the whole process once targets are set.
    public class SkipgateOperation_Recall : SkipgateOperation
    {
        private SkipgateRecallMode mode;
        private Thing beacon;
        private float emergencyRecallActualCost;

        private const int ValidityCheckInterval = 60;
        private float tilesAtClaim;
        private int validityTicks;

        private string GateLabel => gate.GateLabel;
        private CompSkipBeacon BeaconComp => SkipBeaconUtil.BeaconComp(beacon);
        private Pawn Wearer => SkipBeaconUtil.WearerOf(beacon);
        private Caravan TargetCaravan => SkipBeaconUtil.CaravanOf(beacon);

        public override string CancelLabel => IsEmergency ? "Cancel emergency recall" : "Cancel recall";
        public override string CancelDesc => $"Stop recalling {TargetLabel}.\n\nThe current charge will remain but slowly drain.";

        public SkipgateRecallMode Mode => mode;
        public Thing Beacon => beacon;
        public bool IsEmergency => mode == SkipgateRecallMode.Emergency;
        protected override int ChargeTicks => IsEmergency ? gate.Props.emergencyChargeTicks : base.ChargeTicks;
        protected override bool ChecksCapacityPolicy => !IsEmergency;
        public override bool IgnoresCooldown => IsEmergency;
        protected override bool UsesCapacitor => !IsEmergency;
        public override SkipgateOperationType Type => SkipgateOperationType.Recall;
        public string TargetLabel
        {
            get
            {
                if (beacon == null) { return "none"; }

                Pawn holder = SkipBeaconUtil.HolderOf(beacon);
                if (holder == null) { return "an unheld beacon"; }

                Caravan caravan = SkipBeaconUtil.CaravanOf(beacon);

                return caravan != null ? $"{holder.LabelShortCap} ({caravan.LabelCap})" : $"{holder.LabelShortCap}";
            }
        }
        public SkipgateOperation_Recall(CompSkipgate gate) : base(gate)
        {
        }

        public SkipgateOperation_Recall(CompSkipgate gate, Thing beacon, SkipgateRecallMode mode = SkipgateRecallMode.Normal) : this(gate)
        {
            this.beacon = beacon;
            this.mode = mode;

            tilesAtClaim = SkipgateCostUtil.TilesBetween(gate, SkipBeaconUtil.TileOf(beacon));

            if (IsEmergency)
            {
                requiredCharge = 0f;
                return;
            }

            requiredCharge = SkipgateCostUtil.CalculateRecallCost(gate, SkipBeaconUtil.YoinkSetMass(beacon), tilesAtClaim);
        }

        public override AcceptanceReport CanStart()
        {
            if (beacon == null || beacon.Destroyed) { return "That skip beacon is gone."; }
            if (!SkipBeaconUtil.IsTargetable(beacon)) { return "Nobody is wearing that skip beacon any more."; }

            CompSkipBeacon comp = BeaconComp;
            if (comp.IsClaimed) { return $"That beacon is already being recalled by {comp.RecallingGate.RenamableLabel}."; }

            Pawn wearer = Wearer;
            if (wearer.Downed) { return $"{wearer.LabelShortCap} is in no state to channel a skip."; }

            Caravan caravan = TargetCaravan;
            if (caravan != null && AnyOtherClaimedBeaconInCaravan(caravan)) { return $"{caravan.LabelCap} is already being recalled."; }
            if (caravan == null && OverlappingBeacon() != null) { return "Another skipfield anchor is already active inside that radius."; }

            return base.CanStart();
        }

        private bool AnyOtherClaimedBeaconInCaravan(Caravan caravan)
        {
            List<Thing> beacons = new List<Thing>();
            SkipBeaconUtil.BeaconsInCaravan(caravan, beacons);

            for (int i = 0; i < beacons.Count; i++)
            {
                if (beacons[i] != beacon && SkipBeaconUtil.BeaconComp(beacons[i]).IsClaimed) { return true; }
            }

            return false;
        }

        private Thing OverlappingBeacon()
        {
            Map map = SkipBeaconUtil.MapOf(beacon);
            if (map == null) { return null; }

            List<Thing> beacons = new List<Thing>();
            SkipBeaconUtil.BeaconsOnMap(map, beacons);

            IntVec3 here = beacon.PositionHeld;
            float radius = SkipBeaconUtil.RadiusOf(beacon);

            for (int i = 0; i < beacons.Count; i++)
            {
                Thing other = beacons[i];
                if (other == beacon) { continue; }

                CompSkipBeacon comp = SkipBeaconUtil.BeaconComp(other);

                if (comp.IsClaimed && here.InHorDistOf(other.PositionHeld, radius + comp.Radius)) { return other; }
            }

            return null;
        }

        protected override void Execute()
        {
            if (!TrySpendRequiredCharge()) { return; }

            if (mode == SkipgateRecallMode.Emergency)
            {
                Messages.Message($"{gate} successfully performed {Type}. DEV Penalize the player for {emergencyRecallActualCost} units.", MessageTypeDefOf.CautionInput);
                CompleteOperation(emergencyRecallActualCost);
                return;
            }
            Messages.Message($"{gate} successfully performed {Type}.", MessageTypeDefOf.NeutralEvent);
            CompleteOperation(requiredCharge);
        }

        public override void Start()
        {
            base.Start();

            BeaconComp.Claim(gate.Gate);

            Caravan caravan = TargetCaravan;
            if (caravan != null)
            {
                caravan.pather.StopDead();
                return;
            }

            StartChannelJob();
        }

        private void StartChannelJob()
        {
            Pawn wearer = Wearer;
            if (wearer == null) { return; }

            wearer.jobs.TryTakeOrderedJob(JobMaker.MakeJob(SkiptechDefOf.MigCorp_ChannelSkipBeacon, wearer), JobTag.Misc);
        }

        private void ReleaseBeacon()
        {
            CompSkipBeacon comp = BeaconComp;
            if (comp == null) { return; }

            Pawn wearer = Wearer;

            comp.ClearClaim();

            if (wearer?.jobs?.curJob?.def == SkiptechDefOf.MigCorp_ChannelSkipBeacon)
            {
                wearer.jobs.EndCurrentJob(JobCondition.Succeeded);
            }
        }

        protected override void OnCompleted() => ReleaseBeacon();

        protected override void OnCancelled() => ReleaseBeacon();

        protected override void OnFailed(string reason)
        {
            Messages.Message(reason, gate.parent, MessageTypeDefOf.NegativeEvent, historical: false);

            ReleaseBeacon();
        }

        public override void AppendInspectLines(StringBuilder sb)
        {
            base.AppendInspectLines(sb);

            sb.AppendLine(IsEmergency
                ? $"EMERGENCY recall: {TargetLabel} (charge {RequiredCharge:F0})"
                : $"Recalling: {TargetLabel} (cost {RequiredCharge:F0})");
        }

        public override void DrawExtraSelectionOverlays()
        {
            if (beacon == null || SkipBeaconUtil.CaravanOf(beacon) != null) { return; }

            Map map = SkipBeaconUtil.MapOf(beacon);
            if (map == null || map != gate.parent.Map) { return; }

            GenDraw.DrawRadiusRing(beacon.PositionHeld, SkipBeaconUtil.RadiusOf(beacon));
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref mode, "recallMode", SkipgateRecallMode.Normal);
            Scribe_References.Look(ref beacon, "beacon");
            Scribe_Values.Look(ref emergencyRecallActualCost, "emergencyRecallActualCost", 0f);
            Scribe_Values.Look(ref tilesAtClaim, "tilesAtClaim", 0f);
            Scribe_Values.Look(ref validityTicks, "validityTicks", 0);
        }

        protected override void TickPreparing(int delta)
        {
            if (CheckChannelLost(delta)) { return; }

            base.TickPreparing(delta);
        }

        protected override void TickDialing(int delta)
        {
            if (CheckChannelLost(delta)) { return; }

            base.TickDialing(delta);
        }

        private bool CheckChannelLost(int delta)
        {
            validityTicks += delta;
            if (validityTicks < ValidityCheckInterval) { return false; }

            validityTicks = 0;

            string broken = ChannelBroken();
            if (broken == null)
            {
                Recost();
                return false;
            }

            Messages.Message(broken, gate.parent, MessageTypeDefOf.NegativeEvent, historical: false);
            TryCancel();

            return true;
        }

        private string ChannelBroken()
        {
            if (beacon == null || beacon.Destroyed) { return $"{GateLabel} lost the skip beacon it was locked onto."; }

            Pawn wearer = Wearer;
            if (wearer == null) { return $"{GateLabel} lost its anchor - nobody is wearing the beacon."; }
            if (wearer.Dead || wearer.Downed || !wearer.Awake()) { return $"{wearer.LabelShortCap} can no longer channel the skip."; }

            Caravan caravan = TargetCaravan;
            if (caravan != null) { return caravan.pather.Moving ? $"{caravan.LabelCap} moved and broke the channel." : null; }

            if (SkipBeaconUtil.MapOf(beacon) == null) { return $"{GateLabel} lost track of the skip beacon."; }

            // Fix channel state when job disappears after load.
            return wearer.jobs?.curJob?.def == SkiptechDefOf.MigCorp_ChannelSkipBeacon
                ? null
                : $"{wearer.LabelShortCap} stopped channelling the skip.";
        }

        private void Recost()
        {
            if (IsEmergency) { return; } // Fixed fee. Its real price is settled at execute.

            requiredCharge = SkipgateCostUtil.CalculateRecallCost(gate, SkipBeaconUtil.YoinkSetMass(beacon), tilesAtClaim);
            ApplyRuntimeState();
        }

        public override void ResumeAfterLoad()
        {
            base.ResumeAfterLoad();

            CompSkipBeacon comp = BeaconComp;
            if (comp == null || comp.RecallingGateComp != gate)
            {
                Messages.Message($"{GateLabel} lost its recall target.", gate.parent, MessageTypeDefOf.NegativeEvent, historical: false);
                TryCancel();
                return;
            }

            // A job does not reliably survive the trip. Re-issue rather than cancel.
            if (TargetCaravan == null && Wearer?.jobs?.curJob?.def != SkiptechDefOf.MigCorp_ChannelSkipBeacon)
            {
                StartChannelJob();
            }
        }
    }
}
