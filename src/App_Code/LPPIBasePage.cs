using System;
using System.Text;
using System.Web;
using System.Web.UI;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Tiny base for LPPI admin pages. Renders the shared header strip
    /// (brand + nav + env chip + user) so every page is consistent without
    /// needing a master page.
    /// </summary>
    public class LPPIBasePage : Page
    {
        public string CurrentEnv  { get { return LPPIHelper.Environment; } }
        public string CurrentUser { get { return LPPIHelper.CurrentUserDisplayName(); } }
        public string EnvCssClass { get { return CurrentEnv.ToLowerInvariant(); } }

        /// <summary>
        /// Override to false on pages that do not require admin access.
        /// Currently only LPPI_Review.aspx, which authenticates via token.
        /// </summary>
        protected virtual bool RequiresAdminAccess { get { return true; } }

        protected override void OnLoad(EventArgs e)
        {
            if (RequiresAdminAccess && !LPPIHelper.HasLppiAccess())
            {
                Response.Redirect("~/LPPI/LPPI_Info.aspx", true);
            }
            base.OnLoad(e);
        }

        /// <summary>
        /// Render the standard LPPI page header. Pass the active nav key:
        /// "dashboard","load","batches","cm","reasons","sendouts","export","adminusers".
        /// </summary>
        public string RenderHeader(string active)
        {
            var nav = new[] {
                new { Key="dashboard",  Label="Dashboard",           Url="LPPI_Admin.aspx" },
                new { Key="load",       Label="Load file",           Url="LPPI_Load.aspx" },
                new { Key="batches",    Label="Batches",             Url="LPPI_Batches.aspx" },
                new { Key="sendouts",   Label="Send-outs",           Url="LPPI_SendOuts.aspx" },
                new { Key="cm",         Label="Capability Managers", Url="LPPI_CapabilityManagers.aspx" },
                new { Key="reasons",    Label="Reason Codes",        Url="LPPI_ReasonCodes.aspx" },
                new { Key="export",     Label="Export",              Url="LPPI_Export.aspx" },
                new { Key="adminusers", Label="Admin users",         Url="LPPI_AdminUsers.aspx" }
            };

            // Support mailto — To: LPPI inbox, CC: DFSPI
            string supportTo = LPPIHelper.Setting("LPPI.SupportMailboxTo", "");
            string supportCc = LPPIHelper.Setting("LPPI.SupportMailboxCc", "");
            var supportHref = new StringBuilder("mailto:");
            supportHref.Append(HttpUtility.HtmlAttributeEncode(supportTo));
            supportHref.Append("?cc=");
            supportHref.Append(HttpUtility.HtmlAttributeEncode(supportCc));
            supportHref.Append("&subject=");
            supportHref.Append(HttpUtility.HtmlAttributeEncode("LPPI Review \u2014 Feedback & Support"));

            var sb = new StringBuilder();
            sb.Append("<header class=\"lppi-header\">");

            // Brand
            sb.Append("<a href=\"LPPI_Admin.aspx\" class=\"lppi-brand\">");
            sb.Append("<span class=\"mark\"><svg viewBox=\"0 0 24 24\"><path d=\"M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z\"/><path d=\"M14 2v6h6\"/><circle cx=\"12\" cy=\"15\" r=\"3\"/><path d=\"M12 13v2l1 1\"/></svg></span>");
            sb.Append("<span class=\"lppi-brand-text\">");
            sb.Append("<span class=\"lppi-brand-title\">LPPI Review</span>");
            sb.Append("<span class=\"lppi-brand-subtitle\">Review LPPI lines and record pay\u00a0/\u00a0no-pay decisions</span>");
            sb.Append("</span>");
            sb.Append("</a>");

            // Nav
            sb.Append("<nav class=\"lppi-nav\">");
            foreach (var n in nav)
            {
                var cls = n.Key == active ? " class=\"active\"" : "";
                sb.Append("<a href=\"").Append(HttpUtility.HtmlAttributeEncode(n.Url)).Append("\"").Append(cls).Append(">")
                  .Append(HttpUtility.HtmlEncode(n.Label)).Append("</a>");
            }
            sb.Append("</nav>");

            // Right-side: env chip, user, support button
            sb.Append("<div class=\"lppi-header-right\">");
            sb.Append("<span class=\"env-chip ").Append(HttpUtility.HtmlAttributeEncode(EnvCssClass)).Append("\">")
              .Append(HttpUtility.HtmlEncode(CurrentEnv)).Append("</span>");
            sb.Append("<span class=\"lppi-user\">").Append(HttpUtility.HtmlEncode(CurrentUser)).Append("</span>");

            if (!string.IsNullOrEmpty(supportTo))
            {
                sb.Append("<a href=\"").Append(supportHref).Append("\" class=\"btn btn-sm btn-ghost lppi-support-btn\" title=\"Feedback &amp; support\">")
                  .Append("Feedback &amp; support")
                  .Append("</a>");
            }

            sb.Append("</div>");
            sb.Append("</header>");
            return sb.ToString();
        }
    }
}
