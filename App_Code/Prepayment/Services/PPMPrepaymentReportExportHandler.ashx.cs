using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;
using Prepayment.Web.DataAccess;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.Services
{
    /// <summary>
    /// Streams the Prepayment Report grid for management reporting (§3.7 export options:
    /// Export CSV / Export to Excel / Export PDF), honouring the active filters
    /// (?period= &amp; ?group= &amp; ?gl= &amp; ?status=):
    ///   ?format=csv   → comma-separated download (.csv)
    ///   ?format=excel → an HTML table served as application/vnd.ms-excel (.xls), opens in Excel
    ///   ?format=pdf   → a print-optimised HTML page that auto-invokes the browser print dialog
    ///                   (Save as PDF) — no server-side PDF dependency.
    /// </summary>
    public class PPMPrepaymentReportExportHandler : IHttpHandler
    {
        private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

        public bool IsReusable
        {
            get { return false; }
        }

        public void ProcessRequest(HttpContext context)
        {
            string format = (context.Request.QueryString["format"] ?? "csv").ToLowerInvariant();
            string period = Blank(context.Request.QueryString["period"]);
            string status = Blank(context.Request.QueryString["status"]);
            long?  groupId = ParseLong(context.Request.QueryString["group"]);
            long?  glId    = ParseLong(context.Request.QueryString["gl"]);

            var repo = new PPMPrepaymentReportRepository();
            var rows = repo.GetGrid(period, groupId, glId, status).ToList();
            var matchedPeriod = repo.GetPeriods().FirstOrDefault(p => p.PeriodKey == period);
            string periodLabel = (matchedPeriod != null ? matchedPeriod.PeriodLabel : null)
                                 ?? period ?? "All periods";
            string stamp = DateTime.Now.ToString("yyyyMMdd");

            switch (format)
            {
                case "excel": WriteExcel(context, rows, periodLabel, stamp); break;
                case "pdf":   WritePdf(context, rows, periodLabel, stamp);   break;
                default:      WriteCsv(context, rows, stamp);                break;
            }
        }

        // ── CSV ──────────────────────────────────────────────────────────────────────

        private static void WriteCsv(HttpContext ctx, System.Collections.Generic.List<PPMReportGridRow> rows, string stamp)
        {
            // §3.7 summary report columns (Group, Group name, GL account, Vendor, Recognised,
            // Amortised, Outstanding, % amortised, Periods left, End date, Status). GL description
            // is included as a reporting convenience next to the account code.
            var sb = new StringBuilder();
            sb.AppendLine("Group,Group Name,GL Account,GL Description,Vendor,Recognised Amount,Amortised To Date,Outstanding Balance,% Amortised,Periods Left,End Date,Status");
            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvField(r.DeliveryGroupCode), CsvField(r.GroupName),
                    CsvField(r.GlAccount), CsvField(r.GlDescription), CsvField(r.Vendor),
                    r.Recognised.ToString("F2", Au), r.Amortised.ToString("F2", Au),
                    r.Outstanding.ToString("F2", Au),
                    r.PercentAmortised.ToString("F1", Au), r.PeriodsLeft.ToString(),
                    CsvField(r.EndDate.HasValue ? r.EndDate.Value.ToString("yyyy-MM-dd", Au) : ""),
                    CsvField(r.Status),
                }));
            }
            // Totals row.
            sb.AppendLine(string.Join(",", new[]
            {
                CsvField("TOTAL"), "", "", "", "",
                rows.Sum(r => r.Recognised).ToString("F2", Au),
                rows.Sum(r => r.Amortised).ToString("F2", Au),
                rows.Sum(r => r.Outstanding).ToString("F2", Au),
                "", "", "", "",
            }));

            ctx.Response.ContentType = "text/csv";
            ctx.Response.AddHeader("Content-Disposition", "attachment; filename=\"PrepaymentReport_" + stamp + ".csv\"");
            ctx.Response.Write(sb.ToString());
            ctx.Response.End();
        }

        // ── Excel (HTML table → application/vnd.ms-excel) ─────────────────────────────

        private static void WriteExcel(HttpContext ctx, System.Collections.Generic.List<PPMReportGridRow> rows, string periodLabel, string stamp)
        {
            string table = BuildHtmlTable(rows, periodLabel);
            var sb = new StringBuilder();
            sb.Append("<html xmlns:x=\"urn:schemas-microsoft-com:office:excel\"><head><meta charset=\"utf-8\" />");
            sb.Append("<style>table{border-collapse:collapse}td,th{border:1px solid #ccc;padding:4px 8px}th{background:#f0f0f0}</style></head><body>");
            sb.Append(table);
            sb.Append("</body></html>");

            ctx.Response.ContentType = "application/vnd.ms-excel";
            ctx.Response.AddHeader("Content-Disposition", "attachment; filename=\"PrepaymentReport_" + stamp + ".xls\"");
            ctx.Response.Write(sb.ToString());
            ctx.Response.End();
        }

        // ── PDF (print-optimised HTML; browser "Save as PDF") ─────────────────────────

        private static void WritePdf(HttpContext ctx, System.Collections.Generic.List<PPMReportGridRow> rows, string periodLabel, string stamp)
        {
            string table = BuildHtmlTable(rows, periodLabel);
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\" /><title>Prepayment Report — " + Enc(periodLabel) + "</title>");
            sb.Append("<style>");
            sb.Append("body{font-family:Segoe UI,Arial,sans-serif;color:#222;margin:24px}");
            sb.Append("h1{font-size:18px;margin:0 0 2px}.sub{color:#666;font-size:12px;margin-bottom:16px}");
            sb.Append("table{border-collapse:collapse;width:100%;font-size:11px}");
            sb.Append("td,th{border:1px solid #ddd;padding:5px 7px;text-align:left}th{background:#f4f6f8}");
            sb.Append(".num{text-align:right;font-variant-numeric:tabular-nums}.tot{font-weight:700;background:#f7f8fa}");
            sb.Append("@media print{.noprint{display:none}}");
            sb.Append("</style></head><body onload=\"window.print()\">");
            sb.Append("<div class=\"noprint\" style=\"margin-bottom:14px\"><button onclick=\"window.print()\">Print / Save as PDF</button></div>");
            sb.Append("<h1>Prepayment Report by Group</h1>");
            sb.Append("<div class=\"sub\">Period: " + Enc(periodLabel) + " &middot; Generated " + DateTime.Now.ToString("dd MMM yyyy HH:mm", Au) + "</div>");
            sb.Append(table);
            sb.Append("</body></html>");

            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Write(sb.ToString());
            ctx.Response.End();
        }

        // ── Shared HTML table builder (Excel + PDF) ───────────────────────────────────

        private static string BuildHtmlTable(System.Collections.Generic.List<PPMReportGridRow> rows, string periodLabel)
        {
            var sb = new StringBuilder();
            sb.Append("<table><thead><tr>");
            foreach (var h in new[] { "Group", "Group name", "GL account", "GL description", "Vendor",
                                      "Recognised amount", "Amortised to date", "Outstanding balance",
                                      "% amortised", "Periods left", "End date", "Status" })
                sb.Append("<th>" + Enc(h) + "</th>");
            sb.Append("</tr></thead><tbody>");

            foreach (var r in rows)
            {
                sb.Append("<tr>");
                sb.Append("<td>" + Enc(r.DeliveryGroupCode) + "</td>");
                sb.Append("<td>" + Enc(r.GroupName) + "</td>");
                sb.Append("<td>" + Enc(r.GlAccount) + "</td>");
                sb.Append("<td>" + Enc(r.GlDescription) + "</td>");
                sb.Append("<td>" + Enc(r.Vendor) + "</td>");
                sb.Append("<td class=\"num\">" + r.Recognised.ToString("C0", Au) + "</td>");
                sb.Append("<td class=\"num\">" + r.Amortised.ToString("C0", Au) + "</td>");
                sb.Append("<td class=\"num\">" + r.Outstanding.ToString("C0", Au) + "</td>");
                sb.Append("<td class=\"num\">" + r.PercentAmortised.ToString("0.#", Au) + "%</td>");
                sb.Append("<td>" + (r.PeriodsLeft > 0 ? r.PeriodsLeft.ToString() : "—") + "</td>");
                sb.Append("<td>" + Enc(r.EndDate.HasValue ? r.EndDate.Value.ToString("MMM yyyy", Au) : "—") + "</td>");
                sb.Append("<td>" + Enc(r.Status) + "</td>");
                sb.Append("</tr>");
            }

            sb.Append("<tr class=\"tot\"><td colspan=\"5\">Total — " + rows.Count + " row" + (rows.Count == 1 ? "" : "s") + "</td>");
            sb.Append("<td class=\"num\">" + rows.Sum(r => r.Recognised).ToString("C0", Au) + "</td>");
            sb.Append("<td class=\"num\">" + rows.Sum(r => r.Amortised).ToString("C0", Au) + "</td>");
            sb.Append("<td class=\"num\">" + rows.Sum(r => r.Outstanding).ToString("C0", Au) + "</td>");
            sb.Append("<td colspan=\"4\"></td></tr>");
            sb.Append("</tbody></table>");
            return sb.ToString();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        private static string Enc(string s)
        {
            return HttpUtility.HtmlEncode(s ?? "");
        }
        private static string Blank(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
        private static string CsvField(string s)
        {
            return "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        }
        private static long? ParseLong(string s)
        {
            long v;
            return long.TryParse(s, out v) && v > 0 ? (long?)v : null;
        }
    }
}
