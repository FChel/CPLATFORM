using System;
using System.Data.OleDb;
using System.Text;
using System.Web;
using System.Web.UI;

namespace CPlatform.NORM
{
    /// <summary>Common identity gate and safe error presentation for NORM pages.</summary>
    public class NORMBasePage : Page
    {
        private bool accessDenied;

        protected virtual bool RequiresPrepareAccess { get { return true; } }
        protected virtual bool RequiresAdministratorAccess { get { return false; } }

        public string CurrentEnvironment { get { return NORMHelper.Environment; } }
        public string CurrentUser { get { return NORMHelper.CurrentUserDisplayName(); } }
        public bool IsAdministrator { get { return NORMHelper.HasAdminAccess(); } }

        protected override void OnLoad(EventArgs e)
        {
            if (RequiresAdministratorAccess && !NORMHelper.HasAdminAccess())
            {
                DenyAccess("You do not have administrator access to NORM.");
                return;
            }
            if (RequiresPrepareAccess && !NORMHelper.HasPrepareAccess())
            {
                DenyAccess("You do not have preparer access to NORM.");
                return;
            }
            base.OnLoad(e);
        }

        private void DenyAccess(string message)
        {
            accessDenied = true;
            Response.StatusCode = 403;
            Response.TrySkipIisCustomErrors = true;
            Response.ContentType = "text/plain; charset=utf-8";
            Response.Write(message);
            Context.ApplicationInstance.CompleteRequest();
        }

        protected override void Render(HtmlTextWriter writer)
        {
            // CompleteRequest skips later pipeline events but does not suppress
            // WebForms rendering. Without this guard, the denied page HTML is
            // appended after the plain-text 403 message and can show misleading
            // uninitialised state such as "database objects are not installed".
            if (!accessDenied) { base.Render(writer); }
        }

        protected override void OnError(EventArgs e)
        {
            Exception error = Server.GetLastError();
            if (error is System.Threading.ThreadAbortException) { return; }

            string title = NORMErrorHandling.IsNarrativeSave(Context) ? "Narrative save result" :
                error is OleDbException ? "The database request failed" : "NORM could not complete the request";
            string detail = NORMErrorHandling.UserMessage(error, Context, "Request");
            int failureStatus = Response.StatusCode;
            if (Response.HeadersWritten)
            {
                Response.SuppressContent = true;
                Server.ClearError();
                Context.ApplicationInstance.CompleteRequest();
                return;
            }

            StringBuilder html = new StringBuilder();
            html.Append("<!doctype html><html lang=\"en-AU\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            html.Append("<title>NORM</title><link rel=\"stylesheet\" href=\"");
            html.Append(ResolveUrl("~/css/norm.css"));
            html.Append("\"></head><body class=\"norm-page\"><main class=\"norm-error\"><div class=\"norm-kicker\">NORM</div><h1>");
            html.Append(HttpUtility.HtmlEncode(title));
            html.Append("</h1><p>").Append(HttpUtility.HtmlEncode(detail)).Append("</p><a class=\"norm-button\" href=\"");
            int narrativeRelease = NORMErrorHandling.NarrativeEditorRelease(Context);
            html.Append(HttpUtility.HtmlAttributeEncode(ResolveUrl(narrativeRelease > 0
                ? "~/NORM/NORM_Reporting.aspx?narratives=1&release=" + narrativeRelease.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "~/NORM/NORM_Statements.aspx")));
            html.Append(narrativeRelease > 0 ? "\">Return to narrative editor" : "\">Return to statements");
            html.Append("</a></main></body></html>");

            Response.Clear();
            Response.StatusCode = failureStatus;
            Response.TrySkipIisCustomErrors = true;
            Response.ContentType = "text/html; charset=utf-8";
            Response.Write(html.ToString());
            Server.ClearError();
            Response.End();
        }
    }
}
