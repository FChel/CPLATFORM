using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using System.Web;

/// <summary>
/// Produces an editable Microsoft Word document without a server-side Office
/// dependency. Word opens this standards-compliant HTML document as a .doc;
/// preparers can apply final entity branding and save it as DOCX.
/// </summary>
public class NORM_WordExport : IHttpHandler
{
    public bool IsReusable { get { return false; } }

    public void ProcessRequest(HttpContext context)
    {
        int runId;
        if (!Int32.TryParse(context.Request.QueryString["run"], out runId) || runId <= 0)
        {
            WriteError(context, 400, "A completed calculation run is required.");
            return;
        }
        DataTable meta = NORMHelper.Query(
            "SELECT r.ConfigurationReleaseId,i.FinancialYear,i.EntityCode,e.EntityName,c.VersionCode,c.ReleaseLabel " +
            "FROM dbo.tblNORM_CalculationRun r INNER JOIN dbo.tblNORM_Import i ON i.ImportId=r.ImportId " +
            "INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=r.ConfigurationReleaseId " +
            "LEFT JOIN dbo.tblNORM_ReportingEntity e ON e.FinancialYear=i.FinancialYear AND e.EntityCode=i.EntityCode AND e.IsDeactivated=0 " +
            "WHERE r.CalculationRunId=@run AND r.StatusCode='Complete' AND r.IsDeactivated=0 AND i.IsDeactivated=0",
            NORMHelper.P("@run", runId));
        if (meta.Rows.Count == 0)
        {
            WriteError(context, 404, "The completed calculation run was not found.");
            return;
        }

        DataRow header = meta.Rows[0];
        int releaseId = NORMHelper.Int(header, "ConfigurationReleaseId");
        int year = NORMHelper.Int(header, "FinancialYear");
        string entity = NORMHelper.Str(header, "EntityName") ?? NORMHelper.Str(header, "EntityCode");
        NORMReportingFramework.ReportingProfile profile = NORMReportingFramework.LoadProfile(releaseId);
        List<NORMReportingFramework.Disclosure> disclosures = NORMReportingFramework.IsInstalled()
            ? NORMReportingFramework.LoadDisclosures(runId, releaseId, profile)
            : new List<NORMReportingFramework.Disclosure>();
        NORMStatementEnhancements.ApplyManualInputs(runId, disclosures);

        string document = BuildDocument(runId, releaseId, year, entity, NORMHelper.Str(header, "VersionCode"), profile, disclosures);
        string fileName = "NORM_FY" + year.ToString(CultureInfo.InvariantCulture) + "_Run_" + runId.ToString(CultureInfo.InvariantCulture) + "_Financial_Statements.doc";
        context.Response.Clear();
        context.Response.ContentType = "application/msword";
        context.Response.ContentEncoding = Encoding.UTF8;
        context.Response.Charset = "utf-8";
        context.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + fileName + "\"");
        context.Response.Write(document);
        context.ApplicationInstance.CompleteRequest();
    }

    private string BuildDocument(int runId, int releaseId, int year, string entity, string version,
        NORMReportingFramework.ReportingProfile profile, List<NORMReportingFramework.Disclosure> disclosures)
    {
        StringBuilder html = new StringBuilder();
        html.Append("<!doctype html><html xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:w=\"urn:schemas-microsoft-com:office:word\" lang=\"en-AU\"><head><meta charset=\"utf-8\">");
        html.Append("<title>").Append(Enc(entity)).Append(" financial statements</title>");
        html.Append("<!--[if gte mso 9]><xml><w:WordDocument><w:View>Print</w:View><w:Zoom>100</w:Zoom><w:DoNotOptimizeForBrowser/></w:WordDocument></xml><![endif]-->");
        html.Append("<style>@page{size:A4;margin:20mm 18mm 18mm}body{font-family:Arial,sans-serif;color:#171717;font-size:9.5pt;line-height:1.35}h1{font-size:22pt;margin:0 0 8pt}h2{font-size:15pt;border-bottom:2pt solid #e87722;padding-bottom:5pt;margin:0 0 14pt}h3{font-size:11.5pt;margin:14pt 0 7pt}p{margin:0 0 8pt}.cover{padding-top:65mm}.eyebrow{color:#b64d00;font-weight:bold;text-transform:uppercase;letter-spacing:.7pt}.meta{margin-top:30pt;border-top:1pt solid #bbb;padding-top:10pt}.page{page-break-before:always}.note{page-break-before:always}.section{font-weight:bold;background:#f1f2f4}table{width:100%;border-collapse:collapse;margin:7pt 0 12pt}tr{page-break-inside:avoid}th,td{padding:4pt 5pt;border-bottom:.5pt solid #c8c8c8;vertical-align:top}th{text-align:left}.note tbody th{font-weight:normal}.note .total th{font-weight:bold}.amount{text-align:right;width:21%}.total th,.total td{font-weight:bold;border-top:1pt solid #222;border-bottom:2pt double #222}.policy{background:#f7f7f7;border-left:3pt solid #e87722;padding:8pt 10pt;margin:8pt 0 12pt}.small{font-size:8pt;color:#555}.register td:first-child{width:10%}.status{font-weight:bold}.footer{margin-top:18pt;border-top:.5pt solid #aaa;padding-top:6pt;font-size:8pt;color:#555}</style></head><body>");
        html.Append("<section class=\"cover\"><p class=\"eyebrow\">Financial statements preparation copy</p><h1>").Append(Enc(entity)).Append("</h1><h2>Financial statements for the year ended 30 June ").Append(year).Append("</h2>");
        html.Append("<p>Editable preparation copy generated from NORM calculation run #").Append(runId).Append(".</p><div class=\"meta\"><p><b>Configuration:</b> ").Append(Enc(version)).Append("</p>");
        html.Append("<p><b>Reporting profile:</b> ").Append(Enc(ProfileLabel(profile))).Append("</p><p><b>Generated:</b> ").Append(DateTime.UtcNow.ToString("d MMMM yyyy 'at' HH:mm 'UTC'", CultureInfo.GetCultureInfo("en-AU"))).Append("</p></div></section>");

        AppendStatement(html, runId, releaseId, year, "SOCI", "Statement of Comprehensive Income", false);
        AppendStatement(html, runId, releaseId, year, "SOFP", "Statement of Financial Position", true);
        AppendEquity(html, runId, releaseId, year);
        AppendCashFlow(html, runId, releaseId, year);
        AppendNotes(html, year, disclosures);
        AppendRegister(html, disclosures);
        html.Append("</body></html>");
        return html.ToString();
    }

    private void AppendStatement(StringBuilder html, int runId, int releaseId, int year, string code, string title, bool atDate)
    {
        DataTable table = NORMHelper.Query(
            "SELECT t.LineType,t.LineLabel,t.NoteRef,r.ComputedAmount,p.AmountPrior FROM dbo.tblNORM_StatementLine t " +
            "LEFT JOIN dbo.tblNORM_LineResult r ON r.StatementLineId=t.StatementLineId AND r.CalculationRunId=@run AND r.IsDeactivated=0 " +
            "LEFT JOIN dbo.tblNORM_PublishedFigure p ON p.ConfigurationReleaseId=t.ConfigurationReleaseId AND p.StatementCode=t.StatementCode " +
            "AND p.LineCode=t.LineCode AND p.IsDeactivated=0 WHERE t.ConfigurationReleaseId=@release AND t.StatementCode=@code " +
            "AND t.IsDeactivated=0 ORDER BY t.SeqNo",
            NORMHelper.P("@run", runId), NORMHelper.P("@release", releaseId), NORMHelper.P("@code", code));
        html.Append("<section class=\"page\"><h2>").Append(Enc(title)).Append("</h2><p>").Append(atDate ? "As at" : "For the year ended").Append(" 30 June ").Append(year).Append("</p>");
        html.Append("<table><thead><tr><th></th><th>Notes</th><th class=\"amount\">").Append(year).Append("<br>$'000</th><th class=\"amount\">").Append(year - 1).Append("<br>$'000</th></tr></thead><tbody>");
        for (int i = 0; i < table.Rows.Count; i++)
        {
            DataRow row = table.Rows[i];
            string type = NORMHelper.Str(row, "LineType");
            if (type == "section")
            {
                html.Append("<tr class=\"section\"><th colspan=\"4\">").Append(Enc(NORMHelper.Str(row, "LineLabel"))).Append("</th></tr>");
                continue;
            }
            html.Append("<tr class=\"").Append(type == "total" ? "total" : "").Append("\"><th>").Append(Enc(NORMHelper.Str(row, "LineLabel"))).Append("</th><td>")
                .Append(Enc(CanonicalNote(code, NORMHelper.Str(row, "LineLabel"), NORMHelper.Str(row, "NoteRef")))).Append("</td><td class=\"amount\">").Append(Amount(row, "ComputedAmount")).Append("</td><td class=\"amount\">").Append(Amount(row, "AmountPrior")).Append("</td></tr>");
        }
        html.Append("</tbody></table><p class=\"footer\">This statement should be read with the accompanying notes.</p></section>");
    }

    private void AppendEquity(StringBuilder html, int runId, int releaseId, int year)
    {
        DataTable table = NORMHelper.Query(
            "SELECT r.LineCode,r.ComputedAmount,p.AmountPrior FROM dbo.tblNORM_LineResult r LEFT JOIN dbo.tblNORM_PublishedFigure p " +
            "ON p.ConfigurationReleaseId=@release AND p.StatementCode=r.StatementCode AND p.LineCode=r.LineCode AND p.IsDeactivated=0 " +
            "WHERE r.CalculationRunId=@run AND r.LineCode IN ('Operating result','Statement of Changes in Equity') AND r.IsDeactivated=0",
            NORMHelper.P("@release", releaseId), NORMHelper.P("@run", runId));
        decimal result = LineAmount(table, "Operating result", "ComputedAmount");
        decimal priorResult = LineAmount(table, "Operating result", "AmountPrior");
        decimal closing = LineAmount(table, "Statement of Changes in Equity", "ComputedAmount");
        decimal opening = LineAmount(table, "Statement of Changes in Equity", "AmountPrior");
        html.Append("<section class=\"page\"><h2>Statement of Changes in Equity</h2><p>For the year ended 30 June ").Append(year).Append("</p><table><thead><tr><th></th><th class=\"amount\">").Append(year).Append("<br>$'000</th><th class=\"amount\">").Append(year - 1).Append("<br>$'000</th></tr></thead><tbody>");
        AppendAmountRow(html, "Opening balance", opening, null, false);
        AppendAmountRow(html, "Total comprehensive income/(loss)", result, priorResult, false);
        AppendAmountRow(html, "Transactions with owners in their capacity as owners", closing - opening - result, null, false);
        AppendAmountRow(html, "Closing balance", closing, opening, true);
        html.Append("</tbody></table><p class=\"footer\">Equity components should be expanded to the entity's PRIMA configuration before approval.</p></section>");
    }

    private void AppendCashFlow(StringBuilder html, int runId, int releaseId, int year)
    {
        DataTable flows = NORMHelper.Query(
            "SELECT CashFlowClassSnapshot,AccountTypeSnapshot,SUM(SourceAmount)/1000.0 AS Amount FROM dbo.tblNORM_Lineage " +
            "WHERE CalculationRunId=@run AND DerivationCode='GL_MAPPING' AND CashFlowClassSnapshot IS NOT NULL GROUP BY CashFlowClassSnapshot,AccountTypeSnapshot " +
            "ORDER BY CashFlowClassSnapshot",
            NORMHelper.P("@run", runId));
        DataTable cash = NORMHelper.Query(
            "SELECT r.ComputedAmount,p.AmountPrior FROM dbo.tblNORM_LineResult r LEFT JOIN dbo.tblNORM_PublishedFigure p " +
            "ON p.ConfigurationReleaseId=@release AND p.StatementCode=r.StatementCode AND p.LineCode=r.LineCode AND p.IsDeactivated=0 " +
            "WHERE r.CalculationRunId=@run AND r.LineCode='Cash and cash equivalents' AND r.IsDeactivated=0",
            NORMHelper.P("@release", releaseId), NORMHelper.P("@run", runId));
        decimal ending = cash.Rows.Count == 0 ? 0m : NORMHelper.Dec(cash.Rows[0], "ComputedAmount");
        decimal beginning = cash.Rows.Count == 0 || cash.Rows[0].IsNull("AmountPrior") ? 0m : NORMHelper.Dec(cash.Rows[0], "AmountPrior");
        html.Append("<section class=\"page\"><h2>Cash Flow Statement</h2><p>For the year ended 30 June ").Append(year).Append("</p><table><thead><tr><th></th><th class=\"amount\">").Append(year).Append("<br>$'000</th><th class=\"amount\">").Append(year - 1).Append("<br>$'000</th></tr></thead><tbody>");
        string[] categories = new string[] { "OPERATING", "INVESTING", "FINANCING" };
        decimal net = 0m;
        for (int c = 0; c < categories.Length; c++)
        {
            html.Append("<tr class=\"section\"><th colspan=\"3\">").Append(CaseLabel(categories[c])).Append(" activities</th></tr>");
            decimal total = 0m;
            for (int i = 0; i < flows.Rows.Count; i++)
            {
                string label = NORMHelper.Str(flows.Rows[i], "CashFlowClassSnapshot");
                if (CashCategory(label) != categories[c]) { continue; }
                decimal amount = NORMHelper.Dec(flows.Rows[i], "Amount");
                amount = IsOutflow(label) ? -Math.Abs(amount) : Math.Abs(amount);
                total += amount;
                AppendAmountRow(html, label, amount, null, false);
            }
            net += total;
            AppendAmountRow(html, "Net cash from/(used by) " + CaseLabel(categories[c]).ToLowerInvariant() + " activities", total, null, true);
        }
        AppendAmountRow(html, "Net increase/(decrease) in cash held", net, null, true);
        AppendAmountRow(html, "Cash and cash equivalents at the beginning of the reporting period", beginning, null, false);
        AppendAmountRow(html, "Cash and cash equivalents at the end of the reporting period", ending, beginning, true);
        html.Append("</tbody></table><p class=\"footer\">Cash-flow classes are generated from the FY mapping configuration and remain subject to the cash reconciliation control.</p></section>");
    }

    private void AppendNotes(StringBuilder html, int year, List<NORMReportingFramework.Disclosure> disclosures)
    {
        for (int i = 0; i < disclosures.Count; i++)
        {
            NORMReportingFramework.Disclosure item = disclosures[i];
            if (!item.Required || String.IsNullOrWhiteSpace(item.NoteRef)) { continue; }
            html.Append("<section class=\"note\"><p class=\"eyebrow\">Note ").Append(Enc(item.NoteRef)).Append("</p><h2>").Append(Enc(item.Title)).Append("</h2>");
            if (item.Lines.Count > 0)
            {
                html.Append("<table><thead><tr><th></th><th class=\"amount\">").Append(year).Append("<br>$'000</th><th class=\"amount\">").Append(year - 1).Append("<br>$'000</th></tr></thead><tbody>");
                decimal priorTotal = 0m;
                bool hasPrior = false;
                for (int l = 0; l < item.Lines.Count; l++)
                {
                    html.Append("<tr><th>").Append(Enc(item.Lines[l].Label)).Append("</th><td class=\"amount\">").Append(FormatAmount(item.Lines[l].Amount)).Append("</td><td class=\"amount\">")
                        .Append(item.Lines[l].Prior.HasValue ? FormatAmount(item.Lines[l].Prior.Value) : "-").Append("</td></tr>");
                    if (item.Lines[l].Prior.HasValue) { priorTotal += item.Lines[l].Prior.Value; hasPrior = true; }
                }
                html.Append("<tr class=\"total\"><th>Total ").Append(Enc(item.Title.ToLowerInvariant())).Append("</th><td class=\"amount\">").Append(FormatAmount(item.Amount)).Append("</td><td class=\"amount\">")
                    .Append(hasPrior ? FormatAmount(priorTotal) : "-").Append("</td></tr></tbody></table>");
            }
            else { html.Append("<p><i>No mapped balance. Complete this required disclosure before sign-off.</i></p>"); }
            if (!String.IsNullOrWhiteSpace(item.Narrative))
            {
                html.Append("<div class=\"policy\"><b>Accounting policy / entity commentary</b><p>").Append(Enc(item.Narrative).Replace("\r\n", "<br>").Replace("\n", "<br>")).Append("</p></div>");
            }
            html.Append("</section>");
        }
    }

    private void AppendRegister(StringBuilder html, List<NORMReportingFramework.Disclosure> disclosures)
    {
        html.Append("<section class=\"page\"><h2>PRIMA disclosure register</h2><table class=\"register\"><thead><tr><th>Ref</th><th>Disclosure</th><th>Applicability</th><th>Status</th></tr></thead><tbody>");
        for (int i = 0; i < disclosures.Count; i++)
        {
            NORMReportingFramework.Disclosure item = disclosures[i];
            html.Append("<tr><td>").Append(Enc(item.NoteRef ?? item.SectionCode)).Append("</td><td>").Append(Enc(item.Title)).Append("</td><td>")
                .Append(item.Required ? "Required" : "Not applicable").Append("</td><td class=\"status\">").Append(Enc(item.CompletionStatus)).Append("</td></tr>");
        }
        html.Append("</tbody></table></section>");
    }

    private void AppendAmountRow(StringBuilder html, string label, decimal current, decimal? prior, bool total)
    {
        html.Append("<tr class=\"").Append(total ? "total" : "").Append("\"><th>").Append(Enc(label)).Append("</th><td class=\"amount\">")
            .Append(FormatAmount(current)).Append("</td><td class=\"amount\">").Append(prior.HasValue ? FormatAmount(prior.Value) : "-").Append("</td></tr>");
    }

    private decimal LineAmount(DataTable table, string lineCode, string column)
    {
        for (int i = 0; i < table.Rows.Count; i++)
        {
            if (String.Equals(NORMHelper.Str(table.Rows[i], "LineCode"), lineCode, StringComparison.OrdinalIgnoreCase))
            {
                return table.Rows[i].IsNull(column) ? 0m : NORMHelper.Dec(table.Rows[i], column);
            }
        }
        return 0m;
    }

    private string Amount(DataRow row, string column) { return row.IsNull(column) ? "-" : FormatAmount(NORMHelper.Dec(row, column)); }

    private string FormatAmount(decimal amount)
    {
        string value = Math.Abs(Math.Round(amount)).ToString("N0", CultureInfo.GetCultureInfo("en-AU"));
        return amount < 0m ? "(" + value + ")" : value;
    }

    private string ProfileLabel(NORMReportingFramework.ReportingProfile profile)
    {
        return profile.EntityType + "; " + profile.ReportingBasis + "; " + profile.DisclosureTier;
    }

    private string CanonicalNote(string statementCode, string label, string configured)
    {
        if (statementCode == "SOCI")
        {
            string[,] values = new string[,] {
                { "Employee benefits", "1.1A" }, { "Supplier expenses", "1.1B" }, { "Grants", "1.1C" },
                { "Finance costs", "1.1D" }, { "Impairment loss allowance on financial instruments", "1.1E" },
                { "Write-down of non-financial assets", "1.1F" }, { "Net foreign exchange losses", "1.1G" },
                { "Other expenses", "1.1H" }, { "Revenue from contracts with customers", "1.2A" },
                { "Rental income", "1.2E" }, { "Other revenue", "1.2F" },
                { "Reversals of previous asset write-downs", "1.2H" }, { "Other gains", "1.2I" },
                { "Revenue from Government", "1.2J" }
            };
            for (int i = 0; i < values.GetLength(0); i++)
            {
                if (String.Equals(label, values[i, 0], StringComparison.OrdinalIgnoreCase)) { return values[i, 1]; }
            }
        }
        if (statementCode == "SOFP")
        {
            if (label == "Prepayments") { return "3.2C"; }
            if (label == "Assets held for sale") { return "3.2D"; }
            if (label == "Employee payables" || label == "Other payables") { return "3.3"; }
            if (label == "Employee provisions" || label == "Asset restoration provisions" || label == "Other provisions") { return "3.5"; }
        }
        return configured;
    }

    private string CashCategory(string label)
    {
        string value = (label ?? "").ToLowerInvariant();
        if (value.IndexOf("purchase") >= 0 || value.IndexOf("proceeds from sale") >= 0 || value.IndexOf("proceeds from investment") >= 0 || value.IndexOf("investing") >= 0) { return "INVESTING"; }
        if (value.IndexOf("appropriation") >= 0 || value.IndexOf("opa") >= 0 || value.IndexOf("principal payments of lease") >= 0 || value.IndexOf("special account") >= 0) { return "FINANCING"; }
        return "OPERATING";
    }

    private bool IsOutflow(string label)
    {
        string value = (label ?? "").ToLowerInvariant();
        return value.IndexOf("payment") >= 0 || value.IndexOf("purchase") >= 0 || value.IndexOf("used") >= 0 || value.IndexOf("paid") >= 0 || value.IndexOf("return") >= 0 || value.IndexOf("selling cost") >= 0;
    }

    private string CaseLabel(string value)
    {
        return value.Substring(0, 1) + value.Substring(1).ToLowerInvariant();
    }

    private string Enc(string value) { return HttpUtility.HtmlEncode(value ?? ""); }

    private void WriteError(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.TrySkipIisCustomErrors = true;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Write(message);
    }
}
