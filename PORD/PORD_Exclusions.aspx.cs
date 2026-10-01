using System;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.PORD
{
    public partial class PORD_Exclusions : PORDBasePage
    {
        protected int ActiveCount, ExpiringCount, ExpiredCount, RevokedCount;

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) Bind();
        }

        private void Bind()
        {
            var list = PORDHelper.Store.GetExclusions();
            var today = DateTime.Today;
            RevokedCount  = list.Count(x => x.IsRevoked);
            ExpiredCount  = list.Count(x => !x.IsRevoked && x.ExpiryDate < today);
            ActiveCount   = list.Count(x => !x.IsRevoked && x.ExpiryDate >= today);
            ExpiringCount = list.Count(x => !x.IsRevoked && x.ExpiryDate >= today && x.ExpiryDate <= today.AddDays(30));
            rpt.DataSource = list;
            rpt.DataBind();
        }

        protected PordExclusion X(object o) { return (PordExclusion)o; }

        protected string StatusPill(PordExclusion x)
        {
            if (x.IsRevoked) return "<span class=\"pill cancelled\">Revoked</span>";
            if (x.ExpiryDate < DateTime.Today) return "<span class=\"pill closed\">Expired</span>";
            if (x.ExpiryDate <= DateTime.Today.AddDays(30)) return "<span class=\"pill duesoon\">Expiring</span>";
            return "<span class=\"pill finalised\">Active</span>";
        }

        protected void rpt_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "revoke") return;
            string po = Convert.ToString(e.CommandArgument);
            PORDHelper.Store.RevokeExclusion(po, CurrentUser);
            phMsg.Controls.Add(new LiteralControl("<div class=\"alert ok\">Exclusion for PO " + PORDHelper.Enc(po) + " revoked.</div>"));
            Bind();
        }
    }
}
