<%@ WebHandler Language="C#" Class="CPlatform.LPPI.LPPI_Review_Save" %>

using System;
using System.Data;
using System.Text;
using System.Web;

namespace CPlatform.LPPI
{
    public class LPPI_Review_Save : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext ctx)
        {
            ctx.Response.ContentType = "application/json";
            ctx.Response.Cache.SetCacheability(HttpCacheability.NoCache);

            try
            {
                string token = (ctx.Request.Form["token"] ?? "").Trim();
                string action = (ctx.Request.Form["action"] ?? "").Trim();

                if (token.Length == 0) { Write(ctx, false, "Missing token."); return; }

                // Resolve package by token
                DataTable pkg = LPPIHelper.ExecuteTable(
                    "SELECT PackageID, Status FROM tblLPPI_ReviewPackages WHERE Token = @t",
                    LPPIHelper.P("@t", token));
                if (pkg.Rows.Count != 1) { Write(ctx, false, "Invalid link."); return; }
                if (!string.Equals(Convert.ToString(pkg.Rows[0]["Status"]), "Open", StringComparison.OrdinalIgnoreCase))
                {
                    Write(ctx, false, "This review package is closed."); return;
                }
                int packageId = Convert.ToInt32(pkg.Rows[0]["PackageID"]);

                if (action == "markFinal")
                {
                    LPPIHelper.ExecuteNonQuery(@"
                        UPDATE r
                        SET r.IsFinal = 1
                        FROM tblLPPI_Reviews r
                        INNER JOIN tblLPPI_ReviewPackageDocuments pd ON pd.DocumentID = r.DocumentID
                        WHERE pd.PackageID = @p",
                        LPPIHelper.P("@p", packageId));
                    Write(ctx, true, null);
                    return;
                }

                // Per-row save
                int docId;
                if (!int.TryParse(ctx.Request.Form["docId"], out docId)) { Write(ctx, false, "Missing docId."); return; }

                // Confirm the doc belongs to this package
                object belongs = LPPIHelper.ExecuteScalar(
                    "SELECT 1 FROM tblLPPI_ReviewPackageDocuments WHERE PackageID = @p AND DocumentID = @d",
                    LPPIHelper.P("@p", packageId),
                    LPPIHelper.P("@d", docId));
                if (belongs == null) { Write(ctx, false, "Document is not in this package."); return; }

                int? reasonId = null;
                string reasonRaw = ctx.Request.Form["reasonId"] ?? "";
                if (reasonRaw.Length > 0)
                {
                    int v;
                    if (int.TryParse(reasonRaw, out v) && v > 0) { reasonId = v; }
                }
                string comments = (ctx.Request.Form["comments"] ?? "").Trim();
                string objref   = (ctx.Request.Form["objref"]   ?? "").Trim();

                // -----------------------------------------------------------
                // Mandatory-field rules.
                //   1. RequiresComments flag (RC07 / RC16): Comments must be present
                //      regardless of outcome.
                //   2. NEW — Outcome = 'NotPayable': BOTH Comments AND
                //      ObjectiveReference are mandatory.
                // Fetch both flag + outcome in a single round-trip.
                // -----------------------------------------------------------
                if (reasonId.HasValue)
                {
                    DataTable rc = LPPIHelper.ExecuteTable(
                        "SELECT RequiresComments, Outcome FROM tblLPPI_ReasonCodes WHERE ReasonCodeID = @id",
                        LPPIHelper.P("@id", reasonId.Value));
                    if (rc.Rows.Count == 0) { Write(ctx, false, "Unknown reason code."); return; }

                    bool requiresComments = Convert.ToBoolean(rc.Rows[0]["RequiresComments"]);
                    string outcome = Convert.ToString(rc.Rows[0]["Outcome"]);
                    bool isNotPayable = string.Equals(outcome, "NotPayable", StringComparison.OrdinalIgnoreCase);

                    // Rule 1 — RequiresComments applies to any outcome
                    if (requiresComments && comments.Length == 0)
                    {
                        Write(ctx, false, "Comments are required for this reason code.");
                        return;
                    }

                    // Rule 2 — NotPayable needs BOTH fields. We call out whichever
                    // one (or both) is missing so the reviewer knows exactly what
                    // to fill in.
                    if (isNotPayable)
                    {
                        bool missingComments = comments.Length == 0;
                        bool missingObjRef   = objref.Length == 0;
                        if (missingComments && missingObjRef)
                        {
                            Write(ctx, false,
                                "For a Not-Payable outcome both Comments and Objective Reference are required.");
                            return;
                        }
                        if (missingComments)
                        {
                            Write(ctx, false, "Comments are required when the outcome is Not Payable.");
                            return;
                        }
                        if (missingObjRef)
                        {
                            Write(ctx, false, "Objective Reference is required when the outcome is Not Payable.");
                            return;
                        }
                    }
                }

                // Upsert the review row
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
                    LPPIHelper.P("@d",   docId),
                    LPPIHelper.P("@rc",  (object)reasonId ?? DBNull.Value),
                    LPPIHelper.P("@cm",  comments),
                    LPPIHelper.P("@obj", objref),
                    LPPIHelper.P("@uid", LPPIHelper.CurrentUserId()),
                    LPPIHelper.P("@uname", LPPIHelper.CurrentUserDisplayName()));

                Write(ctx, true, null);
            }
            catch (Exception ex)
            {
                Write(ctx, false, "Server error: " + ex.Message);
            }
        }

        private void Write(HttpContext ctx, bool ok, string err)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ok\":").Append(ok ? "true" : "false");
            if (!ok)
            {
                sb.Append(",\"error\":\"").Append(JsEscape(err ?? "")).Append("\"");
            }
            sb.Append("}");
            ctx.Response.Write(sb.ToString());
        }

        private static string JsEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
