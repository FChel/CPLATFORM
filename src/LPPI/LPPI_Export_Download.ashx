<%@ WebHandler Language="C#" Class="CPlatform.LPPI.LPPI_Export_Download" %>

using System;
using System.Data;
using System.Globalization;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Streams a previously-generated ERP payment file.
    ///
    /// Per-company files (current model) live in tblLPPI_ExportBatchFiles, one
    /// row per company code under an export batch — requested with
    /// ?f=ExportBatchFileID. Legacy single-file batches (generated before the
    /// per-company split) store their bytes on tblLPPI_ExportBatches and are
    /// requested with ?b=ExportBatchID.
    ///
    /// AUTHENTICATION
    ///   Admin-only. Validates the current Windows identity against the admin
    ///   allow-list before streaming. The file contains payment data —
    ///   vendors, GL accounts, dollar amounts — and must not be served to
    ///   unauthenticated callers.
    ///
    /// QUERY STRING
    ///   f = ExportBatchFileID (per-company file) — preferred
    ///   b = ExportBatchID     (legacy combined file) — fallback
    ///
    /// RESPONSE
    ///   200 — Content-Type: xlsx, body = file bytes.
    ///   400 — bad request (no valid f or b).
    ///   403 — caller is not an admin.
    ///   404 — file not found OR bytes empty/null.
    /// </summary>
    public class LPPI_Export_Download : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        private const string XlsxMimeType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        public void ProcessRequest(HttpContext ctx)
        {
            // Admin gate. Handlers are not pages, so we replicate the admin
            // page check via the same helper.
            if (!LPPIHelper.IsAdminUser())
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.ContentType = "text/plain";
                ctx.Response.Write("Forbidden — admin access required.");
                ctx.Response.End();
                return;
            }

            // Per-company file by ExportBatchFileID — the current path.
            int fileId;
            if (int.TryParse((ctx.Request.QueryString["f"] ?? "").Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out fileId)
                && fileId > 0)
            {
                DataTable t = LPPIHelper.ExecuteTable(@"
SELECT FileName, ContentType, FileBytes
  FROM dbo.tblLPPI_ExportBatchFiles
 WHERE ExportBatchFileID = @ID;",
                    LPPIHelper.P("@ID", fileId));
                StreamRow(ctx, t, "LPPI_Export_File" + fileId + ".xlsx");
                return;
            }

            // Legacy combined file by ExportBatchID.
            int batchId;
            if (int.TryParse((ctx.Request.QueryString["b"] ?? "").Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out batchId)
                && batchId > 0)
            {
                DataTable t = LPPIHelper.ExecuteTable(@"
SELECT FileName, ContentType, FileBytes
  FROM dbo.tblLPPI_ExportBatches
 WHERE ExportBatchID = @ID;",
                    LPPIHelper.P("@ID", batchId));
                StreamRow(ctx, t, "LPPI_Export_Batch" + batchId + ".xlsx");
                return;
            }

            ctx.Response.StatusCode = 400;
            ctx.Response.ContentType = "text/plain";
            ctx.Response.Write("Missing or invalid file/batch id.");
            ctx.Response.End();
        }

        private static void StreamRow(HttpContext ctx, DataTable t, string fallbackName)
        {
            if (t == null || t.Rows.Count == 0)
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/plain";
                ctx.Response.Write("Export file not found.");
                ctx.Response.End();
                return;
            }

            DataRow r = t.Rows[0];
            byte[] bytes = r["FileBytes"] as byte[];
            if (bytes == null || bytes.Length == 0)
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/plain";
                ctx.Response.Write("Export file has no stored bytes. It may pre-date the file-storage feature.");
                ctx.Response.End();
                return;
            }

            string fileName = Convert.ToString(r["FileName"]);
            if (string.IsNullOrEmpty(fileName)) fileName = fallbackName;

            string contentType = Convert.ToString(r["ContentType"]);
            if (string.IsNullOrEmpty(contentType)) contentType = XlsxMimeType;

            ctx.Response.Clear();
            ctx.Response.ContentType = contentType;
            ctx.Response.AppendHeader("Content-Disposition",
                "attachment; filename=\"" + fileName.Replace("\"", "") + "\"");
            ctx.Response.AppendHeader("Content-Length",
                bytes.Length.ToString(CultureInfo.InvariantCulture));
            ctx.Response.BinaryWrite(bytes);
            ctx.Response.End();
        }
    }
}