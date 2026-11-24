using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using CPlatform.NORM;
using OfficeOpenXml;

public static partial class NORMHelper
{
    public static Func<Call, object> EmployeeBenefitsTransactionScalar;
    public static bool MappingAdministrator = true;
    public static bool DraftReviewRun;
    public static bool HasAdminAccess() { return MappingAdministrator; }
    public static bool CanAccessCompletedRun(int runId) { return runId == 42 && !DraftReviewRun; }
    public static bool CanAccessStatementRun(int runId) { return runId == 42; }
    public static int LatestAccessibleCompletedRun() { return 42; }
    public static string SourceCompany(DataRow row) { return null; }
    public static string SourceLedgerLabel(DataRow row) { return null; }
    public static string SapGlLineItemsLink(string account, string company, int year) { return null; }
}

public static class NORMStartOfYearSetup
{
    // This pilot supplies current-year data only.
    public static int RunFinancialYear(int runId) { return 2026; }
    public static Dictionary<string, decimal> LoadPriorActualFigures(string entity, int? year = null) { return Empty(); }
    public static Dictionary<string, decimal> LoadPriorNoteFigures(string entity, int? year = null) { return Empty(); }
    public static Dictionary<string, decimal> LoadPriorAssetMovementFigures(string entity, int? year = null) { return Empty(); }
    public static Dictionary<string, decimal> LoadOriginalBudgetFigures(string entity, int? year = null) { return Empty(); }
    public static decimal? FigureValue(Dictionary<string, decimal> values, string statement, string line, decimal? fallback)
    {
        decimal value;
        return values.TryGetValue(statement + "|" + line, out value) ? (decimal?)value : fallback;
    }
    private static Dictionary<string, decimal> Empty() { return new Dictionary<string, decimal>(); }
}

namespace CPlatform.NORM
{
    public class NORMBasePage : System.Web.UI.Page
    {
        protected virtual bool RequiresPrepareAccess { get { return true; } }
    }
}

public static class NORMImportService
{
    internal static int FindCurrentAnnualDepartmentalImport(string entity, int year)
    {
        if (entity == "NIHRACS" && year == 2026) return 25;
        throw new InvalidOperationException("Unexpected annual fixture scope.");
    }

    internal static int FindAnnualDepartmentalImport(string entity, int reportingYear, int sourceYear)
    {
        if (entity == "NIHRACS" && reportingYear == 2026 && sourceYear == 2025) return 24;
        throw new InvalidOperationException("Unexpected comparative fixture scope.");
    }

    public static NORMImportOutcome CreateTestBreak(int importId, decimal delta, string user)
    { throw new InvalidOperationException("Source edits are outside the pilot."); }
}

internal static partial class PilotTests
{
    private const string Parent = "Employee benefits";
    private const string Detail = "employee-detail";
    private static readonly string[] GLs = { "200000", "200001", "200101", "200102", "200200" };
    private static readonly string[] Destinations = { "EMP_WAGES", "EMP_WAGES", "EMP_DC", "EMP_DB", "EMP_LEAVE" };
    private static readonly string[] DetailCodes = { "EMP_WAGES", "EMP_DC", "EMP_DB", "EMP_LEAVE" };
    private static readonly string[] Labels = { "Wages and salaries", "Defined contribution plans", "Defined benefit plans", "Leave and other entitlements" };
    private static readonly List<NORMHelper.Call> InsertedResults = new List<NORMHelper.Call>();
    private static DataTable templates;
    private static decimal lastSourceAmount;
    private static bool legacy;
    private static string fingerprint;
    private static DataTable draft;
    private static DataTable accountMap;
    private static int mappingChanges;

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static object Value(object value) { return value ?? DBNull.Value; }

    private static DataTable Table(string columns, params object[][] rows)
    {
        DataTable table = new DataTable();
        foreach (string column in columns.Split(' ')) table.Columns.Add(column, typeof(object));
        foreach (object[] row in rows) table.Rows.Add(row.Select(Value).ToArray());
        return table;
    }

    private static DataTable Templates(bool isLegacy, string parentFormula, string expensesFormula, string wagesLabel)
    {
        DataTable table = Table("StatementLineId StatementCode LineCode SeqNo CalculationKind FormulaSpec LineType LineLabel NoteRef NaturalSign IsClickable ParentLineCode IsDeactivated");
        if (!isLegacy)
            for (int i = 0; i < DetailCodes.Length; i++)
                table.Rows.Add(100 + i, "SOCI", DetailCodes[i], 20 + i, "Mapped", DBNull.Value, Detail,
                    i == 0 ? wagesLabel : Labels[i], DBNull.Value, "D", true, Parent, false);
        table.Rows.Add(110, "SOCI", Parent, 30, isLegacy ? "Mapped" : "Formula",
            Value(parentFormula), "line", Parent, "1.1A", "D", true, DBNull.Value, false);
        table.Rows.Add(111, "SOCI", "Total expenses", 40, "Formula", expensesFormula, "total", "Total expenses", DBNull.Value, "D", true, DBNull.Value, false);
        table.Rows.Add(112, "SOFP", "Statement of Changes in Equity", 50, "Mapped", DBNull.Value, "total", "Total equity", DBNull.Value, "C", true, DBNull.Value, false);
        table.Rows.Add(113, "SOCI", "Revenue", 10, "Mapped", DBNull.Value, "line", "Revenue", DBNull.Value, "C", true, DBNull.Value, false);
        table.Rows.Add(114, "SOCI", "Net cost of services", 41, "Formula", "+Total expenses|-Revenue", "total", "Net cost of services", DBNull.Value, "D", true, DBNull.Value, false);
        return table;
    }

