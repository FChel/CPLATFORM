<%@ WebHandler Language="C#" Class="CPlatform.PORD.PORD_Review_Finalise" %>

using System;
using System.Web;

namespace CPlatform.PORD
{
    /// <summary>
    /// Finalise / reopen a package from the reviewer page. AS Fin tokens only —
    /// POC tokens are refused (a PO contact never closes the package).
    /// Posted form: token, action = "finalise" | "unfinalise".
    /// Response: { ok, error, autoApplied }
    /// </summary>
    public class PORD_Review_Finalise : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext ctx)
        {
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            if (!string.Equals(ctx.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 405;
                ctx.Response.Write("{\"ok\":false,\"error\":\"POST only\"}");
                return;
            }

            var store = PORDHelper.Store;
            int packageId; string pocEmail;
            var kind = store.ResolveToken(ctx.Request.Form["token"], out packageId, out pocEmail);
            if (kind != PordTokenKind.AsFin)
            {
                ctx.Response.Write("{\"ok\":false,\"error\":" + PORDHelper.Js("Only the AS Fin link can finalise this package.") + "}");
                return;
            }

            string user = PORDHelper.CurrentUser, error;
            bool ok; int auto = 0;
            if (ctx.Request.Form["action"] == "unfinalise") ok = store.Unfinalise(packageId, user, out error);
            else ok = store.Finalise(packageId, user, out auto, out error);

            ctx.Response.Write("{\"ok\":" + (ok ? "true" : "false") + ",\"error\":" + PORDHelper.Js(error) + ",\"autoApplied\":" + auto + "}");
        }
    }
}
