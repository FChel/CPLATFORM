using System;
using System.Linq;

namespace CPlatform.PORD
{
    /// <summary>
    /// Streams a rendered notification email into the Send-outs preview iframe.
    /// Query: pkg (PackageID), aud = asfin | poc, poc (POC email when aud=poc).
    /// Admin-gated by PORDBasePage.
    /// </summary>
    public partial class PORD_EmailPreview : PORDBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            var store = PORDHelper.Store;
            int id;
            int.TryParse(Request.QueryString["pkg"], out id);
            var pkg = store.GetPackage(id);
            if (pkg == null) { Write("<p style=\"font-family:Segoe UI,Arial,sans-serif;padding:24px;\">Package not found.</p>"); return; }

            var pos = store.GetPos(id);
            var pocs = store.GetPocs(id);
            // Links in the email point back at this server. In demo mode use the
            // current host so previews work wherever the demo is running.
            string here = Request.Url.GetLeftPart(UriPartial.Authority) + Request.ApplicationPath.TrimEnd('/') + "/";
            string baseUrl = PORDHelper.DemoMode ? here : PORDHelper.Setting("PORD.BaseUrl", LPPI.LPPIHelper.Setting("LPPI.BaseUrl", here));
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            bool reminder = pkg.Status == PordStatus.Sent || pkg.Status == PordStatus.InReview;

            if (Request.QueryString["aud"] == "poc")
            {
                string email = Request.QueryString["poc"];
                var poc = pocs.FirstOrDefault(p => string.Equals(p.PocEmail, email, StringComparison.OrdinalIgnoreCase)) ?? pocs.FirstOrDefault();
                if (poc == null) { Write("<p>No PO contacts in this package.</p>"); return; }
                Write(PORDEmail.BuildPoc(pkg, poc, pos, baseUrl, reminder));
            }
            else
            {
                Write(PORDEmail.BuildAsFin(pkg, pos, pocs, baseUrl, reminder));
            }
        }

        private void Write(string html)
        {
            Response.Clear();
            Response.ContentType = "text/html; charset=utf-8";
            Response.Write(html);
            Response.End();
        }
    }
}