    private static decimal? Reported(NORMHelper.Call result)
    {
        NORMHelper.Call update = NORMHelper.Committed.SingleOrDefault(call =>
            call.Sql.StartsWith("UPDATE dbo.tblNORM_LineResult SET ReportedAmount") && Convert.ToInt64(call["@id"]) == result.InsertedId);
        return update == null ? (decimal?)null : (decimal)update["@amount"];
    }

    private static NORMHelper.Call Result(string code)
    { return InsertedResults.Single(result => (string)result["@line"] == code); }

    private static DataTable RowsWithResults(bool faceOnly, int? runId = null)
    {
        DataTable table = templates.Clone();
        foreach (string column in new[] { "LineResultId", "ComputedAmount", "PublishedAmount", "Variance", "StatusCode", "ReportedAmount", "SourceCount" })
            table.Columns.Add(column, typeof(object));
        foreach (DataRow row in templates.Rows)
        {
            if ((bool)row["IsDeactivated"] || (string)row["StatementCode"] == "SCFREF") continue;
            string code = (string)row["LineCode"];
            if (faceOnly && ((string)row["LineType"] == Detail || (string)row["LineType"] == "hierarchy-detail" ||
                (draft == null && (string)row["StatementCode"] != "SOCI" && !(bool)(row.Table.ExtendedProperties["NIHRACS"] ?? false)))) continue;
            NORMHelper.Call result = runId.HasValue ? InsertedResults.Single(r =>
                (string)r["@line"] == code && Convert.ToInt32(r["@run"]) == runId.Value) : Result(code);
            DataRow target = table.NewRow();
            target.ItemArray = row.ItemArray;
            target["LineResultId"] = result.InsertedId;
            target["ComputedAmount"] = result["@computed"];
            target["StatusCode"] = "Mapped";
            target["ReportedAmount"] = Value(Reported(result));
            target["SourceCount"] = NORMHelper.Committed.Count(call => call.Sql.StartsWith("INSERT dbo.tblNORM_Lineage") &&
                Convert.ToInt64(call["@result"]) == result.InsertedId);
            table.Rows.Add(target);
        }
        return table;
    }

