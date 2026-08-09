using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.Skipgate.Comps
{
    public class CompProperties_SkipgateCapacitor : CompProperties
    {
        public float wattsPerCharge = 1600f; // 1600W = 1 Charge
        public float chargingWatts = 1600f; // How much power the capacitor takes from the grid to charge
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
        private float activeChargingWatts;

        private CompPowerTrader powerComp;

        public CompProperties_SkipgateCapacitor Props =>
            (CompProperties_SkipgateCapacitor)props;

        public float Charge => currentCharge;
        public float Target => targetCharge;
        public float ActiveChargingWatts => activeChargingWatts;
        public bool HasTarget => targetCharge > 0f;
        public bool TargetReached => HasTarget && currentCharge >= targetCharge - 0.0001f;
        public bool Powered => powerComp == null || powerComp.PowerOn;
        private float ChargePerSecond => activeChargingWatts / Props.wattsPerCharge;

        public bool IsWithinCapacity(float cost) => Props.maxCharge < 0f || cost <= Props.maxCharge;

        public void SetTarget(float cost)
        {
            targetCharge = Mathf.Max(cost, 0f);
            activeChargingWatts = Props.chargingWatts;
        }
        public void SetTarget(float cost, float rate)
        {
            targetCharge = Mathf.Max(cost, 0f);
            activeChargingWatts = rate;
        }

        public void ClearTarget() => targetCharge = 0f;

        public void SetLoad(float chargePerSecond) => loadPerSecond = Mathf.Max(chargePerSecond, 0f);

        public float Spend(float amount, bool keepTarget = false)
        {
            amount = Mathf.Clamp(amount, 0f, currentCharge);
            currentCharge -= amount;
            if (!keepTarget) { targetCharge = 0f; }

            return amount;
        }

        public float SpendAll() { return Spend(currentCharge); }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentCharge, "capacitorCharge");
            Scribe_Values.Look(ref targetCharge, "capacitorTarget");
            Scribe_Values.Look(ref activeChargingWatts, "capacitorChargingWatts");
            Scribe_Values.Look(ref loadPerSecond, "capacitorLoad");
        }

        public override void CompTickInterval(int delta)
        {
            float dt = delta / 60f;

            bool powered = Powered;
            bool charging = powered && HasTarget && currentCharge < targetCharge;

            if (charging)
            {
                currentCharge = Mathf.Min(currentCharge + ChargePerSecond * dt, targetCharge);

                if (activeChargingWatts <= 0) { activeChargingWatts = Props.chargingWatts; }
            }

            // A load is paid by the power net while powered.
            // Without power, it eats the stored reserve instead.
            if (loadPerSecond > 0f)
            {
                if (!powered)
                {
                    currentCharge = Mathf.Max(currentCharge - loadPerSecond * dt, 0f);
                }
            }

            // Slowly drain if there's nothing to charge toward, or we lost power before being fully charged.
            else if (currentCharge > 0f
                && (!HasTarget || (!powered && currentCharge < targetCharge)))
            {
                currentCharge = Mathf.Max(currentCharge - Props.decayPerSecond * dt, 0f);
            }

            // Set power draw.
            if (powerComp != null)
            {
                float powerDraw = loadPerSecond * Props.wattsPerCharge;

                if (charging) { powerDraw += activeChargingWatts; }

                // Only add the base idle power consumption if its not doing anything
                // i.e. has no target, or reached its target with no load.
                if (!HasTarget
                    || currentCharge >= targetCharge && loadPerSecond <= 0f)
                {
                    powerDraw += powerComp.Props.PowerConsumption;
                }

                powerComp.PowerOutput = -powerDraw;
            }
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
                    int ticksLeft = Mathf.CeilToInt((targetCharge - currentCharge) / ChargePerSecond * 60f);
                    sb.Append(activeChargingWatts < Props.chargingWatts
                        ? $" (slow-charging, {ticksLeft.ToStringTicksToPeriod()})"
                        : $" (charging, {ticksLeft.ToStringTicksToPeriod()})");
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
                    int bufferTicks = Mathf.CeilToInt(
                        currentCharge / loadPerSecond * 60f);
                    sb.Append($"\nReserve: {bufferTicks.ToStringTicksToPeriod()} remaining at current load");
                }
            }

            return sb.ToString();
        }

        // DEV Only
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!DebugSettings.ShowDevGizmos) { yield break; }

            yield return new Command_Action
            {
                defaultLabel = "DEV: Target 66",
                action = () => SetTarget(66f)
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Clear target",
                action = ClearTarget
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Fill",
                action = () => currentCharge = HasTarget ? targetCharge : Props.maxCharge > 0f ? Props.maxCharge : 66f
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Empty",
                action = () => currentCharge = 0f
            };
            yield return new Command_Action
            {
                defaultLabel = loadPerSecond > 0f
                    ? "DEV: Drain off"
                    : "DEV: Drain 0.4/s",
                action = () => SetLoad(loadPerSecond > 0f ? 0f : 0.4f)
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Slow target 66 (200W)",
                action = () => SetTarget(66f, 200f)
            };
        }
    }
}