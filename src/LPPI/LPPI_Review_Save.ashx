<%@ WebHandler Language="C#" Class="CPlatform.LPPI.LPPI_Review_Save" %>

using System;
using System.Data;
using System.Text;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Save handler for the reviewer page. Authoritative gate for write
    /// access — only Complete and Cancelled packages reject writes. NotSent,
    /// Sent and InReview all accept writes.
    ///
    /// Side effects on a successful save:
    ///   1. If the package was Sent, flip to InReview (first save by reviewer).
    ///      Editing a NotSent package does NOT flip its status — the package
    ///      only becomes Sent when the operator hits Send on the Send-outs
    ///      page, which stamps SentDate at the same time.
    ///   2. If every document in the package now has a non-null ReasonCodeID,
    ///      flip to Complete and stamp ClosedDate. This only fires for
    ///      InReview, so a fully-coded NotSent package stays NotSent until
    ///      the operator sends it.
    /// Both transitions are guarded by the matching prior status so concurrent
    /// saves cannot overshoot or regress the lifecycle.
    /// </summary>
    public class LPPI_Review_Save : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext ctx)
        {
            ctx.Response.ContentType = "application/json";
            ctx.Response.Cache.SetCacheability(HttpCacheability.NoCache);

            try
            {
                string token  = (ctx.Request.Form["token"]  ?? "").Trim();
                string action = (ctx.Request.Form["action"] ?? "").Trim();

                if (token.Length == 0) { Write(ctx, false, "Missing token.", null); return; }

                DataTable pkg = LPPIHelper.ExecuteTable(
                    "SELECT PackageID, Status FROM tblLPPI_ReviewPackages WHERE Token = @t",
                    LPPIHelper.P("@t", token));
                if (pkg.Rows.Count != 1) { Write(ctx, false, "Invalid link.", null); return; }

                string status = Convert.ToString(pkg.Rows[0]["Status"]);
                bool readOnly =
                    string.Equals(status, "Complete",  StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);
                if (readOnly)
                {
                    Write(ctx, false,
                        "This review package is closed (status: " + status + ") and cannot be changed.",
                        null);
                    return;
                }
                int packageId = Convert.ToInt32(pkg.Rows[0]["PackageID"]);

                // docNo (DocNoAccounting) is the sole document key posted by the client.
                string docNo = (ctx.Request.Form["docNo"] ?? "").Trim();
                if (docNo.Length == 0) { Write(ctx, false, "Missing docNo.", null); return; }

                // Resolve the first-line DocumentID using the package itself as the
                // authority — avoids the cross-batch bug where MIN(DocumentID) across
                // ALL batches returns an older row that is not in this package.
                //
                // This single query both confirms membership AND returns the correct id.
                object flObj = LPPIHelper.ExecuteScalar(@"
                    SELECT pd.DocumentID
                    FROM   tblLPPI_ReviewPackageDocuments pd
                    INNER JOIN tblLPPI_Documents d ON d.DocumentID = pd.DocumentID
                    WHERE  pd.PackageID     = @p
                    AND    d.DocNoAccounting = @dn",
                    LPPIHelper.P("@p",  packageId),
                    LPPIHelper.P("@dn", docNo));

                if (flObj == null || flObj == DBNull.Value)
                {
                    Write(ctx, false, "Document is not in this package.", null); return;
                }
                int firstLineDocId = Convert.ToInt32(flObj);

                // Parse review fields.
                // Field name posted by JS is "reasonCodeId" — matches here.
                int? reasonId = null;
                string reasonRaw = (ctx.Request.Form["reasonCodeId"] ?? "").Trim();
                if (reasonRaw.Length > 0)
                {
                    int v;
                    if (int.TryParse(reasonRaw, out v) && v > 0) reasonId = v;
                }
                string comments = (ctx.Request.Form["comments"] ?? "").Trim();
                string objref   = (ctx.Request.Form["objref"]   ?? "").Trim();

                // Mandatory-field validation (server-side — mirrors client rules).
                if (reasonId.HasValue)
                {
                    DataTable rcRow = LPPIHelper.ExecuteTable(
                        "SELECT Outcome, RequiresComments FROM tblLPPI_ReasonCodes WHERE ReasonCodeID = @r",
                        LPPIHelper.P("@r", reasonId.Value));
                    if (rcRow.Rows.Count == 1)
                    {
                        string outcome  = Convert.ToString(rcRow.Rows[0]["Outcome"]);
                        bool   requires = Convert.ToBoolean(rcRow.Rows[0]["RequiresComments"]);
                        bool   notPay   = string.Equals(outcome, "NotPayable", StringComparison.OrdinalIgnoreCase);

                        if (requires && comments.Length == 0)
                        {
                            Write(ctx, false, "Comments are required for this reason code.", null); return;
                        }
                        if (notPay && comments.Length == 0)
                        {
                            Write(ctx, false, "Not-Payable requires both a Comment and an Objective Reference.", null); return;
                        }
                        if (notPay && objref.Length == 0)
                        {
                            Write(ctx, false, "Objective Reference is required when the outcome is Not Payable.", null); return;
                        }
                    }
                }

                // MERGE on the first-line DocumentID.
                LPPIHelper.ExecuteNonQuery(@"
                    MERGE tblLPPI_Reviews AS tgt
                    USING (SELECT @d AS DocumentID) AS src
                       ON tgt.DocumentID = src.DocumentID
                    WHEN MATCHED THEN UPDATE SET
                        ReasonCodeID       = @rc,
                        Comments           = @cm,
                        ObjectiveReference = @obj,
                        ReviewedByUserId   = @uid,
                        ReviewedByName     = @uname,
                        ReviewedDate       = SYSDATETIME()
                    WHEN NOT MATCHED THEN INSERT
                        (DocumentID, ReasonCodeID, Comments, ObjectiveReference,
                         ReviewedByUserId, ReviewedByName, ReviewedDate, IsFinal)
                        VALUES (@d, @rc, @cm, @obj, @uid, @uname, SYSDATETIME(), 0);",
                    LPPIHelper.P("@d",     firstLineDocId),
                    LPPIHelper.P("@rc",    (object)reasonId ?? DBNull.Value),
                    LPPIHelper.P("@cm",    comments),
                    LPPIHelper.P("@obj",   objref),
                    LPPIHelper.P("@uid",   LPPIHelper.CurrentUserId()),
                    LPPIHelper.P("@uname", LPPIHelper.CurrentUserDisplayName()));

                // ------------------------------------------------------------
                // Lifecycle transitions
                // ------------------------------------------------------------

                // (1) First save by reviewer flips Sent -> InReview.
                //     The WHERE Status = 'Sent' guard ensures editing a NotSent
                //     package will NOT trip this update — only an actual Sent
                //     package will match. Concurrent saves on the same Sent
                //     package can each safely fire this — only the first one
                //     will match.
                LPPIHelper.ExecuteNonQuery(@"
                    UPDATE tblLPPI_ReviewPackages
                       SET Status = 'InReview'
                     WHERE PackageID = @p
                       AND Status   = 'Sent';",
                    LPPIHelper.P("@p", packageId));

                // (2) If every doc in the package now has a non-null reason
                //     code, flip InReview -> Complete and stamp ClosedDate.
                //     The WHERE Status = 'InReview' guard ensures a fully-
                //     coded NotSent package stays NotSent — it does not
                //     auto-complete before the operator has had a chance to
                //     send it.
                LPPIHelper.ExecuteNonQuery(@"
                    UPDATE tblLPPI_ReviewPackages
                       SET Status     = 'Complete',
                           ClosedDate = SYSDATETIME()
                     WHERE PackageID = @p
                       AND Status   = 'InReview'
                       AND NOT EXISTS (
                           SELECT 1
                             FROM tblLPPI_ReviewPackageDocuments pd
                             LEFT JOIN tblLPPI_Reviews r ON r.DocumentID = pd.DocumentID
                            WHERE pd.PackageID = @p
                              AND (r.ReasonCodeID IS NULL)
                       );",
                    LPPIHelper.P("@p", packageId));

                Write(ctx, true, null, docNo);
            }
            catch (Exception ex)
            {
                Write(ctx, false, "Server error: " + ex.Message, null);
            }
        }

        private void Write(HttpContext ctx, bool ok, string err, string docNo)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ok\":").Append(ok ? "true" : "false");
            if (!string.IsNullOrEmpty(err))
            {
                sb.Append(",\"error\":\"").Append(JsEscape(err)).Append("\"");
            }
            if (!string.IsNullOrEmpty(docNo))
            {
                sb.Append(",\"docNo\":\"").Append(JsEscape(docNo)).Append("\"");
            }
            sb.Append("}");
            ctx.Response.Write(sb.ToString());
        }

        private static string JsEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:X4}", (int)c);
                        else          sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
