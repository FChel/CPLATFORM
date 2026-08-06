using System;
using System.IO;
using System.Web;
using System.Web.Script.Serialization;
using Prepayment.Web.Models.Dtos;

namespace Prepayment.Web.Services
{
    /// <summary>
    /// AJAX endpoint for the Admin Control Tower action panel (Tab 4 / §3.4 "Admin actions"):
    ///   POST …/PPMAdminActionHandler.ashx?action=forceadvance  body: { PoNumber }
    ///   POST …/PPMAdminActionHandler.ashx?action=reassign      body: { PoNumber, ApproverUserId }
    ///   POST …/PPMAdminActionHandler.ashx?action=clear         body: { ExceptionId, Note }
    ///   POST …/PPMAdminActionHandler.ashx?action=reexport      body: { BatchId }
    /// Each returns { ok, data:{ affected } } and performs a real DB state change.
    /// </summary>
    public class PPMAdminActionHandler : IHttpHandler
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private readonly PPMAdminService _service = new PPMAdminService();

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
                int userId = PPMCurrentUser.RequireRole(context, "Admin");
                var req = Json.Deserialize<PPMAdminActionRequest>(ReadBody(context)) ?? new PPMAdminActionRequest();

                int affected;
                switch (action)
                {
                    case "forceadvance":
                        affected = _service.ForceAdvance(req.PoNumber, userId);
                        break;
                    case "reassign":
                        affected = _service.ReassignApprover(req.PoNumber, req.ApproverUserId, userId);
                        break;
                    case "clear":
                        affected = _service.ResolveException(req.ExceptionId, req.Note, userId);
                        break;
                    case "reexport":
                        affected = _service.ReExportBatch(req.BatchId, userId);
                        break;
                    default:
                        WriteError(context, "Unknown action '" + action + "'.");
                        return;
                }

                WriteOk(context, new { affected });
            }
            catch (UnauthorizedAccessException ex)
            {
                context.Response.StatusCode = 403;
                context.Response.Write(Json.Serialize(new { ok = false, error = ex.Message }));
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
