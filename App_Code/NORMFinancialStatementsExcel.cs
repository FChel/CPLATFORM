using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Web;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace CPlatform.NORM
{
    /// <summary>
    /// Produces the accountant-facing financial statement workbook. Each face and required
    /// PRIMA note is a separate worksheet so teams can retain it as the cover sheet for the
    /// supporting workpaper prepared underneath.
    /// </summary>
    public class NORM_FinancialStatementsExcel : IHttpHandler
    {
        private static readonly Color Ink = Color.FromArgb(28, 35, 40);
        private static readonly Color Green = Color.FromArgb(0, 133, 91);
        private static readonly Color DarkGreen = Color.FromArgb(48, 66, 60);
        private static readonly Color PaleGreen = Color.FromArgb(229, 242, 236);
        private static readonly Color PaleGrey = Color.FromArgb(224, 224, 224);
        private static readonly Color SoftGrey = Color.FromArgb(244, 245, 246);
        private static readonly Color Amber = Color.FromArgb(255, 243, 205);
        private const string AmountFormat = "#,##0;[Red](#,##0);-";

        private sealed class ExportContext
        {
            public int RunId;
            public int ReleaseId;
            public int Year;
            public string EntityCode;
            public string Entity;
            public string Version;
            public NORMReportingFramework.ReportingProfile Profile;
            public List<NORMReportingFramework.Disclosure> Disclosures;
            public Dictionary<string, decimal> Budgets;
            public Dictionary<string, decimal> PriorFigures;
            public DataTable ManualInputs;
        }

        private sealed class FaceRow
        {
            public string Type;
            public string Code;
            public string Label;
            public string Note;
            public decimal? Current;
            public decimal? Prior;
            public decimal? Budget;
            public string FormulaSpec;
        }

        private sealed class CashRow
        {
            public string Category;
            public string Label;
            public decimal Current;
        }

        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext context)
        {
            if (context.User == null || context.User.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                WriteError(context, 401, "Sign in to export the financial statements.");
                return;
            }
            int runId;
            if (!Int32.TryParse(context.Request.QueryString["run"], out runId) || runId <= 0)
            {
                WriteError(context, 400, "Choose a completed calculation run.");
                return;
            }
            ExportContext model = LoadContext(runId);
            if (model == null)
            {
                WriteError(context, 404, "The completed calculation run was not found.");
                return;
            }

            using (ExcelPackage package = BuildWorkbook(model))
            {
                byte[] content = package.GetAsByteArray();
                string fileName = SafeFileName(model.Entity) + "_FY" + model.Year.ToString(CultureInfo.InvariantCulture) +
                    "_Financial_Statements_Run_" + runId.ToString(CultureInfo.InvariantCulture) + ".xlsx";
                context.Response.Clear();
                context.Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                context.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + fileName + "\"");
                context.Response.AddHeader("X-Content-Type-Options", "nosniff");
                context.Response.OutputStream.Write(content, 0, content.Length);
                context.Response.Flush();
                context.ApplicationInstance.CompleteRequest();
            }
        }

        private static ExcelPackage BuildWorkbook(ExportContext model)
        {
            ExcelPackage package = new ExcelPackage();
            package.Workbook.Properties.Title = model.Entity + " financial statements FY" + model.Year.ToString();
            package.Workbook.Properties.Subject = "Financial statement and note workpaper coversheets";
            package.Workbook.Properties.Author = NORMHelper.CurrentUserId();
            package.Workbook.Properties.Company = model.Entity;

            ExcelWorksheet contents = package.Workbook.Worksheets.Add("Contents");
            List<Tuple<string, string, string>> index = new List<Tuple<string, string, string>>();
            AddOverview(package, model, index);
            AddSignOff(package, model, index);
            AddFace(package, model, "SOCI", "SoCI", "Statement of Comprehensive Income", false, index);
            AddFace(package, model, "SOFP", "SoFP", "Statement of Financial Position", true, index);
            AddEquity(package, model, index);
            AddCashFlow(package, model, index);

            if (Required(model.Profile, "ADMINISTERED_ACTIVITIES"))
            {
                AddAdministeredTemplate(package, model, "Admin SoCI", "Administered Schedule of Comprehensive Income", false, index);
                AddAdministeredTemplate(package, model, "Admin SoFP", "Administered Schedule of Assets and Liabilities", true, index);
                AddAdministeredTemplate(package, model, "Admin SoCE", "Administered Reconciliation Schedule", false, index);
                AddAdministeredTemplate(package, model, "Admin Cash Flow", "Administered Cash Flow Statement", false, index);
            }

            AddNotes(package, model, index);
            AddContents(contents, model, index);
            foreach (ExcelWorksheet worksheet in package.Workbook.Worksheets)
            {
                FinishSheet(worksheet);
            }
            package.Workbook.View.ActiveTab = 0;
            return package;
        }

        private static ExportContext LoadContext(int runId)
        {
            DataTable table = NORMHelper.Query(
                "SELECT r.ConfigurationReleaseId,i.FinancialYear,i.EntityCode,e.EntityName,c.VersionCode " +
                "FROM dbo.tblNORM_CalculationRun r INNER JOIN dbo.tblNORM_Import i ON i.ImportId=r.ImportId " +
                "INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=r.ConfigurationReleaseId " +
                "LEFT JOIN dbo.tblNORM_ReportingEntity e ON e.FinancialYear=i.FinancialYear AND e.EntityCode=i.EntityCode AND e.IsDeactivated=0 " +
                "WHERE r.CalculationRunId=@run AND r.StatusCode='Complete' AND r.IsDeactivated=0 AND i.IsDeactivated=0",
                NORMHelper.P("@run", runId));
            if (table.Rows.Count == 0) { return null; }
            DataRow row = table.Rows[0];
            ExportContext model = new ExportContext();
            model.RunId = runId;
            model.ReleaseId = NORMHelper.Int(row, "ConfigurationReleaseId");
            model.Year = NORMStartOfYearSetup.ResolveCurrentFinancialYear(
                NORMHelper.Str(row, "EntityCode"), NORMHelper.Int(row, "FinancialYear"));
            model.EntityCode = NORMHelper.Str(row, "EntityCode");
            model.Entity = NORMHelper.Str(row, "EntityName") ?? NORMHelper.Str(row, "EntityCode");
            model.Version = NORMHelper.Str(row, "VersionCode");
            model.Profile = NORMReportingFramework.LoadProfile(model.ReleaseId);
            model.Disclosures = NORMReportingFramework.LoadDisclosures(runId, model.ReleaseId, model.Profile);
            NORMStatementEnhancements.ApplyManualInputs(runId, model.Disclosures);
            model.Budgets = NORMStatementEnhancements.LoadBudgetFigures(runId);
            NORMStartOfYearSetup.OverlayFigures(model.Budgets, NORMStartOfYearSetup.LoadOriginalBudgetFigures(model.EntityCode));
            model.PriorFigures = NORMStartOfYearSetup.LoadPriorActualFigures(model.EntityCode);
            model.ManualInputs = NORMStatementEnhancements.LoadManualInputs(runId);
            return model;
        }

        private static void AddContents(ExcelWorksheet sheet, ExportContext model, List<Tuple<string, string, string>> index)
        {
            sheet.View.ShowGridLines = false;
            sheet.Cells[1, 1, 1, 5].Merge = true;
            sheet.Cells[1, 1].Value = model.Entity;
            sheet.Cells[2, 1, 2, 5].Merge = true;
            sheet.Cells[2, 1].Value = "Financial statements · FY" + model.Year.ToString();
            sheet.Cells[4, 1, 4, 5].Merge = true;
            sheet.Cells[4, 1].Value = "Workbook contents";
            StyleTitle(sheet.Cells[1, 1, 1, 5]);
            sheet.Cells[2, 1, 2, 5].Style.Font.Size = 14;
            sheet.Cells[2, 1, 2, 5].Style.Font.Italic = true;
            string[] headers = { "Section", "Reference", "Worksheet", "Purpose", "Open" };
            for (int c = 0; c < headers.Length; c++) sheet.Cells[6, c + 1].Value = headers[c];
            StyleHeader(sheet.Cells[6, 1, 6, 5], false);
            for (int i = 0; i < index.Count; i++)
            {
                int row = 7 + i;
                string[] parts = index[i].Item3.Split(new char[] { '|' }, 2);
                sheet.Cells[row, 1].Value = parts[0];
                sheet.Cells[row, 2].Value = index[i].Item1;
                sheet.Cells[row, 3].Value = index[i].Item2;
                sheet.Cells[row, 4].Value = parts.Length > 1 ? parts[1] : "Workpaper cover sheet";
                sheet.Cells[row, 5].Value = "Open →";
                sheet.Cells[row, 5].Hyperlink = new ExcelHyperLink("'" + index[i].Item2.Replace("'", "''") + "'!A1", "Open →");
                sheet.Cells[row, 5].Style.Font.Color.SetColor(Green);
                sheet.Cells[row, 5].Style.Font.UnderLine = true;
                if (parts[0] == "Administered") SetFill(sheet.Cells[row, 1, row, 5], PaleGrey);
            }
            sheet.Cells[7, 1, Math.Max(7, 6 + index.Count), 5].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            sheet.Cells[7, 1, Math.Max(7, 6 + index.Count), 5].Style.Border.Bottom.Style = ExcelBorderStyle.Hair;
            sheet.Column(1).Width = 18;
            sheet.Column(2).Width = 14;
            sheet.Column(3).Width = 32;
            sheet.Column(4).Width = 68;
            sheet.Column(5).Width = 12;
            sheet.View.FreezePanes(7, 1);
        }

        private static void AddOverview(ExcelPackage package, ExportContext model, List<Tuple<string, string, string>> index)
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Overview");
            AddBackLink(sheet);
            AddStatementTitle(sheet, model, "Financial statements overview", false);
            string[,] facts = {
                { "Reporting entity", model.Entity },
                { "Reporting period", "Year ended 30 June " + model.Year.ToString() },
                { "Reporting basis", model.Profile.ReportingBasis },
                { "Disclosure tier", model.Profile.DisclosureTier },
                { "Configuration", model.Version },
                { "Calculation run", "#" + model.RunId.ToString() }
            };
            int row = 7;
            for (int i = 0; i < facts.GetLength(0); i++, row++)
            {
                sheet.Cells[row, 1].Value = facts[i, 0];
                sheet.Cells[row, 2, row, 5].Merge = true;
                sheet.Cells[row, 2].Value = facts[i, 1];
                sheet.Cells[row, 1].Style.Font.Bold = true;
                sheet.Cells[row, 1, row, 5].Style.Border.Bottom.Style = ExcelBorderStyle.Hair;
            }
            row += 2;
            sheet.Cells[row, 1, row, 5].Merge = true;
            sheet.Cells[row, 1].Value = "Preparation control";
            StyleSection(sheet.Cells[row, 1, row, 5], false);
            row++;
            sheet.Cells[row, 1, row + 2, 5].Merge = true;
            sheet.Cells[row, 1].Value = "Figures are generated from the selected frozen NORM run. Blank comparative, budget or note-movement cells identify controlled inputs that have not yet been supplied; they are not zero balances.";
            sheet.Cells[row, 1].Style.WrapText = true;
            sheet.Cells[row, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            index.Add(Tuple.Create("", sheet.Name, "Front matter|Entity, period and source-run overview"));
        }

        private static void AddSignOff(ExcelPackage package, ExportContext model, List<Tuple<string, string, string>> index)
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Sign-off");
            AddBackLink(sheet);
            AddStatementTitle(sheet, model, "Financial statements sign-off", false);
            string[] headers = { "Stage", "Name", "Position", "Date", "Signature / evidence reference" };
            for (int c = 0; c < headers.Length; c++) sheet.Cells[7, c + 1].Value = headers[c];
            StyleHeader(sheet.Cells[7, 1, 7, 5], false);
            string[] stages = { "Prepared", "Financial statements lead review", "Chief Financial Officer review", "Audit clearance", "Accountable Authority approval" };
            for (int i = 0; i < stages.Length; i++)
            {
                int row = 8 + i * 2;
                sheet.Cells[row, 1].Value = stages[i];
                sheet.Cells[row, 1].Style.Font.Bold = true;
                sheet.Cells[row, 1, row + 1, 1].Merge = true;
                sheet.Cells[row, 1, row + 1, 5].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            }
            sheet.Column(1).Width = 31;
            sheet.Column(2).Width = 24;
            sheet.Column(3).Width = 31;
            sheet.Column(4).Width = 15;
            sheet.Column(5).Width = 45;
            index.Add(Tuple.Create("", sheet.Name, "Front matter|Preparation, review and approval record"));
        }

        private static void AddFace(ExcelPackage package, ExportContext model, string code, string sheetName,
            string title, bool atDate, List<Tuple<string, string, string>> index)
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add(sheetName);
            AddBackLink(sheet);
            AddStatementTitle(sheet, model, title, atDate);
            List<FaceRow> rows = LoadFaceRows(model, code);
            int headerRow = 7;
            sheet.Cells[headerRow, 1].Value = "";
            sheet.Cells[headerRow, 2].Value = "Notes";
            sheet.Cells[headerRow, 3].Value = model.Year;
            sheet.Cells[headerRow, 4].Value = model.Year - 1;
            sheet.Cells[headerRow, 5].Value = "Original Budget";
            StyleHeader(sheet.Cells[headerRow, 1, headerRow, 5], false);
            sheet.Cells[headerRow, 3, headerRow, 5].Style.VerticalAlignment = ExcelVerticalAlignment.Bottom;
            sheet.Cells[headerRow, 3, headerRow, 5].Style.Numberformat.Format = "0\n$'000";

            Dictionary<string, int> excelRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Count; i++)
            {
                FaceRow item = rows[i];
                int row = headerRow + 1 + i;
                if (item.Type == "major" || item.Type == "section" || item.Type == "subsection")
                {
                    sheet.Cells[row, 1, row, 5].Merge = true;
                    sheet.Cells[row, 1].Value = item.Label;
                    StyleSection(sheet.Cells[row, 1, row, 5], item.Type == "major");
                    continue;
                }
                sheet.Cells[row, 1].Value = item.Label;
                sheet.Cells[row, 2].Value = item.Note;
                SetRounded(sheet.Cells[row, 3], item.Current);
                SetRounded(sheet.Cells[row, 4], item.Prior);
                SetRounded(sheet.Cells[row, 5], item.Budget);
                sheet.Cells[row, 3, row, 5].Style.Numberformat.Format = AmountFormat;
                sheet.Cells[row, 3, row, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                if (!String.IsNullOrWhiteSpace(item.Code)) excelRows[item.Code] = row;
                if (item.Type == "total") StyleTotal(sheet.Cells[row, 1, row, 5]);
            }

            ApplyFaceFormulas(sheet, rows, excelRows, headerRow);
            if (code == "SOFP") ApplySofpBalanceFormula(sheet, excelRows);
            sheet.Column(1).Width = 53;
            sheet.Column(2).Width = 11;
            sheet.Column(3).Width = 17;
            sheet.Column(4).Width = 17;
            sheet.Column(5).Width = 19;
            sheet.View.FreezePanes(headerRow + 1, 3);
            index.Add(Tuple.Create("", sheet.Name, "Primary statements|" + title));
        }

        private static List<FaceRow> LoadFaceRows(ExportContext model, string code)
        {
            DataTable table = NORMHelper.Query(
                "SELECT t.SeqNo,t.LineType,t.LineCode,t.LineLabel,t.NoteRef,t.CalculationKind,t.FormulaSpec," +
                "r.LineResultId,r.ComputedAmount,p.AmountCurrent AS PublishedCurrent,p.AmountPrior,b.OriginalBudget " +
                "FROM dbo.tblNORM_StatementLine t " +
                "LEFT JOIN dbo.tblNORM_LineResult r ON r.StatementLineId=t.StatementLineId AND r.CalculationRunId=@run AND r.IsDeactivated=0 " +
                "LEFT JOIN dbo.tblNORM_PublishedFigure p ON p.ConfigurationReleaseId=t.ConfigurationReleaseId AND p.StatementCode=t.StatementCode AND p.LineCode=t.LineCode AND p.IsDeactivated=0 " +
                "LEFT JOIN dbo.tblNORM_BudgetFigure b ON b.CalculationRunId=@run AND b.StatementCode=t.StatementCode AND b.LineCode=t.LineCode AND b.IsDeactivated=0 " +
                "WHERE t.ConfigurationReleaseId=@release AND t.StatementCode=@code AND t.IsDeactivated=0 ORDER BY t.SeqNo",
                NORMHelper.P("@run", model.RunId), NORMHelper.P("@release", model.ReleaseId), NORMHelper.P("@code", code));
            List<FaceRow> rows = new List<FaceRow>();
            List<FaceRow> ownSource = new List<FaceRow>();
            List<FaceRow> gains = new List<FaceRow>();
            bool ownSourceTotalAdded = false;
            bool gainsTotalAdded = false;
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
            if (code == "SOCI") rows.Add(Heading("major", "NET COST OF SERVICES"));
            if (code == "SOFP") rows.Add(Heading("major", "ASSETS"));
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow source = table.Rows[i];
                string lineCode = NORMHelper.Str(source, "LineCode");
                string type = NORMHelper.Str(source, "LineType");
                string label = NORMHelper.Str(source, "LineLabel");
                if (code == "SOFP" && type == "section" && String.Equals(label, "Financial assets", StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(Heading("subsection", "Financial assets"));
                    continue;
                }
                if (code == "SOFP" && type == "section" && String.Equals(label, "Non-financial assets", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (code == "SOCI" && type == "section" && String.Equals(label, "Own-source income", StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(Heading("subsection", "LESS:"));
                    rows.Add(Heading("subsection", "INCOME"));
                    continue;
                }
                if (code == "SOFP" && type == "section" && String.Equals(label, "Liabilities", StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(Heading("major", "LIABILITIES"));
                    rows.Add(Heading("subsection", "Payables"));
                    continue;
                }
                if (code == "SOFP" && type == "section" && String.Equals(label, "Equity", StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(Heading("major", "EQUITY"));
                    continue;
                }
                if (code == "SOFP" && lineCode == "Cash and cash equivalents" && !hasFinancialAssetsHeading) rows.Add(Heading("subsection", "Financial assets"));
                if (code == "SOCI" && lineCode == "Revenue from contracts with customers") rows.Add(Heading("subsection", "Own-source revenue"));
                if (code == "SOCI" && lineCode == "Gain on sale of asset")
                {
                    AddSyntheticTotal(rows, ownSource, "TOTAL_OSR", "Total own-source revenue");
                    ownSourceTotalAdded = true;
                    rows.Add(Heading("subsection", "Gains"));
                }
                if (code == "SOCI" && lineCode == "Total own-source income" && !gainsTotalAdded)
                {
                    AddSyntheticTotal(rows, gains, "TOTAL_GAINS", "Total gains");
                    gainsTotalAdded = true;
                }
                if (code == "SOCI" && lineCode == "Other gains" && !hasForeignExchangeGains)
                {
                    FaceRow foreignExchangeGain = new FaceRow();
                    foreignExchangeGain.Type = "line";
                    foreignExchangeGain.Code = "Foreign exchange gains";
                    foreignExchangeGain.Label = "Net foreign exchange gains";
                    foreignExchangeGain.Note = "1.2F";
                    foreignExchangeGain.Prior = NORMStartOfYearSetup.FigureValue(model.PriorFigures, code, foreignExchangeGain.Code, null);
                    foreignExchangeGain.Budget = NORMStartOfYearSetup.FigureValue(model.Budgets, code, foreignExchangeGain.Code, null);
                    rows.Add(foreignExchangeGain);
                    gains.Add(foreignExchangeGain);
                }
                if (code == "SOCI" && lineCode == "Revenue from Government") rows.Add(Heading("major", "REVENUE FROM GOVERNMENT"));
                if (code == "SOFP" && lineCode == "Leases") rows.Add(Heading("subsection", "Interest-bearing liabilities"));
                if (code == "SOFP" && lineCode == "Employee provisions") rows.Add(Heading("subsection", "Provisions"));
                if (code == "SOFP" && lineCode == "Property plant and equipment")
                {
                    rows.Add(new FaceRow { Type = "total", Code = "TOTAL_FINANCIAL_ASSETS", Label = "Total financial assets" });
                    rows.Add(Heading("subsection", "Non-financial assets"));
                    AddAssetSplits(rows, model, source);
                    continue;
                }
                if (code == "SOFP" && lineCode == "Assets held for sale")
                    rows.Add(new FaceRow { Type = "total", Code = "TOTAL_NON_FINANCIAL_ASSETS", Label = "Total non-financial assets" });
                if (code == "SOFP" && lineCode == "Statement of Changes in Equity")
                {
                    AddEquitySplits(rows, model.RunId);
                    FaceRow totalEquity = DataRowToFace(source);
                    if (!source.IsNull("PublishedCurrent")) totalEquity.Current = NORMHelper.Dec(source, "PublishedCurrent");
                    totalEquity.Prior = NORMStartOfYearSetup.FigureValue(model.PriorFigures, code, lineCode, totalEquity.Prior);
                    totalEquity.Budget = NORMStartOfYearSetup.FigureValue(model.Budgets, code, lineCode, totalEquity.Budget);
                    totalEquity.Label = "Total equity";
                    totalEquity.Type = "total";
                    rows.Add(totalEquity);
                    continue;
                }
                FaceRow row = DataRowToFace(source);
                if (code == "SOFP" && !source.IsNull("PublishedCurrent")) row.Current = NORMHelper.Dec(source, "PublishedCurrent");
                row.Prior = NORMStartOfYearSetup.FigureValue(model.PriorFigures, code, lineCode, row.Prior);
                row.Budget = NORMStartOfYearSetup.FigureValue(model.Budgets, code, lineCode, row.Budget);
                if (code == "SOCI" && lineCode == "Total own-source income") row.Label = "Total income";
                if (code == "SOCI" && lineCode == "Net cost of services") row.Label = "Net cost of services";
                if (code == "SOCI" && lineCode == "Operating result") row.Label = "(Deficit) / Surplus";
                if (type == "section") row.Type = "subsection";
                rows.Add(row);
                if (code == "SOCI")
                {
                    if (lineCode == "Revenue from contracts with customers" || lineCode == "Revenue in relation to special accounts" ||
                        lineCode == "Rental income" || lineCode == "Other revenue") ownSource.Add(row);
                    if (lineCode == "Gain on sale of asset" || lineCode == "Reversals of previous asset write-downs" || lineCode == "Foreign exchange gains" ||
                        lineCode == "Other gains") gains.Add(row);
                }
            }
            if (code == "SOCI" && !ownSourceTotalAdded) AddSyntheticTotal(rows, ownSource, "TOTAL_OSR", "Total own-source revenue");
            if (code == "SOCI" && !gainsTotalAdded) AddSyntheticTotal(rows, gains, "TOTAL_GAINS", "Total gains");
            if (code == "SOCI")
            {
                ApplyFaceAggregate(rows, "Total own-source income", new string[] { "TOTAL_OSR", "TOTAL_GAINS" });
                ApplyFaceDifference(rows, "Operating result", "Revenue from Government", "Net cost of services");
                Dictionary<string, decimal> auditedOci = NORMStatementEnhancements.LoadSourceFigures(model.ReleaseId, "SOCE", "AuditedActual");
                Dictionary<string, decimal> priorOci = NORMStatementEnhancements.LoadSourceFigures(model.ReleaseId, "SOCE", "PriorActual");
                Dictionary<string, decimal> budgetOci = NORMStatementEnhancements.LoadSourceFigures(model.ReleaseId, "SOCE", "OriginalBudget");
                decimal? effectivePrior = NORMStartOfYearSetup.FigureValue(model.PriorFigures, "SOCE", "SOCE_TOTAL_OCI",
                    SourceValue(priorOci, "SOCE_TOTAL_OCI"));
                effectivePrior = NORMStartOfYearSetup.FigureValue(model.PriorFigures, "SOCI", "OCI_REVALUATION", effectivePrior);
                decimal? effectiveBudget = NORMStartOfYearSetup.FigureValue(model.Budgets, "SOCE", "SOCE_TOTAL_OCI",
                    SourceValue(budgetOci, "SOCE_TOTAL_OCI"));
                rows.Add(Heading("major", "OTHER COMPREHENSIVE INCOME / (LOSS)"));
                rows.Add(Heading("subsection", "Items not subject to subsequent reclassification to net cost of services"));
                rows.Add(new FaceRow { Type = "line", Code = "OCI_REVALUATION", Label = "Changes in asset revaluation reserves",
                    Note = "1.3", Current = SourceValue(auditedOci, "SOCE_TOTAL_OCI"), Prior = effectivePrior, Budget = effectiveBudget });
                rows.Add(new FaceRow { Type = "total", Code = "OCI_SUBTOTAL", Label = "Total other comprehensive income / (loss)" });
                rows.Add(new FaceRow { Type = "total", Code = "OCI_TOTAL", Label = "Total comprehensive (loss) / income" });
                ApplyFaceAggregate(rows, "OCI_SUBTOTAL", new string[] { "OCI_REVALUATION" });
                ApplyFaceAggregate(rows, "OCI_TOTAL", new string[] { "Operating result", "OCI_REVALUATION" });
            }
            if (code == "SOFP")
            {
                ApplyFaceAggregate(rows, "TOTAL_FINANCIAL_ASSETS",
                    new string[] { "Cash and cash equivalents", "Trade and other receivables" });
                ApplyFaceAggregate(rows, "TOTAL_NON_FINANCIAL_ASSETS", new string[] {
                    "PPE_LAND", "PPE_BUILDINGS", "PPE_SPECIALIST_MILITARY_EQUIPMENT", "PPE_INFRASTRUCTURE",
                    "PPE_PLANT_AND_EQUIPMENT", "PPE_HERITAGE_AND_CULTURAL_ASSETS", "PPE_INTANGIBLES",
                    "Inventories", "Prepayments" });
                ApplyFaceAggregate(rows, "Total assets", new string[] { "TOTAL_FINANCIAL_ASSETS", "TOTAL_NON_FINANCIAL_ASSETS", "Assets held for sale" });
            }
            return rows;
        }

        private static void AddSyntheticTotal(List<FaceRow> target, List<FaceRow> components, string code, string label)
        {
            if (components.Count == 0) { return; }
            FaceRow row = new FaceRow();
            row.Type = "total";
            row.Code = code;
            row.Label = label;
            row.Current = components.Where(x => x.Current.HasValue).Sum(x => x.Current.Value);
            if (components.Any(x => x.Prior.HasValue)) row.Prior = components.Where(x => x.Prior.HasValue).Sum(x => x.Prior.Value);
            if (components.Any(x => x.Budget.HasValue)) row.Budget = components.Where(x => x.Budget.HasValue).Sum(x => x.Budget.Value);
            row.FormulaSpec = String.Join("|", components.Where(x => !String.IsNullOrWhiteSpace(x.Code)).Select(x => "+" + x.Code).ToArray());
            target.Add(row);
        }

        private static void ApplyFaceAggregate(List<FaceRow> rows, string targetCode, string[] componentCodes)
        {
            FaceRow target = rows.FirstOrDefault(x => String.Equals(x.Code, targetCode, StringComparison.OrdinalIgnoreCase));
            if (target == null) { return; }
            List<FaceRow> components = rows.Where(x => componentCodes.Any(code =>
                String.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))).ToList();
            if (components.Count == 0) { return; }
            target.Current = components.Any(x => x.Current.HasValue)
                ? (decimal?)components.Where(x => x.Current.HasValue).Sum(x => x.Current.Value) : null;
            target.Prior = components.Any(x => x.Prior.HasValue)
                ? (decimal?)components.Where(x => x.Prior.HasValue).Sum(x => x.Prior.Value) : null;
            target.Budget = components.Any(x => x.Budget.HasValue)
                ? (decimal?)components.Where(x => x.Budget.HasValue).Sum(x => x.Budget.Value) : null;
            target.FormulaSpec = String.Join("|", componentCodes.Select(x => "+" + x).ToArray());
        }

        private static void ApplyFaceDifference(List<FaceRow> rows, string targetCode, string positiveCode, string negativeCode)
        {
            FaceRow target = rows.FirstOrDefault(x => String.Equals(x.Code, targetCode, StringComparison.OrdinalIgnoreCase));
            FaceRow positive = rows.FirstOrDefault(x => String.Equals(x.Code, positiveCode, StringComparison.OrdinalIgnoreCase));
            FaceRow negative = rows.FirstOrDefault(x => String.Equals(x.Code, negativeCode, StringComparison.OrdinalIgnoreCase));
            if (target == null || positive == null || negative == null) { return; }
            if (positive.Current.HasValue && negative.Current.HasValue) target.Current = positive.Current.Value - negative.Current.Value;
            if (positive.Prior.HasValue && negative.Prior.HasValue) target.Prior = positive.Prior.Value - negative.Prior.Value;
            if (positive.Budget.HasValue && negative.Budget.HasValue) target.Budget = positive.Budget.Value - negative.Budget.Value;
            target.FormulaSpec = "+" + positiveCode + "|-" + negativeCode;
        }

        private static decimal? SourceValue(Dictionary<string, decimal> values, string code)
        {
            decimal value;
            return values != null && values.TryGetValue(code, out value) ? (decimal?)value : null;
        }

        private static FaceRow DataRowToFace(DataRow source)
        {
            FaceRow row = new FaceRow();
            row.Type = NORMHelper.Str(source, "LineType");
            row.Code = NORMHelper.Str(source, "LineCode");
            row.Label = NORMHelper.Str(source, "LineLabel");
            row.Note = NORMHelper.Str(source, "NoteRef");
            row.Current = NullableDecimal(source, "ComputedAmount");
            row.Prior = NullableDecimal(source, "AmountPrior");
            row.Budget = NullableDecimal(source, "OriginalBudget");
            row.FormulaSpec = NORMHelper.Str(source, "FormulaSpec");
            return row;
        }

        private static void AddAssetSplits(List<FaceRow> rows, ExportContext model, DataRow aggregate)
        {
            DataTable table = NORMHelper.Query(
                "SELECT CASE " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'LAND%' THEN 'Land' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'BUILD%' THEN 'Buildings' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'HCA%' THEN 'Heritage and cultural' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'SME%' THEN 'Specialist military equipment' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'IFA%' THEN 'Infrastructure' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'P&E%' THEN 'Plant and equipment' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'CS%' THEN 'Computer software' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE '%INTANGIBLE%' THEN 'Other intangibles' " +
                "ELSE 'Other property, plant and equipment' END AS AssetClass,SUM(PresentedContribution) AS Amount " +
                "FROM dbo.tblNORM_Lineage l INNER JOIN dbo.tblNORM_LineResult r ON r.LineResultId=l.LineResultId " +
                "WHERE l.CalculationRunId=@run AND r.LineCode='Property plant and equipment' " +
                "AND l.DerivationCode='GL_MAPPING' GROUP BY CASE " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'LAND%' THEN 'Land' WHEN UPPER(NoteSubLineSnapshot) LIKE 'BUILD%' THEN 'Buildings' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'HCA%' THEN 'Heritage and cultural' WHEN UPPER(NoteSubLineSnapshot) LIKE 'SME%' THEN 'Specialist military equipment' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'IFA%' THEN 'Infrastructure' WHEN UPPER(NoteSubLineSnapshot) LIKE 'P&E%' THEN 'Plant and equipment' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'CS%' THEN 'Computer software' WHEN UPPER(NoteSubLineSnapshot) LIKE '%INTANGIBLE%' THEN 'Other intangibles' " +
                "ELSE 'Other property, plant and equipment' END ORDER BY AssetClass", NORMHelper.P("@run", model.RunId));
            Dictionary<string, decimal> mapped = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < table.Rows.Count; i++)
            {
                string label = NormaliseAssetClass(NORMHelper.Str(table.Rows[i], "AssetClass"));
                decimal amount = NORMHelper.Dec(table.Rows[i], "Amount");
                mapped[label] = mapped.ContainsKey(label) ? mapped[label] + amount : amount;
            }
            Dictionary<string, decimal> current = NORMStatementEnhancements.LoadSourceFigures(model.ReleaseId, "SOFP", "AuditedActual");
            Dictionary<string, decimal> prior = NORMStatementEnhancements.LoadSourceFigures(model.ReleaseId, "SOFP", "PriorActual");
            Dictionary<string, decimal> budget = NORMStatementEnhancements.LoadSourceFigures(model.ReleaseId, "SOFP", "OriginalBudget");
            string[,] classes = AssetFaceClasses();
            for (int i = 0; i < classes.GetLength(0); i++)
            {
                string classCode = classes[i, 0], label = classes[i, 1];
                decimal mappedAmount; mapped.TryGetValue(label, out mappedAmount);
                rows.Add(new FaceRow { Type = "line", Code = classCode, Label = label, Note = "3.2A",
                    Current = SourceValue(current, classCode) ?? (decimal?)mappedAmount,
                    Prior = NORMStartOfYearSetup.FigureValue(model.PriorFigures, "SOFP", classCode, SourceValue(prior, classCode)),
                    Budget = NORMStartOfYearSetup.FigureValue(model.Budgets, "SOFP", classCode, SourceValue(budget, classCode)) });
            }
        }

        private static string[,] AssetFaceClasses()
        {
            return new string[,] { { "PPE_LAND", "Land" }, { "PPE_BUILDINGS", "Buildings" },
                { "PPE_SPECIALIST_MILITARY_EQUIPMENT", "Specialist military equipment" },
                { "PPE_INFRASTRUCTURE", "Infrastructure" }, { "PPE_PLANT_AND_EQUIPMENT", "Plant and equipment" },
                { "PPE_HERITAGE_AND_CULTURAL_ASSETS", "Heritage and cultural assets" }, { "PPE_INTANGIBLES", "Intangibles" } };
        }

        private static string NormaliseAssetClass(string label)
        {
            if (String.Equals(label, "Computer software", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(label, "Other intangibles", StringComparison.OrdinalIgnoreCase)) return "Intangibles";
            if (String.Equals(label, "Heritage and cultural", StringComparison.OrdinalIgnoreCase)) return "Heritage and cultural assets";
            if (String.Equals(label, "Other property, plant and equipment", StringComparison.OrdinalIgnoreCase)) return "Plant and equipment";
            return label;
        }

        private static void AddEquitySplits(List<FaceRow> rows, int runId)
        {
            DataTable table = NORMHelper.Query(
                "SELECT CASE WHEN LOWER(NoteSubLineSnapshot) LIKE '%contributed%' THEN 'Contributed equity' " +
                "WHEN LOWER(NoteSubLineSnapshot) LIKE '%reserve%' THEN 'Reserves' " +
                "WHEN LOWER(NoteSubLineSnapshot) LIKE '%retained%' OR LOWER(NoteSubLineSnapshot) LIKE '%accumulated%' THEN 'Retained surplus/(Accumulated deficit)' " +
                "ELSE 'Other equity' END AS EquityClass,SUM(PresentedContribution) AS Amount " +
                "FROM dbo.tblNORM_Lineage l INNER JOIN dbo.tblNORM_LineResult r ON r.LineResultId=l.LineResultId " +
                "WHERE l.CalculationRunId=@run AND r.LineCode='Statement of Changes in Equity' " +
                "GROUP BY CASE WHEN LOWER(NoteSubLineSnapshot) LIKE '%contributed%' THEN 'Contributed equity' " +
                "WHEN LOWER(NoteSubLineSnapshot) LIKE '%reserve%' THEN 'Reserves' " +
                "WHEN LOWER(NoteSubLineSnapshot) LIKE '%retained%' OR LOWER(NoteSubLineSnapshot) LIKE '%accumulated%' THEN 'Retained surplus/(Accumulated deficit)' ELSE 'Other equity' END",
                NORMHelper.P("@run", runId));
            for (int i = 0; i < table.Rows.Count; i++)
            {
                FaceRow row = new FaceRow();
                row.Type = "line";
                row.Code = "EQUITY_" + i.ToString(CultureInfo.InvariantCulture);
                row.Label = NORMHelper.Str(table.Rows[i], "EquityClass");
                row.Current = NORMHelper.Dec(table.Rows[i], "Amount");
                rows.Add(row);
            }
        }

        private static void ApplyFaceFormulas(ExcelWorksheet sheet, List<FaceRow> rows,
            Dictionary<string, int> excelRows, int headerRow)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                FaceRow item = rows[i];
                if (String.IsNullOrWhiteSpace(item.FormulaSpec) || !excelRows.ContainsKey(item.Code)) { continue; }
                int targetRow = excelRows[item.Code];
                string[] terms = item.FormulaSpec.Split('|');
                for (int col = 3; col <= 5; col++)
                {
                    List<string> formulaTerms = new List<string>();
                    for (int t = 0; t < terms.Length; t++)
                    {
                        string term = terms[t].Trim();
                        if (term.Length < 2 || !excelRows.ContainsKey(term.Substring(1))) { continue; }
                        formulaTerms.Add((term[0] == '-' ? "-" : "+") + sheet.Cells[excelRows[term.Substring(1)], col].Address);
                    }
                    if (formulaTerms.Count > 0) sheet.Cells[targetRow, col].Formula = String.Join("", formulaTerms.ToArray()).TrimStart('+');
                }
            }
        }

        private static void ApplySofpBalanceFormula(ExcelWorksheet sheet, Dictionary<string, int> rows)
        {
            if (rows.ContainsKey("Net assets") && rows.ContainsKey("Statement of Changes in Equity"))
            {
                for (int col = 3; col <= 5; col++)
                    sheet.Cells[rows["Statement of Changes in Equity"], col].Formula = sheet.Cells[rows["Net assets"], col].Address;
            }
        }

        private static void AddEquity(ExcelPackage package, ExportContext model, List<Tuple<string, string, string>> index)
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("SoCE");
            AddBackLink(sheet);
            AddStatementTitle(sheet, model, "Statement of Changes in Equity", false);
            string[] headers = { "", "Contributed equity", "Reserves", "Retained earnings", "Total equity" };
            for (int i = 0; i < headers.Length; i++) sheet.Cells[7, i + 1].Value = headers[i];
            StyleHeader(sheet.Cells[7, 1, 7, 5], false);
            sheet.Cells[8, 1].Value = "Opening balance";
            sheet.Cells[9, 1].Value = "Total comprehensive income/(loss)";
            sheet.Cells[10, 1].Value = "Transactions with owners in their capacity as owners";
            sheet.Cells[11, 1].Value = "Closing balance";

            DataTable equity = NORMHelper.Query(
                "SELECT r.LineCode,r.ComputedAmount,p.AmountPrior FROM dbo.tblNORM_LineResult r " +
                "LEFT JOIN dbo.tblNORM_PublishedFigure p ON p.ConfigurationReleaseId=@release AND p.StatementCode=r.StatementCode AND p.LineCode=r.LineCode AND p.IsDeactivated=0 " +
                "WHERE r.CalculationRunId=@run AND r.LineCode IN ('Operating result','Statement of Changes in Equity') AND r.IsDeactivated=0",
                NORMHelper.P("@release", model.ReleaseId), NORMHelper.P("@run", model.RunId));
            decimal result = FindAmount(equity, "Operating result", "ComputedAmount");
            decimal closing = FindAmount(equity, "Statement of Changes in Equity", "ComputedAmount");
            decimal baselineOpening = FindAmount(equity, "Statement of Changes in Equity", "AmountPrior");
            decimal opening = NORMStartOfYearSetup.FigureValue(model.PriorFigures, "SOCE", "Statement of Changes in Equity", baselineOpening) ?? 0m;
            sheet.Cells[8, 5].Value = Round(opening);
            sheet.Cells[9, 4].Value = Round(result);
            sheet.Cells[9, 5].Formula = "SUM(B9:D9)";
            sheet.Cells[10, 5].Formula = "E11-E8-E9";
            sheet.Cells[11, 5].Value = Round(closing);
            sheet.Cells[8, 2, 11, 5].Style.Numberformat.Format = AmountFormat;
            sheet.Cells[8, 2, 11, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            StyleTotal(sheet.Cells[11, 1, 11, 5]);
            sheet.Cells[13, 1, 15, 5].Merge = true;
            sheet.Cells[13, 1].Value = "Equity-component opening balances remain a controlled workpaper input. The total column is formula-controlled and reconciles the displayed movements to closing equity.";
            SetFill(sheet.Cells[13, 1], Amber);
            sheet.Cells[13, 1].Style.WrapText = true;
            sheet.Column(1).Width = 55;
            for (int col = 2; col <= 5; col++) sheet.Column(col).Width = 19;
            index.Add(Tuple.Create("", sheet.Name, "Primary statements|Statement of Changes in Equity"));
        }

        private static void AddCashFlow(ExcelPackage package, ExportContext model, List<Tuple<string, string, string>> index)
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Cash Flow");
            AddBackLink(sheet);
            AddStatementTitle(sheet, model, "Cash Flow Statement", false);
            string[] headers = { "", "Notes", model.Year.ToString(), (model.Year - 1).ToString(), "Original Budget" };
            for (int i = 0; i < headers.Length; i++) sheet.Cells[7, i + 1].Value = headers[i];
            StyleHeader(sheet.Cells[7, 1, 7, 5], false);
            sheet.Cells[7, 3, 7, 5].Style.VerticalAlignment = ExcelVerticalAlignment.Bottom;
            List<CashRow> flows = LoadCashRows(model.RunId);
            DataTable journals = NORMStatementEnhancements.LoadCashFlowJournals(model.RunId);
            for (int i = 0; i < journals.Rows.Count; i++)
            {
                string status = NORMHelper.Str(journals.Rows[i], "StatusCode");
                if (status != "Approved" && status != "Posted") { continue; }
                string raw = NORMHelper.Str(journals.Rows[i], "CashFlowClass");
                string label = CanonicalCashLabel(raw);
                if (label == null) { continue; }
                CashRow row = new CashRow();
                row.Category = CashCategory(label);
                row.Label = label;
                row.Current = NORMHelper.Dec(journals.Rows[i], "Amount");
                flows.Add(row);
            }
            int rowNumber = 8;
            List<string> excluded = new List<string>();
            foreach (string category in new string[] { "OPERATING", "INVESTING", "FINANCING" })
            {
                sheet.Cells[rowNumber, 1, rowNumber, 5].Merge = true;
                sheet.Cells[rowNumber, 1].Value = TitleCase(category) + " activities";
                StyleSection(sheet.Cells[rowNumber, 1, rowNumber, 5], false);
                rowNumber++;
                int first = rowNumber;
                foreach (IGrouping<string, CashRow> group in flows.Where(x => x.Category == category).GroupBy(x => x.Label).OrderBy(x => OtherLast(x.Key)))
                {
                    sheet.Cells[rowNumber, 1].Value = group.Key;
                    sheet.Cells[rowNumber, 3].Value = Round(group.Sum(x => x.Current));
                    sheet.Cells[rowNumber, 3, rowNumber, 5].Style.Numberformat.Format = AmountFormat;
                    sheet.Cells[rowNumber, 3, rowNumber, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    rowNumber++;
                }
                sheet.Cells[rowNumber, 1].Value = "Net cash from/(used by) " + TitleCase(category).ToLowerInvariant() + " activities";
                if (rowNumber > first) sheet.Cells[rowNumber, 3].Formula = "SUM(C" + first.ToString() + ":C" + (rowNumber - 1).ToString() + ")";
                StyleTotal(sheet.Cells[rowNumber, 1, rowNumber, 5]);
                rowNumber++;
            }
            int operatingTotal = FindLabelRow(sheet, "Net cash from/(used by) operating activities", 8, rowNumber);
            int investingTotal = FindLabelRow(sheet, "Net cash from/(used by) investing activities", 8, rowNumber);
            int financingTotal = FindLabelRow(sheet, "Net cash from/(used by) financing activities", 8, rowNumber);
            sheet.Cells[rowNumber, 1].Value = "Net increase/(decrease) in cash held";
            sheet.Cells[rowNumber, 3].Formula = "C" + operatingTotal + "+C" + investingTotal + "+C" + financingTotal;
            StyleTotal(sheet.Cells[rowNumber, 1, rowNumber, 5]);
            rowNumber++;
            DataTable cash = NORMHelper.Query(
                "SELECT r.ComputedAmount,p.AmountPrior FROM dbo.tblNORM_LineResult r LEFT JOIN dbo.tblNORM_PublishedFigure p " +
                "ON p.ConfigurationReleaseId=@release AND p.StatementCode=r.StatementCode AND p.LineCode=r.LineCode AND p.IsDeactivated=0 " +
                "WHERE r.CalculationRunId=@run AND r.LineCode='Cash and cash equivalents' AND r.IsDeactivated=0",
                NORMHelper.P("@release", model.ReleaseId), NORMHelper.P("@run", model.RunId));
            decimal closing = cash.Rows.Count == 0 ? 0m : NORMHelper.Dec(cash.Rows[0], "ComputedAmount");
            decimal? baselineOpening = cash.Rows.Count == 0 || cash.Rows[0].IsNull("AmountPrior") ? (decimal?)null : NORMHelper.Dec(cash.Rows[0], "AmountPrior");
            decimal opening = NORMStartOfYearSetup.FigureValue(model.PriorFigures, "CASH", "Cash and cash equivalents", baselineOpening) ?? 0m;
            sheet.Cells[rowNumber, 1].Value = "Cash and cash equivalents at the beginning of the reporting period";
            sheet.Cells[rowNumber, 3].Value = Round(opening);
            rowNumber++;
            sheet.Cells[rowNumber, 1].Value = "Cash and cash equivalents at the end of the reporting period";
            sheet.Cells[rowNumber, 2].Value = "3.1A";
            sheet.Cells[rowNumber, 3].Value = Round(closing);
            StyleTotal(sheet.Cells[rowNumber, 1, rowNumber, 5]);
            sheet.Cells[8, 3, rowNumber, 5].Style.Numberformat.Format = AmountFormat;

            DataTable exceptions = LoadCashExceptions(model.RunId);
            if (exceptions.Rows.Count > 0)
            {
                rowNumber += 3;
                sheet.Cells[rowNumber, 1, rowNumber, 5].Merge = true;
                sheet.Cells[rowNumber, 1].Value = "Cash-flow mapping exceptions — excluded from the face statement";
                SetFill(sheet.Cells[rowNumber, 1, rowNumber, 5], Amber);
                sheet.Cells[rowNumber, 1, rowNumber, 5].Style.Font.Bold = true;
                rowNumber++;
                for (int i = 0; i < exceptions.Rows.Count; i++, rowNumber++)
                {
                    sheet.Cells[rowNumber, 1].Value = NORMHelper.Str(exceptions.Rows[i], "CashFlowClassSnapshot");
                    sheet.Cells[rowNumber, 3].Value = Round(NORMHelper.Dec(exceptions.Rows[i], "Amount"));
                    sheet.Cells[rowNumber, 3].Style.Numberformat.Format = AmountFormat;
                }
            }
            sheet.Column(1).Width = 58;
            sheet.Column(2).Width = 11;
            for (int col = 3; col <= 5; col++) sheet.Column(col).Width = 18;
            index.Add(Tuple.Create("", sheet.Name, "Primary statements|Cash Flow Statement and mapping exceptions"));
        }

        private static List<CashRow> LoadCashRows(int runId)
        {
            DataTable table = NORMHelper.Query(
                "SELECT CashFlowClassSnapshot,SUM(SourceAmount)/1000.0 AS Amount FROM dbo.tblNORM_Lineage " +
                "WHERE CalculationRunId=@run AND DerivationCode='GL_MAPPING' AND CashFlowClassSnapshot IS NOT NULL " +
                "GROUP BY CashFlowClassSnapshot ORDER BY CashFlowClassSnapshot", NORMHelper.P("@run", runId));
            List<CashRow> values = new List<CashRow>();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                string label = CanonicalCashLabel(NORMHelper.Str(table.Rows[i], "CashFlowClassSnapshot"));
                if (label == null) { continue; }
                CashRow row = new CashRow();
                row.Label = label;
                row.Category = CashCategory(label);
                decimal amount = NORMHelper.Dec(table.Rows[i], "Amount");
                row.Current = IsOutflow(label) ? -Math.Abs(amount) : Math.Abs(amount);
                values.Add(row);
            }
            return values;
        }

        private static DataTable LoadCashExceptions(int runId)
        {
            DataTable source = NORMHelper.Query(
                "SELECT CashFlowClassSnapshot,SUM(SourceAmount)/1000.0 AS Amount FROM dbo.tblNORM_Lineage " +
                "WHERE CalculationRunId=@run AND DerivationCode='GL_MAPPING' AND CashFlowClassSnapshot IS NOT NULL " +
                "GROUP BY CashFlowClassSnapshot ORDER BY CashFlowClassSnapshot", NORMHelper.P("@run", runId));
            DataTable exceptions = source.Clone();
            for (int i = 0; i < source.Rows.Count; i++)
                if (CanonicalCashLabel(NORMHelper.Str(source.Rows[i], "CashFlowClassSnapshot")) == null)
                    exceptions.ImportRow(source.Rows[i]);
            return exceptions;
        }

        private static string CanonicalCashLabel(string raw)
        {
            string value = (raw ?? "").Trim();
            string lower = value.ToLowerInvariant();
            if (lower.Length == 0 || lower == "0" || lower == "0.0" || lower.IndexOf("clearing") >= 0) return null;
            if (lower.IndexOf("employee") >= 0 || lower.IndexOf("salary") >= 0) return "Payments to employees";
            if (lower.IndexOf("supplier") >= 0 || lower.IndexOf("vendor") >= 0) return "Payments to suppliers";
            if (lower.IndexOf("grant") >= 0) return lower.IndexOf("receipt") >= 0 ? "Grant receipts" : "Grants paid";
            if (lower.IndexOf("purchase") >= 0 && (lower.IndexOf("asset") >= 0 || lower.IndexOf("p&e") >= 0 || lower.IndexOf("property") >= 0)) return "Purchase of property, plant and equipment and intangibles";
            if (lower.IndexOf("proceeds") >= 0 && lower.IndexOf("sale") >= 0) return "Proceeds from sale of property, plant and equipment and intangibles";
            if (lower.IndexOf("lease") >= 0 && lower.IndexOf("principal") >= 0) return "Principal payments of lease liabilities";
            if (lower.IndexOf("contributed equity") >= 0) return "Contributed equity";
            if (lower.IndexOf("appropriation") >= 0 || lower.IndexOf("government") >= 0) return lower.IndexOf("return") >= 0 ? "Return of appropriations" : "Receipts from Government";
            if (lower.IndexOf("customer") >= 0 || lower.IndexOf("goods and services") >= 0) return "Receipts from customers";
            if (lower.IndexOf("interest") >= 0) return lower.IndexOf("payment") >= 0 || lower.IndexOf("paid") >= 0 ? "Interest paid" : "Interest received";
            if (lower.IndexOf("gst") >= 0) return "Net GST received";
            if (lower.IndexOf("payment") >= 0 || lower.IndexOf("paid") >= 0) return "Other payments";
            if (lower.IndexOf("receipt") >= 0 || lower.IndexOf("proceeds") >= 0) return "Other receipts";
            return null;
        }

        private static string CashCategory(string label)
        {
            string value = (label ?? "").ToLowerInvariant();
            if (value.IndexOf("purchase of property") >= 0 || value.IndexOf("proceeds from sale") >= 0 || value.IndexOf("investment") >= 0) return "INVESTING";
            if (value.IndexOf("contributed equity") >= 0 || value.IndexOf("principal payments") >= 0 || value.IndexOf("return of appropriation") >= 0) return "FINANCING";
            return "OPERATING";
        }

        private static bool IsOutflow(string label)
        {
            string value = (label ?? "").ToLowerInvariant();
            return value.IndexOf("payment") >= 0 || value.IndexOf("purchase") >= 0 || value.IndexOf("paid") >= 0 || value.IndexOf("return") >= 0;
        }

        private static void AddAdministeredTemplate(ExcelPackage package, ExportContext model, string sheetName,
            string title, bool atDate, List<Tuple<string, string, string>> index)
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add(sheetName);
            AddBackLink(sheet);
            AddStatementTitle(sheet, model, title, atDate);
            sheet.Cells[7, 1].Value = "Administered item";
            sheet.Cells[7, 2].Value = "Notes";
            sheet.Cells[7, 3].Value = model.Year;
            sheet.Cells[7, 4].Value = model.Year - 1;
            sheet.Cells[7, 5].Value = "Original Budget";
            StyleHeader(sheet.Cells[7, 1, 7, 5], true);
            SetFill(sheet.Cells[8, 1, 12, 5], PaleGrey);
            sheet.Cells[8, 1, 10, 5].Merge = true;
            sheet.Cells[8, 1].Value = "Controlled administered template — populate from the administered mapping and workpaper set. No departmental balance has been copied into this schedule.";
            sheet.Cells[8, 1].Style.WrapText = true;
            sheet.Cells[8, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            sheet.Cells[8, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            sheet.Column(1).Width = 58;
            sheet.Column(2).Width = 11;
            for (int col = 3; col <= 5; col++) sheet.Column(col).Width = 18;
            index.Add(Tuple.Create("", sheet.Name, "Administered|" + title + " · controlled template"));
        }

        private static void AddNotes(ExcelPackage package, ExportContext model, List<Tuple<string, string, string>> index)
        {
            HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ExcelWorksheet existing in package.Workbook.Worksheets) usedNames.Add(existing.Name);
            for (int i = 0; i < model.Disclosures.Count; i++)
            {
                NORMReportingFramework.Disclosure disclosure = model.Disclosures[i];
                if (!disclosure.Required || String.IsNullOrWhiteSpace(disclosure.NoteRef)) { continue; }
                string sheetName = UniqueSheetName(disclosure.NoteRef + " " + disclosure.Title, usedNames);
                ExcelWorksheet sheet = package.Workbook.Worksheets.Add(sheetName);
                AddBackLink(sheet);
                AddStatementTitle(sheet, model, "Note " + disclosure.NoteRef + ": " + disclosure.Title, false,
                    disclosure.Code == "N3_2A" ? 8 : 5);
                if (disclosure.Code == "N3_2A") AddAssetMovementNote(sheet, model, disclosure);
                else AddStandardNote(sheet, model, disclosure);
                index.Add(Tuple.Create(disclosure.NoteRef, sheet.Name, "Notes|" + disclosure.Title));
            }
        }

        private static void AddStandardNote(ExcelWorksheet sheet, ExportContext model, NORMReportingFramework.Disclosure disclosure)
        {
            sheet.Cells[7, 1].Value = "Account / disclosure line";
            sheet.Cells[7, 2].Value = model.Year;
            sheet.Cells[7, 3].Value = model.Year - 1;
            sheet.Cells[7, 4].Value = "Workpaper reference";
            StyleHeader(sheet.Cells[7, 1, 7, 4], false);
            List<NORMReportingFramework.NoteLine> lines = disclosure.Lines.OrderBy(x => OtherLast(x.Label)).ToList();
            Dictionary<string, decimal?> prior = ManualPrior(model.ManualInputs, disclosure.Code);
            int row = 8;
            for (int i = 0; i < lines.Count; i++, row++)
            {
                sheet.Cells[row, 1].Value = lines[i].Label;
                sheet.Cells[row, 2].Value = Round(lines[i].Amount);
                decimal? priorValue;
                if (lines[i].Prior.HasValue)
                    sheet.Cells[row, 3].Value = Round(lines[i].Prior.Value);
                else if (prior.TryGetValue(lines[i].Label.Replace(" (manual input)", ""), out priorValue) && priorValue.HasValue)
                    sheet.Cells[row, 3].Value = Round(priorValue.Value);
                sheet.Cells[row, 2, row, 3].Style.Numberformat.Format = AmountFormat;
                sheet.Cells[row, 2, row, 3].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                sheet.Cells[row, 1].Style.Font.Bold = false;
            }
            if (lines.Count == 0)
            {
                sheet.Cells[row, 1, row + 1, 4].Merge = true;
                sheet.Cells[row, 1].Value = "Required disclosure — complete the controlled input and supporting workpaper before sign-off.";
                SetFill(sheet.Cells[row, 1], Amber);
                sheet.Cells[row, 1].Style.WrapText = true;
                row += 2;
            }
            else
            {
                sheet.Cells[row, 1].Value = "Total " + disclosure.Title.ToLowerInvariant();
                sheet.Cells[row, 2].Formula = "SUM(B8:B" + (row - 1).ToString(CultureInfo.InvariantCulture) + ")";
                sheet.Cells[row, 3].Formula = "IF(COUNT(C8:C" + (row - 1).ToString(CultureInfo.InvariantCulture) + ")=0,\"\",SUM(C8:C" + (row - 1).ToString(CultureInfo.InvariantCulture) + "))";
                sheet.Cells[row, 2, row, 3].Style.Numberformat.Format = AmountFormat;
                StyleTotal(sheet.Cells[row, 1, row, 4]);
                row++;
            }
            AddWorkpaperArea(sheet, row + 2);
            sheet.Column(1).Width = 61;
            sheet.Column(2).Width = 18;
            sheet.Column(3).Width = 18;
            sheet.Column(4).Width = 35;
            sheet.View.FreezePanes(8, 2);
        }

        private static void AddAssetMovementNote(ExcelWorksheet sheet, ExportContext model, NORMReportingFramework.Disclosure disclosure)
        {
            string[] classes = { "Land", "Buildings", "Heritage and cultural", "Plant and equipment", "Computer software", "Other intangibles", "Total" };
            sheet.Cells[7, 1].Value = "Movement";
            for (int i = 0; i < classes.Length; i++) sheet.Cells[7, i + 2].Value = classes[i];
            StyleHeader(sheet.Cells[7, 1, 7, classes.Length + 1], false);
            string[] movements = { "Opening carrying amount", "Additions", "Revaluations and impairments", "Depreciation and amortisation", "Disposals and transfers", "Other movements", "Closing carrying amount" };
            for (int i = 0; i < movements.Length; i++) sheet.Cells[8 + i, 1].Value = movements[i];
            Dictionary<string, decimal> closing = AssetClassAmounts(model.RunId, "Property plant and equipment");
            Dictionary<string, decimal> depreciation = AssetClassAmounts(model.RunId, "Depreciation and amortisation");
            for (int c = 0; c < classes.Length - 1; c++)
            {
                decimal value;
                if (closing.TryGetValue(classes[c], out value)) sheet.Cells[14, c + 2].Value = Round(value);
                if (depreciation.TryGetValue(classes[c], out value)) sheet.Cells[11, c + 2].Value = Round(value);
            }
            for (int r = 8; r <= 14; r++) sheet.Cells[r, classes.Length + 1].Formula = "SUM(B" + r.ToString() + ":G" + r.ToString() + ")";
            sheet.Cells[8, 2, 14, classes.Length + 1].Style.Numberformat.Format = AmountFormat;
            sheet.Cells[8, 2, 14, classes.Length + 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            StyleTotal(sheet.Cells[14, 1, 14, classes.Length + 1]);
            sheet.Cells[16, 1, 18, classes.Length + 1].Merge = true;
            sheet.Cells[16, 1].Value = "Opening balances, additions, revaluations, disposals and other movements are controlled asset-register inputs. Closing balances and depreciation are generated from frozen NORM lineage.";
            SetFill(sheet.Cells[16, 1], Amber);
            sheet.Cells[16, 1].Style.WrapText = true;
            AddWorkpaperArea(sheet, 20);
            sheet.Column(1).Width = 42;
            for (int col = 2; col <= classes.Length + 1; col++) sheet.Column(col).Width = 18;
            sheet.View.FreezePanes(8, 2);
        }

        private static Dictionary<string, decimal> AssetClassAmounts(int runId, string lineCode)
        {
            DataTable table = NORMHelper.Query(
                "SELECT CASE WHEN UPPER(NoteSubLineSnapshot) LIKE 'LAND%' THEN 'Land' WHEN UPPER(NoteSubLineSnapshot) LIKE 'BUILD%' THEN 'Buildings' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'HCA%' THEN 'Heritage and cultural' WHEN UPPER(NoteSubLineSnapshot) LIKE 'P&E%' OR UPPER(NoteSubLineSnapshot) LIKE 'IFA%' OR UPPER(NoteSubLineSnapshot) LIKE 'SME%' THEN 'Plant and equipment' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'CS%' THEN 'Computer software' WHEN UPPER(NoteSubLineSnapshot) LIKE '%INTANGIBLE%' THEN 'Other intangibles' ELSE 'Plant and equipment' END AS AssetClass," +
                "SUM(PresentedContribution) AS Amount FROM dbo.tblNORM_Lineage l INNER JOIN dbo.tblNORM_LineResult r ON r.LineResultId=l.LineResultId " +
                "WHERE l.CalculationRunId=@run AND r.LineCode=@line GROUP BY CASE WHEN UPPER(NoteSubLineSnapshot) LIKE 'LAND%' THEN 'Land' WHEN UPPER(NoteSubLineSnapshot) LIKE 'BUILD%' THEN 'Buildings' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'HCA%' THEN 'Heritage and cultural' WHEN UPPER(NoteSubLineSnapshot) LIKE 'P&E%' OR UPPER(NoteSubLineSnapshot) LIKE 'IFA%' OR UPPER(NoteSubLineSnapshot) LIKE 'SME%' THEN 'Plant and equipment' " +
                "WHEN UPPER(NoteSubLineSnapshot) LIKE 'CS%' THEN 'Computer software' WHEN UPPER(NoteSubLineSnapshot) LIKE '%INTANGIBLE%' THEN 'Other intangibles' ELSE 'Plant and equipment' END",
                NORMHelper.P("@run", runId), NORMHelper.P("@line", lineCode));
            Dictionary<string, decimal> values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < table.Rows.Count; i++) values[NORMHelper.Str(table.Rows[i], "AssetClass")] = NORMHelper.Dec(table.Rows[i], "Amount");
            return values;
        }

        private static Dictionary<string, decimal?> ManualPrior(DataTable inputs, string disclosureCode)
        {
            Dictionary<string, decimal?> values = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < inputs.Rows.Count; i++)
            {
                if (!String.Equals(NORMHelper.Str(inputs.Rows[i], "DisclosureCode"), disclosureCode, StringComparison.OrdinalIgnoreCase)) continue;
                values[NORMHelper.Str(inputs.Rows[i], "InputLabel")] = NullableDecimal(inputs.Rows[i], "AmountPrior");
            }
            return values;
        }

        private static void AddWorkpaperArea(ExcelWorksheet sheet, int row)
        {
            sheet.Cells[row, 1, row, 4].Merge = true;
            sheet.Cells[row, 1].Value = "Supporting workpaper";
            StyleSection(sheet.Cells[row, 1, row, 4], false);
            row++;
            string[] headers = { "Reference", "Description / calculation", "Amount ($'000)", "Evidence / reviewer note" };
            for (int i = 0; i < headers.Length; i++) sheet.Cells[row, i + 1].Value = headers[i];
            StyleHeader(sheet.Cells[row, 1, row, 4], false);
            for (int r = row + 1; r <= row + 12; r++)
            {
                sheet.Cells[r, 1, r, 4].Style.Border.Bottom.Style = ExcelBorderStyle.Hair;
                sheet.Cells[r, 3].Style.Numberformat.Format = AmountFormat;
            }
        }

        private static void AddStatementTitle(ExcelWorksheet sheet, ExportContext model, string title, bool atDate)
        {
            AddStatementTitle(sheet, model, title, atDate, 5);
        }

        private static void AddStatementTitle(ExcelWorksheet sheet, ExportContext model, string title, bool atDate, int lastColumn)
        {
            sheet.View.ShowGridLines = false;
            sheet.Cells[2, 1, 2, lastColumn].Merge = true;
            sheet.Cells[2, 1].Value = model.Entity;
            sheet.Cells[3, 1, 3, lastColumn].Merge = true;
            sheet.Cells[3, 1].Value = title;
            sheet.Cells[4, 1, 4, lastColumn].Merge = true;
            sheet.Cells[4, 1].Value = (atDate ? "As at" : "For the year ended") + " 30 June " + model.Year.ToString();
            StyleTitle(sheet.Cells[2, 1, 2, lastColumn]);
            sheet.Cells[3, 1, 3, lastColumn].Style.Font.Size = 16;
            sheet.Cells[3, 1, 3, lastColumn].Style.Font.Bold = true;
            sheet.Cells[4, 1, 4, lastColumn].Style.Font.Italic = true;
            sheet.Cells[5, 1, 5, lastColumn].Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
            sheet.Cells[5, 1, 5, lastColumn].Style.Border.Bottom.Color.SetColor(Green);
        }

        private static void AddBackLink(ExcelWorksheet sheet)
        {
            sheet.Cells[1, 1].Value = "← Contents";
            sheet.Cells[1, 1].Hyperlink = new ExcelHyperLink("'Contents'!A1", "← Contents");
            sheet.Cells[1, 1].Style.Font.Color.SetColor(Green);
            sheet.Cells[1, 1].Style.Font.UnderLine = true;
        }

        private static void StyleTitle(ExcelRange range)
        {
            range.Style.Font.Name = "Arial";
            range.Style.Font.Size = 20;
            range.Style.Font.Bold = true;
            range.Style.Font.Color.SetColor(Ink);
        }

        private static void SetFill(ExcelRange range, Color colour)
        {
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(colour);
        }

        private static void StyleHeader(ExcelRange range, bool administered)
        {
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(administered ? Color.FromArgb(110, 110, 110) : Green);
            range.Style.Font.Color.SetColor(Color.White);
            range.Style.Font.Bold = true;
            range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            range.Style.VerticalAlignment = ExcelVerticalAlignment.Bottom;
            range.Style.WrapText = true;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
        }

        private static void StyleSection(ExcelRange range, bool major)
        {
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(major ? DarkGreen : PaleGreen);
            range.Style.Font.Color.SetColor(major ? Color.White : Ink);
            range.Style.Font.Bold = true;
            range.Style.WrapText = true;
        }

        private static void StyleTotal(ExcelRange range)
        {
            range.Style.Font.Bold = true;
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Double;
        }

        private static void FinishSheet(ExcelWorksheet sheet)
        {
            sheet.Cells.Style.Font.Name = "Arial";
            sheet.Cells.Style.Font.Size = 10;
            sheet.Cells.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            sheet.PrinterSettings.ShowGridLines = false;
            sheet.PrinterSettings.Orientation = eOrientation.Portrait;
            sheet.PrinterSettings.FitToPage = true;
            sheet.PrinterSettings.FitToWidth = 1;
            sheet.PrinterSettings.FitToHeight = 0;
            sheet.PrinterSettings.LeftMargin = 0.35m;
            sheet.PrinterSettings.RightMargin = 0.35m;
            sheet.PrinterSettings.TopMargin = 0.5m;
            sheet.PrinterSettings.BottomMargin = 0.5m;
        }

        private static FaceRow Heading(string type, string label)
        {
            FaceRow row = new FaceRow(); row.Type = type; row.Label = label; return row;
        }

        private static decimal? NullableDecimal(DataRow row, string column)
        {
            return row.Table.Columns.Contains(column) && !row.IsNull(column) ? (decimal?)Convert.ToDecimal(row[column]) : null;
        }

        private static void SetRounded(ExcelRange cell, decimal? value)
        {
            if (value.HasValue) cell.Value = Round(value.Value);
        }

        private static decimal Round(decimal value) { return Math.Round(value, 0, MidpointRounding.AwayFromZero); }

        private static decimal FindAmount(DataTable table, string lineCode, string column)
        {
            for (int i = 0; i < table.Rows.Count; i++)
                if (String.Equals(NORMHelper.Str(table.Rows[i], "LineCode"), lineCode, StringComparison.OrdinalIgnoreCase))
                    return table.Rows[i].IsNull(column) ? 0m : NORMHelper.Dec(table.Rows[i], column);
            return 0m;
        }

        private static int FindLabelRow(ExcelWorksheet sheet, string label, int from, int to)
        {
            for (int row = from; row <= to; row++)
                if (String.Equals(Convert.ToString(sheet.Cells[row, 1].Value), label, StringComparison.OrdinalIgnoreCase)) return row;
            return from;
        }

        private static string UniqueSheetName(string value, HashSet<string> used)
        {
            string clean = new string((value ?? "Note").Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
            if (clean.Length > 31) clean = clean.Substring(0, 31).Trim();
            string candidate = clean;
            int suffix = 2;
            while (used.Contains(candidate))
            {
                string ending = " " + suffix.ToString(CultureInfo.InvariantCulture);
                candidate = clean.Substring(0, Math.Min(clean.Length, 31 - ending.Length)).Trim() + ending;
                suffix++;
            }
            used.Add(candidate);
            return candidate;
        }

        private static int OtherLast(string value)
        {
            string normalised = (value ?? "").Trim().ToLowerInvariant();
            return normalised.StartsWith("other") || normalised == "unclassified" ? 1 : 0;
        }

        private static bool Required(NORMReportingFramework.ReportingProfile profile, string code)
        {
            return profile.Requirements.ContainsKey(code) && profile.Requirements[code];
        }

        private static string TitleCase(string value)
        {
            if (String.IsNullOrEmpty(value)) return value;
            return value.Substring(0, 1).ToUpperInvariant() + value.Substring(1).ToLowerInvariant();
        }

        private static string SafeFileName(string value)
        {
            string clean = new string((value ?? "NORM").Select(c => Char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
            return clean.Length == 0 ? "NORM" : clean;
        }

        private static void WriteError(HttpContext context, int status, string message)
        {
            context.Response.StatusCode = status;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Write(message);
        }
    }
}
