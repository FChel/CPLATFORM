using System;
using System.Text;
using System.Web;
using Prepayment.Web.DataAccess;

namespace Prepayment.Web.Services
{
    public class PPMAdminExportHandler : IHttpHandler
    {
        public bool IsReusable
        {
            get { return false; }
        }

        public void ProcessRequest(HttpContext context)
        {
            PPMCurrentUser.RequireRole(context, "Admin");
            var repo = new PPMAdminRepository();
            var rows = repo.GetProcessTracker();

            var sb = new StringBuilder();
            sb.AppendLine("PO Number,Vendor,Total Value,PO Flagged,Invoice Received,Setup,Recognition,Amortisation,Export,Overall Status");

            foreach (var r in rows)
            {
                var status = OverallStatus(r);
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvField(r.PoNumber),
                    CsvField(r.VendorName),
                    r.TotalValue.ToString("F2"),
                    PoFlagName(r.PoFlagStage),
                    InvoiceName(r.InvoiceStage),
                    SetupName(r.SetupStage),
                    JournalName(r.RecognitionStage),
                    JournalName(r.AmortisationStage),
                    StageName(r.ExportStage),
                    CsvField(status),
                }));
            }

            var filename = "ProcessTracker_" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
            context.Response.ContentType = "text/csv";
            context.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + filename + "\"");
            context.Response.Write(sb.ToString());
            context.Response.End();
        }

        private static string StageName(int stage)
        {
            if (stage == 1) return "Complete";
            if (stage == 2) return "In Progress";
            return "Not Started";
        }

        // PO flag: 1 = Prepayment, 3 = NotPrepayment, 0 = Pending / none.
        private static string PoFlagName(int stage)
        {
            if (stage == 1) return "Prepayment";
            if (stage == 3) return "Not Prepayment";
            return "Pending";
        }

        // Invoice: 1 = received, 3 = none.
        private static string InvoiceName(int stage)
        {
            return stage == 1 ? "Yes" : "No";
        }

        // Setup text mirrors the underlying SetupStatus value.
        private static string SetupName(int stage)
        {
            if (stage == 1) return "Complete";
            if (stage == 2) return "AmortisationNeeded";
            return "Not Started";
        }

        // Recognition / Amortisation journals: 1 = approved/exported, 2 = pending, 3 = rejected, 0 = none.
        private static string JournalName(int stage)
        {
            if (stage == 1) return "Approved";
            if (stage == 2) return "Pending Approval";
            if (stage == 3) return "Rejected";
            return "Not Started";
        }

        private static string OverallStatus(Prepayment.Web.Models.Entities.PPMAdminProcessTrackerRow r)
        {
            if (r.ExportStage == 1)        return "Completed";
            if (r.ExportStage == 2)        return "Ready for export";
            if (r.AmortisationStage == 3)  return "Amortisation rejected";
            if (r.AmortisationStage == 2)  return "Pending approval";
            if (r.AmortisationStage == 1)  return "Amortising";
            if (r.RecognitionStage == 3)   return "Recognition rejected";
            if (r.RecognitionStage == 2)   return "Pending approval";
            if (r.RecognitionStage == 1)   return "Journal pending";
            if (r.SetupStage == 2)         return "Setup in progress";
            if (r.SetupStage == 1)         return "Setup complete";
            if (r.InvoiceStage == 1)       return "Invoice received";
            if (r.PoFlagStage == 1)        return "PO flagged";
            if (r.PoFlagStage == 3)        return "Not prepayment";
            return "New";
        }

        private static string CsvField(string s)
        {
            return "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        }
    }
}
