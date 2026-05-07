<%@ WebHandler Language="C#" Class="CPlatform.LPPI.LPPI_Export_Download" %>

using System;
using System.Globalization;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Streams a previously-generated ERP payment file from
    /// tblLPPI_ExportBatches.FileBytes. Used by the Download buttons in
    /// the Recent export batches table on LPPI_Export.aspx.
    ///
    /// AUTHENTICATION
    ///   Admin-only. Validates the current Windows identity against the
    ///   admin allow-list before streaming. The file contains payment
    ///   data — vendors, GL accounts, dollar amounts — and must not be
    ///   served to unauthenticated callers. Same gate as every other
    ///   admin page in this module.
    ///
    /// QUERY STRING
    ///   b = ExportBatchID (required)
    ///
    /// RESPONSE
    ///   200 — Content-Type: xlsx, body = file bytes.
    ///   400 — bad request (missing/invalid b).
    ///   403 — caller is not an admin.
    ///   404 — batch not found OR file bytes empty/null.
    /// </summary>
    public class LPPI_Export_Download : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext ctx)
        {
            // Admin gate. We deliberately do NOT use LPPIBasePage here
            // because handlers are not pages — but we replicate the same
            // check by calling the same helper. IsAdminUser() reads the
            // current Windows identity from HttpContext.Current.User and
            // queries tblLPPI_AdminUsers; cached per request.
            if (!LPPIHelper.IsAdminUser())
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.ContentType = "text/plain";
                ctx.Response.Write("Forbidden — admin access required.");
                ctx.Response.End();
                return;
            }

            // Parse batch id.
            string raw = (ctx.Request.QueryString["b"] ?? "").Trim();
            int batchId;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out batchId)
                || batchId <= 0)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.ContentType = "text/plain";
                ctx.Response.Write("Missing or invalid batch id.");
                ctx.Response.End();
                return;
            }

            // Fetch the row. We pull FileName + ContentType + FileBytes in
            // a single round-trip; FileBytes is varbinary(max) so this is a
            // single blob read rather than three separate scalar lookups.
            var row = LPPIHelper.ExecuteTable(@"
SELECT FileName, ContentType, FileBytes
  FROM dbo.tblLPPI_ExportBatches
 WHERE ExportBatchID = @ID;",
                LPPIHelper.P("@ID", batchId));

            if (row == null || row.Rows.Count == 0)
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/plain";
                ctx.Response.Write("Export batch not found.");
                ctx.Response.End();
                return;
            }

            var r = row.Rows[0];
            byte[] bytes = r["FileBytes"] as byte[];
            if (bytes == null || bytes.Length == 0)
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/plain";
                ctx.Response.Write("Export batch has no stored file. It may pre-date the file-storage feature.");
                ctx.Response.End();
                return;
            }

            string fileName = Convert.ToString(r["FileName"]);
            if (string.IsNullOrEmpty(fileName))
                fileName = "LPPI_Export_Batch" + batchId + ".xlsx";

            string contentType = Convert.ToString(r["ContentType"]);
            if (string.IsNullOrEmpty(contentType))
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

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
