using System;
using System.Data;
using System.IO;
using System.Web;

namespace CPlatform.NORM
{
    /// <summary>
    /// Serves an original, database-retained source file for a completed run.
    /// Access is preparer-gated and every successful retrieval is audited.
    /// </summary>
    public sealed class NORMSourceFileHandler : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext context)
        {
            if (context.User == null || context.User.Identity == null ||
                !context.User.Identity.IsAuthenticated || !NORMHelper.HasPrepareAccess())
            {
                WriteError(context, 403, "Preparer access is required to retrieve source evidence.");
                return;
            }

            int runId;
            int fileId;
            if (!Int32.TryParse(context.Request.QueryString["run"], out runId) || runId <= 0 ||
                !Int32.TryParse(context.Request.QueryString["file"], out fileId) || fileId <= 0)
            {
                WriteError(context, 400, "A valid run and source file are required.");
                return;
            }

            DataTable table = NORMHelper.Query(
                "SELECT TOP 1 f.ImportFileId,f.SourceType,f.SourceFileName,f.SourceFileHash,f.FileContent " +
                "FROM dbo.tblNORM_ImportFile f " +
                "INNER JOIN dbo.tblNORM_CalculationRun r ON r.ImportId = f.ImportId " +
                "INNER JOIN dbo.tblNORM_Import i ON i.ImportId = r.ImportId " +
                "WHERE r.CalculationRunId = @run AND f.ImportFileId = @file " +
                "AND r.StatusCode = 'Complete' AND r.IsDeactivated = 0 AND i.IsDeactivated = 0",
                NORMHelper.P("@run", runId), NORMHelper.P("@file", fileId));
            if (table.Rows.Count == 0)
            {
                WriteError(context, 404, "The source evidence was not found for this completed run.");
                return;
            }

            DataRow row = table.Rows[0];
            string fileName = SafeFileName(NORMHelper.Str(row, "SourceFileName"));
            byte[] content = (byte[])row["FileContent"];
            string detail = "Run #" + runId.ToString() + "; " +
                NORMHelper.Str(row, "SourceType") + "; " + fileName + "; SHA-256 " +
                NORMHelper.Str(row, "SourceFileHash");
            NORMHelper.Exec(
                "INSERT dbo.tblNORM_AuditEvent (EventCode,EntityType,EntityId,DetailText,PerformedBy) " +
                "VALUES ('SOURCE_FILE_DOWNLOADED','ImportFile',@id,@detail,@user)",
                NORMHelper.P("@id", fileId.ToString()),
                NORMHelper.P("@detail", detail),
                NORMHelper.P("@user", NORMHelper.CurrentUserId()));

            context.Response.Clear();
            context.Response.StatusCode = 200;
            context.Response.ContentType = ContentTypeFor(fileName);
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Cache.SetNoStore();
            context.Response.AddHeader("Pragma", "no-cache");
            context.Response.AddHeader("X-Content-Type-Options", "nosniff");
            context.Response.AddHeader("Content-Disposition",
                "attachment; filename=\"" + fileName.Replace("\"", "") + "\"");
            context.Response.AddHeader("Content-Length", content.LongLength.ToString());
            context.Response.OutputStream.Write(content, 0, content.Length);
        }

        private static string SafeFileName(string value)
        {
            string fileName = Path.GetFileName((value ?? "").Replace("\r", "").Replace("\n", ""));
            return String.IsNullOrWhiteSpace(fileName) ? "NORM-source-evidence.bin" : fileName;
        }

        private static string ContentTypeFor(string fileName)
        {
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension == ".xlsx") { return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"; }
            if (extension == ".xls") { return "application/vnd.ms-excel"; }
            if (extension == ".csv") { return "text/csv"; }
            if (extension == ".txt") { return "text/plain"; }
            return "application/octet-stream";
        }

        private static void WriteError(HttpContext context, int status, string message)
        {
            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Write(message);
        }
    }
}
