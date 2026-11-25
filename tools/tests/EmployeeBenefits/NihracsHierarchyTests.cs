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
        DataTable priorSource = source.Clone();
        var parsed = NihracsTrialBalanceImporter.TryParse(File.ReadAllBytes(Path.Combine(path, "Consolidated TBs.xlsx")));
        foreach (var period in parsed.Where(period => period.FinancialYear == 2025 || period.FinancialYear == 2026))
        {
            DataTable target = period.FinancialYear == 2026 ? source : priorSource;
            foreach (var row in period.Rows)
                target.Rows.Add((long)(period.FinancialYear * 1000 + row.SourceRowNo), DBNull.Value, row.GlAccount, row.GlText,
                    0m, 0m, row.AccumBalance, row.SourceCompanyCode, row.ReportingStream, row.SourceDebit, row.SourceCredit);
        }
        Check(source.Rows.Count == 778 && source.AsEnumerable().Sum(r => (decimal)r["AccumBalance"]) == 0m, "Source fixture changed.");
        Check(priorSource.Rows.Count == 427 && priorSource.AsEnumerable().Sum(r => (decimal)r["AccumBalance"]) == 0m,
            "FY2025 source fixture changed.");
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
        CheckMappingUpload(source, priorSource, uploadCatalogue, disclosures, consolidatedWorkbook, output, path);
        CheckUnmappedActivity();
    }

    private static void CheckMappingUpload(DataTable source, DataTable priorSource, DataTable catalogue,
        DataTable disclosures, string consolidatedWorkbook, string output, string sourceFolder)
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
        bool loseCurrentRows = false;
        DataTable approvalSource = source;
        var revised = fullWorkbook ? ReadRevisedCashFlowSources(sourceFolder, source) : null;
        if (!fullWorkbook)
        {
            priorSource = priorSource.Clone();
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
                return (int)call["@import"] == 24 ? priorSource : approvalSource;
            if (call.Sql.StartsWith("SELECT i.ImportId,c.ConfigurationReleaseId"))
            {
                int import = (int)call["@import"];
                bool prior = import == 24;
                Check(call.Sql.Contains("c.StatusCode='Draft'") && call.Sql.Contains("i.ConfigurationReleaseId IS NULL") &&
                    (int)call["@release"] == 19 && (prior || import == NORMImportService.CurrentAnnualImport) &&
                    call.Sql.Contains("c.FinancialYear=i.FinancialYear+1") == prior,
                    "Draft review did not select the retained annual source and FY2026 configuration.");
                return Table("ImportId ConfigurationReleaseId FinancialYear EntityCode SourceType DataFingerprint VersionCode EntityName DraftRevision",
                    new object[] { import, 19, prior ? 2025 : 2026, "NIHRACS", "ERP", "fixture-" + import, "fixture", "NIHRACS", 255L });
            }
            if (call.Sql.Contains("AS DraftRevision FROM dbo.tblNORM_ConfigurationRelease"))
            {
                Check(call.InTransaction && call.Sql.Contains("UPDLOCK,HOLDLOCK") && call.Sql.Contains("c.StatusCode='Draft'") &&
                    (int)call["@release"] == 19, "Draft review did not lock its current configuration.");
                return Table("DraftRevision", new object[] { lockedRevision });
            }
            if (call.Sql.StartsWith("SELECT TbRowId,SourceLedger"))
                return (int)call["@import"] == 24 ? priorSource : loseCurrentRows ? source.Clone() :
                    (int)call["@import"] == 99 ? revised[2026] : source;
            if (call.Sql.StartsWith("SELECT TbRowId,GlAccount,AccumBalance"))
            {
                Check(call.InTransaction, "Cash flow read source rows outside the report transaction.");
                switch ((int)call["@import"])
                {
                    case 23: return revised[2024];
                    case 24: return priorSource;
                    case 25: return source;
                    case 99: return revised[2026];
                    default: throw new Exception("Unexpected cash-flow source import.");
                }
            }
            if (call.Sql.StartsWith("SELECT StatementLineId,LineCode,LineType,ParentLineCode"))
                return TableFrom(templates, row => (string)row["StatementCode"] == "SCFREF" && !(bool)row["IsDeactivated"]);
            if (call.Sql.StartsWith("SELECT r.CalculationRunId,r.ImportId,r.InputFingerprint,c.StatusCode"))
                return Table("CalculationRunId ImportId InputFingerprint StatusCode", NORMHelper.Committed
                    .Where(saved => saved.Sql.StartsWith("INSERT dbo.tblNORM_CalculationRun") &&
                        (saved.InsertedId == (int)call["@run"] || saved.InsertedId == (int)call["@prior"]))
                    .Select(saved => new object[] { saved.InsertedId, saved["@import"], saved["@fingerprint"], "Draft" }).ToArray());
            if (call.Sql.StartsWith("SELECT CalculationRunId,CheckCode,ResultCode,ActualValue"))
                return Table(EvidenceColumns, NORMHelper.Committed
                    .Where(saved => saved.Sql.StartsWith("INSERT dbo.tblNORM_ValidationResult"))
                    .Select(saved => new object[] { saved["@run"], saved["@code"], saved["@result"], saved["@actual"],
                        saved["@expected"], saved["@difference"], saved["@detail"] }).ToArray());
            if (call.Sql.StartsWith("SELECT CalculationRunId,StatementCode,LineCode,ReportedAmount"))
                return Table("CalculationRunId StatementCode LineCode ReportedAmount", InsertedResults
                    .Select(saved => new object[] { saved["@run"], saved["@statement"], saved["@line"], Reported(saved) }).ToArray());
            if (call.Sql.StartsWith("SELECT r.CalculationRunId,r.LineResultId,r.LineCode"))
                return CashFlowPresentationResults((int)call["@run"], (int)call["@prior"]);
            if (call.Sql.StartsWith("SELECT DisclosureCode,")) return disclosures;
            if (call.Sql.StartsWith("SELECT r.LineCode,r.ComputedAmount,r.ReportedAmount,t.ParentLineCode") ||
                call.Sql.StartsWith("SELECT LineCode,ReportedAmount FROM dbo.tblNORM_LineResult"))
                return RowsWithResults(false, (int)call["@run"]);
            if (call.Sql.StartsWith("SELECT r.LineCode,r.ReportedAmount") ||
                call.Sql.StartsWith("SELECT LineCode,LineResultId,ComputedAmount,ReportedAmount"))
                return TableFrom(RowsWithResults(true, (int)call["@run"]), row => (string)row["StatementCode"] == (string)call["@statement"]);
            if (call.Sql.Contains("AS IsHierarchy") && call.Sql.Contains("LEFT JOIN dbo.tblNORM_LineResult"))
            {
                DataTable face = TableFrom(RowsWithResults(true, (int)call["@run"]), row => (string)row["StatementCode"] ==
                    (string)call[call.Values.ContainsKey("@code") ? "@code" : "@statement"]);
                face.Columns.Add("IsHierarchy", typeof(int));
                foreach (DataRow row in face.Rows) row["IsHierarchy"] = 1;
                return face;
            }
            return Query(call);
        };
        var draftScalar = NORMHelper.ScalarCallResponse;
        NORMHelper.ScalarCallResponse = call => call.Sql.StartsWith("SELECT COUNT(*) FROM dbo.tblNORM_StatementLine")
            ? (object)60 : call.Sql.StartsWith("SELECT TOP 1 i.EntityCode") ? "NIHRACS" : draftScalar(call);
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
        Check(applied.Validation.UnmappedCountsByYear[2026] == (fullWorkbook ? 10 : 2) &&
            (fullWorkbook || applied.Validation.UnmappedCount == 2) &&
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
        if (!fullWorkbook)
        {
            priorSource.Rows.Add(2025001L, DBNull.Value, "PRIOR_ONLY", "Comparative-only GL", 0m, 0m, 500m,
                "NIHRA", "Departmental", 500m, 0m);
            var priorValidation = NORMMappingManagement.Validate(19);
            Check(!priorValidation.CanApprove && priorValidation.Errors.Any(error =>
                error.StartsWith("FY2025:") && error.Contains("PRIOR_ONLY")) &&
                !priorValidation.Errors.Any(error => error.StartsWith("FY2026:")) &&
                priorValidation.UnmappedCount == 3 && priorValidation.UnmappedCountsByYear[2026] == 2 &&
                priorValidation.UnmappedCountsByYear[2025] == 1,
                "An unresolved comparative-only GL should block approval for its own year.");

        }
        Console.WriteLine("PASS duplicate rejection; " + applied.ChangedCount + " rows accepted, " + outsideSource.Count +
            " outside Import 25; incomplete mappings reported and approval checked against source impact");
        if (fullWorkbook)
        {
            CheckDraftReview(output, source, priorSource);
            CheckCashFlowReview(output, revised, priorSource);
            NORMHelper.Committed.Clear();
            loseCurrentRows = true;
            Throws(() => NORMMappingManagement.CalculateDraftReview(19, "fixture"), "FY2026 has no retained reporting rows");
            Check(NORMHelper.Committed.Count(call => call.Sql.StartsWith("INSERT dbo.tblNORM_CalculationRun")) == 1 &&
                !NORMHelper.Committed.Any(call => call.Sql.StartsWith("INSERT dbo.tblNORM_Lineage") ||
                    call.Sql.StartsWith("UPDATE dbo.tblNORM_CalculationRun SET StatusCode = 'Complete'")),
                "An incomplete pair committed comparative rows after the reporting period failed.");
            loseCurrentRows = false;
            lockedRevision = 256L;
            Throws(() => NORMMappingManagement.CalculateDraftReview(19, "fixture"), "The mapping draft changed");
            accountMap.Clear();
            Check(NORMMappingManagement.ApplyWorkbook(19, File.ReadAllBytes(consolidatedWorkbook), "older-layout.xlsx", "fixture").ChangedCount == 433,
                "The previous seven-column hierarchy format no longer uploads.");
        }
    }

    private static void CheckComparativeMappingSummary()
    {
        Prepare();
        ConfigureDraft();
        draft.Rows[0]["ConfigurationReleaseId"] = 19;
        draft.Rows[0]["EntityCode"] = "NIHRACS";
        templates.Clear();
        templates.Rows.Add(1, "SOCI", "Total expenses", 1, "Formula", DBNull.Value, "total", "Total expenses", DBNull.Value, "D", true, DBNull.Value, false);
        templates.Rows.Add(2, "SOCI", "FIXTURE_MAPPED", 2, "Mapped", DBNull.Value, "hierarchy-detail", "Fixture detail", DBNull.Value, "D", true, "Total expenses", false);
        accountMap.Clear();
        accountMap.Rows.Add(1, "MAPPED", "Expense", "FIXTURE_MAPPED");
        var shared = new[] { "SHARED1", "SHARED2", "SHARED3", "SHARED4" };
        DataTable current = UnmappedFixtureRows(shared.Concat(new[] { "CURRENT1", "CURRENT2", "SHARED1", "MAPPED" }));
        DataTable prior = UnmappedFixtureRows(shared.Select(gl => gl.ToLowerInvariant())
            .Concat(Enumerable.Range(1, 6).Select(index => "PRIOR" + index)).Concat(new[] { "MAPPED" }));
        NORMHelper.QueryCallResponse = call => call.Sql.StartsWith("SELECT GlAccount,SourceCompanyCode")
            ? ((int)call["@import"] == 24 ? prior : current) : Query(call);
        var draftScalar = NORMHelper.ScalarCallResponse;
        NORMHelper.ScalarCallResponse = call => call.Sql.StartsWith("SELECT COUNT(*) FROM dbo.tblNORM_StatementLine")
            ? (object)2 : draftScalar(call);
        var combined = NORMMappingManagement.Validate(19);
        Check(combined.UnmappedCount == 12 && combined.UnmappedCountsByYear[2026] == 6 &&
            combined.UnmappedCountsByYear[2025] == 10, "Count distinct GLs in either year and count a shared GL once.");
        Check(!combined.CanApprove && combined.ErrorCount == 2 && combined.Errors.Count == 2 &&
            combined.Errors.Any(error => error.StartsWith("FY2026: 6 unmapped") && error.Contains("CURRENT1")) &&
            combined.Errors.Any(error => error.StartsWith("FY2025: 10 unmapped") && error.Contains("PRIOR6")),
            "The two blocking messages and existing year-specific approval rules must remain intact.");
        Console.WriteLine("PASS 12 distinct unmapped GLs across FY2026 (6) and FY2025 (10), with four shared GLs and two blocking checks");
    }

    private static DataTable UnmappedFixtureRows(IEnumerable<string> accounts)
    {
        return Table("GlAccount SourceCompanyCode SourceLedger ReportingStream AccumBalance DebitMovement CreditMovement SourceDebit SourceCredit",
            accounts.Select(gl => new object[] { gl, "NIHRA", null, "Departmental", 500m, 500m, 0m, 500m, 0m }).ToArray());
    }

    private static void CheckDraftReview(string output, DataTable source, DataTable priorSource)
    {
        Throws(() => NORMMappingManagement.RecalculateLatest(19, "fixture"), "Approve the mapping release before recalculating");
        NORMHelper.Committed.Clear();
        InsertedResults.Clear();
        string draftHash = (string)draft.Rows[0]["ContentHash"];
        int runId = NORMMappingManagement.CalculateDraftReview(19, "fixture");
        var checks = NORMHelper.Committed.Where(call => call.Sql.StartsWith("INSERT dbo.tblNORM_ValidationResult")).ToList();
        var marker = checks.Single(call => (string)call["@code"] == NORMStatementEngine.DraftReviewCheckCode);
        var unmapped = checks.Single(call => (string)call["@code"] == "UNMAPPED_DISPOSITION" && (int)call["@run"] == runId);
        Check((string)marker["@detail"] == "255" && (string)marker["@severity"] == "Blocking" && (string)marker["@result"] == "Fail" &&
            (string)unmapped["@result"] == "Fail" && Convert.ToInt32(unmapped["@actual"]) == 10 &&
            (string)InsertedResults.Single(result => (int)result["@run"] == runId && (string)result["@line"] == "UNMAPPED")["@status"] ==
                "Unmapped", "Draft marker or unresolved source checks were lost.");
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
        int comparison = NORMStatementEngine.ComparativeRunId(runId);
        Check(comparison > runId && checks.Any(call => (int)call["@run"] == comparison &&
            (string)call["@code"] == NORMStatementEngine.ComparativePeriodCheckCode), "Comparative component missing or unmarked.");
        CheckComparison(output, runId, comparison, source, priorSource);
        var validations = checks.Select(call => new {
            code = call["@code"], label = call["@label"], severity = call["@severity"], result = call["@result"],
            detail = call == marker ? "Calculation uses a draft mapping release." :
                ((int)call["@run"] == comparison ? "FY2025: " : "FY2026: ") + call["@detail"]
        });
        File.WriteAllText(Path.Combine(output, "review-checks.json"), new JavaScriptSerializer().Serialize(validations));
        Console.WriteLine("PASS partial mappings calculated through the draft action, with immutable review marker, ten unresolved GLs and no source or approval writes");
    }

    private static Dictionary<int, DataTable> ReadRevisedCashFlowSources(string folder, DataTable schema)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(folder, "Consolidated TBs (From Statements).xlsx"));
        var sources = new Dictionary<int, DataTable>();
        foreach (var period in NihracsTrialBalanceImporter.TryParse(bytes))
        {
            DataTable table = schema.Clone();
            foreach (var row in period.Rows)
                table.Rows.Add((long)(period.FinancialYear * 1000 + row.SourceRowNo), DBNull.Value, row.GlAccount, row.GlText,
                    0m, 0m, row.AccumBalance, row.SourceCompanyCode, row.ReportingStream, row.SourceDebit, row.SourceCredit);
            sources.Add(period.FinancialYear, table);
        }
        Check(sources[2026].Rows.Count == 771 && sources[2025].Rows.Count == 427 && sources[2024].Rows.Count == 413 &&
            sources.Values.All(table => table.AsEnumerable().Sum(row => (decimal)row["AccumBalance"]) == 0m),
            "The revised NIHRACS sources no longer match their expected controls.");
        return sources;
    }

    private static void CheckCashFlowReview(string output, Dictionary<int, DataTable> sources, DataTable retainedPrior)
    {
        Check(retainedPrior.AsEnumerable().Select(row => (decimal)row["AccumBalance"])
            .SequenceEqual(sources[2025].AsEnumerable().Select(row => (decimal)row["AccumBalance"])),
            "The replacement workbook changed the retained FY2025 cash-flow fixture.");
        NORMImportService.CurrentAnnualImport = 99; // Simulated accepted replacement, distinct from Import 25.
        NORMHelper.Committed.Clear();
        InsertedResults.Clear();
        int run = NORMMappingManagement.CalculateDraftReview(19, "fixture");
        int prior = NORMStatementEngine.ComparativeRunId(run);
        string cashFingerprint = fingerprint;
        var checks = NORMHelper.Committed.Where(call => call.Sql.StartsWith("INSERT dbo.tblNORM_ValidationResult")).ToList();
        var lines = templates.AsEnumerable().Where(row => (string)row["StatementCode"] == "SCFREF").ToList();
        var cashKeys = lines.Where(row => (string)row["LineType"] == "cashflow-detail")
            .Select(row => (string)row["LineCode"]).ToArray();
        var cashMapping = accountMap.AsEnumerable().Where(row => !row.IsNull("CashFlowClass"))
            .ToDictionary(row => (string)row["GlCode"], row => (string)row["CashFlowClass"]);
        var cashAccounts = accountMap.AsEnumerable().Where(row => NORMHelper.Str(row, "StatementLine") == "CSH_COH")
            .Select(row => (string)row["GlCode"]).ToArray();
        var lineage = new Dictionary<long, List<Dictionary<string, object>>>();
        foreach (var period in new[] { Tuple.Create(2026, run, 99, 24), Tuple.Create(2025, prior, 24, 23) })
        {
            int year = period.Item1;
            int id = period.Item2;
            var current = sources[year].AsEnumerable().ToArray();
            var opening = sources[year - 1].AsEnumerable().ToArray();
            var closingBalances = CashBalancesByGl(current);
            var openingBalances = CashBalancesByGl(opening);
            var amounts = cashKeys.ToDictionary(key => key, key => 0m);
            var expectedRows = cashKeys.ToDictionary(key => key, key => new Dictionary<long, decimal>());
            foreach (string gl in closingBalances.Keys.Union(openingBalances.Keys))
            {
                string key;
                if (!cashMapping.TryGetValue(gl, out key) || !amounts.ContainsKey(key)) continue;
                decimal closingAmount, priorAmount;
                closingBalances.TryGetValue(gl, out closingAmount);
                openingBalances.TryGetValue(gl, out priorAmount);
                bool movement = Int32.Parse(gl, CultureInfo.InvariantCulture) >= 60000;
                amounts[key] += ((movement ? priorAmount : 0m) - closingAmount) / 1000m;
                foreach (DataRow row in current.Where(row => (string)row["GlAccount"] == gl))
                    expectedRows[key].Add((long)row["TbRowId"], -(decimal)row["AccumBalance"] / 1000m);
                if (movement)
                    foreach (DataRow row in opening.Where(row => (string)row["GlAccount"] == gl))
                        expectedRows[key].Add((long)row["TbRowId"], (decimal)row["AccumBalance"] / 1000m);
            }
            var results = InsertedResults.Where(call => (int)call["@run"] == id && (string)call["@statement"] == "SCF")
                .ToDictionary(call => (string)call["@line"]);
            var sourceRows = current.Concat(opening).ToDictionary(row => (long)row["TbRowId"]);
            foreach (string key in cashKeys)
            {
                var result = results[key];
                Check((decimal)result["@computed"] == amounts[key] &&
                    Reported(result) == Math.Round(amounts[key], 0, MidpointRounding.AwayFromZero),
                    "Incorrect exact or reported cash-flow classification: FY" + year + " " + key);
                var contributions = NORMHelper.Committed.Where(call => call.Sql.StartsWith("INSERT dbo.tblNORM_Lineage") &&
                    Convert.ToInt32(call["@result"]) == result.InsertedId).ToList();
                Check(contributions.Count == expectedRows[key].Count && contributions.All(call =>
                    expectedRows[key][(long)call["@row"]] == (decimal)call["@presented"] &&
                    (decimal)sourceRows[(long)call["@row"]]["AccumBalance"] == (decimal)call["@source"] &&
                    (string)call["@cash"] == key && (int)call["@release"] == 19 &&
                    (string)call["@derivation"] == "CASH_FLOW"), "Incorrect cash-flow source lineage: FY" + year + " " + key);
                lineage[result.InsertedId] = contributions.Select(call => new Dictionary<string, object> {
                    { "gl", sourceRows[(long)call["@row"]]["GlAccount"] }, { "year", (long)call["@row"] / 1000 },
                    { "importId", (long)call["@row"] / 1000 == year ? period.Item3 : period.Item4 },
                    { "derivation", "CASH_FLOW" }, { "sourceAmount", call["@source"] }, { "amount", call["@presented"] }
                }).ToList();
            }
            foreach (DataRow total in lines.Where(row => (string)row["LineType"] == "cashflow-total"))
            {
                string key = (string)total["LineCode"];
                var childResults = lines.Where(row => NORMHelper.Str(row, "ParentLineCode") == key)
                    .Select(row => results[(string)row["LineCode"]]).ToArray();
                Check(Reported(results[key]) == childResults.Sum(row => Reported(row).Value) &&
                    (decimal)results[key]["@computed"] == childResults.Sum(row => (decimal)row["@computed"]),
                    "Cash-flow parent did not sum its children: " + key);
            }
            decimal closingCash = current.Where(row => cashAccounts.Contains((string)row["GlAccount"]))
                .Sum(row => (decimal)row["AccumBalance"]) / 1000m;
            decimal openingCash = opening.Where(row => cashAccounts.Contains((string)row["GlAccount"]))
                .Sum(row => (decimal)row["AccumBalance"]) / 1000m;
            decimal balanceMovement = closingCash - openingCash;
            decimal difference = amounts.Values.Sum() - balanceMovement;
            Check((decimal)results["CF_CSH_CLOSE"]["@computed"] == closingCash &&
                (decimal)results["CF_CSH_OPEN"]["@computed"] == openingCash &&
                (decimal)results["CF_CSH_CHANGE"]["@computed"] == balanceMovement &&
                (decimal)results["CF_CSH_RECON"]["@computed"] == difference &&
                Reported(results["CF_CSH_RECON"]) == Reported(results["CF_CSH_NET"]) -
                    (Reported(results["CF_CSH_CLOSE"]) - Reported(results["CF_CSH_OPEN"])),
                "Reported or exact reconciliation incorrectly changed the cash figures.");
            Check(Reported(results["CF_CSH_CLOSE"]) == Reported(InsertedResults.Single(call =>
                (int)call["@run"] == id && (string)call["@line"] == "Cash and cash equivalents")),
                "Closing cash differs from the unchanged Statement of Financial Position.");
            Check(checks.Any(call => (int)call["@run"] == id && (string)call["@code"] == "CASH_FLOW_SOURCE" &&
                    Convert.ToInt32(call["@actual"]) == period.Item4) &&
                checks.Any(call => (int)call["@run"] == id && (string)call["@code"] == "CASH_FLOW_CLASSIFICATION" &&
                    ((string)call["@detail"]).Contains("58140 (CF_OPA_USE_SUP1)") && (string)call["@severity"] == "Blocking") &&
                checks.Any(call => (int)call["@run"] == id && (string)call["@code"] == "CASH_FLOW_RECONCILIATION" &&
                    (string)call["@severity"] == "Blocking" && (string)call["@result"] == "Fail"),
                "Cash-flow source identity, unresolved key or reconciliation warning was lost.");
            foreach (string key in new[] { "CF_OPA", "CF_INV", "CF_FIN", "CF_CSH_NET", "CF_CSH_OPEN", "CF_CSH_CLOSE", "CF_CSH_CHANGE", "CF_CSH_RECON" })
                Console.WriteLine("FY" + year + " " + key + " exact $000 " + results[key]["@computed"] + " reported " + Reported(results[key]));
            Console.WriteLine("FY" + year + " GL 58140 excluded signed activity AUD " + -closingBalances["58140"]);
        }
        Check(!NORMHelper.Committed.Any(call => call.Sql.StartsWith("UPDATE dbo.tblNORM_ConfigurationRelease") ||
            call.Sql.StartsWith("INSERT dbo.tblNORM_AccountMap") || call.Sql.StartsWith("INSERT dbo.tblNORM_Import") ||
            call.Sql.StartsWith("UPDATE dbo.tblNORM_Import") || call.Sql.StartsWith("INSERT dbo.tblNORM_TrialBalanceRow")),
            "Cash-flow draft calculation changed its source or mapping configuration.");
        CheckDraftDisclosureEvidence(run, prior);
        var statement = Invoke(new NORM_Statements(), typeof(NORM_Statements), "BuildDraftCashFlowStatement", run, prior, lineage);
        File.WriteAllText(Path.Combine(output, "cash-flow-presentation.json"), new JavaScriptSerializer().Serialize(new {
            meta = new { yearCurrent = 2026, yearPrior = 2025, entity = "NIHRACS", runId = run, draftReview = true },
            statements = new[] { statement },
            validations = checks.Where(call => ((string)call["@code"]).StartsWith("CASH_FLOW_")).Select(call => new {
                code = call["@code"], label = call["@label"], severity = call["@severity"], result = call["@result"],
                detail = ((int)call["@run"] == prior ? "FY2025: " : "FY2026: ") + call["@detail"]
            })
        }));
        NORMImportService.OpeningAnnualImport = 0;
        NORMHelper.Committed.Clear();
        InsertedResults.Clear();
        int withoutOpening = NORMMappingManagement.CalculateDraftReview(19, "fixture");
        int priorWithoutOpening = NORMStatementEngine.ComparativeRunId(withoutOpening);
        Check(fingerprint != cashFingerprint &&
            InsertedResults.Any(call => (int)call["@run"] == withoutOpening && (string)call["@line"] == "CF_CSH_NET") &&
            InsertedResults.Any(call => (int)call["@run"] == priorWithoutOpening && (string)call["@line"] == "CF_CSH_CLOSE") &&
            !InsertedResults.Any(call => (int)call["@run"] == priorWithoutOpening && (string)call["@line"] == "CF_CSH_NET") &&
            NORMHelper.Committed.Any(call => call.Sql.StartsWith("INSERT dbo.tblNORM_ValidationResult") &&
                (int)call["@run"] == priorWithoutOpening && (string)call["@code"] == "CASH_FLOW_SOURCE" &&
                (string)call["@result"] == "Fail"), "Missing FY2024 must not manufacture FY2025 activity or hide FY2026.");
        NORMImportService.OpeningAnnualImport = 23;
        CheckCashFlowBoundary(sources[2026], retainedPrior);
        NORMImportService.CurrentAnnualImport = 25;
        Console.WriteLine("PASS cash-flow GL rules, two-year rounding, row lineage, replacement identity, reconciliation and missing FY2024 warning");
    }

    private static void CheckCashFlowBoundary(DataTable current, DataTable prior)
    {
        var retainedCurrent = current.AsEnumerable().Select(row => row.ItemArray).ToArray();
        var retainedPrior = prior.AsEnumerable().Select(row => row.ItemArray).ToArray();
        var retainedMappings = accountMap.AsEnumerable().Select(row => row.ItemArray).ToArray();
        current.Clear();
        prior.Clear();
        accountMap.Clear();
        try
        {
            accountMap.Rows.Add(1, "59999", DBNull.Value, DBNull.Value, DBNull.Value, "CF_OPA_REC_RAG", "Balance");
            accountMap.Rows.Add(2, "60000", DBNull.Value, DBNull.Value, DBNull.Value, "CF_OPA_USE_SUP", "Movement");
            accountMap.Rows.Add(3, "60001", DBNull.Value, DBNull.Value, DBNull.Value, "CF_INV_REC_PPE", "Preceding year only");
            accountMap.Rows.Add(4, "70000", "Asset", "CSH_COH", DBNull.Value, DBNull.Value, "Cash, blank cash-flow key");
            current.Rows.Add(2026001L, DBNull.Value, "59999", "Balance", 0m, 0m, -1500m, "NIHRA", "Departmental", 0m, 1500m);
            current.Rows.Add(2026002L, DBNull.Value, "60000", "Movement", 0m, 0m, 2500m, "NIHRA", "Departmental", 2500m, 0m);
            current.Rows.Add(2026003L, DBNull.Value, "70000", "Cash", 0m, 0m, -1000m, "NIHRA", "Departmental", 0m, 1000m);
            prior.Rows.Add(2025001L, DBNull.Value, "59999", "Ignored preceding balance", 0m, 0m, -500m, "NIHRA", "Departmental", 0m, 500m);
            prior.Rows.Add(2025002L, DBNull.Value, "60000", "Movement", 0m, 0m, 1000m, "NIHRA", "Departmental", 1000m, 0m);
            prior.Rows.Add(2025003L, DBNull.Value, "60001", "Preceding year only", 0m, 0m, 1500m, "NIHRA", "Departmental", 1500m, 0m);
            prior.Rows.Add(2025004L, DBNull.Value, "70000", "Cash", 0m, 0m, -2000m, "NIHRA", "Departmental", 0m, 2000m);
            InsertedResults.Clear();
            NORMHelper.Committed.Clear();
            int run = NORMMappingManagement.CalculateDraftReview(19, "fixture");
            var results = InsertedResults.Where(call => (int)call["@run"] == run && (string)call["@statement"] == "SCF")
                .ToDictionary(call => (string)call["@line"]);
            Check((decimal)results["CF_OPA_REC_RAG"]["@computed"] == 1.5m && Reported(results["CF_OPA_REC_RAG"]) == 2m &&
                (decimal)results["CF_OPA_USE_SUP"]["@computed"] == -1.5m && Reported(results["CF_OPA_USE_SUP"]) == -2m &&
                (decimal)results["CF_INV_REC_PPE"]["@computed"] == 1.5m && Reported(results["CF_INV_REC_PPE"]) == 2m &&
                Reported(results["CF_CSH_NET"]) == 2m && (decimal)results["CF_CSH_CLOSE"]["@computed"] == -1m &&
                !NORMHelper.Committed.Any(call => call.Sql.StartsWith("INSERT dbo.tblNORM_ValidationResult") &&
                    (int)call["@run"] == run && (string)call["@code"] == "CASH_FLOW_CLASSIFICATION"),
                "The cash-flow threshold, blank keys, signed midpoints or preceding-only GL changed.");
            Console.WriteLine("PASS cash-flow GLs 59999 and 60000, preceding-only GL, blank key and signed midpoint rounding");
        }
        finally
        {
            current.Clear();
            prior.Clear();
            accountMap.Clear();
            foreach (object[] row in retainedCurrent) current.Rows.Add(row);
            foreach (object[] row in retainedPrior) prior.Rows.Add(row);
            foreach (object[] row in retainedMappings) accountMap.Rows.Add(row);
        }
    }

    private static Dictionary<string, decimal> CashBalancesByGl(IEnumerable<DataRow> rows)
    {
        return rows.GroupBy(row => (string)row["GlAccount"])
            .ToDictionary(group => group.Key, group => group.Sum(row => (decimal)row["AccumBalance"]));
    }

    private static DataTable CashFlowPresentationResults(int run, int prior)
    {
        DataTable results = Table("CalculationRunId LineResultId LineCode ComputedAmount ReportedAmount LineLabel LineType SeqNo");
        var references = templates.AsEnumerable().Where(row => (string)row["StatementCode"] == "SCFREF")
            .ToDictionary(row => (string)row["LineCode"]);
        string[] cash = { "CF_CSH_OPEN", "CF_CSH_CLOSE", "CF_CSH_CHANGE", "CF_CSH_RECON" };
        foreach (var call in InsertedResults.Where(call => (string)call["@statement"] == "SCF" &&
            ((int)call["@run"] == run || (int)call["@run"] == prior)))
        {
            string code = (string)call["@line"];
            DataRow reference;
            references.TryGetValue(code, out reference);
            results.Rows.Add(call["@run"], (long)call.InsertedId, code, call["@computed"], Reported(call),
                reference == null ? DBNull.Value : reference["LineLabel"], reference == null ? DBNull.Value : reference["LineType"],
                reference == null ? 122 + Array.IndexOf(cash, code) : reference["SeqNo"]);
        }
        return results.AsEnumerable().OrderBy(row => (int)row["SeqNo"]).CopyToDataTable();
    }

    private static void CheckComparison(string output, int run, int prior, DataTable source, DataTable priorSource)
    {
        var classification = accountMap.AsEnumerable().ToDictionary(row => (string)row["GlCode"],
            row => Convert.ToString(row["StatementLine"]), StringComparer.OrdinalIgnoreCase);
        var employeeKeys = templates.AsEnumerable().Where(row => !(bool)row["IsDeactivated"] &&
            Convert.ToString(row["ParentLineCode"]) == Parent)
            .Select(row => (string)row["LineCode"]).ToArray();
        var periods = new[] { Tuple.Create(run, 25, source), Tuple.Create(prior, 24, priorSource) };
        var reported = new Dictionary<int, decimal>();
        foreach (var period in periods)
        {
            int periodRun = period.Item1;
            var rows = period.Item3.AsEnumerable().ToDictionary(row => (long)row["TbRowId"]);
            var edges = NORMHelper.Committed.Where(call => call.Sql.StartsWith("INSERT dbo.tblNORM_Lineage") &&
                (int)call["@run"] == periodRun && (string)call["@derivation"] != "CASH_FLOW").ToList();
            Check(edges.Select(call => (long)call["@row"]).Distinct().Count() == rows.Count &&
                edges.All(call => (int)call["@release"] == 19 && (decimal)call["@source"] ==
                    (decimal)rows[(long)call["@row"]]["AccumBalance"]), "Lineage used another period or changed a source amount.");
            var values = RowsWithResults(false, periodRun).AsEnumerable().ToDictionary(row => (string)row["LineCode"]);
            decimal detailTotal = 0m, exactTotal = 0m;
            foreach (string key in employeeKeys)
            {
                decimal exact = rows.Values.Where(row => classification.ContainsKey((string)row["GlAccount"]) &&
                    classification[(string)row["GlAccount"]] == key).Sum(row => (decimal)row["AccumBalance"]) / 1000m;
                decimal rounded = Math.Round(exact, 0, MidpointRounding.AwayFromZero);
                Check((decimal)values[key]["ComputedAmount"] == exact && (decimal)values[key]["ReportedAmount"] == rounded,
                    "A detail did not use the period's source and current mapping.");
                exactTotal += exact;
                detailTotal += rounded;
            }
            Check((decimal)values[Parent]["ComputedAmount"] == exactTotal &&
                (decimal)values[Parent]["ReportedAmount"] == detailTotal, "Calculated parent did not sum its own rounded details.");
            foreach (var total in templates.AsEnumerable().Where(row => !(bool)row["IsDeactivated"] &&
                (string)row["CalculationKind"] == "Formula" && row.IsNull("FormulaSpec")))
            {
                string code = (string)total["LineCode"];
                var children = templates.AsEnumerable().Where(row => !(bool)row["IsDeactivated"] &&
                    Convert.ToString(row["ParentLineCode"]) == code);
                decimal sum = children.Sum(row => (decimal)values[(string)row["LineCode"]]["ReportedAmount"]);
                Check((decimal)values[code]["ReportedAmount"] == sum, "Additive parent did not reconcile in import " +
                    period.Item2 + ": " + code);
            }
            var check = NORMHelper.Committed.Single(call => call.Sql.StartsWith("INSERT dbo.tblNORM_ValidationResult") &&
                (int)call["@run"] == periodRun && (string)call["@code"] == "UNMAPPED_DISPOSITION");
            int unmapped = rows.Values.Where(row => !classification.ContainsKey((string)row["GlAccount"]) ||
                !values.ContainsKey(classification[(string)row["GlAccount"]])).GroupBy(row => (string)row["GlAccount"])
                .Count(gl => gl.Sum(row => (decimal)row["AccumBalance"]) != 0m);
            Check((string)check["@result"] == "Fail" && Convert.ToInt32(check["@actual"]) == unmapped && unmapped > 0,
                "Unresolved balances were not calculated independently by year.");
            Check(NORMHelper.Committed.Any(call => call.Sql.StartsWith("INSERT dbo.tblNORM_CalculationRun") &&
                call.InsertedId == periodRun && (int)call["@import"] == period.Item2 && (int)call["@release"] == 19),
                "Run used the wrong import or release.");
            reported[periodRun] = detailTotal;
            Console.WriteLine("PASS import #" + period.Item2 + " preserved " + rows.Count + " source rows with FY2026 mapping. " +
                "Employee benefits exact $000 " + exactTotal + ", reported " + detailTotal + ", unresolved GLs " + unmapped);
            foreach (string code in new[] { "Total expenses", "Net cost of services", "Total assets", "Total liabilities", "Net assets" })
                Console.WriteLine("Import #" + period.Item2 + " " + code + " exact $000 " + values[code]["ComputedAmount"] +
                    " reported " + values[code]["ReportedAmount"]);
        }
        Check(reported[run] != reported[prior], "The comparative fixture must distinguish source years.");
        var notes = NORMReportingFramework.LoadDisclosures(run, 19, new NORMReportingFramework.ReportingProfile());
        var employee = notes.Single(n => n.Code == "N1_1A");
        Check(employee.ReportedAmount == reported[run] && employee.PriorAmount == reported[prior] &&
            employee.Lines.Sum(line => line.Prior.Value) == reported[prior], "Comparative note total differs from its detail or face.");
        var page = new NORM_Statements();
        var empty = new Dictionary<string, decimal>();
        var lineage = new Dictionary<long, List<Dictionary<string, object>>>();
        foreach (var item in InsertedResults)
            lineage.Add(item.InsertedId, new List<Dictionary<string, object>> { new Dictionary<string, object>() });
        object income = Invoke(page, typeof(NORM_Statements), "BuildStatement", 19, run, "SOCI", "Income Statement", lineage, empty, empty, prior);
        object balance = Invoke(page, typeof(NORM_Statements), "BuildStatement", 19, run, "SOFP", "Balance Sheet", lineage, empty, empty, prior);
        File.WriteAllText(Path.Combine(output, "comparative-presentation.json"), new JavaScriptSerializer().Serialize(new {
            meta = new { yearCurrent = 2026, yearPrior = 2025, priorImportId = 24, entity = "NIHRACS", runId = run },
            statements = new[] { income, balance, Invoke(page, typeof(NORM_Statements), "BuildNotesStatement", notes) },
            employeeCurrent = reported[run], employeePrior = reported[prior]
        }));
        var word = new NORM_WordExport();
        var html = new StringBuilder();
        Invoke(word, typeof(NORM_WordExport), "AppendStatement", html, run, 19, 2026, "SOCI", "Income Statement", false);
        Invoke(word, typeof(NORM_WordExport), "AppendNotes", html, run, 2026, notes);
        string priorText = reported[prior].ToString("N0", CultureInfo.GetCultureInfo("en-AU"));
        Check(Regex.Matches(html.ToString(), "<td class=\"amount\">" + Regex.Escape(priorText) + "</td>").Count >= 2,
            "Word renderer did not reuse the comparative parent on face and note.");
        Type excel = typeof(NORM_FinancialStatementsExcel);
        Type ctx = excel.GetNestedType("ExportContext", BindingFlags.NonPublic);
        object model = Activator.CreateInstance(ctx, true);
        foreach (var field in new Dictionary<string, object> {
            { "RunId", run }, { "ReleaseId", 19 }, { "Year", 2026 }, { "Entity", "NIHRACS" },
            { "Budgets", empty }, { "PriorFigures", empty }, { "ManualInputs", new DataTable() }, { "Disclosures", notes }
        })
            ctx.GetField(field.Key).SetValue(model, field.Value);
        using (var package = new ExcelPackage())
        {
            var index = new List<Tuple<string, string, string>>();
            Invoke(null, excel, "AddFace", package, model, "SOCI", "SoCI", "Income Statement", false, index);
            var sheet = package.Workbook.Worksheets["SoCI"];
            int employeeRow = Enumerable.Range(1, sheet.Dimension.End.Row).Single(row => sheet.Cells[row, 1].Text == Parent);
            Check(Convert.ToDecimal(sheet.Cells[employeeRow, 4].Value) == reported[prior], "Excel renderer lost the comparative face amount.");
        }
        Console.WriteLine("PASS both periods' additive totals reconcile and their reported values agree across the note and face renderers");
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
        object income = Invoke(page, web, "BuildStatement", 10, run, "SOCI", "Income Statement", lineage, figures, figures, 0);
        object balance = Invoke(page, web, "BuildStatement", 10, run, "SOFP", "Balance Sheet", lineage, figures, figures, 0);
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
