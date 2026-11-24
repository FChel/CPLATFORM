using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
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

    private static void AddCashFlowReferences(string root, string path)
    {
        string sql = File.ReadAllText(Path.Combine(root, "sql/NORM_Stage2B_NIHRACS_Cash_Flow_References.sql"));
        string values = sql.Split(new[] { "FROM (VALUES" }, StringSplitOptions.None)[1].Split(new[] { ") v(" }, StringSplitOptions.None)[0];
        var definitions = SqlValues(values).ToDictionary(row => row[2], StringComparer.OrdinalIgnoreCase);
        templates.Columns.Add("MappingDescription", typeof(string));
        using (var workbook = new ExcelPackage(new FileInfo(Path.Combine(path, "NIHRACS Annual Statements.xlsm"))))
        {
            var notes = workbook.Workbook.Worksheets["Notes"];
            var accounts = workbook.Workbook.Worksheets["Accounts"];
            var noteKeys = Enumerable.Range(1, notes.Dimension.End.Row).Select(row => notes.Cells[row, 4].Text.Trim())
                .Where(key => key.StartsWith("CF_", StringComparison.Ordinal)).ToList();
            Check(noteKeys.Distinct().Count() == noteKeys.Count, "The source contains duplicate cash-flow definitions.");
            foreach (string[] definition in definitions.Values)
            {
                int row = Int32.Parse(definition[0], CultureInfo.InvariantCulture);
                Check(notes.Cells[row, 4].Text.Trim() == definition[2] && notes.Cells[row, 6].Text.Trim() == definition[3] &&
                    (definition[4] == null || definitions.ContainsKey(definition[4])),
                    "A cash-flow key, label or destination does not match the reference.");
                templates.Rows.Add(templates.Rows.Count + 500, "SCFREF", definition[2], row, "Heading", DBNull.Value,
                    definition[1], definition[3], DBNull.Value, DBNull.Value, false, Value(definition[4]), false, Value(definition[5]));
            }
            var assignments = Enumerable.Range(4, accounts.Dimension.End.Row - 3)
                .Where(row => accounts.Cells[row, 22].Text.Trim().StartsWith("CF_", StringComparison.Ordinal))
                .ToDictionary(row => accounts.Cells[row, 1].Text.Trim(), row => accounts.Cells[row, 22].Text.Trim());
            var detail = definitions.Values.Where(row => row[1] == "cashflow-detail").Select(row => row[2]).ToList();
            Check(assignments.Count == 350 && detail.Count == 10 && definitions.Count == 19 &&
                detail.All(key => assignments.Values.Contains(key)) && assignments.Values.Except(detail).SequenceEqual(new[] { "CF_OPA_USE_SUP1" }) &&
                assignments["58140"] == "CF_OPA_USE_SUP1" && !noteKeys.Contains("CF_OPA_USE_SUP1"),
                "Cash-flow reference reconciliation or the Audit Fee exception changed.");
            Console.WriteLine("PASS source workbook: 350 assignments, 10 defined detail keys and GL 58140 with the only undefined GL key");
        }
    }

    private static void CheckNihracs(string output, string root, string consolidatedWorkbook)
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
        AddCashFlowReferences(root, path);
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
        DataTable source = Table("TbRowId SourceLedger GlAccount GlText DebitMovement CreditMovement AccumBalance SourceCompanyCode ReportingStream SourceDebit SourceCredit");
        using (var file = new ExcelPackage(new FileInfo(Path.Combine(path, "Consolidated TBs.xlsx"))))
        {
            ExcelWorksheet sheet = file.Workbook.Worksheets["2026"];
            for (int i = 1; i <= sheet.Dimension.End.Row; i++)
            {
                if (sheet.Cells[i, 1].Text.Trim() != "NIHRA") continue;
                decimal debit = Convert.ToDecimal(sheet.Cells[i, 6].Value ?? 0m, CultureInfo.InvariantCulture);
                decimal credit = Convert.ToDecimal(sheet.Cells[i, 7].Value ?? 0m, CultureInfo.InvariantCulture);
                source.Rows.Add((long)i, DBNull.Value, sheet.Cells[i, 2].Text.Trim(), sheet.Cells[i, 4].Text, 0m, 0m,
                    Decimal.Round(debit - credit, 2), "NIHRA", "Departmental", debit, credit);
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
        Check(!source.AsEnumerable().Any(row => (string)row["GlAccount"] == "58200"), "Model-only account is now present in the source fixture.");
        DataRow modelOnly = mappings.Rows.Add(245, "58200", "Expense", "SUP_GAS_GDS_OTH", DBNull.Value, DBNull.Value, "Inventory Adjustment");
        DataRow incomplete = mappings.Rows.Add(246, "33016", "Equity", DBNull.Value, DBNull.Value, DBNull.Value, "Unresolved equity");
        int run = NORMStatementEngine.RunAnnualSource(25, 10, "local-fixture");
        mappings.Rows.Remove(modelOnly);
        mappings.Rows.Remove(incomplete);
        Check((decimal)Result(Parent)["@computed"] == 8290.88415m && Reported(Result(Parent)) == 8291m,
            "Employee benefits exact or reported balance changed.");
        Check(InsertedResults.All(result => !((string)result["@line"]).StartsWith("CF_", StringComparison.Ordinal)),
            "Cash-flow references must not generate statement results.");
        Check((decimal)Result("Operating result")["@computed"] == -1107.73337m &&
            (decimal)Result("Net assets")["@computed"] == 100.29768m, "Exact control totals changed.");
        var leafIds = types.Keys.Select(code => Result(code).InsertedId).ToList();
        var contributions = NORMHelper.Committed.Where(call => call.Sql.StartsWith("INSERT dbo.tblNORM_Lineage") &&
            leafIds.Contains(Convert.ToInt32(call["@result"]))).ToList();
        Check(contributions.Count == 772 && contributions.Select(c => c["@row"]).Distinct().Count() == 772,
            "Source rows were repeated in, or omitted from, the detail population.");
        var unmapped = NORMHelper.Committed.Single(call => call.Sql.StartsWith("INSERT dbo.tblNORM_ValidationResult") &&
            (string)call["@code"] == "UNMAPPED_DISPOSITION");
        int unmappedResult = Result("UNMAPPED").InsertedId;
        Check((string)unmapped["@result"] == "Fail" && (string)unmapped["@severity"] == "Blocking" && Convert.ToInt32(unmapped["@actual"]) == 6 &&
            NORMHelper.Committed.Count(call => call.Sql.StartsWith("INSERT dbo.tblNORM_Lineage") &&
                Convert.ToInt32(call["@result"]) == unmappedResult) == 6,
            "Unmapped source rows must remain visible in validation and lineage.");
        Console.WriteLine("PASS model-only mapping adds no source contributions; six unmapped rows remain in validation and lineage");
        Check(!NORMHelper.Committed.Any(c => c.Sql.StartsWith("UPDATE dbo.tblNORM_Import") || c.Sql.StartsWith("INSERT dbo.tblNORM_Import")),
            "Recalculation made an import write.");
        foreach (string line in new[] { Parent, "Total expenses", "Total own-source income", "Net cost of services",
            "Operating result", "Total assets", "Total liabilities", "Net assets" })
            Console.WriteLine(line + " exact $000 " + Result(line)["@computed"] + " reported " + Reported(Result(line)));
        CheckNihracsOutputs(output, run);
        DataTable uploadCatalogue = templates.Copy();
        ConfigureDraft();
        draft.Rows[0]["EntityCode"] = "NIHRACS";
        accountMap = mappings;
        NORMHelper.QueryCallResponse = call =>
        {
            if (call.Sql.StartsWith("SELECT i.ImportId,c.ConfigurationReleaseId"))
                return Table("ImportId ConfigurationReleaseId FinancialYear EntityCode SourceType DataFingerprint VersionCode EntityName",
                    new object[] { 25, 10, 2026, "NIHRACS", "ERP", "fixture", "fixture", "NIHRACS" });
            if (call.Sql.StartsWith("SELECT TbRowId,SourceLedger") || call.Sql.StartsWith("SELECT GlAccount,SourceCompanyCode")) return source;
            if (call.Sql.Contains("FROM dbo.tblNORM_DisclosureRule") && call.Sql.StartsWith("SELECT DisclosureCode"))
                return disclosures;
            if (call.Sql.StartsWith("SELECT r.LineCode,r.ComputedAmount,r.ReportedAmount,t.ParentLineCode"))
                return RowsWithResults(false);
            if (call.Sql.StartsWith("SELECT s.LineCode,s.LineLabel,s.StatementCode FROM dbo.tblNORM_StatementLine s"))
                return TableFrom(templates, row => (string)row["LineCode"] == (string)call["@parent"]);
            return Query(call);
        };
        TestHierarchyChanges(source, mappings);
        CheckMappingUpload(source, uploadCatalogue, consolidatedWorkbook, output);
        CheckUnmappedActivity();
    }

    private static void CheckMappingUpload(DataTable source, DataTable catalogue, string consolidatedWorkbook, string output)
    {
        Prepare();
        templates = catalogue;
        ConfigureDraft();
        draft.Rows[0]["ConfigurationReleaseId"] = 19;
        draft.Rows[0]["ParentConfigurationReleaseId"] = 0;
        draft.Rows[0]["EntityCode"] = "NIHRACS";
        accountMap = Table("AccountMapId GlCode AccountType StatementLine NoteSubLine CashFlowClass GlDescription");
        DataTable sourceAccounts = Table("GlAccount GlDescription");
        foreach (var group in source.AsEnumerable().GroupBy(row => (string)row["GlAccount"]))
            sourceAccounts.Rows.Add(group.Key, group.Max(row => (string)row["GlText"]));
        bool fullWorkbook = !String.IsNullOrEmpty(consolidatedWorkbook);
        long lockedRevision = 255L;
        DataTable approvalSource = source;
        if (!fullWorkbook)
        {
            approvalSource = source.Clone();
            approvalSource.Rows.Add(1L, DBNull.Value, "90004", "Idle", 0m, 0m, 0m, "NIHRA", "Departmental", 0m, 0m);
            approvalSource.Rows.Add(2L, DBNull.Value, "90005", "Offsetting", 0m, 0m, 0m, "NIHRA", "Departmental", 2m, 2m);
        }
        NORMHelper.QueryCallResponse = call =>
        {
            if (call.Sql.StartsWith("SELECT GlAccount,MAX(GlText"))
            {
                Check(Convert.ToInt32(call["@import"]) == 25, "Upload used the wrong description source.");
                return sourceAccounts;
            }
            if (call.Sql.StartsWith("SELECT GlAccount,SourceCompanyCode"))
                return approvalSource;
            if (call.Sql.StartsWith("SELECT i.ImportId,c.ConfigurationReleaseId"))
            {
                Check(call.Sql.Contains("c.StatusCode='Draft'") && call.Sql.Contains("i.ConfigurationReleaseId IS NULL") &&
                    (int)call["@release"] == 19 && (int)call["@import"] == 25, "Draft review did not select the retained annual source and draft.");
                return Table("ImportId ConfigurationReleaseId FinancialYear EntityCode SourceType DataFingerprint VersionCode EntityName DraftRevision",
                    new object[] { 25, 19, 2026, "NIHRACS", "ERP", "fixture", "fixture", "NIHRACS", 255L });
            }
            if (call.Sql.Contains("AS DraftRevision FROM dbo.tblNORM_ConfigurationRelease"))
            {
                Check(call.InTransaction && call.Sql.Contains("UPDLOCK,HOLDLOCK") && call.Sql.Contains("c.StatusCode='Draft'") &&
                    (int)call["@release"] == 19, "Draft review did not lock its current configuration.");
                return Table("DraftRevision", new object[] { lockedRevision });
            }
            if (call.Sql.StartsWith("SELECT TbRowId,SourceLedger")) return source;
            return Query(call);
        };
        var draftScalar = NORMHelper.ScalarCallResponse;
        NORMHelper.ScalarCallResponse = call => call.Sql.StartsWith("SELECT COUNT(*) FROM dbo.tblNORM_StatementLine")
            ? 60 : draftScalar(call);
        var commitDraft = NORMHelper.OnCommit;
        NORMHelper.OnCommit = changes =>
        {
            foreach (NORMHelper.Call change in changes.Where(call => call.Sql.StartsWith("INSERT dbo.tblNORM_AccountMap")))
                accountMap.Rows.Add(accountMap.Rows.Count + 1, change["@gl"], change["@type"], change["@line"],
                    change["@note"], change["@cash"], change["@description"]);
            commitDraft(changes);
        };

        byte[] starting = NORMMappingManagement.BuildEditableWorkbook(19);
        using (var sample = new ExcelPackage(new MemoryStream(starting.ToArray())))
        {
            var mapping = sample.Workbook.Worksheets["Mappings"];
            var lists = sample.Workbook.Worksheets["Reference lists"];
            var cash = Enumerable.Range(2, lists.Dimension.End.Row - 1).Select(row => lists.Cells[row, 3].Text)
                .Where(value => value.Length > 0).ToList();
            Check(mapping.Dimension.End.Column == 5 && sample.Workbook.Worksheets["_NORM"].Cells[1, 2].Text == "5" &&
                mapping.DataValidations.Count == 2 && cash.Count == 10 && cash.Contains("CF_OPA_USE_SUP | Supplier payments") &&
                !cash.Any(value => value.StartsWith("CF_OPA_USE_SUP1")) &&
                Enumerable.Range(2, lists.Dimension.End.Row - 1).Any(row => lists.Cells[row, 1].Text == "EMP_WAGES | Wages and salaries (Employee benefits)"),
                "Five-column workbook or descriptive lists are incorrect.");
        }
        File.WriteAllBytes(Path.Combine(output, "NIHRACS_Hierarchy_Mapping_Sample.xlsx"), starting);
        Throws(() => NORMMappingManagement.ApplyWorkbook(19, UploadFixture(starting, secondGl: "58200"), "local.xlsx", "fixture"),
            "appears more than once");
        Check(NORMHelper.Commits == 0, "A rejected workbook made writes.");

        byte[] contents = fullWorkbook ? ConvertConsolidatedWorkbook(starting, consolidatedWorkbook) : UploadFixture(starting);
        NORMMappingUploadOutcome applied = NORMMappingManagement.ApplyWorkbook(19, contents, "local.xlsx", "fixture");
        int expectedRows = fullWorkbook ? 433 : 4;
        int expectedIncomplete = fullWorkbook ? 36 : 3;
        var outsideSource = accountMap.AsEnumerable().Where(row => !sourceAccounts.AsEnumerable()
            .Any(sourceRow => (string)sourceRow["GlAccount"] == (string)row["GlCode"])).ToList();
        Check(applied.ChangedCount == expectedRows && applied.Validation.MappingCount == expectedRows &&
            outsideSource.Count == (fullWorkbook ? 183 : 4), "Mapping upload omitted GLs outside the retained trial balance.");
        Check((string)outsideSource.Single(row => (string)row["GlCode"] == "58200")["GlDescription"] == "Inventory Adjustment",
            "The new GL description did not come from the workbook.");
        Check(applied.Validation.UnmappedCount == (fullWorkbook ? 10 : 2) &&
            applied.Validation.Warnings.Any(warning => warning.StartsWith(expectedIncomplete + " saved mapping")),
            "Incomplete classifications must remain blank and visible before approval.");
        if (!fullWorkbook)
        {
            Check((string)accountMap.Rows[0]["AccountType"] == "Expense" && accountMap.Rows[1].IsNull("StatementLine") &&
                (string)accountMap.Rows[2]["StatementLine"] == "NO_SUCH_CLASSIFICATION" && accountMap.Rows[2].IsNull("AccountType") &&
                (string)accountMap.Rows[3]["StatementLine"] == "REV_DON" && accountMap.Rows[3].IsNull("AccountType"),
                "Upload failed to derive the active type or retain the incomplete keys.");
            Check(applied.Validation.Warnings.Any(warning => warning.Contains("activity that offsets to zero") && warning.Contains("90005")) &&
                !applied.Validation.Warnings.Any(warning => warning.Contains("90004")),
                "Zero-balance accounts with and without activity were not distinguished.");
        }
        Check(accountMap.AsEnumerable().All(row => !(NORMHelper.Str(row, "StatementLine") ?? "").Contains(" | ") &&
            !(NORMHelper.Str(row, "CashFlowClass") ?? "").Contains(" | ")) &&
            accountMap.AsEnumerable().Any(row => NORMHelper.Str(row, "CashFlowClass") == "CF_OPA_USE_SUP"),
            "The classification display string was persisted instead of its key.");
        string exceptionGl = fullWorkbook ? "58140" : "90002";
        Check(accountMap.AsEnumerable().Single(row => (string)row["GlCode"] == exceptionGl)["CashFlowClass"].Equals("CF_OPA_USE_SUP1") &&
            applied.Validation.Warnings.Any(warning => warning.Contains("undefined cash-flow classification") &&
                warning.Contains(exceptionGl + " (CF_OPA_USE_SUP1)")) && accountMap.AsEnumerable().Any(row => row.IsNull("CashFlowClass")),
            "An unresolved or blank cash-flow key was not preserved for review.");
        using (var exported = new ExcelPackage(new MemoryStream(NORMMappingManagement.BuildEditableWorkbook(19))))
        {
            var rows = exported.Workbook.Worksheets["Mappings"];
            int exceptionRow = Enumerable.Range(5, rows.Dimension.End.Row - 4).Single(row => rows.Cells[row, 1].Text == exceptionGl);
            Check(rows.Cells[exceptionRow, 4].Text == "CF_OPA_USE_SUP1", "The exported workbook concealed or changed the unresolved cash-flow key.");
        }
        Check((string)draft.Rows[0]["StatusCode"] == "Draft" &&
            NORMHelper.Committed.Count(call => call.Sql.StartsWith("INSERT dbo.tblNORM_MappingChange")) == expectedRows &&
            !NORMHelper.Committed.Any(call => call.Sql.Contains("dbo.tblNORM_TrialBalanceRow") || call.Sql.Contains("dbo.tblNORM_Import")),
            "The workbook did not audit each mapping or attempted a source write.");
        Throws(() => NORMMappingManagement.Approve(19, fullWorkbook, "fixture"),
            fullWorkbook ? "Resolve all mapping validation errors" : "Acknowledge the mapping warnings");
        Check(applied.Validation.CanApprove != fullWorkbook, "Only impactful source omissions must block approval.");
        Console.WriteLine("PASS duplicate rejection; " + applied.ChangedCount + " rows accepted, " + outsideSource.Count +
            " outside Import 25; incomplete mappings reported and approval checked against source impact");
        if (fullWorkbook)
        {
            CheckDraftReview(output);
            lockedRevision = 256L;
            Throws(() => NORMMappingManagement.CalculateDraftReview(19, "fixture"), "The mapping draft changed");
            accountMap.Clear();
            Check(NORMMappingManagement.ApplyWorkbook(19, File.ReadAllBytes(consolidatedWorkbook), "older-layout.xlsx", "fixture").ChangedCount == 433,
                "The previous seven-column hierarchy format no longer uploads.");
        }
    }

    private static void CheckDraftReview(string output)
    {
        Throws(() => NORMMappingManagement.RecalculateLatest(19, "fixture"), "Approve the mapping release before recalculating");
        NORMHelper.Committed.Clear();
        InsertedResults.Clear();
        string draftHash = (string)draft.Rows[0]["ContentHash"];
        int runId = NORMMappingManagement.CalculateDraftReview(19, "fixture");
        var checks = NORMHelper.Committed.Where(call => call.Sql.StartsWith("INSERT dbo.tblNORM_ValidationResult")).ToList();
        var marker = checks.Single(call => (string)call["@code"] == NORMStatementEngine.DraftReviewCheckCode);
        var unmapped = checks.Single(call => (string)call["@code"] == "UNMAPPED_DISPOSITION");
        Check((string)marker["@detail"] == "255" && (string)marker["@severity"] == "Blocking" && (string)marker["@result"] == "Fail" &&
            (string)unmapped["@result"] == "Fail" && Convert.ToInt32(unmapped["@actual"]) == 10 &&
            (string)Result("UNMAPPED")["@status"] == "Unmapped", "Draft marker or unresolved source checks were lost.");
        int markerIndex = NORMHelper.Committed.IndexOf(marker);
        Check(markerIndex > 0 && NORMHelper.Committed[markerIndex - 1].Sql.StartsWith("INSERT dbo.tblNORM_CalculationRun"),
            "The review marker must be recorded when the run is created.");
        Check(NORMHelper.Committed.Any(call => call.Sql.StartsWith("UPDATE dbo.tblNORM_CalculationRun SET StatusCode = 'Complete'")) &&
            NORMHelper.Committed.Any(call => call.Sql.StartsWith("INSERT dbo.tblNORM_AuditEvent") &&
                ((string)call["@detail"]).StartsWith("Draft mapping review completed")) &&
            (string)draft.Rows[0]["StatusCode"] == "Draft" && (string)draft.Rows[0]["ContentHash"] == draftHash &&
            !NORMHelper.Committed.Any(call => call.Sql.StartsWith("UPDATE dbo.tblNORM_ConfigurationRelease") ||
                call.Sql.StartsWith("INSERT dbo.tblNORM_Import") || call.Sql.StartsWith("UPDATE dbo.tblNORM_Import") ||
                call.Sql.StartsWith("INSERT dbo.tblNORM_TrialBalanceRow")), "Draft review did not complete without release or source writes.");
        NORMHelper.DraftReviewRun = true;
        foreach (IHttpHandler handler in new IHttpHandler[] {new NORM_WordExport(), new NORM_FinancialStatementsExcel()})
        {
            var response = new HttpResponse(new StringWriter());
            var request = new HttpRequest("", "http://localhost/review.ashx", "run=" + runId.ToString(CultureInfo.InvariantCulture));
            var context = new HttpContext(request, response) { User = HttpContext.Current.User };
            handler.ProcessRequest(context);
            Check(response.StatusCode == 403, "A draft review was allowed through a controlled export.");
        }
        NORMHelper.DraftReviewRun = false;
        Throws(() => NORMMappingManagement.Approve(19, true, "fixture"), "Resolve all mapping validation errors");
        var validations = checks.Select(call => new {
            code = call["@code"], label = call["@label"], severity = call["@severity"], result = call["@result"],
            detail = call == marker ? "Calculation uses a draft mapping release." : call["@detail"]
        });
        File.WriteAllText(Path.Combine(output, "review-checks.json"), new JavaScriptSerializer().Serialize(validations));
        Console.WriteLine("PASS partial mappings calculated through the draft action, with immutable review marker, ten unresolved GLs and no source or approval writes");
    }

    private static byte[] ConvertConsolidatedWorkbook(byte[] starting, string consolidatedWorkbook)
    {
        using (var input = new ExcelPackage(new MemoryStream(File.ReadAllBytes(consolidatedWorkbook))))
        using (var workbook = new ExcelPackage(new MemoryStream(starting.ToArray())))
        {
            var oldRows = input.Workbook.Worksheets["Mappings"];
            var rows = workbook.Workbook.Worksheets["Mappings"];
            var lists = workbook.Workbook.Worksheets["Reference lists"];
            var choices = Enumerable.Range(2, lists.Dimension.End.Row - 1).Select(row => lists.Cells[row, 1].Text)
                .Where(value => value.Length != 0).ToDictionary(value => value.Split('|')[0].Trim());
            var cashChoices = Enumerable.Range(2, lists.Dimension.End.Row - 1).Select(row => lists.Cells[row, 3].Text)
                .Where(value => value.Length != 0).ToDictionary(value => value.Split('|')[0].Trim());
            for (int row = 5; row <= oldRows.Dimension.End.Row; row++)
            {
                string choice;
                string detail = oldRows.Cells[row, 4].Text;
                string cash = oldRows.Cells[row, 1].Text == "58140" ? "CF_OPA_USE_SUP1" : oldRows.Cells[row, 6].Text;
                rows.Cells[row, 1].Value = oldRows.Cells[row, 1].Text;
                rows.Cells[row, 2].Value = oldRows.Cells[row, 2].Text;
                rows.Cells[row, 3].Value = choices.TryGetValue(detail, out choice) ? choice : detail;
                rows.Cells[row, 4].Value = cashChoices.TryGetValue(cash, out choice) ? choice : cash;
                rows.Cells[row, 5].Value = oldRows.Cells[row, 7].Text;
            }
            return workbook.GetAsByteArray();
        }
    }

    private static byte[] UploadFixture(byte[] starting, string secondGl = "90001")
    {
        using (var workbook = new ExcelPackage(new MemoryStream(starting.ToArray())))
        {
            var rows = workbook.Workbook.Worksheets["Mappings"];
            rows.Cells[5, 1].Value = "58200";
            rows.Cells[5, 2].Value = "Inventory Adjustment";
            rows.Cells[5, 3].Value = "SUP_GAS_GDS_OTH";
            rows.Cells[5, 4].Value = "CF_OPA_USE_SUP | Supplier payments";
            rows.Cells[5, 5].Value = "Local Model mapping";
            rows.Cells[6, 1].Value = secondGl;
            rows.Cells[6, 5].Value = "Intentionally unresolved";
            rows.Cells[7, 1].Value = "90002";
            rows.Cells[7, 3].Value = "NO_SUCH_CLASSIFICATION | Review selection";
            rows.Cells[7, 4].Value = "CF_OPA_USE_SUP1";
            rows.Cells[7, 5].Value = "Retain unknown key for review";
            rows.Cells[8, 1].Value = "90003";
            rows.Cells[8, 3].Value = "REV_DON";
            rows.Cells[8, 5].Value = "Retain inactive key for review";
            return workbook.GetAsByteArray();
        }
    }

    private static void CheckUnmappedActivity()
    {
        var rows = Table("GlAccount SourceCompanyCode SourceLedger ReportingStream AccumBalance DebitMovement CreditMovement SourceDebit SourceCredit",
            new object[] { "idle", "A", "0L", "Departmental", 0m, 0m, 0m, 0m, 0m },
            new object[] { "offsetting", "A", "0L", "Departmental", 0m, 3m, -3m, 0m, 0m },
            new object[] { "annual-offset", "A", "0L", "Departmental", 0m, 0m, 0m, 9m, 9m },
            new object[] { "closing", "A", "0L", "Departmental", 1m, 0m, 0m, 0m, 0m },
            new object[] { "movement", "A", "0L", "Departmental", 0m, 2m, 0m, 0m, 0m },
            new object[] { "separate", "A", "0L", "Departmental", 5m, 0m, 0m, 0m, 0m },
            new object[] { "separate", "A", "0L", "Administered", -5m, 0m, 0m, 0m, 0m });
        var check = NORMStatementEngine.AssessUnmapped(rows.AsEnumerable(), true);
        Check(check.Count == 6 && check.Blocking.SequenceEqual(new[] { "closing", "movement", "separate" }) &&
            check.Offsetting.SequenceEqual(new[] { "offsetting", "annual-offset" }),
            "Unmapped reporting impact or offsetting source activity was classified incorrectly.");
        Console.WriteLine("PASS zero impact ignored, offsetting activity reviewed, non-zero balance or movement blocks sign-off per population");
    }

    private static void TestHierarchyChanges(DataTable source, DataTable mappings)
    {
        DataRow wages = templates.AsEnumerable().Single(r => (string)r["LineCode"] == "EMP_WAGES");
        NORMHelper.MappingAdministrator = false;
        DataRow supplierCash = templates.AsEnumerable().Single(r => (string)r["LineCode"] == "CF_OPA_USE_SUP");
        NORMMappingManagement.SaveClassificationText(2, "CF_OPA_USE_SUP", "Operating payments to suppliers", true, "preparer");
        Throws(() => NORMMappingManagement.SaveClassificationText(2, "CF_OPA_USE_SUP", "Changed cash statement wording", false, "preparer"),
            "editable detail classification");
        Check((string)supplierCash["LineLabel"] == "Suppliers" && (string)supplierCash["MappingDescription"] == "Operating payments to suppliers",
            "Changing the cash-flow description changed its source label.");
        NORMMappingManagement.SaveClassificationText(2, "EMP_WAGES", "Salary and wages for employees", true, "preparer");
        Check((string)wages["MappingDescription"] == "Salary and wages for employees" && (string)wages["LineLabel"] == "Wages and salaries",
            "Mapping Description changed the Statement Label.");
        int descriptionRun = RerunNihracs();
        Check(Reported(Result(Parent)) == 8291m && NORMReportingFramework.LoadDisclosures(descriptionRun, 10,
            new NORMReportingFramework.ReportingProfile()).Single(n => n.Code == "N1_1A").Lines.Any(n => n.Label == "Wages and salaries"),
            "The mapping-only description changed the statement or amount.");
        NORMMappingManagement.SaveClassificationText(2, "EMP_WAGES", "Salary costs", false, "preparer");
        Check((string)wages["MappingDescription"] == "Salary and wages for employees" &&
            NORMHelper.Committed.Any(call => call.Sql.StartsWith("INSERT dbo.tblNORM_AuditEvent") && ((string)call["@detail"]).Contains("Statement Label")),
            "The statement label edit changed the mapping description or lost audit evidence.");
        Throws(() => NORMMappingManagement.SaveClassificationText(2, Parent, "Protected face wording", false, "preparer"), "editable detail classification");
        Throws(() => NORMMappingManagement.SaveEmployeeCategory(2, Parent, "EMP_UNAUTHORISED", "Structural edit", false, "preparer"), "Administrator access");
        NORMHelper.EntityAccess = entity => false;
        Throws(() => NORMMappingManagement.SaveClassificationText(2, "EMP_WAGES", "Unauthorised", true, "preparer"), "Entity access denied");
        NORMHelper.EntityAccess = entity => true;
        DataRow leave = templates.AsEnumerable().Single(r => (string)r["LineCode"] == "EMP_LEAVE");
        NORMMappingManagement.SaveClassificationText(2, "EMP_LEAVE", "Other employee leave", false, "preparer");
        Check((string)leave["MappingDescription"] == "Leave and other entitlements (Employee benefits)" &&
            (string)leave["LineLabel"] == "Other employee leave", "A label edit replaced the original default mapping wording.");
        draft.Rows[0]["StatusCode"] = "Approved";
        Throws(() => NORMMappingManagement.SaveClassificationText(2, "EMP_WAGES", "Locked wording", false, "preparer"), "Only a draft");
        Check((string)wages["LineLabel"] == "Salary costs", "An immutable release accepted a wording edit.");
        draft.Rows[0]["StatusCode"] = "Draft";
        NORMHelper.MappingAdministrator = true;
        int run = RerunNihracs();
        var employee = NORMReportingFramework.LoadDisclosures(run, 10, new NORMReportingFramework.ReportingProfile())
            .Single(n => n.Code == "N1_1A");
        Check(employee.ReportedAmount == 8291m && employee.Lines.Any(n => n.Label == "Salary costs"),
            "Changing the display label changed the reported value.");
        DataRow gl = mappings.AsEnumerable().First(r => (string)r["StatementLine"] == "EMP_WAGES");
        NORMMappingManagement.SaveEmployeeCategory(2, Parent, "EMP_ADDITIONAL", "Additional salary costs", false, "local-fixture");
        byte[] starting = NORMMappingManagement.BuildEditableWorkbook(2);
        using (var workbook = new ExcelPackage(new MemoryStream(starting.ToArray())))
        {
            ExcelWorksheet sheet = workbook.Workbook.Worksheets["Mappings"];
            ExcelWorksheet reference = workbook.Workbook.Worksheets["Reference lists"];
            var keys = Enumerable.Range(2, reference.Dimension.End.Row - 1)
                .Select(row => reference.Cells[row, 1].Text).Where(code => code.Length != 0).ToList();
            string option = keys.Single(value => value.StartsWith("EMP_ADDITIONAL | "));
            Check(sheet.Dimension.End.Column == 5 && keys.Count == 61 && !keys.Any(value => value.StartsWith(Parent + " | ")) &&
                keys.Contains("EMP_WAGES | Salary and wages for employees") &&
                Enumerable.Range(2, reference.Dimension.End.Row - 1).Any(row =>
                    reference.Cells[row, 3].Text == "CF_OPA_USE_SUP | Operating payments to suppliers"),
                "Expected five columns and 61 details including custom wording; found " + sheet.Dimension.End.Column +
                    " columns, " + keys.Count + " options and " + keys.Single(value => value.StartsWith("EMP_WAGES | ")) + ".");
            int rowNumber = Enumerable.Range(5, sheet.Dimension.End.Row - 4)
                .Single(i => sheet.Cells[i, 1].Text == (string)gl["GlCode"]);
            sheet.Cells[rowNumber, 3].Value = option;
            sheet.Cells[rowNumber, 5].Value = "Local approval fixture";
            byte[] content = workbook.GetAsByteArray();
            var saved = NORMMappingManagement.ApplyWorkbook(2, content, "fixture.xlsx", "local-fixture");
            Check(saved.ChangedCount == 1 && !saved.Validation.CanApprove &&
                (string)gl["StatementLine"] == "EMP_ADDITIONAL", "A detail-only workbook edit did not save.");
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
        Console.WriteLine("PASS separate wording, unchanged amounts, preparer/admin permissions, protected and approved labels, and hierarchy validation");
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
