using System;
using System.IO;
using System.Web;
using System.Web.Script.Serialization;
using Prepayment.Web.DataAccess;
using Prepayment.Web.Models.Dtos;

namespace Prepayment.Web.Services
{
    /// <summary>
    /// AJAX write-back endpoint for Tab 1. Handles the toggle/save-draft/confirm actions the
    /// UI fires, persisting them through the service + repository (stored procedures).
    /// Uses the built-in JavaScriptSerializer (System.Web.Extensions) — no third-party JSON lib.
    ///
    /// POST /Services/PPMPoIdentificationHandler.ashx?action=flag    body: { DeliveryLineId, PrepaymentFlag, Note }
    /// POST /Services/PPMPoIdentificationHandler.ashx?action=confirm body: { PoId }
    /// </summary>
    public class PPMPoIdentificationHandler : IHttpHandler
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private readonly PPMPoIdentificationService _service = new PPMPoIdentificationService();

        public bool IsReusable
        {
            get { return false; }
        }

        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "application/json";
            try
            {
                string action = (context.Request.QueryString["action"] ?? "").ToLowerInvariant();
                int userId = PPMCurrentUser.ResolveId(context);
                string body = ReadBody(context);

                switch (action)
                {
                    case "flag":
                        {
                            var req = Json.Deserialize<PPMFlagLineRequest>(body) ?? new PPMFlagLineRequest();
                            if (req.DeliveryLineId <= 0) { WriteError(context, "DeliveryLineId is required."); return; }
                            int affected = _service.SaveLineFlag(req, userId);
                            WriteOk(context, new { updated = affected });
                            break;
                        }
                    case "confirm":
                        {
                            var req = Json.Deserialize<ConfirmRequest>(body) ?? new ConfirmRequest();
                            if (req.PoId <= 0) { WriteError(context, "PoId is required."); return; }
                            var result = _service.ConfirmAndAdvance(req.PoId, userId);
                            WriteOk(context, new
                            {
                                status = result.Status,            // Ok | Blocked | NothingFlagged
                                flagged = result.Flagged,
                                pending = result.Pending,
                                invoicesLinked = result.InvoicesLinked
                            });
                            break;
                        }
                    default:
                        WriteError(context, "Unknown action '" + action + "'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                context.Response.Write(Json.Serialize(new { ok = false, error = ex.Message }));
            }
        }

        private static string ReadBody(HttpContext context)
        {
            using (var reader = new StreamReader(context.Request.InputStream))
            {
                return reader.ReadToEnd();
            }
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

        private class ConfirmRequest { public long PoId { get; set; } }
    }
}
