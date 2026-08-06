using System;
using System.Globalization;
using System.Text;
using System.Web;
using Prepayment.Web.DataAccess;

namespace Prepayment.Web.Services
{
    /// <summary>
    /// Streams the reconciliation grid to a CSV download, honouring the active period +
    /// "variances only" filter (?period= &amp; ?variancesOnly=1).
    /// </summary>
    public class PPMGlReconExportHandler : IHttpHandler
    {
        private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

        public bool IsReusable
        {
            get { return false; }
        }

        public void ProcessRequest(HttpContext context)
        {
            string period = Blank(context.Request.QueryString["period"]);
            bool variancesOnly = context.Request.QueryString["variancesOnly"] == "1";

            var rows = new PPMGlReconciliationRepository().GetGrid(period, variancesOnly);

            var sb = new StringBuilder();
            sb.AppendLine("Group,Group Name,GL Account,GL Description,SAP Balance,FINHUB Balance,Variance,Status,Period");
            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvField(r.DeliveryGroupCode),
                    CsvField(r.GroupName),
                    CsvField(r.GlAccount),
                    CsvField(r.GlDescription),
                    r.SapBalance.ToString("F2", Au),
                    r.PrepaymentBalance.ToString("F2", Au),
                    r.Variance.ToString("F2", Au),
                    CsvField(StatusLabel(r.Status)),
                    CsvField(r.Period),
                }));
            }

            var filename = "GLReconciliation_" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
            context.Response.ContentType = "text/csv";
            context.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + filename + "\"");
            context.Response.Write(sb.ToString());
            context.Response.End();
        }

        private static string StatusLabel(string s)
        {
            switch (s)
            {
                case "Reconciled":     return "Reconciled";
                case "Variance":       return "Variance";
                case "JournalPending": return "Journal pending";
                case "NotMatched":     return "Not matched";
                default:               return s;
            }
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
