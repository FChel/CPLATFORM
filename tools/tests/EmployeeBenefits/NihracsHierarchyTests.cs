using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using CPlatform.NORM;
using OfficeOpenXml;

internal static partial class PilotTests
{
    private static IEnumerable<string[]> SqlValues(string section)
    {
        foreach (string line in section.Split('\n').Where(value => value.TrimStart().StartsWith("(")))
        {
            yield return Regex.Matches(line, @"N'((?:''|[^'])*)'|\bNULL\b|\b\d+\b").Cast<Match>()
                .Select(match => match.Groups[1].Success ? match.Groups[1].Value.Replace("''", "'") :
                    match.Value == "NULL" ? null : match.Value).ToArray();
        }
    }

    private static void CheckNihracs(string output, string root)
    {
        Prepare();
        string[] sql = File.ReadAllText(Path.Combine(root, "sql/NORM_Stage2B_NIHRACS_Hierarchy.sql"))
            .Split(new[] { "FROM (VALUES" }, StringSplitOptions.None);
        templates = Table("StatementLineId StatementCode LineCode SeqNo CalculationKind FormulaSpec LineType LineLabel NoteRef NaturalSign IsClickable ParentLineCode IsDeactivated");
        templates.ExtendedProperties["NIHRACS"] = true;
        foreach (string[] row in SqlValues(sql[1].Split(new[] { ") v(" }, StringSplitOptions.None)[0]))
        {
            if (row[3] == null) continue;
            templates.Rows.Add(templates.Rows.Count + 1, row[0], row[3], Int32.Parse(row[1]), row[6], Value(row[7]), row[2],
                row[4], Value(row[5]), Value(row[8]), true, Value(row[9]), row[10] == "1");
        }
        DataTable disclosures = Table("DisclosureCode SectionCode SectionTitle NoteRef DisclosureTitle SortOrder NoteSourceLineCode TriggerCode IsBaseRequired RequiresNarrative GuidanceText");
        foreach (string[] row in SqlValues(sql[2].Split(new[] { ") v(" }, StringSplitOptions.None)[0]))
            disclosures.Rows.Add(row[0], row[1], row[2], Value(row[3]), row[4], Int32.Parse(row[5]), row[6], "ALWAYS", true, false, DBNull.Value);
        Check(disclosures.Rows.Count == 24 && disclosures.AsEnumerable().Select(r => (string)r["NoteSourceLineCode"]).Distinct().Count() == 24,
            "Expected 24 note-backed face destinations.");

        string path = Path.Combine(root, "docs/Trial Balance Examples/NIHRACS Statements");
        var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "EMP_WAS", "EMP_WAGES" }, { "EMP_DCP", "EMP_DC" }, { "EMP_DBP", "EMP_DB" }, { "EMP_LOE", "EMP_LEAVE" }
        };
        var provisional = new Dictionary<string, string>();
        using (var file = new ExcelPackage(new FileInfo(Path.Combine(path, "NIHRACS TB 12.11.26 Mapping.xlsx"))))
        {
            ExcelWorksheet sheet = file.Workbook.Worksheets["TB Mapping"];
            for (int i = 3; i <= sheet.Dimension.End.Row; i++)
            {
                string gl = sheet.Cells[i, 1].Text.Trim();
                string key = sheet.Cells[i, 3].Text.Trim();
                string alias;
                if (gl.Length != 0) provisional.Add(gl, codes.TryGetValue(key, out alias) ? alias : key);
            }
        }
        DataTable source = Table("TbRowId SourceLedger GlAccount GlText DebitMovement CreditMovement AccumBalance");
        using (var file = new ExcelPackage(new FileInfo(Path.Combine(path, "Consolidated TBs.xlsx"))))
        {
            ExcelWorksheet sheet = file.Workbook.Worksheets["2026"];
            for (int i = 1; i <= sheet.Dimension.End.Row; i++)
            {
                if (sheet.Cells[i, 1].Text.Trim() != "NIHRA") continue;
                decimal debit = Convert.ToDecimal(sheet.Cells[i, 6].Value ?? 0m, CultureInfo.InvariantCulture);
                decimal credit = Convert.ToDecimal(sheet.Cells[i, 7].Value ?? 0m, CultureInfo.InvariantCulture);
                source.Rows.Add((long)i, DBNull.Value, sheet.Cells[i, 2].Text.Trim(), sheet.Cells[i, 4].Text, 0m, 0m,
                    Decimal.Round(debit - credit, 2));
            }
        }
        Check(source.Rows.Count == 778 && source.AsEnumerable().Sum(r => (decimal)r["AccumBalance"]) == 0m, "Source fixture changed.");
        var mappings = Table("AccountMapId GlCode AccountType StatementLine NoteSubLine CashFlowClass GlDescription");
        var existingQuery = NORMHelper.QueryCallResponse;
        Func<NORMHelper.Call, object> existingScalar = NORMHelper.ScalarCallResponse;
        NORMHelper.ScalarCallResponse = call => call.Sql.StartsWith("SELECT TOP 1 i.EntityCode") ? "NIHRACS" : existingScalar(call);
        NORMHelper.QueryCallResponse = call =>
        {
            string query = call.Sql;
            if (query.StartsWith("SELECT i.ImportId,c.ConfigurationReleaseId"))
                return Table("ImportId ConfigurationReleaseId FinancialYear EntityCode SourceType DataFingerprint VersionCode EntityName",
                    new object[] { 25, 10, 2026, "NIHRACS", "ERP", "fixture", "fixture", "NIHRACS" });
            if (query.StartsWith("SELECT AccountMapId,GlCode")) return mappings;
            if (query.StartsWith("SELECT s.LineCode,s.LineLabel,s.StatementCode FROM dbo.tblNORM_StatementLine s"))
                return TableFrom(templates, row => (string)row["LineCode"] == (string)call["@parent"]);
            if (query.StartsWith("SELECT TbRowId,SourceLedger")) return source;
            if (query.Contains("FROM dbo.tblNORM_DisclosureRule")) return disclosures;
            if (query.StartsWith("SELECT r.LineCode,r.ComputedAmount,r.ReportedAmount,t.ParentLineCode"))
                return RowsWithResults(false);
            if (query.StartsWith("SELECT r.LineCode,r.ReportedAmount") && call.Values.ContainsKey("@statement"))
                return TableFrom(RowsWithResults(true), r => (string)r["StatementCode"] == (string)call["@statement"]);
            if (query.Contains("AS IsHierarchy") && query.Contains("LEFT JOIN dbo.tblNORM_LineResult"))
            {
                DataTable face = TableFrom(RowsWithResults(true), r => (string)r["StatementCode"] ==
                    (string)call[call.Values.ContainsKey("@code") ? "@code" : "@statement"]);
                face.Columns.Add("IsHierarchy", typeof(int), null).DefaultValue = 1;
                foreach (DataRow row in face.Rows) row["IsHierarchy"] = 1;
                return face;
            }
            return existingQuery(call);
        };
        var types = NORMStatementEngine.LoadHierarchyAccountTypes(10);
        Check(types != null && types.Count == 60, "Expected exactly 60 active detail classifications.");
        foreach (string gl in source.AsEnumerable().Select(row => (string)row["GlAccount"]).Distinct())
        {
            string destination = provisional[gl];
            if (destination.StartsWith("EQU_")) continue;
            mappings.Rows.Add(mappings.Rows.Count + 1, gl, types[destination], destination, DBNull.Value, DBNull.Value, gl);
        }
        Check(mappings.Rows.Count == 244, "Non-equity GL coverage changed.");
        int run = NORMStatementEngine.RunAnnualSource(25, 10, "local-fixture");
        Check((decimal)Result(Parent)["@computed"] == 8290.88415m && Reported(Result(Parent)) == 8291m,
            "Employee benefits exact or reported balance changed.");
        Check((decimal)Result("Operating result")["@computed"] == -1107.73337m &&
            (decimal)Result("Net assets")["@computed"] == 100.29768m, "Exact control totals changed.");
        var leafIds = types.Keys.Select(code => Result(code).InsertedId).ToList();
        var contributions = NORMHelper.Committed.Where(call => call.Sql.StartsWith("INSERT dbo.tblNORM_Lineage") &&
            leafIds.Contains(Convert.ToInt32(call["@result"]))).ToList();
        Check(contributions.Count == 772 && contributions.Select(c => c["@row"]).Distinct().Count() == 772,
            "Source rows were repeated in, or omitted from, the detail population.");
        Check(!NORMHelper.Committed.Any(c => c.Sql.StartsWith("UPDATE dbo.tblNORM_Import") || c.Sql.StartsWith("INSERT dbo.tblNORM_Import")),
            "Recalculation made an import write.");
        foreach (string line in new[] { Parent, "Total expenses", "Total own-source income", "Net cost of services",
            "Operating result", "Total assets", "Total liabilities", "Net assets" })
            Console.WriteLine(line + " exact $000 " + Result(line)["@computed"] + " reported " + Reported(Result(line)));
        CheckNihracsOutputs(output, run);
        ConfigureDraft();
        draft.Rows[0]["EntityCode"] = "NIHRACS";
        accountMap = mappings;
        NORMHelper.QueryCallResponse = call =>
        {
            if (call.Sql.StartsWith("SELECT i.ImportId,c.ConfigurationReleaseId"))
                return Table("ImportId ConfigurationReleaseId FinancialYear EntityCode SourceType DataFingerprint VersionCode EntityName",
                    new object[] { 25, 10, 2026, "NIHRACS", "ERP", "fixture", "fixture", "NIHRACS" });
            if (call.Sql.StartsWith("SELECT TbRowId,SourceLedger")) return source;
            if (call.Sql.Contains("FROM dbo.tblNORM_DisclosureRule") && call.Sql.StartsWith("SELECT DisclosureCode"))
                return disclosures;
            if (call.Sql.StartsWith("SELECT r.LineCode,r.ComputedAmount,r.ReportedAmount,t.ParentLineCode"))
                return RowsWithResults(false);
            if (call.Sql.StartsWith("SELECT s.LineCode,s.LineLabel,s.StatementCode FROM dbo.tblNORM_StatementLine s"))
                return TableFrom(templates, row => (string)row["LineCode"] == (string)call["@parent"]);
            return Query(call);
        };
        TestHierarchyChanges(source, mappings);
    }

    private static void TestHierarchyChanges(DataTable source, DataTable mappings)
    {
        DataRow wages = templates.AsEnumerable().Single(r => (string)r["LineCode"] == "EMP_WAGES");
        wages["LineLabel"] = "Salary costs";
        int run = RerunNihracs();
        var employee = NORMReportingFramework.LoadDisclosures(run, 10, new NORMReportingFramework.ReportingProfile())
            .Single(n => n.Code == "N1_1A");
        Check(employee.ReportedAmount == 8291m && employee.Lines.Any(n => n.Label == "Salary costs"),
            "Changing the display label changed the reported value.");
        DataRow gl = mappings.AsEnumerable().First(r => (string)r["StatementLine"] == "EMP_WAGES");
        NORMMappingManagement.SaveEmployeeCategory(2, Parent, "EMP_ADDITIONAL", "Additional salary costs", false, "local-fixture");
        byte[] starting = NORMMappingManagement.BuildEditableWorkbook(2);
        foreach (bool hasIndependentNote in new[] { true, false })
        using (var workbook = new ExcelPackage(new MemoryStream(starting.ToArray())))
        {
            ExcelWorksheet sheet = workbook.Workbook.Worksheets["Mappings"];
            ExcelWorksheet reference = workbook.Workbook.Worksheets["Reference lists"];
            var keys = Enumerable.Range(2, reference.Dimension.End.Row - 1)
                .Select(row => reference.Cells[row, 3].Text).Where(code => code.Length != 0).ToList();
            Check(sheet.Dimension.End.Column == 7 && keys.Count == 61 && keys.Contains("EMP_ADDITIONAL") && !keys.Contains(Parent),
                "The standard workbook did not offer only the detailed classification keys.");
            int rowNumber = Enumerable.Range(5, sheet.Dimension.End.Row - 4)
                .Single(i => sheet.Cells[i, 1].Text == (string)gl["GlCode"]);
            sheet.Cells[rowNumber, 4].Value = "EMP_ADDITIONAL";
            sheet.Cells[rowNumber, 5].Value = hasIndependentNote ? "Extra independent note" : "";
            sheet.Cells[rowNumber, 7].Value = "Local approval fixture";
            byte[] content = workbook.GetAsByteArray();
            if (hasIndependentNote)
                Throws(() => NORMMappingManagement.ApplyWorkbook(2, content, "fixture.xlsx", "local-fixture"),
                    "leave Note sub-line blank");
            else
            {
                var saved = NORMMappingManagement.ApplyWorkbook(2, content, "fixture.xlsx", "local-fixture");
                Check(saved.ChangedCount == 1 && saved.Validation.CanApprove &&
                    (string)gl["StatementLine"] == "EMP_ADDITIONAL", "A detail-only workbook edit did not save.");
            }
        }
        RerunNihracs();
        decimal additional = source.AsEnumerable().Where(r => (string)r["GlAccount"] == (string)gl["GlCode"])
            .Sum(r => (decimal)r["AccumBalance"]) / 1000m;
        Check(Reported(Result("EMP_ADDITIONAL")) == Math.Round(additional, 0, MidpointRounding.AwayFromZero) &&
            (decimal)Result(Parent)["@computed"] == 8290.88415m &&
            Reported(Result(Parent)) == 772m + 1140m + Reported(Result("EMP_WAGES")) + Reported(Result("EMP_ADDITIONAL")),
            "Additional category did not aggregate from displayed values.");
        DataRow parent = templates.AsEnumerable().Single(r => (string)r["LineCode"] == Parent);
        Check(parent.IsNull("FormulaSpec"), "The additive parent formula changed.");
        parent["ParentLineCode"] = Parent;
        Throws(() => NORMStatementEngine.LoadHierarchyAccountTypes(10), "circular");
        parent["ParentLineCode"] = "Total expenses";
        DataRow op = templates.AsEnumerable().Single(r => (string)r["LineCode"] == "Operating result");
        op["FormulaSpec"] = "+Net cost of services|+Total expenses";
        Throws(() => NORMStatementEngine.LoadHierarchyAccountTypes(10), "more than once");
        Console.WriteLine("PASS stable description change, automatic new detail, cycle and duplicate contribution rejection");
    }

    private static int RerunNihracs()
    {
        NORMHelper.Committed.Clear();
        InsertedResults.Clear();
        return NORMStatementEngine.RunAnnualSource(25, 10, "local-fixture");
    }

    private static void CheckNihracsOutputs(string output, int run)
    {
        var disclosures = NORMReportingFramework.LoadDisclosures(run, 10, new NORMReportingFramework.ReportingProfile());
        Check(disclosures.Count == 24 && disclosures.All(n => n.ReportedAmount.HasValue), "Reported note data was omitted.");
        var employee = disclosures.Single(n => n.Code == "N1_1A");
        Check(employee.ReportedAmount == 8291m && employee.Lines.Sum(n => n.ReportedAmount.Value) == 8291m,
            "Employee benefits note and face differ.");
        var page = new NORM_Statements();
        Type web = typeof(NORM_Statements);
        var figures = new Dictionary<string, decimal>();
        var lineage = new Dictionary<long, List<Dictionary<string, object>>>();
        object income = Invoke(page, web, "BuildStatement", 10, run, "SOCI", "Income Statement", lineage, figures, figures);
        object balance = Invoke(page, web, "BuildStatement", 10, run, "SOFP", "Balance Sheet", lineage, figures, figures);
        object notes = Invoke(page, web, "BuildNotesStatement", disclosures);
        File.WriteAllText(Path.Combine(output, "presentation.json"), new JavaScriptSerializer().Serialize(new {
            meta = new { yearCurrent = 2026, yearPrior = 2025, entity = "NIHRACS", runId = run },
            statements = new[] { income, balance, notes }
        }));
        var word = new NORM_WordExport();
        typeof(NORM_WordExport).GetField("priorFigures", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(word, figures);
        var html = new StringBuilder();
        Invoke(word, typeof(NORM_WordExport), "AppendStatement", html, run, 10, 2026, "SOCI", "Income Statement", false);
        Invoke(word, typeof(NORM_WordExport), "AppendStatement", html, run, 10, 2026, "SOFP", "Balance Sheet", true);
        Invoke(word, typeof(NORM_WordExport), "AppendNotes", html, run, 2026, disclosures);
        Check(html.ToString().Contains("<th>Employee benefits</th><td>1.1A</td><td class=\"amount\">8,291</td>") &&
            html.ToString().Contains("<th>Total employee benefits</th><td class=\"amount\">8,291</td>") &&
            html.ToString().Contains("<th>Term deposits</th><td class=\"amount\">407</td>"), "Word output lost the reported amount.");
        Type excel = typeof(NORM_FinancialStatementsExcel);
        Type ctx = excel.GetNestedType("ExportContext", BindingFlags.NonPublic);
        object model = Activator.CreateInstance(ctx, true);
        foreach (var field in new Dictionary<string, object> {
            { "RunId", run }, { "ReleaseId", 10 }, { "Year", 2026 }, { "Entity", "NIHRACS" },
            { "Budgets", figures }, { "PriorFigures", figures }, { "ManualInputs", new DataTable() }, { "Disclosures", disclosures }
        })
            ctx.GetField(field.Key).SetValue(model, field.Value);
        using (var package = new ExcelPackage())
        {
            var index = new List<Tuple<string, string, string>>();
            Invoke(null, excel, "AddFace", package, model, "SOCI", "SOCI", "Income Statement", false, index);
            Invoke(null, excel, "AddFace", package, model, "SOFP", "SOFP", "Balance Sheet", true, index);
            Invoke(null, excel, "AddNotes", package, model, index);
            using (var saved = new ExcelPackage(new MemoryStream(package.GetAsByteArray())))
            {
                Check(ExcelValue(saved.Workbook.Worksheets["SOCI"], Parent, 3) == 8291m &&
                    ExcelValue(saved.Workbook.Worksheets["SOFP"], "Net assets", 3) == Reported(Result("Net assets")) &&
                    ExcelValue(saved.Workbook.Worksheets["1.1A Employee benefits"], "Total employee benefits", 2) == 8291m,
                    "Saved Excel values differ from the calculation.");
            }
        }
        Console.WriteLine("PASS 24 face destinations, 60 active keys, 778 retained rows, 772 single contributions, 24 notes, Word and Excel");
    }
}
