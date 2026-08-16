using MigCorp.Skiptech.Skipgate.Comps;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    public class Alert_SkipgateLinkFailing : Alert
    {
        private readonly List<Thing> culprits = new List<Thing>();

        public Alert_SkipgateLinkFailing()
        {
            defaultLabel = "Skipgate link failing";
            defaultPriority = AlertPriority.High;
        }

        public override string GetLabel()
        {
            int worst = int.MaxValue;
            foreach (Thing t in culprits)
            {
                CompSkipgate comp = (t as Building_Skipgate)?.skipgateComp;
                if (comp == null || comp.Capacitor.LoadPerSecond <= 0f) { continue; }

                worst = Mathf.Min(worst, comp.Capacitor.BufferTicksRemaining);
            }

            return worst == int.MaxValue
                ? "Skipgate link failing"
                : $"Skipgate link failing ({worst.ToStringTicksToPeriod()})";
        }

        public override TaggedString GetExplanation() =>
            "An unpowered skipgate is draining its capacitor to keep the link open. " +
            "If the capacitor runs dry, the link will collapse and BOTH gates will overheat.\n\n" +
            "Restore power, or unlink deliberately to control when the heat hits.";

        public override AlertReport GetReport()
        {
            culprits.Clear();

            foreach (Map map in Find.Maps)
            {
                foreach (Building_Skipgate gate in map.listerBuildings.AllBuildingsColonistOfClass<Building_Skipgate>())
                {
                    CompSkipgate comp = gate.skipgateComp;
                    if (comp?.LinkedFarGate != null && !comp.Capacitor.Powered)
                    {
                        culprits.Add(gate);
                    }
                }
            }

            return AlertReport.CulpritsAre(culprits);
        }
    }
}