using MigCorp.Skiptech.Skipgate.Comps;
using System.Text;
using Verse;

namespace MigCorp.Skiptech.Skipgate
{
    public class Building_Skipgate : Building, IRenameable
    {

        public CompSkipgate skipgateComp;
        public CompSkipgateCapacitor capacitorComp;

        private string skipgateName;
        public string RenamableLabel
        {
            get { return skipgateName.NullOrEmpty() ? BaseLabel : skipgateName; }
            set { skipgateName = value?.Trim(); }
        }

        public string BaseLabel => def.LabelCap;
        public string InspectLabel => BaseLabel;

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref skipgateName, "skipgateName");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            skipgateComp = GetComp<CompSkipgate>();
            capacitorComp = GetComp<CompSkipgateCapacitor>();
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder();

            if (!skipgateName.NullOrEmpty()) { sb.AppendLine($"ID: {skipgateName}"); }

            return sb.ToString().TrimEndNewlines() + base.GetInspectString();
        }
    }
}
