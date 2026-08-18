using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using RimWorld.Planet;
using System.Text;
using Verse;

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

        public SkipgateRecallMode Mode => mode;
        public Thing Beacon => beacon;
        public bool IsEmergency => mode == SkipgateRecallMode.Emergency;
        protected override int ChargeTicks => IsEmergency ? gate.Props.emergencyChargeTicks : base.ChargeTicks;
        protected override bool ChecksCapacityPolicy => !IsEmergency;
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

            if (IsEmergency)
            {
                requiredCharge = gate.Props.emergencyRequiredCharge;
                emergencyRecallActualCost = requiredCharge;
                return;
            }

            requiredCharge = SkipgateCostUtil.CalculateRecallCost(gate, beacon);
        }

        public override AcceptanceReport CanStart()
        {
            if (beacon == null || beacon.Destroyed) { return "That skip beacon is gone."; }
            if (!SkipBeaconUtil.IsTargetable(beacon)) { return "Nobody is carrying that skip beacon any more."; }

            return base.CanStart();
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

        public override string CancelLabel => IsEmergency ? "Cancel emergency recall" : "Cancel recall";

        public override string CancelDesc => $"Stop recalling {TargetLabel}.\n\nThe current charge will remain but slowly drain.";

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
        }
    }
}
