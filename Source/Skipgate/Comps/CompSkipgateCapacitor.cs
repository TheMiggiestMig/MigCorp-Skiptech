using RimWorld;
using System.Text;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.Skipgate.Comps
{
    public class CompProperties_SkipgateCapacitor : CompProperties
    {
        public float wattsPerCharge => 1600f;
        public int chargeTicks => 2500;
        public float decayPercent = 0.01f;
        public float decayPerSecond = 0.25f;
        public float maxCharge = -1f; // -1 for unlimited, specifically once Skipgate Linking has been researched.

        public CompProperties_SkipgateCapacitor()
        {
            compClass = typeof(CompSkipgateCapacitor);
        }
    }

    /// <summary>
    /// A floating capacitor.
    /// </summary>
    public class CompSkipgateCapacitor : ThingComp
    {
        private float currentCharge;
        private float targetCharge;
        private float loadPerSecond;
        private float requestedChargingWatts;

        private CompPowerTrader powerComp;

        public CompProperties_SkipgateCapacitor Props => (CompProperties_SkipgateCapacitor)props;

        public float Charge => currentCharge;
        public float Target => targetCharge;
        public float LoadPerSecond => loadPerSecond;
        public float RequestedChargingWatts => requestedChargingWatts;
        public bool HasTarget => targetCharge > 0f;
        public bool TargetReached => HasTarget && currentCharge >= targetCharge;
        public bool Powered => powerComp == null || powerComp.PowerOn;
        public bool WantsToCharge => HasTarget && currentCharge < targetCharge;
        private float ChargePerSecond => Props.wattsPerCharge <= 0f ? 0f : requestedChargingWatts / Props.wattsPerCharge;
        public bool IsWithinCapacity(float cost) => cost >= 0f && (Props.maxCharge < 0f || cost <= Props.maxCharge);
        public bool HasCharge(float amount) => amount >= 0f && currentCharge >= amount;
        public int ChargeEtaTicks()
        {
            float rate = ChargePerSecond;

            return rate <= 0f ? 0 : Mathf.CeilToInt(Mathf.Max(targetCharge - currentCharge, 0f) / rate * 60f);
        }
        public int EstimateChargeTicks(float cost)
        {
            float rate = Props.wattsPerCharge <= 0f ? 0f : WattsForCost(cost) / Props.wattsPerCharge;

            return rate <= 0f ? 0 : Mathf.CeilToInt(Mathf.Max(cost - currentCharge, 0f) / rate * 60f);
        }
        public int BufferTicksRemaining => loadPerSecond <= 0f ? 0 : Mathf.CeilToInt(currentCharge / loadPerSecond * 60f);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();

            if (powerComp == null)
            {
                Log.Error(parent + " has CompSkipgateCapacitor but no CompPowerTrader.");
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentCharge, "capacitorCharge");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                currentCharge = Mathf.Max(currentCharge, 0f);

                // These will be reapplied by the loaded operation during save/load.
                targetCharge = 0f;
                requestedChargingWatts = 0f;
                loadPerSecond = 0f;
            }
        }
        public void SetTarget(float cost, float rate)
        {
            targetCharge = Mathf.Max(cost, 0f);
            requestedChargingWatts = Mathf.Max(rate, 0f);
        }
        public void SetTarget(float cost) => SetTarget(cost, WattsForCost(cost));

        public void ClearTarget()
        {
            targetCharge = 0f;
            requestedChargingWatts = 0f;
        }

        public float WattsForCost(float cost, int ticks)
        {
            return ticks <= 0 ? 0f : cost * Props.wattsPerCharge / (ticks / 60f);
        }

        public float WattsForCost(float cost) => WattsForCost(cost, Props.chargeTicks);


        public void SetLoad(float chargePerSecond) => loadPerSecond = Mathf.Max(chargePerSecond, 0f);

        public void ClearDemand()
        {
            ClearTarget();
            loadPerSecond = 0f;
        }
        public bool TrySpend(float amount, bool keepTarget = false)
        {
            amount = Mathf.Max(amount, 0f);

            if (!HasCharge(amount)) { return false; }

            currentCharge = Mathf.Max(currentCharge - amount, 0f);

            if (!keepTarget) { ClearTarget(); }

            return true;
        }

        public float SpendAll()
        {
            float spent = currentCharge;

            currentCharge = 0f;
            ClearTarget();

            return spent;
        }

        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);

            float seconds = delta / 60f;

            bool powered = Powered;
            bool wantsToCharge = WantsToCharge;
            bool charging = Powered && wantsToCharge;

            if (charging)
            {
                currentCharge = Mathf.Min(currentCharge + ChargePerSecond * seconds, targetCharge);
            }

            // A load is paid by the power net while powered.
            // Without power, it eats the stored reserve instead.
            if (loadPerSecond > 0f)
            {
                if (!powered)
                {
                    currentCharge = Mathf.Max(currentCharge - loadPerSecond * seconds, 0f);
                }
            }

            // Slowly drain if there's nothing to charge toward.
            else if (currentCharge > 0f && !HasTarget)
            {
                float decay = Mathf.Max(Props.decayPerSecond, Props.decayPercent * currentCharge);
                currentCharge = Mathf.Max(currentCharge - decay * seconds, 0f);
            }

            // Set power draw.
            UpdatePowerDraw(wantsToCharge);
        }

        private void UpdatePowerDraw(bool wantsToCharge)
        {
            if (powerComp == null) { return; }

            float powerDraw = 0f;

            if (wantsToCharge) { powerDraw += requestedChargingWatts; }

            if (loadPerSecond > 0f) { powerDraw += loadPerSecond * Props.wattsPerCharge; }

            // Only add the base idle power consumption if its not doing anything
            // i.e. has no target, or reached its target with no load.
            bool idle = !wantsToCharge && loadPerSecond <= 0f;
            if (idle) { powerDraw += powerComp.Props.PowerConsumption; }

            powerComp.PowerOutput = -powerDraw;
        }

        public override string CompInspectStringExtra()
        {
            // nothing interesting to report
            if (currentCharge <= 0f && !HasTarget && loadPerSecond <= 0f)
            {
                return null;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append($"Capacitor: {currentCharge:F1}");
            if (HasTarget) { sb.Append($" / {targetCharge:F1}"); }

            if (HasTarget && currentCharge < targetCharge)
            {
                if (Powered)
                {
                    int ticksLeft = ChargeEtaTicks();
                    sb.Append($" (charging, {ticksLeft.ToStringTicksToPeriod()})");
                }
                else
                {
                    sb.Append(" (no power - draining)");
                }
            }
            else if (TargetReached)
            {
                sb.Append(" (ready)");
            }
            else if (currentCharge > 0f && !HasTarget)
            {
                sb.Append(" (discharging)");
            }

            if (loadPerSecond > 0f)
            {
                if (Powered)
                {
                    sb.Append("\nReserve: sustained by power");
                }
                else
                {
                    sb.Append($"\nReserve: {BufferTicksRemaining.ToStringTicksToPeriod()} remaining at current load");
                }
            }

            return sb.ToString();
        }
    }
}