using System;
using System.Linq;
using System.Text;

namespace CPlatform.PORD
{
    public partial class PORD_Help : PORDBasePage
    {
        protected string ChecksTableHtml = "";

        protected void Page_Load(object sender, EventArgs e)
        {
            pnlSampleData.Visible = PORDHelper.DemoMode;
            var sb = new StringBuilder();
            foreach (var c in PORDChecks.All)
            {
                sb.Append("<tr><td><strong>").Append(PORDHelper.Enc(c.Name)).Append("</strong></td><td>").Append(PORDChecks.CheckBadge(c))
                  .Append("</td><td>").Append(PORDHelper.Enc(c.Description)).Append("</td><td>")
                  .Append(string.IsNullOrEmpty(c.FilePattern) ? "<span class=\"muted\">TBC</span>" : "<code>" + PORDHelper.Enc(c.FilePattern) + "</code>")
                  .Append("</td></tr>");
            }
            ChecksTableHtml = sb.ToString();
        }

        protected void btnRestore_Click(object sender, EventArgs e)
        {
            if (!PORDHelper.DemoMode) return;
            if (!chkRestore.Checked) { litRestore.Text = "<p role=\"alert\">Confirm the checkbox before restoring the sample data.</p>"; return; }
            PORDHelper.Store.Reset();
            Response.Redirect("PORD_Admin.aspx", true);
        }
    }
}