    private static DataTable Query(NORMHelper.Call call)
    {
        string sql = call.Sql;
        if (sql.StartsWith("SELECT i.ImportId,i.ConfigurationReleaseId"))
            return Table("ImportId ConfigurationReleaseId FinancialYear EntityCode SourceType DataFingerprint VersionCode EntityName",
                new object[] { 1, 2, 2026, "PILOT", "ERP", "test", "pilot", "Synthetic pilot" });
        if (sql.StartsWith("SELECT 'M' AS Kind"))
            return Table("Kind A B C D E", new object[] { "S", "SOCI", Parent, templates.Rows[0]["LineLabel"], "", "" });
        if (draft != null)
        {
            if (sql.StartsWith("SELECT * FROM dbo.tblNORM_ConfigurationRelease") ||
                sql.StartsWith("SELECT EntityCode,FinancialYear FROM dbo.tblNORM_ConfigurationRelease") ||
                sql.StartsWith("SELECT ContentHash FROM dbo.tblNORM_ConfigurationRelease"))
                return !sql.Contains("StatusCode='Draft'") || (string)draft.Rows[0]["StatusCode"] == "Draft" ? draft : draft.Clone();
            if (sql.StartsWith("SELECT MAX(GlText") || sql.StartsWith("SELECT GlAccount,MAX(GlText") ||
                sql.StartsWith("SELECT DISTINCT StatementLine,NoteSubLine") || sql.StartsWith("SELECT DISTINCT CashFlowClass"))
                return new DataTable();
            if (sql.Contains("FROM dbo.tblNORM_AccountMap m") && sql.Contains("UNION SELECT 'Expense'"))
            {
                DataTable types = Table("AccountType LineCode");
                foreach (DataRow line in templates.Rows)
                    if ((string)line["CalculationKind"] == "Mapped" && !(bool)line["IsDeactivated"])
                        types.Rows.Add((string)line["LineType"] == Detail ? "Expense" : "Equity", line["LineCode"]);
                return types;
            }
            if (sql.Contains("FROM dbo.tblNORM_AccountMap") &&
                (sql.StartsWith("SELECT *") || sql.StartsWith("SELECT GlCode") || sql.StartsWith("SELECT DISTINCT AccountType")))
                return accountMap;
            if (sql.StartsWith("SELECT CalculationKind,FormulaSpec,ParentLineCode"))
                return TableFrom(templates, row => (string)row["LineCode"] == Parent);
            if (sql.StartsWith("SELECT s.LineCode,s.LineLabel,p.LineLabel AS ParentLabel"))
            {
                DataTable options = Table("LineCode LineLabel ParentLabel MappingDescription");
                foreach (DataRow line in templates.AsEnumerable().Where(row => (string)row["LineType"] == "hierarchy-detail" &&
                    (string)row["CalculationKind"] == "Mapped" && !(bool)row["IsDeactivated"]))
                {
                    DataRow parent = templates.AsEnumerable().Single(row => (string)row["LineCode"] == (string)line["ParentLineCode"]);
                    options.Rows.Add(line["LineCode"], line["LineLabel"], parent["LineLabel"], line["MappingDescription"]);
                }
                return options;
            }
            if (sql.StartsWith("SELECT LineCode,LineLabel,") && sql.Contains("StatementCode='SCFREF'"))
                return TableFrom(templates, row => (string)row["StatementCode"] == "SCFREF" &&
                    (string)row["LineType"] == "cashflow-detail" && !(bool)row["IsDeactivated"]);
            if (sql.StartsWith("SELECT StatementLineId,MappingDescription") || sql.StartsWith("SELECT StatementLineId,LineLabel FROM "))
                return TableFrom(templates, row => (string)row["LineCode"] == (string)call["@code"] &&
                    ((string)row["LineType"] == (string)call["@type"] || (sql.Contains("StatementCode='SCFREF'") &&
                        (string)row["StatementCode"] == "SCFREF" && (string)row["LineType"] == "cashflow-detail")) && !(bool)row["IsDeactivated"]);
            if (sql.StartsWith("SELECT LineCode,MappingDescription FROM"))
                return TableFrom(templates, row => !row.IsNull("MappingDescription"));
            if (sql.StartsWith("SELECT s.LineLabel,s.MappingDescription,"))
            {
                DataTable wording = Table("LineLabel MappingDescription ParentLabel");
                DataRow line = templates.AsEnumerable().Single(row => (int)row["StatementLineId"] == (int)call["@line"]);
                if (line.IsNull("MappingDescription"))
                {
                    string parent = (string)line["ParentLineCode"];
                    wording.Rows.Add(line["LineLabel"], DBNull.Value, sql.Contains("p.LineLabel AS ParentLabel")
                        ? templates.AsEnumerable().Single(row => (string)row["LineCode"] == parent)["LineLabel"] : DBNull.Value);
                }
                return wording;
            }
            if (sql.StartsWith("SELECT StatementLineId,LineCode,LineLabel"))
                return TableFrom(templates, row => (string)row["LineCode"] == (string)call["@code"]);
            if (sql.StartsWith("SELECT LineCode,LineLabel,IsDeactivated"))
                return TableFrom(templates, row => (string)row["LineType"] == Detail);
            if (sql.StartsWith("SELECT LineCode,LineLabel,ParentLineCode"))
                return TableFrom(templates, row => (string)row["LineType"] == Detail || (string)row["LineCode"] == Parent);
            if (sql.StartsWith("SELECT LineCode") && sql.Contains("FROM dbo.tblNORM_StatementLine"))
                return TableFrom(templates, row => (string)row["CalculationKind"] == "Mapped" && !(bool)row["IsDeactivated"] &&
                    (!sql.Contains("LineType='employee-detail'") || (string)row["LineType"] == Detail));
            if (sql.StartsWith("SELECT AccountMapId,GlCode")) return accountMap;
        }
        if (sql.StartsWith("SELECT AccountMapId,GlCode"))
        {
            DataTable mappings = Table("AccountMapId GlCode AccountType StatementLine NoteSubLine CashFlowClass GlDescription");
            for (int i = 0; i < GLs.Length; i++)
                mappings.Rows.Add(i + 1, GLs[i], "Expense", legacy ? Parent : Destinations[i], DBNull.Value, DBNull.Value);
            mappings.Rows.Add(6, "300000", "Equity", "Statement of Changes in Equity", DBNull.Value, DBNull.Value);
            return mappings;
        }
        if (sql.Contains("FROM dbo.tblNORM_StatementLine") && call.InTransaction)
            return TableFrom(templates, row => !(bool)row["IsDeactivated"] &&
                (!sql.Contains("StatementCode<>'SCFREF'") || (string)row["StatementCode"] != "SCFREF") &&
                (!sql.Contains("CalculationKind = 'Formula'") || (string)row["CalculationKind"] == "Formula"));
        if (sql.StartsWith("SELECT TbRowId,SourceLedger") || sql.StartsWith("SELECT GlAccount,SourceCompanyCode"))
        {
            DataTable rows = Table("TbRowId SourceLedger GlAccount GlText DebitMovement CreditMovement AccumBalance SourceCompanyCode ReportingStream");
            for (int i = 0; i < GLs.Length; i++)
                rows.Rows.Add((long)i + 1, DBNull.Value, GLs[i], "Employee cost", 0m, 0m, i == 4 ? lastSourceAmount : 500m);
            rows.Rows.Add(6L, DBNull.Value, "300000", "Balancing equity credit", 0m, 0m, -2000m - lastSourceAmount);
            return rows;
        }
        if (sql.Contains("FROM dbo.tblNORM_PublishedFigure")) return Table("StatementCode LineCode AmountCurrent");
        if (sql.Contains("FROM dbo.tblNORM_ImportFile"))
            return Table("SourceType PeriodStart PeriodEnd IsStatementInput", new object[] { "ERP", 1, 12, true });
        if (sql.Contains("FROM dbo.tblNORM_DisclosureRule"))
            return Table("DisclosureCode SectionCode SectionTitle NoteRef DisclosureTitle TriggerCode IsBaseRequired RequiresNarrative SortOrder GuidanceText",
                new object[] { "N1_1A", "1", "Financial performance", "1.1A", Parent, "ALWAYS", true, false, 10, "" });
        if (sql.Contains("AS SubLine,COUNT(*)") || sql.Contains("TemplateText") ||
            sql.Contains("FROM dbo.tblNORM_ManualInput") || sql.Contains("FROM dbo.tblNORM_SourceFigure"))
            return new DataTable();
        if (sql.StartsWith("SELECT r.LineCode,r.ComputedAmount,r.ReportedAmount"))
            return TableFrom(RowsWithResults(false), row => !row.IsNull("ReportedAmount") &&
                ((string)row["LineType"] == Detail || (string)row["LineCode"] == Parent));
        if (sql.StartsWith("SELECT r.LineCode,r.ReportedAmount"))
            return TableFrom(RowsWithResults(true), row => !row.IsNull("ReportedAmount"));
        if (sql.Contains("FROM dbo.tblNORM_StatementLine t") && sql.Contains("LEFT JOIN dbo.tblNORM_LineResult"))
        {
            Check(sql.Contains("t.LineType<>'employee-detail'"), "The face query must exclude detail-only rows.");
            return RowsWithResults(true);
        }
        throw new Exception("Unexpected fixture query: " + sql);
    }

