using System;
using System.Text;
using System.Web;
using Prepayment.Web.DataAccess;

namespace Prepayment.Web.Services
{
    /// <summary>
    /// Streams the Group Workflow grid (§3.5 columns) to a CSV download, honouring the same
    /// Group name / Preparer / Status filters as the on-screen grid
    /// (?group= &amp; ?preparer= &amp; ?status=).
    /// </summary>
    public class PPMGroupWorkflowExportHandler : IHttpHandler
    {
        public bool IsReusable
        {
            get { return false; }
        }

        public void ProcessRequest(HttpContext context)
        {
            string status   = Blank(context.Request.QueryString["status"]);
            string group    = Blank(context.Request.QueryString["group"]);
            string preparer = Blank(context.Request.QueryString["preparer"]);

            var rows = new PPMGroupWorkflowRepository().GetWorkflow(status, group, preparer);

            var sb = new StringBuilder();
            sb.AppendLine("Group,Group Name,Preparer,Approver,POs,Invoices,Journals,Current Stage,Status");

            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvField(r.DeliveryGroupCode),
                    CsvField(r.GroupName),
                    CsvField(r.PreparerName),
                    CsvField(r.ApproverName),
                    r.PoCount.ToString(),
                    r.InvoiceCount.ToString(),
                    r.JournalCount.ToString(),
                    CsvField(PPMGroupWorkflowLabels.Stage(r.CurrentStageKey)),
                    CsvField(PPMGroupWorkflowLabels.Status(r.StatusKey)),
                }));
            }

            var filename = "GroupWorkflow_" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
            context.Response.ContentType = "text/csv";
            context.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + filename + "\"");
            context.Response.Write(sb.ToString());
            context.Response.End();
        }

        private static string Blank(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        private static string CsvField(string s)
        {
            return "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        }
    }
}
