using System;
using System.Web;
using System.Web.Script.Serialization;
using Prepayment.Web.Services.ExcelImport;

namespace Prepayment.Web.Services
{
    /// <summary>
    /// AJAX endpoint for the Import tab.
    /// POST …/PPMImportHandler.ashx?action=import   (multipart/form-data: file = the .xlsx)
    ///        → parses the workbook and FULL-REPLACES the prepayment data, returns a load summary.
    /// </summary>
    public class PPMImportHandler : IHttpHandler
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private readonly PPMImportService _service = new PPMImportService();

        public bool IsReusable
        {
            get { return false; }
        }

        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "application/json";
            string action = (context.Request.QueryString["action"] ?? "").ToLowerInvariant();
            try
            {
                int userId = PPMCurrentUser.RequireRole(context, "Admin");
                switch (action)
                {
                    case "import":
                        {
                            var file = context.Request.Files.Count > 0 ? context.Request.Files[0] : null;
                            if (file == null || file.ContentLength == 0)
                                throw new ArgumentException("No file was uploaded.");

                            string name = (file.FileName ?? "").ToLowerInvariant();
                            if (!name.EndsWith(".xlsx"))
                                throw new ArgumentException("Please upload the .xlsx workbook (e.g. Prepayment Dashboard_2026.xlsx).");

                            var result = _service.ImportWorkbook(file.InputStream, file.FileName, userId);
                            context.Response.Write(Json.Serialize(new { ok = true, data = result }));
                            break;
                        }
                    default:
                        context.Response.StatusCode = 400;
                        context.Response.Write(Json.Serialize(new { ok = false, error = "Unknown action '" + action + "'." }));
                        break;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                context.Response.StatusCode = 403;
                context.Response.Write(Json.Serialize(new { ok = false, error = ex.Message }));
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 400;
                // Surface the inner-most message (Excel parse / SQL errors are most useful unwrapped).
                var msg = ex.Message;
                var inner = ex.InnerException;
                while (inner != null) { msg = inner.Message; inner = inner.InnerException; }
                context.Response.Write(Json.Serialize(new { ok = false, error = msg }));
            }
        }
    }
}
