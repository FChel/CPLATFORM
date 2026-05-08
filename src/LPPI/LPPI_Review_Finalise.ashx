<%@ WebHandler Language="C#" Class="CPlatform.LPPI.LPPI_Review_Finalise" %>

using System;
using System.Data;
using System.Globalization;
using System.Text;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Finalise endpoint for the reviewer page.
    ///
    /// Triggered by the Finalise button on the reviewer page toolbar.
    /// Auto-applies reason code 'RC-NR' (Payable per RMG-417, no response
    /// from CM) to any undecided document, writes a history row for each,
    /// flips the package status to 'Finalised', and stamps FinalisedDate /
    /// FinalisedBy.
    ///
    /// AUTHENTICATION
    ///   Token-auth via the package Token form value. The reviewer page
    ///   lives outside the admin gate (it is reached via an unguessable
    ///   URL), and the Finalise button does the same. Per the design
    ///   discussion, IIS Windows authentication captures the user's
    ///   identity even on the anonymous reviewer page, and that identity
    ///   is recorded as FinalisedBy / ChangedByName so the audit trail
    ///   names the actual AS Fin person who clicked the button.
    ///
    ///   The button is gated on the client by a confirm dialog that
    ///   re-states the action and the default code. The client also
    ///   refuses to call this endpoint when the page is in read-only
    ///   mode (Finalised / Exported / Cancelled).
    ///
    /// May 2026 — POC tokens refused
    /// -------------------------------------------------------------------
    /// Finalise is an AS Fin-only action. POC tokens are recognised by
    /// LPPIHelper.ResolveReviewToken and rejected here with a clear
    /// error so the client can surface the right message. The reviewer
    /// page itself does not render the Finalise button in POC view
    /// (ShowActionButton is gated on !IsPocView) so a normal client
    /// never hits this branch — this is the authoritative server-side
    /// guard.
    ///
    /// POSTED FORM FIELDS
    ///   token   the package or POC token (required)
    ///   action  "finalise" (sanity check)
    ///
    /// RESPONSE
    ///   {
    ///     "ok":              true | false,
    ///     "error":           "...",                  // when ok=false
    ///     "packageStatus":   "Finalised",            // current status after the call
    ///     "autoApplied":     7,                      // # docs auto-coded RC-NR
    ///     "finalisedBy":     "Smith, Jane",
    ///     "finalisedDate":   "2026-05-06 14:32:11.123"
    ///   }
    ///
    /// All status guards (the package being in an editable state etc.)
    /// are enforced server-side in LPPIHelper.FinalisePackage. This
    /// handler is just a thin token-auth wrapper that translates the
    /// helper's result into JSON for the client.
    /// </summary>
    public class LPPI_Review_Finalise : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext ctx)
        {
            ctx.Response.ContentType = "application/json";
            ctx.Response.Cache.SetCacheability(HttpCacheability.NoCache);

            try
            {
                string token = (ctx.Request.Form["token"] ?? "").Trim();
                if (token.Length == 0)
                {
                    Write(ctx, false, "Missing token.", null, 0, null, null);
                    return;
                }

                // Resolve the token. Both AS Fin and POC tokens land here,
                // but only AS Fin is allowed to finalise.
                LPPIHelper.ReviewTokenInfo tokenInfo = LPPIHelper.ResolveReviewToken(token);
                if (tokenInfo.Kind == LPPIHelper.ReviewTokenKind.None)
                {
                    Write(ctx, false, "Invalid link.", null, 0, null, null);
                    return;
                }

                if (tokenInfo.Kind == LPPIHelper.ReviewTokenKind.Poc)
                {
                    Write(ctx, false,
                        "Finalising the package is AS Fin's responsibility — only the AS Fin link can finalise.",
                        null, 0, null, null);
                    return;
                }

                int packageId = tokenInfo.PackageID;

                // Run the finalise transaction. All status checks, history
                // writes and the status flip happen inside this call.
                var res = LPPIHelper.FinalisePackage(packageId);

                // Re-read the package's current status + finalised metadata
                // for the response. Even on failure we return whatever the
                // current status is so the client can refresh its UI.
                DataTable pkg = LPPIHelper.ExecuteTable(@"
                    SELECT Status, FinalisedDate, FinalisedBy
                      FROM dbo.tblLPPI_ReviewPackages
                     WHERE PackageID = @p",
                    LPPIHelper.P("@p", packageId));

                string   status        = null;
                string   finalisedBy   = null;
                string   finalisedDate = null;
                if (pkg.Rows.Count == 1)
                {
                    DataRow r = pkg.Rows[0];
                    status        = Convert.ToString(r["Status"]);
                    finalisedBy   = r["FinalisedBy"]   == DBNull.Value ? null : Convert.ToString(r["FinalisedBy"]);
                    if (r["FinalisedDate"] != DBNull.Value)
                    {
                        DateTime fd = Convert.ToDateTime(r["FinalisedDate"]);
                        finalisedDate = fd.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    }
                }

                Write(ctx,
                      res.Success,
                      res.Success ? null : res.ErrorMessage,
                      status,
                      res.AutoAppliedCount,
                      finalisedBy,
                      finalisedDate);
            }
            catch (Exception ex)
            {
                Write(ctx, false, "Server error: " + ex.Message, null, 0, null, null);
            }
        }

        // -------------------------------------------------------------------
        // Minimal hand-rolled JSON writer — matches LPPI_Review_Save.ashx
        // style. We avoid taking on a JSON dependency for two fields.
        // -------------------------------------------------------------------
        private static void Write(HttpContext ctx, bool ok, string error, string status,
                                  int autoApplied, string finalisedBy, string finalisedDate)
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"ok\":").Append(ok ? "true" : "false");
            if (!string.IsNullOrEmpty(error))
                sb.Append(",\"error\":").Append(JsonStr(error));
            if (!string.IsNullOrEmpty(status))
                sb.Append(",\"packageStatus\":").Append(JsonStr(status));
            sb.Append(",\"autoApplied\":").Append(autoApplied.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(finalisedBy))
                sb.Append(",\"finalisedBy\":").Append(JsonStr(finalisedBy));
            if (!string.IsNullOrEmpty(finalisedDate))
                sb.Append(",\"finalisedDate\":").Append(JsonStr(finalisedDate));
            sb.Append('}');
            ctx.Response.Write(sb.ToString());
        }

        private static string JsonStr(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 4);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