    private static DataTable TableFrom(DataTable table, Func<DataRow, bool> predicate)
    {
        DataTable subset = table.Clone();
        foreach (DataRow row in table.Rows)
            if (predicate(row)) subset.ImportRow(row);
        return subset;
    }

    private static void Prepare(bool isLegacy = false, string parentFormula = null, string expensesFormula = "+Employee benefits",
        string wagesLabel = "Wages and salaries", decimal leave = 500m)
    {
        NORMHelper.Reset();
        NORMHelper.MappingAdministrator = true;
        NORMHelper.DraftReviewRun = false;
        InsertedResults.Clear();
        draft = null;
        legacy = isLegacy;
        lastSourceAmount = leave;
        templates = Templates(isLegacy, parentFormula, expensesFormula, wagesLabel);
        NORMHelper.QueryCallResponse = Query;
        NORMHelper.EmployeeBenefitsTransactionScalar = call =>
        {
            if (call.Sql.Contains("COL_LENGTH")) return 1;
            if (!call.Sql.StartsWith("INSERT dbo.tblNORM_LineResult")) return null;
            call.InsertedId = InsertedResults.Count + 1;
            InsertedResults.Add(call);
            return call.InsertedId;
        };
        NORMHelper.ScalarCallResponse = call =>
        {
            if (call.Sql.Contains("COL_LENGTH") || call.Sql.Contains("OBJECT_ID('dbo.tblNORM_ReportingProfile'")) return 1;
            if (call.Sql.Contains("OBJECT_ID(")) return 0;
            if (call.Sql.StartsWith("SELECT TOP 1 i.EntityCode")) return "PILOT";
            if (call.Sql.StartsWith("SELECT TOP 1 CalculationRunId"))
            {
                fingerprint = (string)call["@fingerprint"];
                return null;
            }
            if (call.Sql.StartsWith("SELECT TOP 1 ComputedAmount")) return null;
            if (call.Sql.StartsWith("SELECT p.CalculationRunId FROM dbo.tblNORM_ValidationResult link"))
                return NORMHelper.Committed.Where(item => item.Sql.StartsWith("INSERT dbo.tblNORM_ValidationResult") &&
                    (string)item["@code"] == "COMPARATIVE_RUN" && (int)item["@run"] == (int)call["@run"])
                    .Select(item => item["@actual"]).SingleOrDefault();
            throw new Exception("Unexpected fixture scalar: " + call.Sql);
        };
        NORMHelper.NonTransactionalExec = call =>
            Check(call.Sql.StartsWith("UPDATE dbo.tblNORM_CalculationRun SET StatusCode = 'Failed'"), "Unexpected outside-transaction write.");
    }

