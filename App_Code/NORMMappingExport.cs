using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Web;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace CPlatform.NORM
{
    /// <summary>Exports the frozen trial-balance-to-statement mapping for one completed NORM run.</summary>
    public class NORM_MappingExport : IHttpHandler
    {
        private static readonly Color Ink = Color.FromArgb(23, 23, 23);
        private static readonly Color Orange = Color.FromArgb(232, 119, 34);
        private static readonly Color PaleOrange = Color.FromArgb(253, 240, 229);
        private static readonly Color PaleRed = Color.FromArgb(252, 232, 232);

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
                WriteError(context, 400, "Choose a valid completed calculation run.");
                return;
            }

            DataTable runTable = NORMHelper.Query(
                "SELECT r.CalculationRunId,r.CompletedUtc,i.ImportId,i.FinancialYear,i.EntityCode,i.SourceFileName," +
                "c.ConfigurationReleaseId,c.VersionCode,c.ReleaseLabel " +
                "FROM dbo.tblNORM_CalculationRun r " +
                "INNER JOIN dbo.tblNORM_Import i ON i.ImportId=r.ImportId " +
                "INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=r.ConfigurationReleaseId " +
                "WHERE r.CalculationRunId=@run AND r.StatusCode='Complete' AND r.IsDeactivated=0 " +
                "AND i.IsDeactivated=0 AND c.IsDeactivated=0",
                NORMHelper.P("@run", runId));
            if (runTable.Rows.Count == 0)
            {
                WriteError(context, 404, "The completed calculation run was not found.");
                return;
            }

            DataRow run = runTable.Rows[0];
            DataTable mappings = LoadMappings(runId);
            using (ExcelPackage package = BuildWorkbook(run, mappings))
            {
                byte[] content = package.GetAsByteArray();
                string fileName = "NORM_FY" + NORMHelper.Int(run, "FinancialYear").ToString(CultureInfo.InvariantCulture) +
                    "_Run_" + runId.ToString(CultureInfo.InvariantCulture) + "_Account_Mapping.xlsx";
                context.Response.Clear();
                context.Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                context.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + fileName + "\"");
                context.Response.AddHeader("X-Content-Type-Options", "nosniff");
                context.Response.OutputStream.Write(content, 0, content.Length);
                context.Response.Flush();
                context.ApplicationInstance.CompleteRequest();
            }
        }

        private static DataTable LoadMappings(int runId)
        {
            return NORMHelper.Query(
                "SELECT tb.GlAccount," +
                "COALESCE(NULLIF(MAX(m.GlDescription),''),NULLIF(MAX(tb.GlText),''),'') AS AccountDescription," +
                "SUM(tb.AccumBalance) AS Balance,m.AccountType,m.StatementLine,t.StatementCode,t.LineLabel,t.NoteRef," +
                "m.NoteSubLine,d.DisclosureTitle " +
                "FROM dbo.tblNORM_CalculationRun r " +
                "INNER JOIN dbo.tblNORM_TrialBalanceRow tb ON tb.ImportId=r.ImportId AND tb.IsDeactivated=0 " +
                "LEFT JOIN dbo.tblNORM_AccountMap m ON m.ConfigurationReleaseId=r.ConfigurationReleaseId " +
                "AND m.GlCode=tb.GlAccount AND m.IsDeactivated=0 " +
                "LEFT JOIN dbo.tblNORM_StatementLine t ON t.ConfigurationReleaseId=r.ConfigurationReleaseId " +
                "AND t.LineCode=m.StatementLine AND t.IsDeactivated=0 " +
                "OUTER APPLY (SELECT TOP 1 dr.DisclosureTitle FROM dbo.tblNORM_DisclosureRule dr " +
                "WHERE dr.ConfigurationReleaseId=r.ConfigurationReleaseId AND dr.NoteRef=t.NoteRef " +
                "AND dr.IsDeactivated=0 ORDER BY dr.SortOrder,dr.DisclosureRuleId) d " +
                "WHERE r.CalculationRunId=@run AND r.StatusCode='Complete' AND r.IsDeactivated=0 " +
                "GROUP BY tb.GlAccount,m.AccountType,m.StatementLine,t.StatementCode,t.LineLabel,t.NoteRef,m.NoteSubLine,d.DisclosureTitle " +
                "ORDER BY tb.GlAccount",
                NORMHelper.P("@run", runId));
        }

        private static ExcelPackage BuildWorkbook(DataRow run, DataTable mappings)
        {
            ExcelPackage package = new ExcelPackage();
            package.Workbook.Properties.Title = "NORM trial balance account mapping - run " + NORMHelper.Int(run, "CalculationRunId").ToString();
            package.Workbook.Properties.Subject = "Frozen trial balance account balances and financial-statement mappings";
            package.Workbook.Properties.Author = NORMHelper.CurrentUserId();
            package.Workbook.Properties.Company = "Defence Finance Group";

            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Account mapping");
            sheet.Cells[1, 1].Value = "NORM account mapping";
            sheet.Cells[1, 1, 1, 4].Merge = true;
            sheet.Cells[1, 1, 1, 4].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, 1, 1, 4].Style.Fill.BackgroundColor.SetColor(Ink);
            sheet.Cells[1, 1].Style.Font.Size = 20;
            sheet.Cells[1, 1].Style.Font.Bold = true;
            sheet.Cells[1, 1].Style.Font.Color.SetColor(Color.White);
            sheet.Row(1).Height = 28;

            sheet.Cells[2, 1].Value = "One row per trial balance account, tied to the frozen calculation run";
            sheet.Cells[2, 1, 2, 4].Merge = true;
            sheet.Cells[2, 1].Style.Font.Color.SetColor(Color.DimGray);

            int unmappedCount = 0;
            for (int i = 0; i < mappings.Rows.Count; i++)
            {
                if (String.IsNullOrWhiteSpace(NORMHelper.Str(mappings.Rows[i], "StatementLine"))) unmappedCount++;
            }
            sheet.Cells[3, 1].Value = "FY" + NORMHelper.Int(run, "FinancialYear").ToString(CultureInfo.InvariantCulture) +
                " · Run " + NORMHelper.Int(run, "CalculationRunId").ToString(CultureInfo.InvariantCulture) +
                " · Import " + NORMHelper.Int(run, "ImportId").ToString(CultureInfo.InvariantCulture) +
                " · Configuration " + NORMHelper.Str(run, "VersionCode") +
                " · " + mappings.Rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " accounts" +
                " · " + unmappedCount.ToString("N0", CultureInfo.InvariantCulture) + " unmapped";
            sheet.Cells[3, 1, 3, 4].Merge = true;
            sheet.Cells[3, 1, 3, 4].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[3, 1, 3, 4].Style.Fill.BackgroundColor.SetColor(PaleOrange);
            sheet.Cells[3, 1].Style.Font.Bold = true;

            int headerRow = 5;
            string[] headers = { "Trial balance account", "Balance ($)", "Face statement mapping", "Notes mapping" };
            for (int column = 0; column < headers.Length; column++) sheet.Cells[headerRow, column + 1].Value = headers[column];
            sheet.Cells[headerRow, 1, headerRow, 4].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[headerRow, 1, headerRow, 4].Style.Fill.BackgroundColor.SetColor(Orange);
            sheet.Cells[headerRow, 1, headerRow, 4].Style.Font.Bold = true;
            sheet.Cells[headerRow, 1, headerRow, 4].Style.Font.Color.SetColor(Color.White);

            for (int i = 0; i < mappings.Rows.Count; i++)
            {
                DataRow source = mappings.Rows[i];
                int row = headerRow + 1 + i;
                string gl = NORMHelper.Str(source, "GlAccount");
                string description = NORMHelper.Str(source, "AccountDescription");
                string statementLine = NORMHelper.Str(source, "StatementLine");
                string statementCode = NORMHelper.Str(source, "StatementCode");
                string lineLabel = NORMHelper.Str(source, "LineLabel");
                string noteRef = NORMHelper.Str(source, "NoteRef");
                string noteTitle = NORMHelper.Str(source, "DisclosureTitle");
                string noteSubLine = NORMHelper.Str(source, "NoteSubLine");

                sheet.Cells[row, 1].Value = gl + (description.Length == 0 ? "" : " — " + description);
                sheet.Cells[row, 2].Value = NORMHelper.Dec(source, "Balance");
                sheet.Cells[row, 3].Value = statementLine.Length == 0
                    ? "Unmapped"
                    : StatementName(statementCode) + " — " + (lineLabel.Length == 0 ? statementLine : lineLabel);
                sheet.Cells[row, 4].Value = BuildNoteMapping(noteRef, noteTitle, noteSubLine);

                if (statementLine.Length == 0)
                {
                    sheet.Cells[row, 1, row, 4].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    sheet.Cells[row, 1, row, 4].Style.Fill.BackgroundColor.SetColor(PaleRed);
                    sheet.Cells[row, 3].Style.Font.Bold = true;
                    sheet.Cells[row, 3].Style.Font.Color.SetColor(Color.DarkRed);
                }
            }

            int lastDataRow = headerRow + mappings.Rows.Count;
            int totalRow = lastDataRow + 1;
            sheet.Cells[totalRow, 1].Value = "Net trial balance";
            sheet.Cells[totalRow, 1].Style.Font.Bold = true;
            if (mappings.Rows.Count > 0)
                sheet.Cells[totalRow, 2].Formula = "SUM(B" + (headerRow + 1).ToString(CultureInfo.InvariantCulture) + ":B" + lastDataRow.ToString(CultureInfo.InvariantCulture) + ")";
            else
                sheet.Cells[totalRow, 2].Value = 0m;
            sheet.Cells[totalRow, 1, totalRow, 4].Style.Border.Top.Style = ExcelBorderStyle.Thin;
            sheet.Cells[totalRow, 1, totalRow, 4].Style.Border.Bottom.Style = ExcelBorderStyle.Double;
            sheet.Cells[totalRow, 2].Style.Font.Bold = true;

            sheet.View.FreezePanes(headerRow + 1, 1);
            if (mappings.Rows.Count > 0) sheet.Cells[headerRow, 1, lastDataRow, 4].AutoFilter = true;
            sheet.Cells[headerRow + 1, 2, totalRow, 2].Style.Numberformat.Format = "#,##0.00;[Red](#,##0.00);-";
            sheet.Cells[headerRow, 1, totalRow, 4].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            sheet.Cells[headerRow + 1, 1, lastDataRow, 4].Style.WrapText = true;
            sheet.Column(1).Width = 58;
            sheet.Column(2).Width = 18;
            sheet.Column(3).Width = 48;
            sheet.Column(4).Width = 58;
            sheet.PrinterSettings.Orientation = eOrientation.Landscape;
            sheet.PrinterSettings.FitToPage = true;
            sheet.PrinterSettings.FitToWidth = 1;
            sheet.PrinterSettings.FitToHeight = 0;
            return package;
        }

        private static string BuildNoteMapping(string noteRef, string noteTitle, string noteSubLine)
        {
            if (String.IsNullOrWhiteSpace(noteRef) && String.IsNullOrWhiteSpace(noteSubLine)) return "No note mapping";
            string value = String.IsNullOrWhiteSpace(noteRef) ? "" : "Note " + noteRef;
            if (!String.IsNullOrWhiteSpace(noteTitle)) value += (value.Length == 0 ? "" : " — ") + noteTitle;
            if (!String.IsNullOrWhiteSpace(noteSubLine)) value += (value.Length == 0 ? "" : " › ") + noteSubLine;
            return value;
        }

        private static string StatementName(string code)
        {
            if (String.Equals(code, "SOCI", StringComparison.OrdinalIgnoreCase)) return "Statement of Comprehensive Income";
            if (String.Equals(code, "SOFP", StringComparison.OrdinalIgnoreCase)) return "Statement of Financial Position";
            if (String.Equals(code, "SOCE", StringComparison.OrdinalIgnoreCase)) return "Statement of Changes in Equity";
            if (String.Equals(code, "CF", StringComparison.OrdinalIgnoreCase)) return "Cash Flow Statement";
            return String.IsNullOrWhiteSpace(code) ? "Face statements" : code;
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
