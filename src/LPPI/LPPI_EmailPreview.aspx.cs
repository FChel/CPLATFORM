using System;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Streams the rendered HTML email body directly to the browser.
    /// Loaded inside the preview modal iframe on LPPI_SendOuts.aspx.
    ///
    /// Query string:
    ///   id   — PackageID (required)
    ///   type — "Initial" or "Reminder" (optional, defaults to Initial)
    /// </summary>
    public partial class LPPI_EmailPreview : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            // ?cm=<CmID> — preview for a group with no open package yet
            string cmParam = Request.QueryString["cm"];
            if (!string.IsNullOrEmpty(cmParam))
            {
                int cmId;
                if (!int.TryParse(cmParam, out cmId)) { WriteError("Invalid CM ID."); return; }
                string html = LPPIEmail.BuildEmailHtmlByCm(cmId);
                WriteHtml(html);
                return;
            }

            // ?id=<PackageID>&type=Initial|Reminder
            int packageId;
            if (!int.TryParse(Request.QueryString["id"], out packageId))
            {
                WriteError("Invalid or missing package ID.");
                return;
            }

            string type = (Request.QueryString["type"] ?? "Initial").Trim();
            if (!type.Equals("Initial", StringComparison.OrdinalIgnoreCase) &&
                !type.Equals("Reminder", StringComparison.OrdinalIgnoreCase))
            {
                type = "Initial";
            }

            WriteHtml(LPPIEmail.BuildEmailHtml(packageId, type));
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