    private static void ConfigureDraft()
    {
        if (!templates.Columns.Contains("MappingDescription")) templates.Columns.Add("MappingDescription", typeof(string));
        accountMap = Query(new NORMHelper.Call { Sql = "SELECT AccountMapId,GlCode" });
        draft = Table("ConfigurationReleaseId ParentConfigurationReleaseId EntityCode FinancialYear StatusCode VersionCode ContentHash",
            new object[] { 2, 1, "PILOT", 2026, "Draft", "pilot", "original" });
        mappingChanges = 0;
        Func<NORMHelper.Call, object> engineScalar = NORMHelper.ScalarCallResponse;
        NORMHelper.ScalarCallResponse = call =>
        {
            if (call.Sql.StartsWith("SELECT TOP 1 i.ImportId")) return 1;
            if (call.Sql.StartsWith("SELECT COUNT(DISTINCT GlCode)")) return mappingChanges;
            if (call.Sql.StartsWith("SELECT COUNT(*) FROM dbo.tblNORM_AuditEvent"))
                return NORMHelper.Committed.Count(change => change.Sql.StartsWith("INSERT dbo.tblNORM_AuditEvent") &&
                    change.Values.ContainsKey("@code") &&
                    (string)change["@code"] == NORMMappingManagement.EmployeeHierarchyEvent);
            if (call.Sql.StartsWith("SELECT COUNT(*)")) return 0;
            return engineScalar(call);
        };
        Func<NORMHelper.Call, object> engineTransactionScalar = NORMHelper.EmployeeBenefitsTransactionScalar;
        NORMHelper.EmployeeBenefitsTransactionScalar = call =>
        {
            if (call.Sql.StartsWith("SELECT ISNULL(MAX(SeqNo)")) return templates.AsEnumerable().Max(row => (int)row["SeqNo"]) + 1;
            if (call.Sql.StartsWith("SELECT COUNT(*) FROM dbo.tblNORM_AccountMap"))
                return accountMap.AsEnumerable().Count(row => String.Equals(row["StatementLine"] as string, (string)call["@code"]));
            return engineTransactionScalar(call);
        };
        NORMHelper.OnCommit = changes =>
        {
            foreach (NORMHelper.Call change in changes)
            {
                if (change.Sql.StartsWith("INSERT dbo.tblNORM_StatementLine"))
                    templates.Rows.Add(400, change["@statement"], change["@code"], change["@seq"], "Mapped", DBNull.Value, change["@type"],
                        change["@label"], DBNull.Value, "D", true, change["@parent"], false);
                if (change.Sql.StartsWith("UPDATE dbo.tblNORM_StatementLine SET LineLabel"))
                {
                    DataRow line = templates.AsEnumerable().Single(row => (int)row["StatementLineId"] == (int)change["@line"]);
                    if (change.Values.ContainsKey("@text")) line["LineLabel"] = change["@text"];
                    else { line["LineLabel"] = change["@label"]; line["IsDeactivated"] = change["@inactive"]; }
                }
                if (change.Sql.StartsWith("UPDATE dbo.tblNORM_StatementLine SET MappingDescription"))
                    templates.AsEnumerable().Single(row => (int)row["StatementLineId"] == (int)change["@line"])["MappingDescription"] = change["@text"];
                if (change.Sql.StartsWith("UPDATE dbo.tblNORM_AccountMap"))
                    accountMap.AsEnumerable().Single(row => (string)row["GlCode"] == (string)change["@gl"])["StatementLine"] = change["@line"];
                if (change.Sql.StartsWith("INSERT dbo.tblNORM_MappingChange")) mappingChanges++;
                if (change.Sql.StartsWith("UPDATE dbo.tblNORM_ConfigurationRelease"))
                {
                    draft.Rows[0]["ContentHash"] = change["@hash"];
                    if (change.Sql.Contains("SET StatusCode='Approved'")) draft.Rows[0]["StatusCode"] = "Approved";
                }
            }
        };
    }

    private static byte[] EditMappingWorkbook(string expectedCategoryLabel, string destination)
    {
        using (var package = new ExcelPackage(new MemoryStream(NORMMappingManagement.BuildEditableWorkbook(2))))
        {
            ExcelWorksheet reference = package.Workbook.Worksheets["Reference lists"];
            Check(Enumerable.Range(2, reference.Dimension.End.Row).Any(row =>
                reference.Cells[row, 3].Text == "EMP_OTHER" && reference.Cells[row, 5].Text == expectedCategoryLabel),
                "Active category is missing from the reference list.");
            Check(Enumerable.Range(2, reference.Dimension.End.Row).Any(row => reference.Cells[row, 15].Text == "EMP_OTHER"),
                "Active category is missing from the Expense dropdown.");
            ExcelWorksheet sheet = package.Workbook.Worksheets["Mappings"];
            int mappingRow = Enumerable.Range(5, sheet.Dimension.End.Row - 4).Single(row => sheet.Cells[row, 1].Text == GLs[0]);
            sheet.Cells[mappingRow, 4].Value = destination;
            sheet.Cells[mappingRow, 7].Value = "Employee category pilot fixture";
            return package.GetAsByteArray();
        }
    }

