using System;
using System.Text;
using System.Web;
using System.Web.UI;

namespace CPlatform.LPPI
{
    // Does NOT inherit LPPIBasePage — inheriting would cause a redirect loop
    // because LPPIBasePage itself redirects non-admins to this page.
    public partial class LPPI_Info : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            // Show the current Windows identity so the user can include it
            // in their access request without having to look it up themselves.
            string identity = "";
            try
            {
                if (User != null && User.Identity != null
                    && !string.IsNullOrEmpty(User.Identity.Name))
                    identity = User.Identity.Name;
            }
            catch { }

            litIdentity.Text = LPPIHelper.Enc(
                string.IsNullOrEmpty(identity) ? "(not available)" : identity);

            litEnv.Text = LPPIHelper.Enc(LPPIHelper.Environment);

            // Build the contact link using the support mailbox app setting.
            // No CC — the LPPI mailbox is the single support landing point.
            string to = LPPIHelper.Setting("LPPI.SupportMailboxTo", "");

            if (!string.IsNullOrEmpty(to))
            {
                var sb = new StringBuilder();
                sb.Append("<p>");
                sb.Append("<a href=\"mailto:");
                sb.Append(HttpUtility.HtmlAttributeEncode(to));
                sb.Append("\" class=\"btn btn-primary\" style=\"display:inline-block;\">");
                sb.Append("Email the LPPI administrator");
                sb.Append("</a>");
                sb.Append("</p>");
                phContact.Controls.Add(new LiteralControl(sb.ToString()));
            }
            else
            {
                phContact.Controls.Add(new LiteralControl(
                    "<p>Contact your LPPI administrator to request access.</p>"));
            }
        }
    }
}
