using System;
using System.IO;
using System.Web;
using System.Web.Script.Serialization;
using Prepayment.Web.Models.Dtos;

namespace Prepayment.Web.Services
{
    /// <summary>
    /// AJAX endpoint for the GL Balance Reconciliation page (Tab 6).
    ///
    /// POST …/PPMGlReconciliationHandler.ashx?action=upload   (multipart/form-data: file + period)
    ///        → parses the CSV, writes the extract, rebuilds the reconciliation for that period
    /// POST …/PPMGlReconciliationHandler.ashx?action=resolve   body: { ReconciliationId, Action, Note }
    /// GET  …/PPMGlReconciliationHandler.ashx?action=template   → downloads the sample CSV
    /// </summary>
    public class PPMGlReconciliationHandler : IHttpHandler
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private readonly PPMGlReconciliationService _service = new PPMGlReconciliationService();

        public bool IsReusable
        {
            get { return false; }
        }

        public void ProcessRequest(HttpContext context)
        {
            string action = (context.Request.QueryString["action"] ?? "").ToLowerInvariant();

            // template is a plain file download, not JSON.
            if (action == "template")
            {
                context.Response.ContentType = "text/csv";
                context.Response.AddHeader("Content-Disposition", "attachment; filename=\"GL_Balance_Template.csv\"");
                context.Response.Write(PPMGlReconciliationService.CsvTemplate());
                context.Response.End();
                return;
            }

            context.Response.ContentType = "application/json";
            try
            {
                int userId = PPMCurrentUser.ResolveId(context);

                switch (action)
                {
                    case "upload":
                        {
                            var file = context.Request.Files.Count > 0 ? context.Request.Files[0] : null;
                            if (file == null || file.ContentLength == 0)
                                throw new ArgumentException("No file was uploaded.");

                            string period = (context.Request.Form["period"] ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(period))
                                throw new ArgumentException("A reporting period is required.");

                            string csv;
                            using (var reader = new StreamReader(file.InputStream))
                                csv = reader.ReadToEnd();

                            var lines = PPMGlReconciliationService.ParseCsv(csv);
                            long fileId = _service.SaveExtract(file.FileName, period, lines, userId);

                            WriteOk(context, new { fileId, rows = lines.Count, period });
                            break;
                        }

                    case "resolve":
                        {
                            var req = Json.Deserialize<PPMReconResolveRequest>(ReadBody(context)) ?? new PPMReconResolveRequest();
                            int updated = _service.Resolve(req.ReconciliationId, req.Action, req.Note, req.AssignedToUserId, userId);
                            WriteOk(context, new { updated });
                            break;
                        }

                    default:
                        WriteError(context, "Unknown action '" + action + "'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 400;
                context.Response.Write(Json.Serialize(new { ok = false, error = ex.Message }));
            }
        }

        private static string ReadBody(HttpContext context)
        {
            using (var reader = new StreamReader(context.Request.InputStream))
                return reader.ReadToEnd();
        }

        private static void WriteOk(HttpContext context, object data)
        {
            context.Response.Write(Json.Serialize(new { ok = true, data }));
        }

        private static void WriteError(HttpContext context, string message)
        {
            context.Response.StatusCode = 400;
            context.Response.Write(Json.Serialize(new { ok = false, error = message }));
        }
    }
}
