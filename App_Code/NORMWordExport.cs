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
    private Dictionary<string, decimal> priorFigures;
    private Dictionary<string, decimal> priorAssetMovements;

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
        int year = NORMStartOfYearSetup.ResolveCurrentFinancialYear(
            NORMHelper.Str(header, "EntityCode"), NORMHelper.Int(header, "FinancialYear"));
        string entity = NORMHelper.Str(header, "EntityName") ?? NORMHelper.Str(header, "EntityCode");
        priorFigures = NORMStartOfYearSetup.LoadPriorActualFigures(NORMHelper.Str(header, "EntityCode"));
        priorAssetMovements = NORMStartOfYearSetup.LoadPriorAssetMovementFigures(NORMHelper.Str(header, "EntityCode"));
        NORMReportingFramework.ReportingProfile profile = NORMReportingFramework.LoadProfile(releaseId);
        List<NORMReportingFramework.Disclosure> disclosures = NORMReportingFramework.IsInstalled()
            ? NORMReportingFramework.LoadDisclosures(runId, releaseId, profile)
            : new List<NORMReportingFramework.Disclosure>();
        NORMStatementEnhancements.ApplyManualInputs(runId, disclosures);

        NORMAdministeredStatements.Model administered = NORMAdministeredStatements.Required(profile)
            ? NORMAdministeredStatements.Load(runId, releaseId, NORMHelper.Str(header, "EntityCode")) : null;
        string document = BuildDocument(runId, releaseId, year, entity, NORMHelper.Str(header, "VersionCode"), profile, disclosures, administered);
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
        NORMReportingFramework.ReportingProfile profile, List<NORMReportingFramework.Disclosure> disclosures,
        NORMAdministeredStatements.Model administered)
    {
        StringBuilder html = new StringBuilder();
        html.Append("<!doctype html><html xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:w=\"urn:schemas-microsoft-com:office:word\" lang=\"en-AU\"><head><meta charset=\"utf-8\">");
        html.Append("<title>").Append(Enc(entity)).Append(" financial statements</title>");
        html.Append("<!--[if gte mso 9]><xml><w:WordDocument><w:View>Print</w:View><w:Zoom>100</w:Zoom><w:DoNotOptimizeForBrowser/></w:WordDocument></xml><![endif]-->");
        html.Append("<style>@page{size:A4;margin:20mm 18mm 18mm}@page assetLandscape{size:29.7cm 21cm;mso-page-orientation:landscape;margin:9mm 10mm 8mm}body{font-family:Arial,sans-serif;color:#171717;font-size:9.5pt;line-height:1.35}h1{font-size:22pt;margin:0 0 8pt}h2{font-size:15pt;border-bottom:2pt solid #e87722;padding-bottom:5pt;margin:0 0 14pt}h3{font-size:11.5pt;margin:14pt 0 7pt}p{margin:0 0 8pt}.cover{padding-top:65mm}.eyebrow{color:#b64d00;font-weight:bold;text-transform:uppercase;letter-spacing:.7pt}.meta{margin-top:30pt;border-top:1pt solid #bbb;padding-top:10pt}.page{page-break-before:always}.note{page-break-before:always}.section{font-weight:bold;background:#f1f2f4}table{width:100%;border-collapse:collapse;margin:7pt 0 12pt}tr{page-break-inside:avoid}th,td{padding:4pt 5pt;border-bottom:.5pt solid #c8c8c8;vertical-align:top}th{text-align:left}.note tbody th{font-weight:normal}.note .note-group th{font-weight:bold;border-bottom:0;padding-top:8pt}.note .subtotal th{font-weight:bold}.note .subtotal td{font-weight:bold;border-top:1pt solid #222;border-bottom:1pt solid #222}.note .total th{font-weight:bold}.amount{text-align:right;width:21%}.total th,.total td{font-weight:bold;border-top:1pt solid #222;border-bottom:2pt double #222}.asset-reconciliation{page:assetLandscape}.asset-reconciliation h2{font-size:8pt;margin-bottom:5pt}.asset-reconciliation table{font-size:5pt;line-height:1.05;table-layout:fixed}.asset-reconciliation th,.asset-reconciliation td{padding:1.3pt 1.5pt}.asset-reconciliation thead th{text-align:right}.asset-reconciliation thead th:first-child{width:43mm;text-align:left}.asset-reconciliation td{text-align:right}.asset-reconciliation .section th{background:#fff;padding-top:3pt}.administered table,.administered table th,.administered table td,.administered .section{background:#d3d3d3}.administered h3{background:#222;color:#fff;padding:6pt}.policy{background:#f7f7f7;border-left:3pt solid #e87722;padding:8pt 10pt;margin:8pt 0 12pt}.small{font-size:8pt;color:#555}.register td:first-child{width:10%}.status{font-weight:bold}.footer{margin-top:18pt;border-top:.5pt solid #aaa;padding-top:6pt;font-size:8pt;color:#555}</style></head><body>");
        html.Append("<section class=\"cover\"><p class=\"eyebrow\">Financial statements preparation copy</p><h1>").Append(Enc(entity)).Append("</h1><h2>Financial statements for the year ended 30 June ").Append(year).Append("</h2>");
        html.Append("<p>Editable preparation copy generated from NORM calculation run #").Append(runId).Append(".</p><div class=\"meta\"><p><b>Configuration:</b> ").Append(Enc(version)).Append("</p>");
        html.Append("<p><b>Reporting profile:</b> ").Append(Enc(ProfileLabel(profile))).Append("</p><p><b>Generated:</b> ").Append(DateTime.UtcNow.ToString("d MMMM yyyy 'at' HH:mm 'UTC'", CultureInfo.GetCultureInfo("en-AU"))).Append("</p></div></section>");

        AppendStatement(html, runId, releaseId, year, "SOCI", "Statement of Comprehensive Income", false);
        AppendStatement(html, runId, releaseId, year, "SOFP", "Statement of Financial Position", true);
        AppendEquity(html, runId, releaseId, year);
        AppendCashFlow(html, runId, releaseId, year);
        if (administered != null)
        {
            for (int i = 0; i < administered.Statements.Count; i++) AppendAdministeredStatement(html, year, administered.Statements[i]);
            AppendAdministeredNotes(html, year, administered.Notes);
        }
        AppendNotes(html, runId, year, disclosures);
        AppendRegister(html, disclosures);
        html.Append("</body></html>");
        return html.ToString();
    }

    private void AppendAdministeredStatement(StringBuilder html, int year, NORMAdministeredStatements.Statement statement)
    {
        html.Append("<section class=\"page administered\"><h2>").Append(Enc(statement.Title)).Append("</h2><p>")
            .Append(statement.AtDate ? "As at" : "For the year ended").Append(" 30 June ").Append(year)
            .Append("</p><table><thead><tr><th></th><th>Notes</th><th class=\"amount\">").Append(year)
            .Append("<br>$'000</th><th class=\"amount\">").Append(year - 1)
            .Append("<br>$'000</th><th class=\"amount\">Original Budget<br>$'000</th></tr></thead><tbody>");
        for (int i = 0; i < statement.Rows.Count; i++)
        {
            NORMAdministeredStatements.Row row = statement.Rows[i];
            if (row.Type == "major" || row.Type == "subsection" || row.Type == "lead" || row.Type == "section")
                html.Append("<tr class=\"section\"><th colspan=\"5\">").Append(Enc(row.Label)).Append("</th></tr>");
            else
                html.Append("<tr class=\"").Append(row.Type == "total" ? "total" : "").Append("\"><th>").Append(Enc(row.Label))
                    .Append("</th><td>").Append(Enc(row.Note)).Append("</td><td class=\"amount\">").Append(Amount(row.Current))
                    .Append("</td><td class=\"amount\">").Append(Amount(row.Prior)).Append("</td><td class=\"amount\">")
                    .Append(Amount(row.Budget)).Append("</td></tr>");
        }
        html.Append("</tbody></table><p class=\"footer\">This administered schedule should be read with the accompanying administered notes.</p></section>");
    }

    private void AppendAdministeredNotes(StringBuilder html, int year, List<NORMAdministeredStatements.NoteSection> notes)
    {
        for (int n = 0; n < notes.Count; n++)
        {
            NORMAdministeredStatements.NoteSection note = notes[n];
            html.Append("<section class=\"note administered\"><h2>").Append(Enc(note.Title)).Append("</h2><table><thead><tr><th></th><th class=\"amount\">")
                .Append(year).Append("<br>$'000</th><th class=\"amount\">").Append(year - 1).Append("<br>$'000</th></tr></thead><tbody>");
            for (int i = 0; i < note.Rows.Count; i++)
            {
                NORMAdministeredStatements.Row row = note.Rows[i];
                if (row.Type == "major" || row.Type == "subsection" || row.Type == "lead" || row.Type == "section")
                    html.Append("<tr class=\"section\"><th colspan=\"3\">").Append(Enc(row.Label)).Append("</th></tr>");
                else
                    html.Append("<tr class=\"").Append(row.Type == "total" ? "total" : "").Append("\"><th>").Append(Enc(row.Label))
                        .Append("</th><td class=\"amount\">").Append(Amount(row.Current)).Append("</td><td class=\"amount\">")
                        .Append(Amount(row.Prior)).Append("</td></tr>");
            }
            html.Append("</tbody></table></section>");
        }
    }

    private void AppendStatement(StringBuilder html, int runId, int releaseId, int year, string code, string title, bool atDate)
    {
        DataTable table = NORMHelper.Query(
            "SELECT t.LineType,t.LineCode,t.LineLabel,t.NoteRef,r.ComputedAmount FROM dbo.tblNORM_StatementLine t " +
            "LEFT JOIN dbo.tblNORM_LineResult r ON r.StatementLineId=t.StatementLineId AND r.CalculationRunId=@run AND r.IsDeactivated=0 " +
            "WHERE t.ConfigurationReleaseId=@release AND t.StatementCode=@code " +
            "AND t.IsDeactivated=0 ORDER BY t.SeqNo",
            NORMHelper.P("@run", runId), NORMHelper.P("@release", releaseId), NORMHelper.P("@code", code));
        bool hasForeignExchangeGains = false;
        bool hasFinancialAssetsHeading = false;
        for (int i = 0; i < table.Rows.Count; i++)
        {
            if (String.Equals(NORMHelper.Str(table.Rows[i], "LineCode"), "Foreign exchange gains", StringComparison.OrdinalIgnoreCase))
                hasForeignExchangeGains = true;
            if (code == "SOFP" && String.Equals(NORMHelper.Str(table.Rows[i], "LineType"), "section", StringComparison.OrdinalIgnoreCase) &&
                String.Equals(NORMHelper.Str(table.Rows[i], "LineLabel"), "Financial assets", StringComparison.OrdinalIgnoreCase))
                hasFinancialAssetsHeading = true;
        }
        decimal totalIncomeCurrent = 0m, totalIncomePrior = 0m;
        bool hasTotalIncomeCurrent = false, hasTotalIncomePrior = false;
        decimal? operatingResultCurrent = null, operatingResultPrior = null;
        decimal? financialAssetsCurrent = null, financialAssetsPrior = null;
        decimal? nonFinancialAssetsCurrent = null, nonFinancialAssetsPrior = null;
        decimal? totalAssetsCurrent = null, totalAssetsPrior = null;
        decimal? totalInterestLiabilitiesCurrent = null, totalInterestLiabilitiesPrior = null;
        HashSet<string> incomeComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Revenue from contracts with customers", "Revenue in relation to special accounts", "Rental income", "Other revenue",
            "Gain on sale of asset", "Reversals of previous asset write-downs", "Foreign exchange gains", "Other gains"
        };
        if (code == "SOCI")
        {
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow source = table.Rows[i];
                string sourceCode = NORMHelper.Str(source, "LineCode");
                if (!incomeComponents.Contains(sourceCode)) { continue; }
                if (!source.IsNull("ComputedAmount")) { totalIncomeCurrent += NORMHelper.Dec(source, "ComputedAmount"); hasTotalIncomeCurrent = true; }
                decimal? effective = NORMStartOfYearSetup.FigureValue(priorFigures, code, sourceCode, null);
                if (effective.HasValue) { totalIncomePrior += effective.Value; hasTotalIncomePrior = true; }
            }
            if (!hasForeignExchangeGains)
            {
                decimal? foreignExchangePrior = NORMStartOfYearSetup.FigureValue(priorFigures, "SOCI", "Foreign exchange gains", null);
                if (foreignExchangePrior.HasValue) { totalIncomePrior += foreignExchangePrior.Value; hasTotalIncomePrior = true; }
            }
            decimal? revenueCurrent = StatementAmount(table, "Revenue from Government", "ComputedAmount");
            decimal? netCostCurrent = StatementAmount(table, "Net cost of services", "ComputedAmount");
            decimal? revenuePrior = EffectivePriorAmount(table, code, "Revenue from Government");
            decimal? netCostPrior = EffectivePriorAmount(table, code, "Net cost of services");
            if (revenueCurrent.HasValue && netCostCurrent.HasValue) operatingResultCurrent = revenueCurrent.Value - netCostCurrent.Value;
            if (revenuePrior.HasValue && netCostPrior.HasValue) operatingResultPrior = revenuePrior.Value - netCostPrior.Value;
        }
        if (code == "SOFP")
        {
            decimal? cashCurrent = StatementAmount(table, "Cash and cash equivalents", "ComputedAmount");
            decimal? receivablesCurrent = StatementAmount(table, "Trade and other receivables", "ComputedAmount");
            decimal? cashPrior = EffectivePriorAmount(table, code, "Cash and cash equivalents");
            decimal? receivablesPrior = EffectivePriorAmount(table, code, "Trade and other receivables");
            if (cashCurrent.HasValue && receivablesCurrent.HasValue) financialAssetsCurrent = cashCurrent.Value + receivablesCurrent.Value;
            if (cashPrior.HasValue && receivablesPrior.HasValue) financialAssetsPrior = cashPrior.Value + receivablesPrior.Value;

            Dictionary<string, decimal> mappedAssets = LoadMappedAssetFigures(runId);
            string[] assetClasses = new string[] { "PPE_LAND", "PPE_BUILDINGS", "PPE_SPECIALIST_MILITARY_EQUIPMENT",
                "PPE_INFRASTRUCTURE", "PPE_PLANT_AND_EQUIPMENT", "PPE_HERITAGE_AND_CULTURAL_ASSETS", "PPE_INTANGIBLES" };
            decimal currentNonFinancial = 0m, priorNonFinancial = 0m;
            bool hasCurrentNonFinancial = false, hasPriorNonFinancial = false;
            for (int i = 0; i < assetClasses.Length; i++)
            {
                decimal? currentValue = SourceValue(mappedAssets, assetClasses[i]);
                decimal? priorValue = NORMStartOfYearSetup.FigureValue(priorFigures, "SOFP", assetClasses[i], null);
                if (currentValue.HasValue) { currentNonFinancial += currentValue.Value; hasCurrentNonFinancial = true; }
                if (priorValue.HasValue) { priorNonFinancial += priorValue.Value; hasPriorNonFinancial = true; }
            }
            string[] otherNonFinancial = new string[] { "Inventories", "Prepayments" };
            for (int i = 0; i < otherNonFinancial.Length; i++)
            {
                decimal? currentValue = StatementAmount(table, otherNonFinancial[i], "ComputedAmount");
                decimal? priorValue = EffectivePriorAmount(table, code, otherNonFinancial[i]);
                if (currentValue.HasValue) { currentNonFinancial += currentValue.Value; hasCurrentNonFinancial = true; }
                if (priorValue.HasValue) { priorNonFinancial += priorValue.Value; hasPriorNonFinancial = true; }
            }
            if (hasCurrentNonFinancial) nonFinancialAssetsCurrent = currentNonFinancial;
            if (hasPriorNonFinancial) nonFinancialAssetsPrior = priorNonFinancial;
            decimal? heldCurrent = StatementAmount(table, "Assets held for sale", "ComputedAmount");
            decimal? heldPrior = EffectivePriorAmount(table, code, "Assets held for sale");
            if (financialAssetsCurrent.HasValue && nonFinancialAssetsCurrent.HasValue && heldCurrent.HasValue)
                totalAssetsCurrent = financialAssetsCurrent.Value + nonFinancialAssetsCurrent.Value + heldCurrent.Value;
            if (financialAssetsPrior.HasValue && nonFinancialAssetsPrior.HasValue && heldPrior.HasValue)
                totalAssetsPrior = financialAssetsPrior.Value + nonFinancialAssetsPrior.Value + heldPrior.Value;
            totalInterestLiabilitiesCurrent = StatementAmount(table, "Leases", "ComputedAmount");
            totalInterestLiabilitiesPrior = EffectivePriorAmount(table, code, "Leases");
        }
        html.Append("<section class=\"page\"><h2>").Append(Enc(title)).Append("</h2><p>").Append(atDate ? "As at" : "For the year ended").Append(" 30 June ").Append(year).Append("</p>");
        html.Append("<table><thead><tr><th></th><th>Notes</th><th class=\"amount\">").Append(year).Append("<br>$'000</th><th class=\"amount\">").Append(year - 1).Append("<br>$'000</th></tr></thead><tbody>");
        for (int i = 0; i < table.Rows.Count; i++)
        {
            DataRow row = table.Rows[i];
            string type = NORMHelper.Str(row, "LineType");
            if (code == "SOCI" && !hasForeignExchangeGains &&
                String.Equals(NORMHelper.Str(row, "LineCode"), "Other gains", StringComparison.OrdinalIgnoreCase))
            {
                decimal? foreignExchangePrior = NORMStartOfYearSetup.FigureValue(priorFigures, "SOCI", "Foreign exchange gains", null);
                html.Append("<tr><th>Net foreign exchange gains</th><td>1.2F</td><td class=\"amount\">-</td><td class=\"amount\">")
                    .Append(Amount(foreignExchangePrior)).Append("</td></tr>");
            }
            if (type == "section")
            {
                if (code == "SOFP" && String.Equals(NORMHelper.Str(row, "LineLabel"), "Non-financial assets", StringComparison.OrdinalIgnoreCase))
                    continue;
                html.Append("<tr class=\"section\"><th colspan=\"4\">").Append(Enc(NORMHelper.Str(row, "LineLabel"))).Append("</th></tr>");
                continue;
            }
            string lineCode = NORMHelper.Str(row, "LineCode");
            if (code == "SOFP" && !hasFinancialAssetsHeading && String.Equals(lineCode, "Cash and cash equivalents", StringComparison.OrdinalIgnoreCase))
                html.Append("<tr class=\"section\"><th colspan=\"4\">Financial assets</th></tr>");
            decimal? effectivePrior = NORMStartOfYearSetup.FigureValue(priorFigures, code, lineCode, null);
            decimal? effectiveCurrent = row.IsNull("ComputedAmount") ? (decimal?)null : NORMHelper.Dec(row, "ComputedAmount");
            string displayLabel = NORMHelper.Str(row, "LineLabel");
            if (code == "SOCI" && String.Equals(lineCode, "Total own-source income", StringComparison.OrdinalIgnoreCase))
            {
                displayLabel = "Total income";
                effectiveCurrent = hasTotalIncomeCurrent ? (decimal?)totalIncomeCurrent : null;
                effectivePrior = hasTotalIncomePrior ? (decimal?)totalIncomePrior : null;
            }
            if (code == "SOCI" && String.Equals(lineCode, "Operating result", StringComparison.OrdinalIgnoreCase))
            {
                displayLabel = "(Deficit) / Surplus";
                if (operatingResultCurrent.HasValue) effectiveCurrent = operatingResultCurrent;
                if (operatingResultPrior.HasValue) effectivePrior = operatingResultPrior;
            }
            if (code == "SOFP" && String.Equals(lineCode, "Property plant and equipment", StringComparison.OrdinalIgnoreCase))
            {
                AppendStatementAmountRow(html, "Total financial assets", "", financialAssetsCurrent, financialAssetsPrior, true);
                html.Append("<tr class=\"section\"><th colspan=\"4\">Non-financial assets</th></tr>");
                AppendWordAssetSplits(html, runId, priorFigures);
                continue;
            }
            if (code == "SOFP" && String.Equals(lineCode, "Total assets", StringComparison.OrdinalIgnoreCase))
            {
                effectiveCurrent = totalAssetsCurrent;
                effectivePrior = totalAssetsPrior;
            }
            if (code == "SOFP" && String.Equals(lineCode, "Assets held for sale", StringComparison.OrdinalIgnoreCase))
                AppendStatementAmountRow(html, "Total non-financial assets", "", nonFinancialAssetsCurrent, nonFinancialAssetsPrior, true);
            if (code == "SOFP" && String.Equals(lineCode, "Employee provisions", StringComparison.OrdinalIgnoreCase))
            {
                AppendStatementAmountRow(html, "Total interest-bearing liabilities", "", totalInterestLiabilitiesCurrent, totalInterestLiabilitiesPrior, true);
                html.Append("<tr class=\"section\"><th colspan=\"4\">Provisions</th></tr>");
            }
            if (code == "SOFP" && String.Equals(lineCode, "Statement of Changes in Equity", StringComparison.OrdinalIgnoreCase))
            {
                AppendWordEquityRows(html, runId, priorFigures);
                continue;
            }
            html.Append("<tr class=\"").Append(type == "total" ? "total" : "").Append("\"><th>").Append(Enc(displayLabel)).Append("</th><td>")
                .Append(Enc(CanonicalNote(code, NORMHelper.Str(row, "LineLabel"), NORMHelper.Str(row, "NoteRef")))).Append("</td><td class=\"amount\">").Append(Amount(effectiveCurrent)).Append("</td><td class=\"amount\">").Append(Amount(effectivePrior)).Append("</td></tr>");
        }
        if (code == "SOCI")
        {
            decimal? currentOci = LineResultValue(runId, "SOCE_TOTAL_OCI") ?? LineResultValue(runId, "OCI_REVALUATION");
            decimal? effectiveOciPrior = NORMStartOfYearSetup.FigureValue(priorFigures, "SOCE", "SOCE_TOTAL_OCI", null);
            effectiveOciPrior = NORMStartOfYearSetup.FigureValue(priorFigures, "SOCI", "OCI_REVALUATION", effectiveOciPrior);
            decimal? currentResult = operatingResultCurrent;
            decimal? priorResult = operatingResultPrior;
            decimal? totalCurrent = currentResult.HasValue && currentOci.HasValue ? (decimal?)(currentResult.Value + currentOci.Value) : null;
            decimal? totalPrior = priorResult.HasValue && effectiveOciPrior.HasValue ? (decimal?)(priorResult.Value + effectiveOciPrior.Value) : null;
            html.Append("<tr class=\"section\"><th colspan=\"4\">OTHER COMPREHENSIVE INCOME / (LOSS)</th></tr>");
            html.Append("<tr class=\"section\"><th colspan=\"4\">Items not subject to subsequent reclassification to net cost of services</th></tr>");
            AppendStatementAmountRow(html, "Changes in asset revaluation reserves", "1.3", currentOci, effectiveOciPrior, false);
            AppendStatementAmountRow(html, "Total other comprehensive income / (loss)", "", currentOci, effectiveOciPrior, true);
            AppendStatementAmountRow(html, "Total comprehensive (loss) / income", "", totalCurrent, totalPrior, true);
        }
        html.Append("</tbody></table><p class=\"footer\">This statement should be read with the accompanying notes.</p></section>");
    }

    private static decimal? StatementAmount(DataTable table, string lineCode, string column)
    {
        for (int i = 0; i < table.Rows.Count; i++)
        {
            DataRow row = table.Rows[i];
            if (String.Equals(NORMHelper.Str(row, "LineCode"), lineCode, StringComparison.OrdinalIgnoreCase))
                return row.IsNull(column) ? (decimal?)null : NORMHelper.Dec(row, column);
        }
        return null;
    }

    private void AppendWordAssetSplits(StringBuilder html, int runId, Dictionary<string, decimal> priorFigures)
    {
        Dictionary<string, decimal> current = LoadMappedAssetFigures(runId);
        string[,] classes = new string[,] { { "PPE_LAND", "Land" }, { "PPE_BUILDINGS", "Buildings" },
            { "PPE_SPECIALIST_MILITARY_EQUIPMENT", "Specialist military equipment" },
            { "PPE_INFRASTRUCTURE", "Infrastructure" }, { "PPE_PLANT_AND_EQUIPMENT", "Plant and equipment" },
            { "PPE_HERITAGE_AND_CULTURAL_ASSETS", "Heritage and cultural assets" }, { "PPE_INTANGIBLES", "Intangibles" } };
        for (int i = 0; i < classes.GetLength(0); i++)
        {
            string code = classes[i, 0];
            decimal? effectivePrior = NORMStartOfYearSetup.FigureValue(priorFigures, "SOFP", code, null);
            AppendStatementAmountRow(html, classes[i, 1], "3.2A", SourceValue(current, code), effectivePrior, false);
        }
    }

    private void AppendWordEquityRows(StringBuilder html, int runId, Dictionary<string, decimal> priorFigures)
    {
        Dictionary<string, decimal> current = LoadMappedEquityFigures(runId);
        string[,] classes = new string[,] {
            { "EQUITY_CONTRIBUTED", "Contributed equity" },
            { "EQUITY_RETAINED", "(Accumulated Deficit) / Retained surpluses" },
            { "EQUITY_RESERVES", "Reserves" }
        };
        decimal currentTotal = 0m, priorTotal = 0m;
        bool hasCurrent = false, hasPrior = false;
        for (int i = 0; i < classes.GetLength(0); i++)
        {
            decimal? currentValue = SourceValue(current, classes[i, 0]);
            decimal? priorValue = NORMStartOfYearSetup.FigureValue(priorFigures, "SOFP", classes[i, 0], null);
            AppendStatementAmountRow(html, classes[i, 1], "", currentValue, priorValue, false);
            if (currentValue.HasValue) { currentTotal += currentValue.Value; hasCurrent = true; }
            if (priorValue.HasValue) { priorTotal += priorValue.Value; hasPrior = true; }
        }
        decimal? controlledCurrentTotal = LineResultValue(runId, "Statement of Changes in Equity") ?? (hasCurrent ? (decimal?)currentTotal : null);
        decimal? controlledPriorTotal = NORMStartOfYearSetup.FigureValue(priorFigures, "SOFP", "EQUITY_TOTAL",
            hasPrior ? (decimal?)priorTotal : null);
        AppendStatementAmountRow(html, "Total equity", "", controlledCurrentTotal, controlledPriorTotal, true);
    }

    private decimal? EffectivePriorAmount(DataTable table, string statementCode, string lineCode)
    {
        for (int i = 0; i < table.Rows.Count; i++)
        {
            DataRow row = table.Rows[i];
            if (!String.Equals(NORMHelper.Str(row, "LineCode"), lineCode, StringComparison.OrdinalIgnoreCase)) { continue; }
            return NORMStartOfYearSetup.FigureValue(priorFigures, statementCode, lineCode, null);
        }
        return null;
    }

    private void AppendEquity(StringBuilder html, int runId, int releaseId, int year)
    {
        DataTable table = NORMHelper.Query(
            "SELECT r.LineCode,r.ComputedAmount FROM dbo.tblNORM_LineResult r " +
            "WHERE r.CalculationRunId=@run AND r.LineCode IN ('Operating result','Statement of Changes in Equity') AND r.IsDeactivated=0",
            NORMHelper.P("@run", runId));
        decimal result = LineAmount(table, "Operating result", "ComputedAmount");
        decimal priorResult = NORMStartOfYearSetup.FigureValue(priorFigures, "SOCE", "Operating result",
            NORMStartOfYearSetup.FigureValue(priorFigures, "SOCI", "Operating result", null)) ?? 0m;
        decimal closing = LineAmount(table, "Statement of Changes in Equity", "ComputedAmount");
        decimal opening = NORMStartOfYearSetup.FigureValue(priorFigures, "SOFP", "EQUITY_TOTAL",
            NORMStartOfYearSetup.FigureValue(priorFigures, "SOFP", "Statement of Changes in Equity",
                NORMStartOfYearSetup.FigureValue(priorFigures, "SOCE", "Statement of Changes in Equity", null))) ?? 0m;
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
            "SELECT r.ComputedAmount FROM dbo.tblNORM_LineResult r " +
            "WHERE r.CalculationRunId=@run AND r.LineCode='Cash and cash equivalents' AND r.IsDeactivated=0",
            NORMHelper.P("@run", runId));
        decimal ending = cash.Rows.Count == 0 ? 0m : NORMHelper.Dec(cash.Rows[0], "ComputedAmount");
        decimal beginning = NORMStartOfYearSetup.FigureValue(priorFigures, "SOFP", "Cash and cash equivalents",
            NORMStartOfYearSetup.FigureValue(priorFigures, "CASH", "Cash and cash equivalents", null)) ?? 0m;
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

    private void AppendNotes(StringBuilder html, int runId, int year, List<NORMReportingFramework.Disclosure> disclosures)
    {
        for (int i = 0; i < disclosures.Count; i++)
        {
            NORMReportingFramework.Disclosure item = disclosures[i];
            if (item.Code == "N2" || item.Code == "N4" || item.Code == "N7_3") { continue; }
            if (!item.Required || String.IsNullOrWhiteSpace(item.NoteRef)) { continue; }
            if (item.Code == "N3_2A")
            {
                AppendAssetMovementNote(html, runId, year, item);
                continue;
            }
            html.Append("<section class=\"note\"><p class=\"eyebrow\">Note ").Append(Enc(item.NoteRef)).Append("</p><h2>").Append(Enc(item.Title)).Append("</h2>");
            if (item.Lines.Count > 0)
            {
                html.Append("<table><thead><tr><th></th><th class=\"amount\">").Append(year).Append("<br>$'000</th><th class=\"amount\">").Append(year - 1).Append("<br>$'000</th></tr></thead><tbody>");
                decimal priorTotal = 0m;
                bool hasPrior = false;
                for (int l = 0; l < item.Lines.Count; l++)
                {
                    NORMReportingFramework.NoteLine line = item.Lines[l];
                    if (String.Equals(line.LineType, "section", StringComparison.OrdinalIgnoreCase))
                    {
                        html.Append("<tr class=\"note-group\"><th colspan=\"3\">").Append(Enc(line.Label)).Append("</th></tr>");
                        continue;
                    }
                    html.Append("<tr").Append(String.Equals(line.LineType, "subtotal", StringComparison.OrdinalIgnoreCase) ? " class=\"subtotal\"" : "")
                        .Append("><th>").Append(Enc(line.Label)).Append("</th><td class=\"amount\">").Append(FormatAmount(line.Amount)).Append("</td><td class=\"amount\">")
                        .Append(line.Prior.HasValue ? FormatAmount(line.Prior.Value) : "-").Append("</td></tr>");
                    if (line.ContributesToTotal && line.Prior.HasValue) { priorTotal += line.Prior.Value; hasPrior = true; }
                }
                html.Append("<tr class=\"total\"><th>").Append(Enc(NoteTotalLabel(item))).Append("</th><td class=\"amount\">").Append(FormatAmount(item.Amount)).Append("</td><td class=\"amount\">")
                    .Append(item.PriorAmount.HasValue ? FormatAmount(item.PriorAmount.Value) : (hasPrior ? FormatAmount(priorTotal) : "-")).Append("</td></tr></tbody></table>");
                if (item.DemoSeeded) html.Append("<p class=\"small\"><b>Demo reconstruction source:</b> ").Append(Enc(item.CurrentSourceReference)).Append("</p>");
            }
            else { html.Append("<p><i>No mapped balance. Complete this required disclosure before sign-off.</i></p>"); }
            if (!String.IsNullOrWhiteSpace(item.Narrative))
            {
                html.Append("<div class=\"policy\"><b>Written disclosures and accounting policy</b><p>").Append(Enc(item.Narrative).Replace("\r\n", "<br>").Replace("\n", "<br>")).Append("</p></div>");
            }
            html.Append("</section>");
        }
    }

    private void AppendAssetMovementNote(StringBuilder html, int runId, int year,
        NORMReportingFramework.Disclosure item)
    {
        string[,] classes = new string[,]
        {
            { "LAND", "Land" }, { "BUILDINGS", "Buildings" },
            { "SPECIALIST_MILITARY_EQUIPMENT", "Specialist military equipment" },
            { "INFRASTRUCTURE", "Infrastructure" }, { "PLANT_EQUIPMENT", "Plant and equipment" },
            { "HERITAGE_CULTURAL", "Heritage and cultural assets" },
            { "COMPUTER_SOFTWARE_PURCHASED", "Computer software - purchased" },
            { "COMPUTER_SOFTWARE_INTERNALLY_GENERATED", "Computer software - internally generated" },
            { "OTHER_INTANGIBLES_PURCHASED", "Other intangibles - purchased" },
            { "OTHER_INTANGIBLES_INTERNALLY_GENERATED", "Other intangibles - internally generated" }
        };
        Dictionary<string, decimal> closing = LoadAssetMovementAmounts(runId, "Property plant and equipment", null);
        Dictionary<string, decimal> closingGross = LoadAssetMovementAmounts(runId, "Property plant and equipment", false);
        Dictionary<string, decimal> closingAccumulated = LoadAssetMovementAmounts(runId, "Property plant and equipment", true);
        Dictionary<string, decimal> depreciation = LoadAssetMovementAmounts(runId, "Depreciation and amortisation", null);
        int count = classes.GetLength(0);
        decimal?[] openingGross = new decimal?[count], openingAccumulated = new decimal?[count], opening = new decimal?[count];
        decimal?[] depreciationMovement = new decimal?[count], residual = new decimal?[count], totalMovement = new decimal?[count];
        decimal?[] close = new decimal?[count], closeGross = new decimal?[count], closeAccumulated = new decimal?[count];
        for (int c = 0; c < count; c++)
        {
            string classCode = classes[c, 0], label = classes[c, 1];
            openingGross[c] = AssetMovementValue("CLOSING_GROSS", classCode);
            openingAccumulated[c] = AssetMovementValue("CLOSING_ACCUMULATED", classCode);
            opening[c] = AssetMovementValue("CLOSING_CARRYING", classCode);
            close[c] = DictionaryValue(closing, label);
            closeGross[c] = DictionaryValue(closingGross, label);
            closeAccumulated[c] = DictionaryValue(closingAccumulated, label);
            depreciationMovement[c] = DictionaryValue(depreciation, label);
            if (depreciationMovement[c].HasValue) depreciationMovement[c] = -Math.Abs(depreciationMovement[c].Value);
            totalMovement[c] = close[c].HasValue && opening[c].HasValue ? (decimal?)(close[c].Value - opening[c].Value) : null;
            residual[c] = totalMovement[c].HasValue ? (decimal?)(totalMovement[c].Value - (depreciationMovement[c] ?? 0m)) : null;
        }

        html.Append("<section class=\"note asset-reconciliation\"><p class=\"eyebrow\">Note ").Append(Enc(item.NoteRef))
            .Append("</p><h2>").Append(Enc(item.Title)).Append("</h2><table><thead><tr><th>Movement</th>");
        for (int c = 0; c < count; c++) html.Append("<th>").Append(Enc(classes[c, 1])).Append("<br>$'000</th>");
        html.Append("<th>Total<br>$'000</th></tr></thead><tbody>");
        AppendAssetSection(html, "As at 1 July " + (year - 1).ToString(CultureInfo.InvariantCulture), count + 2);
        AppendAssetMovementRow(html, "Gross book value", openingGross, false);
        AppendAssetMovementRow(html, "Accumulated depreciation, amortisation and impairment", openingAccumulated, false);
        AppendAssetMovementRow(html, "Total as at 1 July " + (year - 1).ToString(CultureInfo.InvariantCulture), opening, true);
        AppendAssetSection(html, "Additions", count + 2);
        AppendAssetMovementRow(html, "By purchase or internally developed", new decimal?[count], false);
        AppendAssetMovementRow(html, "Right-of-use assets", new decimal?[count], false);
        AppendAssetMovementRow(html, "Revaluations and impairments recognised in other comprehensive income", new decimal?[count], false);
        AppendAssetMovementRow(html, "Reclassification", new decimal?[count], false);
        AppendAssetMovementRow(html, "Depreciation and amortisation", depreciationMovement, false);
        AppendAssetMovementRow(html, "Depreciation of right-of-use assets", new decimal?[count], false);
        AppendAssetMovementRow(html, "Revaluations / write-downs recognised in net cost of services", new decimal?[count], false);
        AppendAssetSection(html, "Other movements", count + 2);
        AppendAssetMovementRow(html, "Reversal of previous asset write-downs and impairment", new decimal?[count], false);
        AppendAssetMovementRow(html, "Transfers in / (out)", new decimal?[count], false);
        AppendAssetMovementRow(html, "Transfers (to) / from assets held for sale", new decimal?[count], false);
        AppendAssetMovementRow(html, "Remeasurement of right-of-use assets", new decimal?[count], false);
        AppendAssetMovementRow(html, "Other movements pending asset-register classification", residual, false);
        AppendAssetSection(html, "Disposals", count + 2);
        AppendAssetMovementRow(html, "Other disposals", new decimal?[count], false);
        AppendAssetMovementRow(html, "Total movements", totalMovement, true);
        AppendAssetMovementRow(html, "Total as at 30 June " + year.ToString(CultureInfo.InvariantCulture), close, true);
        AppendAssetSection(html, "Total as at 30 June " + year.ToString(CultureInfo.InvariantCulture) + " represented by", count + 2);
        AppendAssetMovementRow(html, "Gross book value", closeGross, false);
        AppendAssetMovementRow(html, "Accumulated depreciation, amortisation and impairment", closeAccumulated, false);
        AppendAssetMovementRow(html, "Total as at 30 June " + year.ToString(CultureInfo.InvariantCulture), close, true);
        AppendAssetMovementRow(html, "Carrying amount of right-of-use assets", new decimal?[count], false);
        html.Append("</tbody></table></section>");
        if (!String.IsNullOrWhiteSpace(item.Narrative))
            html.Append("<section class=\"note\"><h2>Note ").Append(Enc(item.NoteRef)).Append(": Written disclosures and accounting policy</h2><div class=\"policy\"><b>Written disclosures and accounting policy</b><p>")
                .Append(Enc(item.Narrative).Replace("\r\n", "<br>").Replace("\n", "<br>")).Append("</p></div></section>");
    }

    private void AppendAssetSection(StringBuilder html, string label, int columns)
    {
        html.Append("<tr class=\"section\"><th colspan=\"").Append(columns).Append("\">").Append(Enc(label)).Append("</th></tr>");
    }

    private void AppendAssetMovementRow(StringBuilder html, string label, decimal?[] values, bool total)
    {
        html.Append("<tr class=\"").Append(total ? "total" : "").Append("\"><th>").Append(Enc(label)).Append("</th>");
        decimal sum = 0m;
        bool hasValue = false;
        for (int i = 0; i < values.Length; i++)
        {
            html.Append("<td>").Append(Amount(values[i])).Append("</td>");
            if (values[i].HasValue) { sum += values[i].Value; hasValue = true; }
        }
        html.Append("<td>").Append(hasValue ? FormatAmount(sum) : "-").Append("</td></tr>");
    }

    private decimal? AssetMovementValue(string rowCode, string classCode)
    {
        decimal value;
        return priorAssetMovements != null && priorAssetMovements.TryGetValue(rowCode + "|" + classCode, out value)
            ? (decimal?)value : null;
    }

    private decimal? DictionaryValue(Dictionary<string, decimal> values, string key)
    {
        decimal value;
        return values != null && values.TryGetValue(key, out value) ? (decimal?)value : null;
    }

    private Dictionary<string, decimal> LoadAssetMovementAmounts(int runId, string lineCode, bool? accumulated)
    {
        string classification = "CASE WHEN UPPER(NoteSubLineSnapshot) LIKE 'LAND%' THEN 'Land' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'BUILD%' THEN 'Buildings' WHEN UPPER(NoteSubLineSnapshot) LIKE 'SME%' THEN 'Specialist military equipment' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'IFA%' THEN 'Infrastructure' WHEN UPPER(NoteSubLineSnapshot) LIKE 'P&E%' THEN 'Plant and equipment' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'HCA%' THEN 'Heritage and cultural assets' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'CS PURCHASED%' THEN 'Computer software - purchased' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'CS INTERNALLY%' THEN 'Computer software - internally generated' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'OTHER INTANGIBLES PURCHASED%' THEN 'Other intangibles - purchased' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'OTHER INTANGIBLES INTERNALLY%' THEN 'Other intangibles - internally generated' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'CS%' THEN 'Computer software - purchased' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE '%INTANGIBLE%' THEN 'Other intangibles - purchased' ELSE 'Plant and equipment' END";
        string filter = !accumulated.HasValue ? "" : accumulated.Value
            ? " AND LOWER(COALESCE(NoteSubLineSnapshot,'')+' '+COALESCE(tb.GlText,'')) LIKE '%accum%' "
            : " AND LOWER(COALESCE(NoteSubLineSnapshot,'')+' '+COALESCE(tb.GlText,'')) NOT LIKE '%accum%' ";
        DataTable table = NORMHelper.Query("SELECT " + classification + " AS AssetClass,SUM(PresentedContribution) Amount " +
            "FROM dbo.tblNORM_Lineage l INNER JOIN dbo.tblNORM_LineResult r ON r.LineResultId=l.LineResultId " +
            "INNER JOIN dbo.tblNORM_TrialBalanceRow tb ON tb.TbRowId=l.TbRowId " +
            "WHERE l.CalculationRunId=@run AND r.LineCode=@line " + filter + "GROUP BY " + classification,
            NORMHelper.P("@run", runId), NORMHelper.P("@line", lineCode));
        return FigureDictionary(table, "AssetClass");
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

    private void AppendStatementAmountRow(StringBuilder html, string label, string note, decimal? current, decimal? prior, bool total)
    {
        html.Append("<tr class=\"").Append(total ? "total" : "").Append("\"><th>").Append(Enc(label)).Append("</th><td>")
            .Append(Enc(note)).Append("</td><td class=\"amount\">").Append(Amount(current)).Append("</td><td class=\"amount\">")
            .Append(Amount(prior)).Append("</td></tr>");
    }

    private decimal? SourceValue(Dictionary<string, decimal> values, string code)
    {
        decimal value;
        return values != null && values.TryGetValue(code, out value) ? (decimal?)value : null;
    }

    private decimal? LineResultValue(int runId, string lineCode)
    {
        object value = NORMHelper.Scalar(
            "SELECT TOP 1 ComputedAmount FROM dbo.tblNORM_LineResult WHERE CalculationRunId=@run " +
            "AND LineCode=@line AND IsDeactivated=0 ORDER BY LineResultId DESC",
            NORMHelper.P("@run", runId), NORMHelper.P("@line", lineCode));
        return value == null || value == DBNull.Value ? (decimal?)null : Convert.ToDecimal(value);
    }

    private Dictionary<string, decimal> LoadMappedAssetFigures(int runId)
    {
        DataTable table = NORMHelper.Query(
            "SELECT CASE WHEN UPPER(NoteSubLineSnapshot) LIKE 'LAND%' THEN 'PPE_LAND' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'BUILD%' THEN 'PPE_BUILDINGS' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'SME%' THEN 'PPE_SPECIALIST_MILITARY_EQUIPMENT' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'IFA%' THEN 'PPE_INFRASTRUCTURE' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'HCA%' THEN 'PPE_HERITAGE_AND_CULTURAL_ASSETS' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'CS%' OR UPPER(NoteSubLineSnapshot) LIKE '%INTANGIBLE%' THEN 'PPE_INTANGIBLES' " +
            "ELSE 'PPE_PLANT_AND_EQUIPMENT' END AS ClassCode,SUM(PresentedContribution) AS Amount " +
            "FROM dbo.tblNORM_Lineage l INNER JOIN dbo.tblNORM_LineResult r ON r.LineResultId=l.LineResultId " +
            "WHERE l.CalculationRunId=@run AND r.LineCode='Property plant and equipment' AND l.DerivationCode='GL_MAPPING' " +
            "GROUP BY CASE WHEN UPPER(NoteSubLineSnapshot) LIKE 'LAND%' THEN 'PPE_LAND' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'BUILD%' THEN 'PPE_BUILDINGS' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'SME%' THEN 'PPE_SPECIALIST_MILITARY_EQUIPMENT' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'IFA%' THEN 'PPE_INFRASTRUCTURE' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'HCA%' THEN 'PPE_HERITAGE_AND_CULTURAL_ASSETS' " +
            "WHEN UPPER(NoteSubLineSnapshot) LIKE 'CS%' OR UPPER(NoteSubLineSnapshot) LIKE '%INTANGIBLE%' THEN 'PPE_INTANGIBLES' " +
            "ELSE 'PPE_PLANT_AND_EQUIPMENT' END", NORMHelper.P("@run", runId));
        return FigureDictionary(table, "ClassCode");
    }

    private Dictionary<string, decimal> LoadMappedEquityFigures(int runId)
    {
        DataTable table = NORMHelper.Query(
            "SELECT CASE WHEN LOWER(NoteSubLineSnapshot) LIKE '%contributed%' THEN 'EQUITY_CONTRIBUTED' " +
            "WHEN LOWER(NoteSubLineSnapshot) LIKE '%reserve%' THEN 'EQUITY_RESERVES' " +
            "WHEN LOWER(NoteSubLineSnapshot) LIKE '%retained%' OR LOWER(NoteSubLineSnapshot) LIKE '%accumulated%' THEN 'EQUITY_RETAINED' " +
            "ELSE 'EQUITY_OTHER' END AS ClassCode,SUM(PresentedContribution) AS Amount " +
            "FROM dbo.tblNORM_Lineage l INNER JOIN dbo.tblNORM_LineResult r ON r.LineResultId=l.LineResultId " +
            "WHERE l.CalculationRunId=@run AND r.LineCode='Statement of Changes in Equity' " +
            "GROUP BY CASE WHEN LOWER(NoteSubLineSnapshot) LIKE '%contributed%' THEN 'EQUITY_CONTRIBUTED' " +
            "WHEN LOWER(NoteSubLineSnapshot) LIKE '%reserve%' THEN 'EQUITY_RESERVES' " +
            "WHEN LOWER(NoteSubLineSnapshot) LIKE '%retained%' OR LOWER(NoteSubLineSnapshot) LIKE '%accumulated%' THEN 'EQUITY_RETAINED' " +
            "ELSE 'EQUITY_OTHER' END", NORMHelper.P("@run", runId));
        return FigureDictionary(table, "ClassCode");
    }

    private Dictionary<string, decimal> FigureDictionary(DataTable table, string codeColumn)
    {
        Dictionary<string, decimal> values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < table.Rows.Count; i++)
            values[NORMHelper.Str(table.Rows[i], codeColumn)] = NORMHelper.Dec(table.Rows[i], "Amount");
        return values;
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
    private string Amount(decimal? amount) { return amount.HasValue ? FormatAmount(amount.Value) : "-"; }

    private string FormatAmount(decimal amount)
    {
        string value = Math.Abs(Math.Round(amount)).ToString("N0", CultureInfo.GetCultureInfo("en-AU"));
        return amount < 0m ? "(" + value + ")" : value;
    }

    private string ProfileLabel(NORMReportingFramework.ReportingProfile profile)
    {
        return profile.EntityType + "; " + profile.ReportingBasis + "; " + profile.DisclosureTier;
    }

    private string NoteTotalLabel(NORMReportingFramework.Disclosure item)
    {
        return item.Code == "N1_1B" ? "Total suppliers expenses" : "Total " + item.Title.ToLowerInvariant();
    }

    private string CanonicalNote(string statementCode, string label, string configured)
    {
        if (statementCode == "SOCI")
        {
            string[,] values = new string[,] {
                { "Employee benefits", "1.1A" }, { "Supplier expenses", "1.1B" }, { "Grants", "1.1C" },
                { "Finance costs", "1.1D" }, { "Impairment loss allowance on financial instruments", "1.1E" },
                { "Write-down of non-financial assets", "1.1F" }, { "Net foreign exchange losses", "1.2F" },
                { "Other expenses", "1.1H" }, { "Revenue from contracts with customers", "1.2A" },
                { "Rental income", "1.2E" }, { "Other revenue", "1.2F" },
                { "Net foreign exchange gains", "1.2F" }, { "Reversals of previous asset write-downs", "1.2H" }, { "Other gains", "1.2I" },
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
