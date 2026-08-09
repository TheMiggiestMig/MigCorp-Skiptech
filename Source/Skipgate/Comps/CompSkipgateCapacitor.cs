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
        //private float activeChargingWatts;
        private float requestedChargingWatts;

        private CompPowerTrader powerComp;

        public CompProperties_SkipgateCapacitor Props =>
            (CompProperties_SkipgateCapacitor)props;

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

        public bool TrySetTarget(float cost, float rate)
        {
            cost = Mathf.Max(cost, 0f);

            if (!IsWithinCapacity(cost)) { return false; }

            targetCharge = cost;
            requestedChargingWatts = Mathf.Max(Props.chargingWatts, 0f);

            return true;
        }
        public bool TrySetTarget(float cost) => TrySetTarget(cost, Props.chargingWatts);

        public void ClearTarget()
        {
            targetCharge = 0f;
            requestedChargingWatts = 0f;
        }

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
                currentCharge = Mathf.Max(currentCharge - Props.decayPerSecond * seconds, 0f);
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
                    int ticksLeft = Mathf.CeilToInt((targetCharge - currentCharge) / ChargePerSecond * 60f);
                    sb.Append(requestedChargingWatts < Props.chargingWatts
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
                action = () => TrySetTarget(66f)
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
                action = () => TrySetTarget(66f, 200f)
            };
        }
    }
}