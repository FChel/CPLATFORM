using System;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Streams the rendered HTML email body directly to the browser.
    /// Loaded inside the preview modal iframe on LPPI_SendOuts.aspx.
    ///
    /// Query string:
    ///   id        — PackageID (required for the package preview path)
    ///   type      — "Initial" or "Reminder" (optional, defaults to Initial)
    ///   audience  — "asfin" (default) or "poc"
    ///   poc       — optional POC email when audience=poc; if omitted, the
    ///               POC template is rendered with placeholder values so the
    ///               operator can see the template shape without picking a
    ///               specific POC. Real per-POC sends always use real data.
    ///   kind      — "notify" renders the Notify AS Fin courtesy email for a
    ///               Finalised package (id required). Mutually exclusive with
    ///               type/audience/poc, which are ignored on the notify path.
    ///
    ///   cm        — alternate path for previewing for a CM with no package
    ///               yet. Mutually exclusive with id; AS Fin template only.
    /// </summary>
    public partial class LPPI_EmailPreview : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            // ?cm=<CmID> — preview for a group with no open package yet.
            string cmParam = Request.QueryString["cm"];
            if (!string.IsNullOrEmpty(cmParam))
            {
                int cmId;
                if (!int.TryParse(cmParam, out cmId)) { WriteError("Invalid CM ID."); return; }
                string html = LPPIEmail.BuildEmailHtmlByCm(cmId);
                WriteHtml(html);
                return;
            }

            // ?id=<PackageID>&type=Initial|Reminder&audience=asfin|poc[&poc=email]
            // or ?id=<PackageID>&kind=notify for the Notify AS Fin preview.
            int packageId;
            if (!int.TryParse(Request.QueryString["id"], out packageId))
            {
                WriteError("Invalid or missing package ID.");
                return;
            }

            // ?kind=notify — Notify AS Fin courtesy email (Finalised packages).
            // Side-effect-free preview; renders from the same builders as the
            // real send so the two cannot drift.
            string kind = (Request.QueryString["kind"] ?? "").Trim();
            if (kind.Equals("notify", StringComparison.OrdinalIgnoreCase))
            {
                WriteHtml(LPPIEmail.BuildNotifyEmailHtml(packageId));
                return;
            }

            string type = (Request.QueryString["type"] ?? "Initial").Trim();
            if (!type.Equals("Initial", StringComparison.OrdinalIgnoreCase) &&
                !type.Equals("Reminder", StringComparison.OrdinalIgnoreCase))
            {
                type = "Initial";
            }

            string audience = (Request.QueryString["audience"] ?? "asfin").Trim();
            if (!audience.Equals("asfin", StringComparison.OrdinalIgnoreCase) &&
                !audience.Equals("poc",   StringComparison.OrdinalIgnoreCase))
            {
                audience = "asfin";
            }

            string pocEmail = Request.QueryString["poc"];
            // null/blank pocEmail with audience=poc is the placeholder path —
            // BuildEmailHtml handles it and renders with <POC_EMAIL> in place
            // of a real address.

            WriteHtml(LPPIEmail.BuildEmailHtml(packageId, type, audience, pocEmail));
        }

        private void WriteHtml(string html)
        {
            Response.Clear();
            Response.ContentType = "text/html";
            Response.ContentEncoding = System.Text.Encoding.UTF8;
            Response.Write(html);
            Response.End();
        }

        private void WriteError(string message)
        {
            Response.Clear();
            Response.ContentType = "text/html";
            Response.Write("<html><body style=\"font-family:Arial,sans-serif;padding:24px;color:#b45309;\">");
            Response.Write("<p>" + HttpUtility.HtmlEncode(message) + "</p>");
            Response.Write("</body></html>");
            Response.End();
        }
    }
}