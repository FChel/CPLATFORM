<%@ WebHandler Language="C#" Class="CPlatform.PORD.PORD_Review_Save" %>

using System;
using System.Collections.Generic;
using System.Text;
using System.Web;

namespace CPlatform.PORD
{
    /// <summary>
    /// Batch save for the reviewer page. Same contract as LPPI_Review_Save:
    /// every dirty row is posted in one request; each row succeeds or fails
    /// independently (validation, stale version, out of scope) and gets its
    /// own result. POC tokens may only save their own POs.
    ///
    /// Posted form: token, rowCount, rows[i].poId, rows[i].response,
    ///   rows[i].reason, rows[i].target (yyyy-MM-dd), rows[i].evidence,
    ///   rows[i].comments, rows[i].version
    /// Response: { ok, error, packageStatus, results:[{poId, ok, code, message, version}] }
    /// </summary>
    public class PORD_Review_Save : IHttpHandler
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
            if (kind == PordTokenKind.None)
            {
                ctx.Response.Write("{\"ok\":false,\"error\":" + PORDHelper.Js("This review link is no longer valid.") + "}");
                return;
            }

            int n;
            int.TryParse(ctx.Request.Form["rowCount"], out n);
            n = Math.Max(0, Math.Min(n, 2000));
            var rows = new List<PordSaveRow>();
            for (int i = 0; i < n; i++)
            {
                string pfx = "rows[" + i + "].";
                int id;
                if (!int.TryParse(ctx.Request.Form[pfx + "poId"], out id)) continue;
                rows.Add(new PordSaveRow
                {
                    PoID        = id,
                    Response    = Clip(ctx.Request.Form[pfx + "response"], 20),
                    ReasonCode  = Clip(ctx.Request.Form[pfx + "reason"], 20),
                    TargetDate  = Clip(ctx.Request.Form[pfx + "target"], 10),
                    EvidenceRef = Clip(ctx.Request.Form[pfx + "evidence"], 100),
                    Comments    = Clip(ctx.Request.Form[pfx + "comments"], 1000),
                    Version     = Clip(ctx.Request.Form[pfx + "version"], 40)
                });
            }

            string status;
            string user = PORDHelper.CurrentUser;
            var results = store.Save(packageId, kind == PordTokenKind.Poc ? pocEmail : null, rows, user, out status);

            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"packageStatus\":").Append(PORDHelper.Js(status)).Append(",\"results\":[");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"poId\":").Append(r.PoID)
                  .Append(",\"ok\":").Append(r.Ok ? "true" : "false")
                  .Append(",\"code\":").Append(PORDHelper.Js(r.ErrorCode))
                  .Append(",\"message\":").Append(PORDHelper.Js(r.Message))
                  .Append(",\"version\":").Append(PORDHelper.Js(r.Version))
                  .Append('}');
            }
            sb.Append("]}");
            ctx.Response.Write(sb.ToString());
        }

        private static string Clip(string s, int max)
        {
            if (s == null) return null;
            return s.Length > max ? s.Substring(0, max) : s;
        }
    }
}
