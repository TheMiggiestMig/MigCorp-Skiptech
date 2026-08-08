using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MigCorp.Skiptech.Skipgate.Comps
{
    public class CompProperties_SkipgateCapacitor : CompProperties
    {
        public CompProperties_SkipgateCapacitor() => compClass = typeof(CompSkipgateCapacitor);
    }

    public class CompSkipgateCapacitor : ThingComp
    {
        public CompProperties_SkipgateCapacitor Props => (CompProperties_SkipgateCapacitor)props;
        public CompPowerTrader powerTrader;

        private int targetCapacity = 0;
        public int TargetCapacity => targetCapacity;
        private int currentCapacity = 0;
        public int CurrentCapacity => currentCapacity;
        public int ChargeRate = 0;
        public int DrainRate = 0;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref targetCapacity, "targetCapacity", defaultValue: 0);
            Scribe_Values.Look(ref currentCapacity, "currentCapacity", defaultValue: 0);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerTrader = parent.GetComp<CompPowerTrader>();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo item in base.CompGetGizmosExtra())
            {
                yield return item;
            }

            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Fill Charge",
                    action = delegate
                    {
                        Messages.Message($"YOLO {powerTrader.PowerOutput}", MessageTypeDefOf.PositiveEvent);
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = "DEV: Empty Charge",
                    action = delegate
                    {
                        Messages.Message($"YOLO {powerTrader.PowerOutput}", MessageTypeDefOf.PositiveEvent);
                    }
                };
            }
        }
    }
}
