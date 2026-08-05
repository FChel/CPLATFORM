using System;

namespace CPlatform.NORM
{
    public partial class NORM_Help : NORMBasePage
    {
        protected override bool RequiresPrepareAccess { get { return false; } }
        protected bool CanPrepare = false;

        protected void Page_Load(object sender, EventArgs e)
        {
            CanPrepare = NORMHelper.HasPrepareAccess();
        }
    }
}