    private static void CheckDraftWorkflow()
    {
        Prepare();
        ConfigureDraft();
        NORMMappingManagement.SaveEmployeeCategory(2, Parent, "EMP_OTHER", "Other benefits", false, "pilot");
        Check(NORMMappingManagement.EmployeeCategories(2).Rows.Count == 5, "New category missing from draft editor.");
        byte[] workbook = EditMappingWorkbook("Other benefits", "EMP_OTHER");
        var applied = NORMMappingManagement.ApplyWorkbook(2, workbook, "pilot.xlsx", "pilot");
        Check(applied.ChangedCount == 1 && applied.Validation.CanApprove, "New category could not be mapped through the workbook.");

        VerifyRecalculation(5m, 5);
        NORMMappingManagement.SaveEmployeeCategory(2, Parent, "EMP_OTHER", "Other staff costs", false, "pilot");
        VerifyRecalculation(5m, 5);
        Check((string)accountMap.Rows[0]["StatementLine"] == "EMP_OTHER" &&
            (string)Result("EMP_OTHER")["@line"] == "EMP_OTHER" && Reported(Result("EMP_OTHER")) == 1m,
            "Renaming changed the stable key or reported balance.");
        Throws(() => NORMMappingManagement.SaveEmployeeCategory(2, "EMP_OTHER", "EMP_WAGES", "Invalid", false, "pilot"), "Select Employee benefits");
        NORMHelper.TransactionEntityAccess = entity => false;
        Throws(() => NORMMappingManagement.SaveEmployeeCategory(2, Parent, "EMP_OTHER", "Unauthorised", false, "pilot"), "Entity access denied");
        NORMHelper.TransactionEntityAccess = null;

        byte[] removeMapping = EditMappingWorkbook("Other staff costs", "EMP_WAGES");
        int draftDetailId = (int)templates.AsEnumerable().Single(row => (string)row["LineCode"] == "EMP_OTHER")["StatementLineId"];
        NORMMappingManagement.SaveEmployeeCategory(2, Parent, "EMP_OTHER", "", true, "pilot");
        Check(!NORMMappingManagement.Validate(2).CanApprove && (string)accountMap.Rows[0]["StatementLine"] == "EMP_OTHER" &&
            (int)NORMHelper.Committed.Last(call => call.Sql.StartsWith("UPDATE dbo.tblNORM_StatementLine"))["@line"] == draftDetailId,
            "Deactivation must update only the draft detail and leave its mapping unresolved.");
        NORMMappingManagement.ApplyWorkbook(2, removeMapping, "pilot.xlsx", "pilot");
        VerifyRecalculation(4m, 4);
        Check(!InsertedResults.Any(row => (string)row["@line"] == "EMP_OTHER"), "Inactive detail was calculated.");
        Check(templates.AsEnumerable().Single(row => (string)row["LineCode"] == Parent).IsNull("FormulaSpec"),
            "The additive subtotal acquired a formula.");
        mappingChanges = 0;
        NORMMappingManagement.Approve(2, false, "pilot");
        Throws(() => NORMMappingManagement.SaveEmployeeCategory(2, Parent, "EMP_OTHER", "Locked", false, "pilot"), "Only a draft");
        Check(NORMHelper.Committed.Count(change => change.Sql.StartsWith("INSERT dbo.tblNORM_AuditEvent") &&
            change.Values.ContainsKey("@code") &&
            (string)change["@code"] == NORMMappingManagement.EmployeeHierarchyEvent) == 3, "The draft changes were not all audited.");
        Console.WriteLine("PASS controlled category add, workbook assignment, rename, remap and deactivate; reported subtotal 4 → 5 → 5 → 4; approval locks category changes");
    }

    private static void VerifyRecalculation(decimal reported, int detailCount)
    {
        NORMHelper.Committed.RemoveAll(call => call.Sql.Contains("tblNORM_LineResult") || call.Sql.Contains("tblNORM_Lineage"));
        InsertedResults.Clear();
        int run = NORMStatementEngine.Run(1, "pilot");
        var note = NORMReportingFramework.LoadDisclosures(run, 2, new NORMReportingFramework.ReportingProfile()).Single();
        Check((decimal)Result(Parent)["@computed"] == 2.5m && Reported(Result(Parent)) == reported &&
            Reported(Result("Total expenses")) == reported && note.ReportedAmount == reported && note.Lines.Count == detailCount,
            "The additive subtotal did not update following the draft change.");
    }

    private static void Throws(Action action, string message)
    {
        try { action(); }
        catch (Exception error) { Check(error.Message.Contains(message), error.ToString()); return; }
        throw new Exception("Expected rejection: " + message);
    }

    private static object Invoke(object target, Type declaringType, string method, params object[] args)
    {
        return declaringType.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Invoke(target, args);
    }

    private static void Reject(string message)
    {
        try { NORMStatementEngine.Run(1, "pilot"); }
        catch (InvalidOperationException exception)
        {
            Check(exception.Message.Contains(message), exception.Message);
            Check(!NORMHelper.Committed.Any(call => call.Sql.StartsWith("INSERT dbo.tblNORM_Lineage")), "Rejected run wrote lineage.");
            return;
        }
        throw new Exception("Expected a rejected calculation.");
    }

