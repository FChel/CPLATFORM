using System;
using System.Web.UI;
using Prepayment.Web.Services;

namespace Prepayment.Web
{
    public partial class PPMDefault : Page
    {
        /// <summary>
        /// The authenticated Windows user (e.g. "DOMAIN\jharrison"), supplied by
        /// Windows Authentication and shown in the header. With &lt;deny users="?"/&gt;
        /// the page is never reached anonymously, so Identity is always authenticated.
        /// </summary>
        protected string PPMCurrentUserName;
        protected bool PPMIsAdmin;

        protected void Page_Load(object sender, EventArgs e)
        {
            // Windows Authentication populates User.Identity with the caller's account.
            PPMCurrentUserName = User.Identity.Name;
            PPMIsAdmin = string.Equals(PPMCurrentUser.ResolveRole(Context), "Admin", StringComparison.OrdinalIgnoreCase);
            ucAdminControlTower.Visible = PPMIsAdmin;
            ucImportData.Visible = PPMIsAdmin;

            // ── AJAX partial render ─────────────────────────────────────────────
            // The dashboard refreshes a tab by fetching ONLY that tab's control HTML
            // (Default.aspx?render=<pane>) and swapping it into the pane client-side. This
            // keeps every tab's data fresh from the DB on each switch without a full reload.
            // We render in PreRender (below) so the child control's own Page_Load — which loads
            // its data — has already run.
            //
            // Every tab (1–7) is now a self-contained DB-backed user control that loads its own
            // data in its Page_Load, so there is nothing to bind at the page level.
        }

        protected void Page_PreRender(object sender, EventArgs e)
        {
            string render = Request.QueryString["render"];
            if (!string.IsNullOrEmpty(render))
            {
                RenderPanePartial(render);
            }
        }

        /// <summary>
        /// Renders a single tab's user control and writes just that HTML to the response, so the
        /// client can AJAX-swap it into the pane. Each control loads its own data in its Page_Load,
        /// so the rendered fragment is always current.
        /// </summary>
        private void RenderPanePartial(string pane)
        {
            System.Web.UI.Control control;
            switch ((pane ?? "").ToLowerInvariant())
            {
                case "poidentification": control = ucPoIdentification;   break;
                case "amortisation":     control = ucAmortisationSetup;  break;
                case "journal":          control = ucJournalGeneration;  break;
                case "admin":
                    if (!PPMIsAdmin)
                    {
                        Response.StatusCode = 403;
                        Response.Write("Administrator access is required.");
                        Response.End();
                        return;
                    }
                    control = ucAdminControlTower;
                    break;
                case "groupworkflow":    control = ucGroupWorkflow;      break;
                case "glreconciliation": control = ucGlReconciliation;    break;
                case "report":           control = ucPrepaymentReport;   break;
                default:
                    Response.StatusCode = 400;
                    Response.Write("Unknown pane '" + Server.HtmlEncode(pane) + "'.");
                    Response.End();
                    return;
            }

            // The control's own Page_Load already ran and loaded its data. Render it to the output.
            Response.Clear();
            Response.ContentType = "text/html; charset=utf-8";
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            using (var sw = new System.IO.StringWriter())
            using (var htw = new System.Web.UI.HtmlTextWriter(sw))
            {
                control.RenderControl(htw);
                Response.Write(sw.ToString());
            }
            Response.End();
        }
    }
}
