<%@ WebHandler Language="C#" Class="CPlatform.LPPI.LPPI_Review_Unfinalise" %>

using System;
using System.Data;
using System.Globalization;
using System.Text;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Unfinalise endpoint for the reviewer page.
    ///
    /// Triggered by the Unfinalise button on the reviewer page toolbar
    /// when the package is Finalised. Mirror of LPPI_Review_Finalise.ashx.
    ///
    /// Wipes the auto-applied RC-NR ("no response") reviews, clears
    /// FinalisedDate / FinalisedBy, and flips status back to InReview.
    /// History rows are written for every wiped review with
    /// ReasonCodeID = NULL so the audit trail captures the rollback.
    ///
    /// AUTHENTICATION
    ///   Token-auth via the package Token. No Windows-identity gate; the
    ///   token-holder population (the AS Fin team for that program) is
    ///   trusted to manage their own review state.
    ///
    ///   Refused server-side if the package is Exported (terminal). The
    ///   button is also hidden client-side in that state, but this is the
    ///   authoritative check.
    ///
    /// May 2026 — POC tokens refused
    /// -------------------------------------------------------------------
    /// Like Finalise, Unfinalise is an AS Fin-only action. POC tokens are
    /// recognised by LPPIHelper.ResolveReviewToken and rejected here. The
    /// reviewer page itself does not render the Unfinalise button in POC
    /// view (ShowActionButton is gated on !IsPocView); this is the
    /// authoritative server-side guard.
    ///
    /// POSTED FORM FIELDS
    ///   token   the package or POC token (required)
    ///   action  "unfinalise" (sanity check)
    ///
    /// RESPONSE
    ///   {
    ///     "ok":            true | false,
    ///     "error":         "...",                  // when ok=false
    ///     "packageStatus": "InReview",             // current status after the call
    ///     "autoCleared":   3                       // # RC-NR rows wiped
    ///   }
    /// </summary>
    public class LPPI_Review_Unfinalise : IHttpHandler
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
                    Write(ctx, false, "Missing token.", null, 0);
                    return;
                }

                // Resolve the token. Both AS Fin and POC tokens land here,
                // but only AS Fin is allowed to unfinalise.
                LPPIHelper.ReviewTokenInfo tokenInfo = LPPIHelper.ResolveReviewToken(token);
                if (tokenInfo.Kind == LPPIHelper.ReviewTokenKind.None)
                {
                    Write(ctx, false, "Invalid link.", null, 0);
                    return;
                }

                if (tokenInfo.Kind == LPPIHelper.ReviewTokenKind.Poc)
                {
                    Write(ctx, false,
                        "Reopening the package is AS Fin's responsibility — only the AS Fin link can unfinalise.",
                        null, 0);
                    return;
                }

                int packageId = tokenInfo.PackageID;

                // Run the unfinalise transaction. Refuses unless status is
                // Finalised; refuses if package is Exported.
                var res = LPPIHelper.UnfinalisePackage(packageId);

                // Re-read current status for the response.
                object stObj = LPPIHelper.ExecuteScalar(
                    "SELECT Status FROM dbo.tblLPPI_ReviewPackages WHERE PackageID = @p",
                    LPPIHelper.P("@p", packageId));
                string status = stObj == null ? null : Convert.ToString(stObj);

                Write(ctx,
                      res.Success,
                      res.Success ? null : res.ErrorMessage,
                      status,
                      res.AutoClearedCount);
            }
            catch (Exception ex)
            {
                Write(ctx, false, "Server error: " + ex.Message, null, 0);
            }
        }

        // -------------------------------------------------------------------
        // Minimal hand-rolled JSON writer — matches LPPI_Review_Save.ashx
        // and LPPI_Review_Finalise.ashx style.
        // -------------------------------------------------------------------
        private static void Write(HttpContext ctx, bool ok, string error, string status, int autoCleared)
        {
            var sb = new StringBuilder(96);
            sb.Append("{\"ok\":").Append(ok ? "true" : "false");
            if (!string.IsNullOrEmpty(error))
                sb.Append(",\"error\":").Append(JsonStr(error));
            if (!string.IsNullOrEmpty(status))
                sb.Append(",\"packageStatus\":").Append(JsonStr(status));
            sb.Append(",\"autoCleared\":").Append(autoCleared.ToString(CultureInfo.InvariantCulture));
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
