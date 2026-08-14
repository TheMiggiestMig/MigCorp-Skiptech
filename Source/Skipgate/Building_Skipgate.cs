using MigCorp.Skiptech.Skipgate.Comps;
using System.Text;
using Verse;
using Verse.Grammar;

namespace MigCorp.Skiptech.Skipgate
{
    public class Building_Skipgate : Building, IRenameable
    {

        public CompSkipgate skipgateComp;
        public CompSkipgateCapacitor capacitorComp;
        public CompTransporter_Skipgate transporterComp;

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
            transporterComp = GetComp<CompTransporter_Skipgate>();

            if (respawningAfterLoad && skipgateName.NullOrEmpty())
            {
                GenerateName();
            }
        }

        public override void PostMake()
        {
            base.PostMake();
            GenerateName();
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder();

            if (!skipgateName.NullOrEmpty()) { sb.AppendLine($"ID: {skipgateName}"); }

            return sb.ToString() + base.GetInspectString();
        }

        private void GenerateName()
        {
            GrammarRequest request = default;

            request.Includes.Add(SkiptechDefOf.MigCorp_SkipgateNameMaker);
            skipgateName = GrammarResolver.Resolve("root", request);
        }
    }
}
