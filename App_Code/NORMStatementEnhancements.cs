using System;
using System.Collections.Generic;
using System.Data;

/// <summary>
/// Run-level inputs that sit beside the immutable NORM calculation: budget figures,
/// manually prepared note schedules and cash-flow classification journals.
/// </summary>
public static class NORMStatementEnhancements
{
    public static bool IsInstalled()
    {
        object value = NORMHelper.Scalar(
            "SELECT CASE WHEN OBJECT_ID('dbo.tblNORM_BudgetFigure','U') IS NOT NULL " +
            "AND OBJECT_ID('dbo.tblNORM_ManualInput','U') IS NOT NULL " +
            "AND OBJECT_ID('dbo.tblNORM_CashFlowJournal','U') IS NOT NULL THEN 1 ELSE 0 END");
        return value != null && Convert.ToInt32(value) == 1;
    }

    public static void EnsureRunTemplates(int runId, string updatedBy)
    {
        if (!IsInstalled()) { return; }
        string[,] rows = new string[,] {
            { "LEASE_RECEIVABLES", "N1_2E", "Finance lease receivables maturity analysis", "MaturityAnalysis", "" },
            { "CONTINGENT_ASSETS", "N7_1", "Quantifiable contingent assets", "Disclosure", "" },
            { "CONTINGENT_LIABILITIES", "N7_1", "Quantifiable contingent liabilities", "Disclosure", "" },
            { "ASSET_REGISTER_CLOSE", "N3_2A", "Asset register closing carrying amount", "Reconciliation", "Property plant and equipment" },
            { "BUDGET_SOCI_COMMENTARY", "", "Statement of Comprehensive Income budget variance commentary", "Commentary", "" },
            { "BUDGET_SOFP_COMMENTARY", "", "Statement of Financial Position budget variance commentary", "Commentary", "" },
            { "BUDGET_CASH_COMMENTARY", "", "Cash Flow Statement budget variance commentary", "Commentary", "" }
        };
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            NORMHelper.Exec(
                "IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_ManualInput WHERE CalculationRunId=@run AND InputCode=@code) " +
                "INSERT dbo.tblNORM_ManualInput " +
                "(CalculationRunId,InputCode,DisclosureCode,InputLabel,InputTypeCode,ReconcileLineCode,UpdatedBy) " +
                "VALUES (@run,@code,@disclosure,@label,@type,@reconcile,@user)",
                NORMHelper.P("@run", runId), NORMHelper.P("@code", rows[i, 0]),
                NORMHelper.P("@disclosure", String.IsNullOrEmpty(rows[i, 1]) ? (object)null : rows[i, 1]),
                NORMHelper.P("@label", rows[i, 2]), NORMHelper.P("@type", rows[i, 3]),
                NORMHelper.P("@reconcile", String.IsNullOrEmpty(rows[i, 4]) ? (object)null : rows[i, 4]),
                NORMHelper.P("@user", updatedBy));
        }
    }

    public static void EnsureBudgetTemplates(int runId, int releaseId, string updatedBy)
    {
        if (!IsInstalled()) { return; }
        NORMHelper.Exec(
            "INSERT dbo.tblNORM_BudgetFigure (CalculationRunId,StatementCode,LineCode,SourceSystem,StatusCode,UpdatedBy) " +
            "SELECT @run,s.StatementCode,s.LineCode,N'Controlled budget input','Loaded',@user " +
            "FROM dbo.tblNORM_StatementLine s WHERE s.ConfigurationReleaseId=@release " +
            "AND s.StatementCode IN ('SOCI','SOFP') AND s.LineCode IS NOT NULL AND s.IsDeactivated=0 " +
            "AND NOT EXISTS (SELECT 1 FROM dbo.tblNORM_BudgetFigure b WHERE b.CalculationRunId=@run " +
            "AND b.StatementCode=s.StatementCode AND b.LineCode=s.LineCode)",
            NORMHelper.P("@run", runId), NORMHelper.P("@release", releaseId), NORMHelper.P("@user", updatedBy));
    }

    public static DataTable LoadManualInputs(int runId)
    {
        if (!IsInstalled()) { return new DataTable(); }
        return NORMHelper.Query(
            "SELECT ManualInputId,InputCode,DisclosureCode,InputLabel,InputTypeCode,AmountCurrent,AmountPrior," +
            "ReconcileLineCode,EvidenceReference,Commentary,StatusCode,UpdatedBy,UpdatedUtc " +
            "FROM dbo.tblNORM_ManualInput WHERE CalculationRunId=@run AND IsDeactivated=0 " +
            "ORDER BY CASE InputTypeCode WHEN 'Reconciliation' THEN 1 WHEN 'MaturityAnalysis' THEN 2 " +
            "WHEN 'Disclosure' THEN 3 ELSE 4 END,ManualInputId",
            NORMHelper.P("@run", runId));
    }

    public static void SaveManualInput(long id, int runId, decimal? current, decimal? prior,
        string evidence, string commentary, string status, string updatedBy)
    {
        if (!IsInstalled()) { return; }
        status = Allowed(status, new string[] { "NotStarted", "Draft", "Prepared", "Validated" }, "NotStarted");
        NORMHelper.Exec(
            "UPDATE dbo.tblNORM_ManualInput SET AmountCurrent=@current,AmountPrior=@prior," +
            "EvidenceReference=@evidence,Commentary=@commentary,StatusCode=@status,UpdatedBy=@user," +
            "UpdatedUtc=SYSUTCDATETIME() WHERE ManualInputId=@id AND CalculationRunId=@run AND IsDeactivated=0",
            NORMHelper.P("@current", current), NORMHelper.P("@prior", prior),
            NORMHelper.P("@evidence", EmptyToNull(evidence, 500)), NORMHelper.P("@commentary", EmptyToNull(commentary, 2000)),
            NORMHelper.P("@status", status), NORMHelper.P("@user", updatedBy),
            NORMHelper.P("@id", id), NORMHelper.P("@run", runId));
    }

    public static Dictionary<string, decimal> LoadBudgetFigures(int runId)
    {
        Dictionary<string, decimal> values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (!IsInstalled()) { return values; }
        DataTable table = NORMHelper.Query(
            "SELECT StatementCode,LineCode,OriginalBudget FROM dbo.tblNORM_BudgetFigure " +
            "WHERE CalculationRunId=@run AND OriginalBudget IS NOT NULL AND IsDeactivated=0",
            NORMHelper.P("@run", runId));
        for (int i = 0; i < table.Rows.Count; i++)
        {
            values[NORMHelper.Str(table.Rows[i], "StatementCode") + "|" + NORMHelper.Str(table.Rows[i], "LineCode")] =
                NORMHelper.Dec(table.Rows[i], "OriginalBudget");
        }
        return values;
    }

    public static DataTable LoadBudgetRegister(int runId)
    {
        if (!IsInstalled()) { return new DataTable(); }
        return NORMHelper.Query(
            "SELECT BudgetFigureId,StatementCode,LineCode,OriginalBudget,SourceSystem,SourceReference,StatusCode " +
            "FROM dbo.tblNORM_BudgetFigure WHERE CalculationRunId=@run AND IsDeactivated=0 " +
            "ORDER BY CASE StatementCode WHEN 'SOCI' THEN 1 WHEN 'SOFP' THEN 2 WHEN 'SOCE' THEN 3 ELSE 4 END,BudgetFigureId",
            NORMHelper.P("@run", runId));
    }

    public static void SaveBudgetFigure(long id, int runId, decimal? budget, string sourceSystem,
        string sourceReference, string status, string updatedBy)
    {
        if (!IsInstalled()) { return; }
        status = Allowed(status, new string[] { "Loaded", "Prepared", "Validated" }, "Loaded");
        NORMHelper.Exec(
            "UPDATE dbo.tblNORM_BudgetFigure SET OriginalBudget=@budget,SourceSystem=@system," +
            "SourceReference=@reference,StatusCode=@status,UpdatedBy=@user,UpdatedUtc=SYSUTCDATETIME() " +
            "WHERE BudgetFigureId=@id AND CalculationRunId=@run AND IsDeactivated=0",
            NORMHelper.P("@budget", budget), NORMHelper.P("@system", EmptyToNull(sourceSystem, 100)),
            NORMHelper.P("@reference", EmptyToNull(sourceReference, 500)), NORMHelper.P("@status", status),
            NORMHelper.P("@user", updatedBy), NORMHelper.P("@id", id), NORMHelper.P("@run", runId));
    }

    public static DataTable LoadCashFlowJournals(int runId)
    {
        if (!IsInstalled()) { return new DataTable(); }
        return NORMHelper.Query(
            "SELECT CashFlowJournalId,JournalReference,JournalDescription,CashFlowClass,Amount," +
            "EvidenceReference,StatusCode,UpdatedBy,UpdatedUtc FROM dbo.tblNORM_CashFlowJournal " +
            "WHERE CalculationRunId=@run AND IsDeactivated=0 ORDER BY CashFlowJournalId",
            NORMHelper.P("@run", runId));
    }

    public static void SaveCashFlowJournal(long id, int runId, string reference, string description,
        string cashFlowClass, decimal amount, string evidence, string status, string updatedBy)
    {
        if (!IsInstalled()) { return; }
        reference = Limit(reference, 100);
        description = Limit(description, 500);
        cashFlowClass = Limit(cashFlowClass, 200);
        status = Allowed(status, new string[] { "Draft", "Prepared", "Approved", "Posted" }, "Draft");
        if (String.IsNullOrWhiteSpace(reference) || String.IsNullOrWhiteSpace(description) || String.IsNullOrWhiteSpace(cashFlowClass)) { return; }
        if (id > 0)
        {
            NORMHelper.Exec(
                "UPDATE dbo.tblNORM_CashFlowJournal SET JournalReference=@reference,JournalDescription=@description," +
                "CashFlowClass=@class,Amount=@amount,EvidenceReference=@evidence,StatusCode=@status," +
                "UpdatedBy=@user,UpdatedUtc=SYSUTCDATETIME() WHERE CashFlowJournalId=@id " +
                "AND CalculationRunId=@run AND IsDeactivated=0",
                NORMHelper.P("@reference", reference), NORMHelper.P("@description", description),
                NORMHelper.P("@class", cashFlowClass), NORMHelper.P("@amount", amount),
                NORMHelper.P("@evidence", EmptyToNull(evidence, 500)), NORMHelper.P("@status", status),
                NORMHelper.P("@user", updatedBy), NORMHelper.P("@id", id), NORMHelper.P("@run", runId));
        }
        else
        {
            NORMHelper.Exec(
                "INSERT dbo.tblNORM_CashFlowJournal " +
                "(CalculationRunId,JournalReference,JournalDescription,CashFlowClass,Amount,EvidenceReference,StatusCode,UpdatedBy) " +
                "VALUES (@run,@reference,@description,@class,@amount,@evidence,@status,@user)",
                NORMHelper.P("@run", runId), NORMHelper.P("@reference", reference),
                NORMHelper.P("@description", description), NORMHelper.P("@class", cashFlowClass),
                NORMHelper.P("@amount", amount), NORMHelper.P("@evidence", EmptyToNull(evidence, 500)),
                NORMHelper.P("@status", status), NORMHelper.P("@user", updatedBy));
        }
    }

    public static void ApplyManualInputs(int runId, List<NORMReportingFramework.Disclosure> disclosures)
    {
        DataTable table = LoadManualInputs(runId);
        for (int i = 0; i < table.Rows.Count; i++)
        {
            DataRow input = table.Rows[i];
            string inputType = NORMHelper.Str(input, "InputTypeCode");
            if (String.Equals(inputType, "Reconciliation", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(inputType, "Commentary", StringComparison.OrdinalIgnoreCase)) { continue; }
            if (input.IsNull("AmountCurrent") || input.IsNull("DisclosureCode")) { continue; }
            string code = NORMHelper.Str(input, "DisclosureCode");
            for (int d = 0; d < disclosures.Count; d++)
            {
                if (!String.Equals(disclosures[d].Code, code, StringComparison.OrdinalIgnoreCase)) { continue; }
                NORMReportingFramework.NoteLine line = new NORMReportingFramework.NoteLine();
                line.Label = NORMHelper.Str(input, "InputLabel") + " (manual input)";
                line.Amount = NORMHelper.Dec(input, "AmountCurrent");
                line.SourceCount = 0;
                disclosures[d].Lines.Add(line);
                disclosures[d].Amount += line.Amount;
                if (String.Equals(NORMHelper.Str(input, "StatusCode"), "Validated", StringComparison.OrdinalIgnoreCase))
                    disclosures[d].CompletionStatus = "Validated";
                else if (disclosures[d].CompletionStatus == "Needs input")
                    disclosures[d].CompletionStatus = "Draft";
                break;
            }
        }
    }

    private static string Allowed(string value, string[] allowed, string fallback)
    {
        for (int i = 0; i < allowed.Length; i++)
            if (String.Equals(value, allowed[i], StringComparison.OrdinalIgnoreCase)) { return allowed[i]; }
        return fallback;
    }

    private static object EmptyToNull(string value, int maximum)
    {
        value = Limit(value, maximum);
        return String.IsNullOrWhiteSpace(value) ? (object)null : value;
    }

    private static string Limit(string value, int maximum)
    {
        value = (value ?? "").Trim();
        return value.Length <= maximum ? value : value.Substring(0, maximum);
    }
}