    private static void CheckRoundTrip(string output)
    {
        int run = NORMStatementEngine.Run(1, "pilot");
        List<NORMReportingFramework.Disclosure> disclosures = NORMReportingFramework.LoadDisclosures(run, 2,
            new NORMReportingFramework.ReportingProfile());
        NORMReportingFramework.Disclosure note = disclosures.Single();
        Check(note.NoteRef == "1.1A" && note.ReportedAmount == 4m && note.Amount == 2.5m, "Employee benefits note values differ.");
        Check(note.Lines.Count == 4 && note.Lines.All(line => line.ReportedAmount == 1m), "A detail row was redistributed.");
        Check(Reported(Result(Parent)) == 4m && Reported(Result("Total expenses")) == 4m, "Income Statement is not additive.");
        var reconciliation = NORMHelper.Committed.Single(call => call.Sql.StartsWith("INSERT dbo.tblNORM_ValidationResult") &&
            (string)call["@code"] == "EMPLOYEE_PRESENTATION_ROUNDING");
        Check((decimal)reconciliation["@expected"] == 3m && (decimal)reconciliation["@difference"] == 1m,
            "Rounding reconciliation is missing.");
        int parentId = Result(Parent).InsertedId;
        var lineage = NORMHelper.Committed.Where(call => call.Sql.StartsWith("INSERT dbo.tblNORM_Lineage") &&
            Convert.ToInt64(call["@result"]) == parentId).ToList();
        Check(lineage.Count == 5 && lineage.Sum(row => (decimal)row["@source"]) == 2500m &&
            lineage.Select(row => row["@row"]).Distinct().Count() == 5, "The five source amounts were not retained once.");
        Check(DetailCodes.Sum(code => NORMHelper.Committed.Count(call => call.Sql.StartsWith("INSERT dbo.tblNORM_Lineage") &&
            Convert.ToInt64(call["@result"]) == Result(code).InsertedId)) == 5, "Each GL must map to one detail.");
        Check(!NORMHelper.Committed.Any(call => call.Sql.StartsWith("UPDATE dbo.tblNORM_TrialBalanceRow")), "Source rows were edited.");
        Console.WriteLine("PASS five GLs: $2,500 exact; independently rounded 3; four detail rows of 1; note, face and expenses 4; difference +1");
        CheckOutputs(output, run, disclosures, 4m, 1m);
    }

