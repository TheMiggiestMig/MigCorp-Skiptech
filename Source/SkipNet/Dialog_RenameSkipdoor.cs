using MigCorp.Skiptech.SkipNet.Comps;
using Verse;

namespace MigCorp.Skiptech.SkipNet
{
    // Need a custom Rename dialogue that allows blank names.
    public class Dialog_RenameSkipdoor : Dialog_Rename<CompSkipdoor>
    {
        public Dialog_RenameSkipdoor(CompSkipdoor skipdoor) : base(skipdoor) { }

        protected override AcceptanceReport NameIsValid(string name) => true;
    }
}