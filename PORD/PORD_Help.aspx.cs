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
    }
}
