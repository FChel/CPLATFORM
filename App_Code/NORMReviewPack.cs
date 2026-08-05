using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Web;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace CPlatform.NORM
{
    /// <summary>Builds an accountant-ready evidence pack from one frozen calculation run.</summary>
    public class NORM_ReviewPack : IHttpHandler
    {
        private static readonly Color Ink = Color.FromArgb(23, 23, 23);
        private static readonly Color Orange = Color.FromArgb(232, 119, 34);
        private static readonly Color PaleOrange = Color.FromArgb(253, 240, 229);
        private static readonly Color PaleGrey = Color.FromArgb(242, 243, 245);

        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext context)
        {
            if (context.User == null || context.User.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                context.Response.StatusCode = 401;
                context.Response.TrySkipIisCustomErrors = true;
                return;
            }

            int runId;
            if (!Int32.TryParse(context.Request.QueryString["run"], out runId) || runId <= 0)
            {
                WriteError(context, 400, "Choose a valid calculation run.");
                return;
            }

            DataTable runTable = NORMHelper.Query(
                "SELECT r.CalculationRunId,r.RunGuid,r.InputFingerprint,r.CompletedUtc,r.StartedBy," +
                "i.ImportId,i.FinancialYear,i.EntityCode,i.SourceType,i.SourceFileName,i.SourceFileHash," +
                "i.[RowCount] AS [RowCount],i.TotalDebit,i.TotalCredit,i.NetBalance,i.ImportedBy,i.ImportedUtc," +
                "c.ConfigurationReleaseId,c.VersionCode,c.ReleaseLabel,c.ApprovedBy,c.ApprovedUtc " +
                "FROM dbo.tblNORM_CalculationRun r " +
                "INNER JOIN dbo.tblNORM_Import i ON i.ImportId = r.ImportId " +
                "INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId = r.ConfigurationReleaseId " +
                "WHERE r.CalculationRunId = @run AND r.StatusCode = 'Complete' " +
                "AND r.IsDeactivated = 0 AND i.IsDeactivated = 0 AND c.IsDeactivated = 0",
                NORMHelper.P("@run", runId));
            if (runTable.Rows.Count == 0)
            {
                WriteError(context, 404, "The completed calculation run was not found.");
                return;
            }

            DataRow run = runTable.Rows[0];
            using (ExcelPackage package = new ExcelPackage())
            {
                package.Workbook.Properties.Title = "NORM review pack - run " + runId.ToString();
                package.Workbook.Properties.Subject = "Frozen financial-statement calculation evidence";
                package.Workbook.Properties.Author = NORMHelper.CurrentUserId();
                package.Workbook.Properties.Company = "Defence Finance Group";
                AddSummary(package, run);
                AddSourceFiles(package, NORMHelper.Int(run, "ImportId"));
                AddAssurance(package, runId);
                AddStatements(package, runId);
                if (NORMReportingFramework.IsInstalled())
                {
                    int releaseId = NORMHelper.Int(run, "ConfigurationReleaseId");
                    AddDisclosureRegister(package, runId, releaseId);
                    AddAuditCommittee(package, runId, releaseId);
                    AddWorkflow(package, runId);
                }
                AddLineage(package, runId, false);
                AddLineage(package, runId, true);

                byte[] content = package.GetAsByteArray();
                string fileName = "NORM_FY" + NORMHelper.Int(run, "FinancialYear").ToString() +
                    "_Run_" + runId.ToString() + "_Review_Pack.xlsx";
                context.Response.Clear();
                context.Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                context.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + fileName + "\"");
                context.Response.AddHeader("X-Content-Type-Options", "nosniff");
                context.Response.OutputStream.Write(content, 0, content.Length);
                context.Response.Flush();
                context.ApplicationInstance.CompleteRequest();
            }
        }

        private static void AddSummary(ExcelPackage package, DataRow run)
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Run summary");
            AddTitle(sheet, "NORM calculation evidence", "Frozen run summary and source fingerprints");
            int row = 4;
            AddFact(sheet, ref row, "Calculation run", NORMHelper.Int(run, "CalculationRunId"));
            AddFact(sheet, ref row, "Run identifier", NORMHelper.Str(run, "RunGuid"));
            AddFact(sheet, ref row, "Financial year", NORMHelper.Int(run, "FinancialYear"));
            AddFact(sheet, ref row, "Reporting entity", NORMHelper.Str(run, "EntityCode"));
            AddFact(sheet, ref row, "Configuration", NORMHelper.Str(run, "VersionCode") + " - " + NORMHelper.Str(run, "ReleaseLabel"));
            AddFact(sheet, ref row, "Source format", NORMHelper.Str(run, "SourceType"));
            AddFact(sheet, ref row, "Source file", NORMHelper.Str(run, "SourceFileName"));
            AddFact(sheet, ref row, "Source rows", NORMHelper.Int(run, "RowCount"));
            AddFact(sheet, ref row, "Total debits ($)", NORMHelper.Dec(run, "TotalDebit"));
            AddFact(sheet, ref row, "Total credits ($)", NORMHelper.Dec(run, "TotalCredit"));
            AddFact(sheet, ref row, "Net balance ($)", NORMHelper.Dec(run, "NetBalance"));
            AddFact(sheet, ref row, "Imported by", NORMHelper.Str(run, "ImportedBy"));
            AddFact(sheet, ref row, "Imported UTC", run["ImportedUtc"]);
            AddFact(sheet, ref row, "Calculated by", NORMHelper.Str(run, "StartedBy"));
            AddFact(sheet, ref row, "Completed UTC", run["CompletedUtc"]);
            AddFact(sheet, ref row, "Source SHA-256", NORMHelper.Str(run, "SourceFileHash"));
            AddFact(sheet, ref row, "Input/configuration fingerprint", NORMHelper.Str(run, "InputFingerprint"));
            row++;
            sheet.Cells[row, 1].Value = "Control statement";
            sheet.Cells[row, 2].Value = "Published figures are comparison evidence only. Calculated figures come from the retained source rows and approved configuration release.";
            sheet.Cells[row, 1, row, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[row, 1, row, 2].Style.Fill.BackgroundColor.SetColor(PaleOrange);
            sheet.Cells[row, 2].Style.WrapText = true;
            sheet.Column(1).Width = 34;
            sheet.Column(2).Width = 92;
            sheet.View.FreezePanes(4, 1);
            sheet.Cells[4, 2, row - 2, 2].Style.Numberformat.Format = "#,##0.00";
        }

        private static void AddSourceFiles(ExcelPackage package, int importId)
        {
            DataTable table = NORMHelper.Query(
                "SELECT SourceType,SourceFileName,SourceFileHash,SourceFileBytes,PeriodStart,PeriodEnd," +
                "[RowCount] AS [RowCount],IsStatementInput,CreatedUtc " +
                "FROM dbo.tblNORM_ImportFile WHERE ImportId = @import ORDER BY PeriodStart,SourceType",
                NORMHelper.P("@import", importId));
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Source files");
            AddTitle(sheet, "Retained source files", "Separate file fingerprints and period coverage for the immutable import");
            string[] headers = { "Source", "File", "Period start", "Period end", "Rows", "Bytes", "Statement input", "SHA-256", "Retained UTC" };
            int headerRow = 4;
            AddHeaders(sheet, headerRow, headers);
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow source = table.Rows[i];
                int row = headerRow + 1 + i;
                sheet.Cells[row, 1].Value = NORMHelper.Str(source, "SourceType");
                sheet.Cells[row, 2].Value = NORMHelper.Str(source, "SourceFileName");
                if (!source.IsNull("PeriodStart")) { sheet.Cells[row, 3].Value = NORMHelper.Int(source, "PeriodStart"); }
                if (!source.IsNull("PeriodEnd")) { sheet.Cells[row, 4].Value = NORMHelper.Int(source, "PeriodEnd"); }
                sheet.Cells[row, 5].Value = NORMHelper.Int(source, "RowCount");
                sheet.Cells[row, 6].Value = NORMHelper.Long(source, "SourceFileBytes");
                sheet.Cells[row, 7].Value = Convert.ToBoolean(source["IsStatementInput"]) ? "Yes" : "Evidence only";
                sheet.Cells[row, 8].Value = NORMHelper.Str(source, "SourceFileHash");
                sheet.Cells[row, 9].Value = source["CreatedUtc"];
            }
            FinishTable(sheet, headerRow, table.Rows.Count, headers.Length);
            sheet.Column(2).Width = 52;
            sheet.Column(7).Width = 18;
            sheet.Column(8).Width = 68;
            sheet.Column(9).Width = 22;
        }

        private static void AddAssurance(ExcelPackage package, int runId)
        {
            DataTable table = NORMHelper.Query(
                "SELECT CheckCode,CheckLabel,SeverityCode,ResultCode,ActualValue,ExpectedValue," +
                "DifferenceValue,ToleranceValue,DetailText FROM dbo.tblNORM_ValidationResult " +
                "WHERE CalculationRunId = @run ORDER BY CASE SeverityCode WHEN 'Blocking' THEN 1 WHEN 'Warning' THEN 2 ELSE 3 END,ValidationResultId",
                NORMHelper.P("@run", runId));
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Assurance");
            AddTitle(sheet, "Assurance checks", "Blocking failures prevent a run from being treated as ready");
            string[] headers = { "Code", "Check", "Severity", "Result", "Actual", "Expected", "Difference", "Tolerance", "Evidence" };
            int headerRow = 4;
            AddHeaders(sheet, headerRow, headers);
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow source = table.Rows[i];
                int row = headerRow + 1 + i;
                sheet.Cells[row, 1].Value = NORMHelper.Str(source, "CheckCode");
                sheet.Cells[row, 2].Value = NORMHelper.Str(source, "CheckLabel");
                sheet.Cells[row, 3].Value = NORMHelper.Str(source, "SeverityCode");
                sheet.Cells[row, 4].Value = NORMHelper.Str(source, "ResultCode");
                SetNullableDecimal(sheet.Cells[row, 5], source, "ActualValue");
                SetNullableDecimal(sheet.Cells[row, 6], source, "ExpectedValue");
                SetNullableDecimal(sheet.Cells[row, 7], source, "DifferenceValue");
                SetNullableDecimal(sheet.Cells[row, 8], source, "ToleranceValue");
                sheet.Cells[row, 9].Value = NORMHelper.Str(source, "DetailText");
                if (String.Equals(NORMHelper.Str(source, "ResultCode"), "Fail", StringComparison.OrdinalIgnoreCase))
                {
                    sheet.Cells[row, 1, row, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    sheet.Cells[row, 1, row, headers.Length].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(252, 232, 232));
                }
            }
            FinishTable(sheet, headerRow, table.Rows.Count, headers.Length);
            sheet.Column(2).Width = 38;
            sheet.Column(9).Width = 80;
            sheet.Cells[headerRow + 1, 9, headerRow + table.Rows.Count, 9].Style.WrapText = true;
        }

        private static void AddStatements(ExcelPackage package, int runId)
        {
            DataTable table = NORMHelper.Query(
                "SELECT r.StatementCode,t.SeqNo,t.LineType,r.LineCode,ISNULL(t.LineLabel,r.LineCode) AS LineLabel,t.NoteRef," +
                "r.ComputedAmount,r.PublishedAmount,p.AmountPrior,r.Variance,r.StatusCode " +
                "FROM dbo.tblNORM_LineResult r " +
                "LEFT JOIN dbo.tblNORM_StatementLine t ON t.StatementLineId = r.StatementLineId " +
                "LEFT JOIN dbo.tblNORM_CalculationRun cr ON cr.CalculationRunId = r.CalculationRunId " +
                "LEFT JOIN dbo.tblNORM_PublishedFigure p ON p.ConfigurationReleaseId = cr.ConfigurationReleaseId " +
                "AND p.StatementCode = r.StatementCode AND p.LineCode = r.LineCode AND p.IsDeactivated = 0 " +
                "WHERE r.CalculationRunId = @run AND r.StatementCode <> 'POOL' AND r.IsDeactivated = 0 " +
                "ORDER BY r.StatementCode,t.SeqNo,r.LineCode",
                NORMHelper.P("@run", runId));
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Statement results");
            AddTitle(sheet, "Statement results", "Calculated values, audited FY2025 comparison and comparative figures in $'000");
            string[] headers = { "Statement", "Sequence", "Type", "Line code", "Line label", "Note", "Calculated", "Audited comparison", "Comparative", "Variance", "Status" };
            int headerRow = 4;
            AddHeaders(sheet, headerRow, headers);
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow source = table.Rows[i];
                int row = headerRow + 1 + i;
                sheet.Cells[row, 1].Value = NORMHelper.Str(source, "StatementCode");
                sheet.Cells[row, 2].Value = NORMHelper.Int(source, "SeqNo");
                sheet.Cells[row, 3].Value = NORMHelper.Str(source, "LineType");
                sheet.Cells[row, 4].Value = NORMHelper.Str(source, "LineCode");
                sheet.Cells[row, 5].Value = NORMHelper.Str(source, "LineLabel");
                sheet.Cells[row, 6].Value = NORMHelper.Str(source, "NoteRef");
                sheet.Cells[row, 7].Value = NORMHelper.Dec(source, "ComputedAmount");
                SetNullableDecimal(sheet.Cells[row, 8], source, "PublishedAmount");
                SetNullableDecimal(sheet.Cells[row, 9], source, "AmountPrior");
                SetNullableDecimal(sheet.Cells[row, 10], source, "Variance");
                sheet.Cells[row, 11].Value = NORMHelper.Str(source, "StatusCode");
            }
            FinishTable(sheet, headerRow, table.Rows.Count, headers.Length);
            sheet.Cells[headerRow + 1, 7, headerRow + table.Rows.Count, 10].Style.Numberformat.Format = "#,##0.000;[Red](#,##0.000)";
            sheet.Column(4).Width = 34;
            sheet.Column(5).Width = 42;
        }

        private static void AddDisclosureRegister(ExcelPackage package, int runId, int releaseId)
        {
            NORMReportingFramework.ReportingProfile profile = NORMReportingFramework.LoadProfile(releaseId);
            System.Collections.Generic.List<NORMReportingFramework.Disclosure> disclosures =
                NORMReportingFramework.LoadDisclosures(runId, releaseId, profile);
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Disclosure register");
            AddTitle(sheet, "PRIMA disclosure register", "Entity-profile assessment, preparation status and generated balance coverage");
            string[] headers = { "Section", "Note", "Disclosure", "Trigger", "Required", "Status", "Source rows", "Generated amount ($'000)", "Guidance" };
            int headerRow = 4;
            AddHeaders(sheet, headerRow, headers);
            for (int i = 0; i < disclosures.Count; i++)
            {
                NORMReportingFramework.Disclosure source = disclosures[i];
                int row = headerRow + 1 + i;
                sheet.Cells[row, 1].Value = source.SectionTitle;
                sheet.Cells[row, 2].Value = source.NoteRef;
                sheet.Cells[row, 3].Value = source.Title;
                sheet.Cells[row, 4].Value = source.TriggerCode;
                sheet.Cells[row, 5].Value = source.Required ? "Yes" : "Not applicable";
                sheet.Cells[row, 6].Value = source.CompletionStatus;
                sheet.Cells[row, 7].Value = source.SourceCount;
                sheet.Cells[row, 8].Value = source.Amount;
                sheet.Cells[row, 9].Value = source.Guidance;
                if (source.Required && source.CompletionStatus == "Needs input")
                {
                    sheet.Cells[row, 1, row, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    sheet.Cells[row, 1, row, headers.Length].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 242, 204));
                }
            }
            FinishTable(sheet, headerRow, disclosures.Count, headers.Length);
            if (disclosures.Count > 0) { sheet.Cells[headerRow + 1, 8, headerRow + disclosures.Count, 8].Style.Numberformat.Format = "#,##0.000;[Red](#,##0.000)"; }
            sheet.Column(1).Width = 36;
            sheet.Column(3).Width = 40;
            sheet.Column(9).Width = 70;
            sheet.Cells[headerRow + 1, 9, headerRow + disclosures.Count, 9].Style.WrapText = true;
        }

        private static void AddAuditCommittee(ExcelPackage package, int runId, int releaseId)
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Audit Committee");
            AddTitle(sheet, "Audit Committee financial reporting pack", "Executive view generated from the selected frozen NORM run");
            int row = 4;
            sheet.Cells[row, 1].Value = "Pack component";
            sheet.Cells[row, 2].Value = "Current evidence / status";
            sheet.Cells[row, 1, row, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[row, 1, row, 2].Style.Fill.BackgroundColor.SetColor(Orange);
            sheet.Cells[row, 1, row, 2].Style.Font.Color.SetColor(Color.White);
            sheet.Cells[row, 1, row, 2].Style.Font.Bold = true;
            row++;

            DataTable assurance = NORMHelper.Query(
                "SELECT SUM(CASE WHEN ResultCode='Fail' THEN 1 ELSE 0 END) AS Failed," +
                "SUM(CASE WHEN ResultCode='Warning' THEN 1 ELSE 0 END) AS Warnings,COUNT(*) AS Checks " +
                "FROM dbo.tblNORM_ValidationResult WHERE CalculationRunId=@run",
                NORMHelper.P("@run", runId));
            string assuranceText = NORMHelper.Int(assurance.Rows[0], "Checks").ToString() + " controls; " +
                NORMHelper.Int(assurance.Rows[0], "Failed").ToString() + " failed; " +
                NORMHelper.Int(assurance.Rows[0], "Warnings").ToString() + " warnings.";
            AddPackRow(sheet, ref row, "Financial statement summary", assuranceText);
            AddPackRow(sheet, ref row, "Significant accounting judgements", "Complete the entity-specific judgement register and cross-reference the relevant policy notes.");
            AddPackRow(sheet, ref row, "New accounting standards", "Review current and future Australian Accounting Standard requirements recorded in the Overview narrative.");
            AddPackRow(sheet, ref row, "Key risks", "Blocking controls, mapping warnings and required disclosures needing input remain visible in the Assurance and Disclosure register sheets.");
            AddPackRow(sheet, ref row, "Draft financial statements", "See Statement results and the editable Word export for the current draft.");
            AddPackRow(sheet, ref row, "Management representation checklist", "Tracked as a separate workflow item with preparer and reviewer ownership.");
            AddPackRow(sheet, ref row, "Internal certification status", "Tracked in the Workflow sheet.");

            row += 2;
            sheet.Cells[row, 1].Value = "Material movements and audited comparison";
            sheet.Cells[row, 1].Style.Font.Bold = true;
            sheet.Cells[row, 1].Style.Font.Size = 13;
            row++;
            string[] headers = { "Statement", "Line", "Calculated", "Audited comparison", "Variance", "Status" };
            AddHeaders(sheet, row, headers);
            int headerRow = row;
            DataTable movements = NORMHelper.Query(
                "SELECT TOP 12 StatementCode,LineCode,ComputedAmount,PublishedAmount,Variance,StatusCode " +
                "FROM dbo.tblNORM_LineResult WHERE CalculationRunId=@run AND PublishedAmount IS NOT NULL AND IsDeactivated=0 " +
                "ORDER BY ABS(Variance) DESC",
                NORMHelper.P("@run", runId));
            for (int i = 0; i < movements.Rows.Count; i++)
            {
                DataRow source = movements.Rows[i];
                int target = headerRow + 1 + i;
                sheet.Cells[target, 1].Value = NORMHelper.Str(source, "StatementCode");
                sheet.Cells[target, 2].Value = NORMHelper.Str(source, "LineCode");
                sheet.Cells[target, 3].Value = NORMHelper.Dec(source, "ComputedAmount");
                sheet.Cells[target, 4].Value = NORMHelper.Dec(source, "PublishedAmount");
                sheet.Cells[target, 5].Value = NORMHelper.Dec(source, "Variance");
                sheet.Cells[target, 6].Value = NORMHelper.Str(source, "StatusCode");
            }
            FinishTable(sheet, headerRow, movements.Rows.Count, headers.Length);
            if (movements.Rows.Count > 0) { sheet.Cells[headerRow + 1, 3, headerRow + movements.Rows.Count, 5].Style.Numberformat.Format = "#,##0.000;[Red](#,##0.000)"; }
            sheet.Column(1).Width = 34;
            sheet.Column(2).Width = 54;
        }

        private static void AddPackRow(ExcelWorksheet sheet, ref int row, string label, string value)
        {
            sheet.Cells[row, 1].Value = label;
            sheet.Cells[row, 1].Style.Font.Bold = true;
            sheet.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(PaleGrey);
            sheet.Cells[row, 2].Value = value;
            sheet.Cells[row, 2].Style.WrapText = true;
            row++;
        }

        private static void AddWorkflow(ExcelPackage package, int runId)
        {
            NORMReportingFramework.EnsureWorkflow(runId, NORMHelper.CurrentUserId());
            DataTable table = NORMReportingFramework.LoadWorkflow(runId);
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Workflow");
            AddTitle(sheet, "Preparation and review workflow", "Financial statements, Audit Committee and annual report modules");
            string[] headers = { "Module", "Deliverable", "Preparer", "Reviewer", "Status", "Due date", "Working note", "Updated UTC" };
            int headerRow = 4;
            AddHeaders(sheet, headerRow, headers);
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow source = table.Rows[i];
                int row = headerRow + 1 + i;
                sheet.Cells[row, 1].Value = NORMHelper.Str(source, "ModuleCode");
                sheet.Cells[row, 2].Value = NORMHelper.Str(source, "ItemLabel");
                sheet.Cells[row, 3].Value = NORMHelper.Str(source, "OwnerUserId");
                sheet.Cells[row, 4].Value = NORMHelper.Str(source, "ReviewerUserId");
                sheet.Cells[row, 5].Value = NORMHelper.Str(source, "StatusCode");
                if (!source.IsNull("DueDate")) { sheet.Cells[row, 6].Value = source["DueDate"]; }
                sheet.Cells[row, 7].Value = NORMHelper.Str(source, "Commentary");
                sheet.Cells[row, 8].Value = source["UpdatedUtc"];
            }
            FinishTable(sheet, headerRow, table.Rows.Count, headers.Length);
            sheet.Column(1).Width = 28;
            sheet.Column(2).Width = 45;
            sheet.Column(7).Width = 64;
            sheet.Cells[headerRow + 1, 7, headerRow + table.Rows.Count, 7].Style.WrapText = true;
        }

        private static void AddLineage(ExcelPackage package, int runId, bool unmappedOnly)
        {
            string poolFilter = unmappedOnly ? " AND r.StatementCode = 'POOL' AND r.LineCode = 'UNMAPPED' " : " AND r.StatementCode <> 'POOL' ";
            DataTable table = NORMHelper.Query(
                "SELECT r.StatementCode,r.LineCode,ISNULL(t.LineLabel,r.LineCode) AS LineLabel,l.DerivationCode," +
                "tb.SourceRowNo,tb.SourceLedger,tb.GlAccount,tb.GlText,tb.RowHash,tb.IsSynthetic,l.SourceAmount,l.PresentedContribution," +
                "l.AccountMapId,l.AccountTypeSnapshot,l.NoteSubLineSnapshot,l.CashFlowClassSnapshot,l.MappingSnapshot,i.FinancialYear " +
                "FROM dbo.tblNORM_Lineage l " +
                "INNER JOIN dbo.tblNORM_LineResult r ON r.LineResultId = l.LineResultId " +
                "INNER JOIN dbo.tblNORM_TrialBalanceRow tb ON tb.TbRowId = l.TbRowId " +
                "INNER JOIN dbo.tblNORM_CalculationRun cr ON cr.CalculationRunId = l.CalculationRunId " +
                "INNER JOIN dbo.tblNORM_Import i ON i.ImportId = cr.ImportId " +
                "LEFT JOIN dbo.tblNORM_StatementLine t ON t.StatementLineId = r.StatementLineId " +
                "WHERE l.CalculationRunId = @run" + poolFilter +
                "ORDER BY r.StatementCode,r.LineCode,ABS(l.PresentedContribution) DESC,tb.GlAccount,tb.SourceRowNo",
                NORMHelper.P("@run", runId));
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add(unmappedOnly ? "Unmapped rows" : "Figure lineage");
            AddTitle(sheet, unmappedOnly ? "Unmapped source rows" : "Figure lineage",
                unmappedOnly ? "Every row requires an accounting disposition" : "Frozen source-to-figure derivation for the selected run");
            string[] headers = { "Statement", "Line code", "Line label", "Derivation", "Source row", "Ledger", "G/L account", "Description", "Row SHA-256", "Source amount ($)", "Contribution ($'000)", "Mapping ID", "Account type", "Note classification", "Cash-flow classification", "Frozen mapping evidence" };
            int headerRow = 4;
            AddHeaders(sheet, headerRow, headers);
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow source = table.Rows[i];
                int row = headerRow + 1 + i;
                string[] textColumns = { "StatementCode", "LineCode", "LineLabel", "DerivationCode" };
                for (int c = 0; c < textColumns.Length; c++) { sheet.Cells[row, c + 1].Value = NORMHelper.Str(source, textColumns[c]); }
                sheet.Cells[row, 5].Value = NORMHelper.Int(source, "SourceRowNo");
                sheet.Cells[row, 6].Value = NORMHelper.Str(source, "SourceLedger");
                sheet.Cells[row, 7].Value = NORMHelper.Str(source, "GlAccount");
                string sapUrl = Convert.ToBoolean(source["IsSynthetic"]) ? "" :
                    NORMHelper.SapGlLineItemsLink(
                        NORMHelper.Str(source, "GlAccount"),
                        NORMHelper.Str(source, "SourceLedger"),
                        NORMHelper.Int(source, "FinancialYear"));
                if (sapUrl.Length > 0)
                {
                    sheet.Cells[row, 7].Hyperlink = new Uri(sapUrl);
                    sheet.Cells[row, 7].Style.Font.Color.SetColor(Color.FromArgb(25, 91, 136));
                    sheet.Cells[row, 7].Style.Font.UnderLine = true;
                }
                sheet.Cells[row, 8].Value = NORMHelper.Str(source, "GlText");
                sheet.Cells[row, 9].Value = NORMHelper.Str(source, "RowHash");
                sheet.Cells[row, 10].Value = NORMHelper.Dec(source, "SourceAmount");
                sheet.Cells[row, 11].Value = NORMHelper.Dec(source, "PresentedContribution");
                if (!source.IsNull("AccountMapId")) { sheet.Cells[row, 12].Value = NORMHelper.Int(source, "AccountMapId"); }
                sheet.Cells[row, 13].Value = NORMHelper.Str(source, "AccountTypeSnapshot");
                sheet.Cells[row, 14].Value = NORMHelper.Str(source, "NoteSubLineSnapshot");
                sheet.Cells[row, 15].Value = NORMHelper.Str(source, "CashFlowClassSnapshot");
                sheet.Cells[row, 16].Value = NORMHelper.Str(source, "MappingSnapshot");
            }
            FinishTable(sheet, headerRow, table.Rows.Count, headers.Length);
            if (table.Rows.Count > 0)
            {
                sheet.Cells[headerRow + 1, 10, headerRow + table.Rows.Count, 10].Style.Numberformat.Format = "#,##0.00;[Red](#,##0.00)";
                sheet.Cells[headerRow + 1, 11, headerRow + table.Rows.Count, 11].Style.Numberformat.Format = "#,##0.000;[Red](#,##0.000)";
            }
            sheet.Column(3).Width = 36;
            sheet.Column(8).Width = 38;
            sheet.Column(9).Width = 68;
            sheet.Column(14).Width = 34;
            sheet.Column(15).Width = 34;
            sheet.Column(16).Width = 72;
        }

        private static void AddTitle(ExcelWorksheet sheet, string title, string subtitle)
        {
            sheet.Cells[1, 1].Value = title;
            sheet.Cells[1, 1].Style.Font.Size = 20;
            sheet.Cells[1, 1].Style.Font.Bold = true;
            sheet.Cells[1, 1].Style.Font.Color.SetColor(Color.White);
            sheet.Cells[1, 1, 1, 16].Merge = true;
            sheet.Cells[1, 1, 1, 16].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, 1, 1, 16].Style.Fill.BackgroundColor.SetColor(Ink);
            sheet.Cells[2, 1].Value = subtitle;
            sheet.Cells[2, 1, 2, 16].Merge = true;
            sheet.Cells[2, 1].Style.Font.Color.SetColor(Color.DimGray);
            sheet.Row(1).Height = 28;
        }

        private static void AddFact(ExcelWorksheet sheet, ref int row, string label, object value)
        {
            sheet.Cells[row, 1].Value = label;
            sheet.Cells[row, 1].Style.Font.Bold = true;
            sheet.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(PaleGrey);
            sheet.Cells[row, 2].Value = value;
            row++;
        }

        private static void AddHeaders(ExcelWorksheet sheet, int row, string[] headers)
        {
            for (int i = 0; i < headers.Length; i++) { sheet.Cells[row, i + 1].Value = headers[i]; }
            sheet.Cells[row, 1, row, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[row, 1, row, headers.Length].Style.Fill.BackgroundColor.SetColor(Orange);
            sheet.Cells[row, 1, row, headers.Length].Style.Font.Bold = true;
            sheet.Cells[row, 1, row, headers.Length].Style.Font.Color.SetColor(Color.White);
        }

        private static void FinishTable(ExcelWorksheet sheet, int headerRow, int rowCount, int columnCount)
        {
            int lastRow = headerRow + Math.Max(rowCount, 1);
            sheet.View.FreezePanes(headerRow + 1, 1);
            if (rowCount > 0) { sheet.Cells[headerRow, 1, lastRow, columnCount].AutoFilter = true; }
            sheet.Cells[headerRow, 1, lastRow, columnCount].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            sheet.Cells[headerRow, 1, lastRow, columnCount].AutoFitColumns(10, 28);
            for (int column = 1; column <= columnCount; column++)
            {
                if (sheet.Column(column).Width > 28) { sheet.Column(column).Width = 28; }
            }
        }

        private static void SetNullableDecimal(ExcelRange cell, DataRow row, string column)
        {
            if (!row.IsNull(column)) { cell.Value = NORMHelper.Dec(row, column); }
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