    private static void CheckOutputs(string output, int run, List<NORMReportingFramework.Disclosure> disclosures,
        decimal total, decimal leave)
    {
        Directory.CreateDirectory(output);
        Type web = typeof(NORM_Statements);
        var page = new NORM_Statements();
        object face = Invoke(page, web, "BuildStatement", 2, run, "SOCI", "Income Statement",
            new Dictionary<long, List<Dictionary<string, object>>>(), new Dictionary<string, decimal>(), new Dictionary<string, decimal>(), 0);
        object notes = Invoke(page, web, "BuildNotesStatement", disclosures);
        string payload = new JavaScriptSerializer().Serialize(new {
            meta = new { yearCurrent = 2026, yearPrior = 2025, entity = "Synthetic pilot", runId = run },
            statements = new[] { face, notes }
        });
        File.WriteAllText(Path.Combine(output, "presentation.json"), payload);

        var word = new NORM_WordExport();
        typeof(NORM_WordExport).GetField("priorFigures", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(word, new Dictionary<string, decimal>());
        var html = new StringBuilder();
        Invoke(word, typeof(NORM_WordExport), "AppendStatement", html, run, 2, 2026, "SOCI", "Income Statement", false);
        Invoke(word, typeof(NORM_WordExport), "AppendNotes", html, run, 2026, disclosures);
        string rendered = html.ToString();
        string displayedLeave = leave < 0 ? "(" + Math.Abs(leave) + ")" : leave.ToString();
        Check(rendered.Contains("<th>Employee benefits</th><td>1.1A</td><td class=\"amount\">" + total + "</td>") &&
            rendered.Contains("<th>Total employee benefits</th><td class=\"amount\">" + total + "</td>") &&
            Regex.IsMatch(rendered, "Leave and other entitlements</th><td class=\"amount\">" + Regex.Escape(displayedLeave) + "</td>"),
            "Word note or face lost shared values.");
        File.WriteAllText(Path.Combine(output, "employee-benefits.html"), rendered);
        Console.WriteLine("PASS Word face and Note 1.1A use shared rounded values");

        Type excel = typeof(NORM_FinancialStatementsExcel);
        Type modelType = excel.GetNestedType("ExportContext", BindingFlags.NonPublic);
        object model = Activator.CreateInstance(modelType, true);
        var figures = new Dictionary<string, decimal>();
        foreach (var field in new Dictionary<string, object> {
            { "RunId", run }, { "ReleaseId", 2 }, { "Year", 2026 }, { "Entity", "Synthetic pilot" },
            { "Budgets", figures }, { "PriorFigures", figures }, { "ManualInputs", new DataTable() },
            { "Disclosures", disclosures }
        })
            modelType.GetField(field.Key).SetValue(model, field.Value);
        using (var package = new ExcelPackage())
        {
            var index = new List<Tuple<string, string, string>>();
            Invoke(null, excel, "AddFace", package, model, "SOCI", "SOCI", "Income Statement", false, index);
            Invoke(null, excel, "AddNotes", package, model, index);
            File.WriteAllBytes(Path.Combine(output, "employee-benefits.xlsx"), package.GetAsByteArray());
        }
        using (var reopened = new ExcelPackage(new FileInfo(Path.Combine(output, "employee-benefits.xlsx"))))
        {
            ExcelWorksheet faceSheet = reopened.Workbook.Worksheets["SOCI"];
            ExcelWorksheet noteSheet = reopened.Workbook.Worksheets["1.1A Employee benefits"];
            Check(ExcelValue(faceSheet, Parent, 3) == total && ExcelValue(faceSheet, "Total expenses", 3) == total &&
                ExcelValue(faceSheet, "Net cost of services", 3) == total &&
                ExcelValue(noteSheet, "Total employee benefits", 2) == total &&
                Labels.Take(3).All(label => ExcelValue(noteSheet, label, 2) == 1m) &&
                ExcelValue(noteSheet, Labels[3], 2) == leave, "Excel note or face lost shared values.");
        }
        Console.WriteLine("PASS saved Excel workbook face and Note 1.1A use shared rounded values");
    }

    private static decimal ExcelValue(ExcelWorksheet sheet, string label, int column)
    {
        int row = Enumerable.Range(1, sheet.Dimension.End.Row).Single(number => sheet.Cells[number, 1].Text == label);
        Check(String.IsNullOrEmpty(sheet.Cells[row, column].Formula), "Reported values must not be overwritten by local Excel formulas.");
        return Convert.ToDecimal(sheet.Cells[row, column].Value);
    }

    public static void Main(string[] args)
    {
        if (args.Length > 1)
        {
            CheckNihracs(args[0], args[1], args.Length > 2 ? args[2] : null);
            return;
        }
        Prepare();
        CheckRoundTrip(args[0]);
        string originalFingerprint = fingerprint;
        Prepare(wagesLabel: "Salary costs");
        int renamedRun = NORMStatementEngine.Run(1, "pilot");
        var renamedNote = NORMReportingFramework.LoadDisclosures(renamedRun, 2, new NORMReportingFramework.ReportingProfile()).Single();
        Check(renamedNote.Lines[0].Label == "Salary costs" && renamedNote.Lines[0].ReportedAmount == 1m &&
            (decimal)Result("EMP_WAGES")["@computed"] == 1m && fingerprint != originalFingerprint, "Renaming detail affected its key or amount.");
        Console.WriteLine("PASS renamed note detail retains its mapped key, exact balance and reported value");

        Prepare();
        templates.Rows[0]["ParentLineCode"] = "MISSING_PARENT";
        Reject("Invalid Employee benefits parent relationship");
        Prepare();
        templates.Rows[0]["ParentLineCode"] = "EMP_WAGES";
        Reject("Invalid Employee benefits parent relationship");
        Prepare();
        templates.Rows[4]["ParentLineCode"] = "EMP_WAGES";
        Reject("Invalid Employee benefits parent relationship");
        Prepare(expensesFormula: "+Employee benefits|+EMP_WAGES");
        Reject("through its subtotal");
        Prepare(expensesFormula: "+Employee benefits|+Employee benefits");
        Reject("more than once");
        Console.WriteLine("PASS missing parents, cycles and double counting rejected");

        foreach (decimal source in new[] { 1499m, 1500m, -1499m, -1500m })
        {
            Prepare(leave: source);
            int run = NORMStatementEngine.Run(1, "pilot");
            decimal expected = source == 1499m ? 1m : source == 1500m ? 2m : source == -1499m ? -1m : -2m;
            Check(Reported(Result("EMP_LEAVE")) == expected && Reported(Result(Parent)) == 3m + expected,
                "Incorrect signed midpoint result for " + source);
            if (source == -1500m)
                CheckOutputs(Path.Combine(args[0], "negative"), run,
                    NORMReportingFramework.LoadDisclosures(run, 2, new NORMReportingFramework.ReportingProfile()), 1m, -2m);
        }
        Console.WriteLine("PASS signed rounding at ±1.499 and ±1.500");

        Prepare(leave: 499.99m);
        NORMStatementEngine.Run(1, "pilot");
        Check((decimal)Result("EMP_LEAVE")["@computed"] == 0.49999m && Reported(Result("EMP_LEAVE")) == 0m &&
            Reported(Result(Parent)) == 3m, "Detail rounding lost cent precision before reporting.");
        Console.WriteLine("PASS a $499.99 detail is rounded from cents before the stored calculation amount loses precision");

        Prepare(isLegacy: true);
        NORMStatementEngine.Run(1, "pilot");
        Check((decimal)Result(Parent)["@computed"] == 2.5m && Reported(Result(Parent)) == null,
            "Legacy flat Employee benefits changed.");
        Console.WriteLine("PASS legacy release retains its original flat calculation");
        CheckDraftWorkflow();
    }
}
