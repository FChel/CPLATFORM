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
        /// Catches any unhandled error during the page lifecycle and renders a
        /// branded, classified message in place of the raw ASP.NET error page.
        /// Covers every LPPI page — admin pages and the token-auth reviewer
        /// page both inherit this base.
        ///
        /// Admin pages (RequiresAdminAccess == true) show the short provider
        /// message to help the internal team; the reviewer page is
        /// outward-facing and stays fully generic.
        /// </summary>
        protected override void OnError(EventArgs e)
        {
            Exception ex = Server.GetLastError();
            if (ex != null)
            {
                ex = ex.GetBaseException();
            }

            // A redirect (e.g. the admin gate above) raises a ThreadAbort; that
            // is not an application fault, so leave it for the framework.
            if (ex is System.Threading.ThreadAbortException)
            {
                return;
            }

            string heading;
            string detail;
            string technical = null;

            System.Data.OleDb.OleDbException dbEx = ex as System.Data.OleDb.OleDbException;
            if (dbEx != null)
            {
                bool isTimeout = false;
                if (dbEx.Message != null &&
                    dbEx.Message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isTimeout = true;
                }
                if (dbEx.ErrorCode == unchecked((int)0x80040E31))
                {
                    isTimeout = true;
                }

                if (isTimeout)
                {
                    heading = "The database is taking too long to respond";
                    detail  = "The server is busy and the request timed out before it finished. This is usually temporary — please wait a moment and try again. If it keeps happening, let support know.";
                }
                else
                {
                    heading = "A database error occurred";
                    detail  = "The request could not be completed because of a database error. Please try again. If it keeps happening, let support know.";
                }
                technical = dbEx.Message;
            }
            else
            {
                heading = "Something went wrong";
                detail  = "The page could not be loaded because of an unexpected error. Please try again. If it keeps happening, let support know.";
                if (ex != null)
                {
                    technical = ex.Message;
                }
            }

            // Internal team sees the provider line; reviewer page does not.
            RenderErrorPage(heading, detail, RequiresAdminAccess ? technical : null);
        }

        /// <summary>
        /// Render a self-contained, inline-styled error page (DFG palette) and
        /// end the request. Inline styles because the response buffer is
        /// cleared first, so the page's lppi.css link is gone. TrySkipIisCustomErrors
        /// stops the IIS httpErrors block swapping our body for its own.
        /// </summary>
        private void RenderErrorPage(string heading, string detail, string technical)
        {
            string support = LPPIHelper.Setting("LPPI.SupportContact", "");

            StringBuilder sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"en-AU\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.Append("<title>LPPI Review</title></head>");
            sb.Append("<body style=\"margin:0;font-family:'Segoe UI',Arial,sans-serif;background:#f4f4f4;color:#222;\">");
            sb.Append("<div style=\"max-width:640px;margin:64px auto;background:#fff;border:1px solid #e0e0e0;border-top:4px solid #d75b07;border-radius:6px;padding:32px 36px;\">");
            sb.Append("<div style=\"font-size:13px;letter-spacing:.04em;text-transform:uppercase;color:#d75b07;font-weight:700;margin-bottom:8px;\">LPPI Review</div>");

            sb.Append("<h1 style=\"font-size:22px;line-height:1.3;margin:0 0 14px;color:#222;\">");
            sb.Append(HttpUtility.HtmlEncode(heading));
            sb.Append("</h1>");

            sb.Append("<p style=\"font-size:15px;line-height:1.6;margin:0 0 18px;color:#444;\">");
            sb.Append(HttpUtility.HtmlEncode(detail));
            sb.Append("</p>");

            if (!string.IsNullOrEmpty(technical))
            {
                sb.Append("<p style=\"font-size:13px;line-height:1.5;margin:0 0 18px;color:#777;\">Details: ");
                sb.Append(HttpUtility.HtmlEncode(technical));
                sb.Append("</p>");
            }

            if (!string.IsNullOrEmpty(support))
            {
                sb.Append("<p style=\"font-size:14px;line-height:1.5;margin:0;color:#444;\">");
                sb.Append(HttpUtility.HtmlEncode(support));
                sb.Append("</p>");
            }

            sb.Append("</div></body></html>");

            Response.Clear();
            Response.TrySkipIisCustomErrors = true;
            Response.StatusCode = 500;
            Response.ContentType = "text/html; charset=utf-8";
            Response.Write(sb.ToString());
            Server.ClearError();
            Response.End();
        }

        /// <summary>
        /// Render the standard LPPI page header. Pass the active nav key:
        /// "dashboard","summary","help","load","batches","sendouts","cm",
        /// "reasons","deactivated","export","adminusers".
        /// </summary>
        public string RenderHeader(string active)
        {
            // "summary" sits between dashboard and help — it is read-only,
            // executive-facing, and conceptually adjacent to the dashboard
            // (operational counterpart to the exec at-a-glance view).
            //
            // "deactivated" sits between "reasons" and "export" — it is a
            // by-product of the reason-code workflow (RC-RL) and a
            // pre-export watch-list, so the nav order reflects that flow:
            //   reasons -> deactivated -> export
            var nav = new[] {
                new { Key="dashboard",   Label="Dashboard",            Url="LPPI_Admin.aspx" },
                new { Key="summary",     Label="Summary",              Url="LPPI_Summary.aspx" },
                new { Key="help",        Label="Help",                 Url="LPPI_Help.aspx" },
                new { Key="load",        Label="Load file",            Url="LPPI_Load.aspx" },
                new { Key="batches",     Label="Batches",              Url="LPPI_Batches.aspx" },
                new { Key="sendouts",    Label="Send-outs",            Url="LPPI_SendOuts.aspx" },
                new { Key="cm",          Label="Capability Managers",  Url="LPPI_CapabilityManagers.aspx" },
                new { Key="reasons",     Label="Reason Codes",         Url="LPPI_ReasonCodes.aspx" },
                new { Key="deactivated", Label="Deactivated",          Url="LPPI_Deactivated.aspx" },
                new { Key="export",      Label="Export",               Url="LPPI_Export.aspx" },
                new { Key="adminusers",  Label="Admin users",          Url="LPPI_AdminUsers.aspx" }
            };

            // Support mailto — To: LPPI inbox
            string supportTo = LPPIHelper.Setting("LPPI.SupportMailboxTo", "");
            var supportHref = new StringBuilder("mailto:");
            supportHref.Append(HttpUtility.HtmlAttributeEncode(supportTo));
            supportHref.Append("?subject=");
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
