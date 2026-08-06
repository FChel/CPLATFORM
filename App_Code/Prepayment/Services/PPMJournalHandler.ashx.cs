using System;
using System.IO;
using System.Web;
using System.Web.Script.Serialization;
using Prepayment.Web.Models.Dtos;

namespace Prepayment.Web.Services
{
    /// <summary>
    /// AJAX write-back endpoint for the Journal Generation page (§3.3). Drives the approval
    /// workflow — submit / approve / reject / export / approve-all — via the service +
    /// repository (stored procedures). Returns JSON.
    ///
    /// POST …/PPMJournalHandler.ashx?action=submit      body: { JournalId }
    /// POST …/PPMJournalHandler.ashx?action=approve     body: { JournalId, Comments }
    /// POST …/PPMJournalHandler.ashx?action=reject      body: { JournalId, ReasonCode, Comments }
    /// POST …/PPMJournalHandler.ashx?action=export      body: { JournalId }   (JournalId 0 = export all approved)
    /// POST …/PPMJournalHandler.ashx?action=approveall  body: { ReasonCode = JournalType ("Recognition"|"Amortisation"|"") }
    /// </summary>
    public class PPMJournalHandler : IHttpHandler
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private readonly PPMJournalService _service = new PPMJournalService();

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
                var req = Json.Deserialize<PPMJournalActionRequest>(ReadBody(context)) ?? new PPMJournalActionRequest();

                switch (action)
                {
                    case "submit":
                        Require(req.JournalId, context);
                        WriteOk(context, new { affected = _service.Submit(req.JournalId, userId) });
                        break;
                    case "approve":
                        Require(req.JournalId, context);
                        WriteOk(context, new { affected = _service.Approve(req.JournalId, userId, req.Comments) });
                        break;
                    case "reject":
                        Require(req.JournalId, context);
                        WriteOk(context, new { affected = _service.Reject(req.JournalId, userId, req.Comments) });
                        break;
                    case "export":
                        {
                            long? id = req.JournalId > 0 ? (long?)req.JournalId : null;
                            WriteOk(context, new { exported = _service.Export(id, userId) });
                            break;
                        }
                    case "approveall":
                        {
                            // ReasonCode field carries the journal type filter (or empty = both).
                            string type = string.IsNullOrWhiteSpace(req.ReasonCode) ? null : req.ReasonCode;
                            WriteOk(context, new { approved = _service.ApproveAllReady(type, userId) });
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

        private static void Require(long journalId, HttpContext context)
        {
            if (journalId <= 0) throw new ArgumentException("JournalId is required.");
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
