using System;
using System.IO;
using System.Web;
using System.Web.Script.Serialization;
using Prepayment.Web.Models.Dtos;

namespace Prepayment.Web.Services
{
    /// <summary>
    /// AJAX write-back endpoint for the Amortisation Setup page (§3.2). Receives the setup-panel
    /// inputs and persists them via the service + repository (stored procedures). Returns JSON.
    ///
    /// POST …/PPMAmortisationSetupHandler.ashx?action=savedraft    body: PPMAmortisationSetupRequest
    /// POST …/PPMAmortisationSetupHandler.ashx?action=generate     body: PPMAmortisationSetupRequest
    /// POST …/PPMAmortisationSetupHandler.ashx?action=saveperiods  body: { InvoiceId, Periods:[{Id,Amount}] }
    /// </summary>
    public class PPMAmortisationSetupHandler : IHttpHandler
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private readonly PPMAmortisationSetupService _service = new PPMAmortisationSetupService();

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
                    case "savedraft":
                        {
                            var req = Json.Deserialize<PPMAmortisationSetupRequest>(body) ?? new PPMAmortisationSetupRequest();
                            if (req.InvoiceId <= 0) { WriteError(context, "InvoiceId is required."); return; }
                            long scheduleId = _service.SaveDraft(req, userId);
                            WriteOk(context, new { scheduleId });
                            break;
                        }
                    case "generate":
                        {
                            var req = Json.Deserialize<PPMAmortisationSetupRequest>(body) ?? new PPMAmortisationSetupRequest();
                            if (req.InvoiceId <= 0) { WriteError(context, "InvoiceId is required."); return; }
                            if (req.Periods == null || req.Periods <= 0) { WriteError(context, "Periods must be greater than zero."); return; }
                            if (req.PrepaymentGlId == null) { WriteError(context, "A prepayment GL account is required."); return; }
                            long scheduleId = _service.GenerateScheduleAndJournals(req, userId);
                            WriteOk(context, new { scheduleId });
                            break;
                        }
                    case "saveperiods":
                        {
                            // body: { InvoiceId, Periods: [{Id, Amount}, ...] }
                            var raw = Json.Deserialize<PPMSavePeriodsRequest>(body) ?? new PPMSavePeriodsRequest();
                            if (raw.InvoiceId <= 0) { WriteError(context, "InvoiceId is required."); return; }
                            string periodsJson = Json.Serialize(raw.Periods ?? new PPMPeriodAmountItem[0]);
                            int updated = _service.SavePeriodAmounts(raw.InvoiceId, periodsJson, userId);
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
                context.Response.StatusCode = 500;
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
