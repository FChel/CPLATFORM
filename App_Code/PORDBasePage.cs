using System;
using System.Text;
using System.Web;
using System.Web.UI;

namespace CPlatform.PORD
{
    /// <summary>
    /// Base for PO Review admin pages. Mirrors LPPIBasePage: shared header
    /// (brand + nav + env chip + user + support), admin gate, and a branded
    /// error page in place of the raw ASP.NET one.
    ///
    /// The header reuses the lppi.css shell classes so both modules share one
    /// design system; module-specific styling lives in css/pord.css.
    /// </summary>
    public class PORDBasePage : Page
    {
        public string CurrentEnv  { get { return PORDHelper.Environment; } }
        public string CurrentUser { get { return PORDHelper.CurrentUser; } }
        public string EnvCssClass { get { return CurrentEnv.ToLowerInvariant(); } }

        /// <summary>False only on the token-authenticated reviewer page.</summary>
        protected virtual bool RequiresAdminAccess { get { return true; } }

        protected override void OnLoad(EventArgs e)
        {
            if (RequiresAdminAccess && !PORDHelper.HasAccess())
            {
                Response.Redirect("~/LPPI/LPPI_Info.aspx", true);
            }
            base.OnLoad(e);
        }

        protected override void OnError(EventArgs e)
        {
            Exception ex = Server.GetLastError();
            if (ex != null) ex = ex.GetBaseException();
            if (ex is System.Threading.ThreadAbortException) return;

            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"en-AU\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>PO Review</title></head>");
            sb.Append("<body style=\"margin:0;font-family:'Segoe UI',Arial,sans-serif;background:#f4f4f4;color:#222;\">");
            sb.Append("<div style=\"max-width:640px;margin:64px auto;background:#fff;border:1px solid #e0e0e0;border-top:4px solid #d75b07;border-radius:6px;padding:32px 36px;\">");
            sb.Append("<div style=\"font-size:13px;letter-spacing:.04em;text-transform:uppercase;color:#d75b07;font-weight:700;margin-bottom:8px;\">PO Review</div>");
            sb.Append("<h1 style=\"font-size:22px;line-height:1.3;margin:0 0 14px;\">Something went wrong</h1>");
            sb.Append("<p style=\"font-size:15px;line-height:1.6;margin:0 0 18px;color:#444;\">The page could not be loaded because of an unexpected error. Please try again. If it keeps happening, let support know.</p>");
            if (RequiresAdminAccess && ex != null)
            {
                sb.Append("<p style=\"font-size:13px;color:#777;\">Details: ").Append(HttpUtility.HtmlEncode(ex.Message)).Append("</p>");
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
        /// Standard header. Nav follows the LPPI order and naming so admins
        /// who run both modules find things in the same place:
        ///   dashboard, help, load, sendouts, asfin, reasons, exclusions, outcomes
        /// </summary>
        public string RenderHeader(string active)
        {
            var nav = new[]
            {
                new { Key = "dashboard",  Label = "Dashboard",      Url = "PORD_Admin.aspx" },
                new { Key = "help",       Label = "Help",           Url = "PORD_Help.aspx" },
                new { Key = "load",       Label = "Load file",      Url = "PORD_Load.aspx" },
                new { Key = "sendouts",   Label = "Send-outs",      Url = "PORD_SendOuts.aspx" },
                new { Key = "asfin",      Label = "AS Fin groups",  Url = "PORD_AsFin.aspx" },
                new { Key = "reasons",    Label = "Reason codes",   Url = "PORD_ReasonCodes.aspx" },
                new { Key = "exclusions", Label = "Exclusions",     Url = "PORD_Exclusions.aspx" },
                new { Key = "outcomes",   Label = "Outcomes",       Url = "PORD_Outcomes.aspx" }
            };

            string supportTo = PORDHelper.SupportMailbox;
            var sb = new StringBuilder();
            sb.Append("<header class=\"lppi-header pord-header\">");
            sb.Append("<a href=\"PORD_Admin.aspx\" class=\"lppi-brand\">");
            sb.Append("<span class=\"mark\"><svg viewBox=\"0 0 24 24\"><path d=\"M6 2h9l5 5v13a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z\"/><path d=\"M14 2v6h6\"/><path d=\"M8 13h5M8 17h3\"/><circle cx=\"16.5\" cy=\"16.5\" r=\"2.5\"/></svg></span>");
            sb.Append("<span class=\"lppi-brand-text\">");
            sb.Append("<span class=\"lppi-brand-title\">PO Review</span>");
            sb.Append("<span class=\"lppi-brand-subtitle\">Purchase order compliance</span>");
            sb.Append("</span></a>");

            sb.Append("<nav class=\"lppi-nav\">");
            foreach (var n in nav)
            {
                sb.Append("<a href=\"").Append(n.Url).Append("\"").Append(n.Key == active ? " class=\"active\"" : "").Append(">")
                  .Append(HttpUtility.HtmlEncode(n.Label)).Append("</a>");
            }
            sb.Append("</nav>");

            sb.Append("<div class=\"lppi-header-right\">");
            if (PORDHelper.DemoMode) sb.Append("<span class=\"pord-demo-chip\" title=\"Running on in-memory demonstration data\">Demo data</span>");
            sb.Append("<span class=\"env-chip ").Append(HttpUtility.HtmlAttributeEncode(EnvCssClass)).Append("\">")
              .Append(HttpUtility.HtmlEncode(CurrentEnv)).Append("</span>");
            sb.Append("<span class=\"lppi-user\">").Append(HttpUtility.HtmlEncode(CurrentUser)).Append("</span>");
            if (!string.IsNullOrEmpty(supportTo))
            {
                sb.Append("<a href=\"mailto:").Append(HttpUtility.HtmlAttributeEncode(supportTo))
                  .Append("?subject=").Append(HttpUtility.HtmlAttributeEncode(Uri.EscapeDataString("PO Review — Feedback & Support")))
                  .Append("\" class=\"btn btn-sm btn-ghost lppi-support-btn\">Feedback &amp; support</a>");
            }
            sb.Append("</div></header>");
            return sb.ToString();
        }

        public string RenderFooter()
        {
            return "<footer class=\"lppi-footer\">Defence Finance Group · PO Review · Financial Operations Compliance Program · "
                 + HttpUtility.HtmlEncode(CurrentEnv) + "</footer>";
        }

        /// <summary>Prominent notice on every admin page while demo data is active.</summary>
        public string RenderDemoNotice()
        {
            if (!PORDHelper.DemoMode) return "";
            return "<div class=\"alert info pord-demo-notice\"><strong>Demonstration build.</strong>&nbsp;"
                 + "Figures, suppliers and people are fictional. Changes are held in memory and shared by everyone viewing this demo; "
                 + "no emails are sent and nothing is written to the database.</div>";
        }
    }
}
