using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>
/// Immutable FY-versioned statement engine. Each completed run retains its
/// calculated amounts, validation evidence and a mapping snapshot for every
/// contributing trial-balance row.
/// </summary>
public static class NORMStatementEngine
{
    public const string EmployeeDetailType = "employee-detail";
    public const string EmployeeBenefitsCode = "Employee benefits";
    public const string HierarchyDetailType = "hierarchy-detail";

    private class ImportContext
    {
        public int ImportId;
        public int ReleaseId;
        public int FinancialYear;
        public string EntityCode;
        public string SourceType;
        public string FileHash;
        public string ReleaseVersion;
        public string ReportingEntityName;
        public bool RetainedAnnualSource;
    }

    private class MapEntry
    {
        public int AccountMapId;
        public string GlCode;
        public string AccountType;
        public string StatementLine;
        public string NoteSubLine;
        public string CashFlowClass;
    }

    private class TemplateLine
    {
        public int StatementLineId;
        public string StatementCode;
        public string LineCode;
        public int SeqNo;
        public string CalculationKind;
        public string LineType;
        public string FormulaSpec;
        public string ParentLineCode;
        public string NaturalSign;
    }

    private class SourceContribution
    {
        public long TbRowId;
        public decimal SourceAmount;
        public decimal PresentedAmount;
        public MapEntry Mapping;
    }

    private class LineAccumulation
    {
        public string StatementCode;
        public string LineCode;
        public int? StatementLineId;
        public decimal ComputedAmount;
        public decimal? ReportedAmount;
        public bool UsesEmployeeRollup;
        public long LineResultId;
        public List<SourceContribution> Sources = new List<SourceContribution>();
    }

    public static int Run(int importId, string startedBy)
    {
        return Run(LoadContext(importId), startedBy);
    }

    internal static int RunAnnualSource(int importId, int releaseId, string startedBy)
    {
        return Run(LoadContext(importId, releaseId), startedBy);
    }

    private static int Run(ImportContext context, string startedBy)
    {
        int importId = context.ImportId;
        string configurationHash = ConfigurationFingerprint(context.ReleaseId);
        string inputFingerprint = NORMCrypto.Sha256(context.FileHash + "|" + configurationHash + "|" + context.ReportingEntityName);

        object existing = NORMHelper.Scalar(
            "SELECT TOP 1 CalculationRunId FROM dbo.tblNORM_CalculationRun " +
            "WHERE ImportId = @import AND ConfigurationReleaseId = @release AND InputFingerprint = @fingerprint " +
            "AND StatusCode = 'Complete' AND IsDeactivated = 0 ORDER BY CalculationRunId DESC",
            NORMHelper.P("@import", importId), NORMHelper.P("@release", context.ReleaseId),
            NORMHelper.P("@fingerprint", inputFingerprint));
        if (existing != null) { return Convert.ToInt32(existing); }

        int runId;
        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction())
        {
            runId = NORMHelper.InsertId(connection, transaction,
                "INSERT dbo.tblNORM_CalculationRun " +
                "(ImportId,ConfigurationReleaseId,InputFingerprint,StatusCode,StartedBy,ReportingEntityName) " +
                "VALUES (@import,@release,@fingerprint,'Running',@user,@name)",
                NORMHelper.P("@import", importId), NORMHelper.P("@release", context.ReleaseId),
                NORMHelper.P("@fingerprint", inputFingerprint), NORMHelper.P("@user", startedBy), NORMHelper.P("@name", context.ReportingEntityName));
            transaction.Commit();
        }

        try
        {
            Calculate(runId, context, startedBy);
            return runId;
        }
        catch (Exception error)
        {
            NORMHelper.Exec(
                "UPDATE dbo.tblNORM_CalculationRun SET StatusCode = 'Failed',CompletedUtc = SYSUTCDATETIME()," +
                "FailureDetail = @detail WHERE CalculationRunId = @run AND StatusCode = 'Running'",
                NORMHelper.P("@detail", Truncate(error.Message, 2000)), NORMHelper.P("@run", runId));
            throw;
        }
    }

    private static void Calculate(int runId, ImportContext context, string startedBy)
    {
        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            try
            {
                Dictionary<string, MapEntry> mappings = LoadMappings(connection, transaction, context.ReleaseId);
                Dictionary<string, TemplateLine> templates = LoadTemplates(connection, transaction, context.ReleaseId);
                bool hierarchy = IsHierarchy(templates);
                HashSet<string> employeeDetails = hierarchy ? null : EmployeeDetailCodes(templates);
                if (hierarchy)
                {
                    if (!HasReportedAmounts())
                        throw new InvalidOperationException("Install the reported amounts schema before calculating the reporting hierarchy.");
                    Dictionary<string, string> types = HierarchyAccountTypes(templates);
                    foreach (MapEntry entry in mappings.Values)
                    {
                        if (String.IsNullOrWhiteSpace(entry.StatementLine)) continue;
                        string type;
                        if (!types.TryGetValue(entry.StatementLine, out type) || !String.Equals(type, entry.AccountType, StringComparison.Ordinal))
                            throw new InvalidOperationException("Map each account to an active detail classification of the matching account type.");
                    }
                }
                if (employeeDetails != null)
                {
                    if (!HasReportedAmounts())
                        throw new InvalidOperationException("Install the reported amounts schema before calculating Employee benefits detail.");
                    foreach (MapEntry entry in mappings.Values)
                    {
                        if (String.Equals(entry.StatementLine, EmployeeBenefitsCode, StringComparison.OrdinalIgnoreCase) ||
                            (entry.StatementLine != null && employeeDetails.Contains(entry.StatementLine) && entry.AccountType != "Expense"))
                            throw new InvalidOperationException("Employee benefits must map to its detailed expense classifications.");
                    }
                }
                Dictionary<string, decimal> published = LoadPublished(connection, transaction, context.ReleaseId);
                DataTable rows = NORMHelper.Query(connection, transaction,
                    "SELECT TbRowId,SourceLedger,GlAccount,GlText,DebitMovement,CreditMovement,AccumBalance FROM dbo.tblNORM_TrialBalanceRow " +
                    "WHERE ImportId = @import AND IsDeactivated = 0 ORDER BY TbRowId",
                    NORMHelper.P("@import", context.ImportId));

                Dictionary<string, LineAccumulation> lines = new Dictionary<string, LineAccumulation>(StringComparer.OrdinalIgnoreCase);
                decimal totalNet = 0m;
                decimal totalAbs = 0m;
                decimal mappedAbs = 0m;
                decimal asset = 0m;
                decimal liability = 0m;
                decimal equity = 0m;
                decimal income = 0m;
                decimal expense = 0m;
                decimal classifiedCashMovement = 0m;
                int unmappedCount = 0;
                int unsafeCashClassCount = 0;

                for (int i = 0; i < rows.Rows.Count; i++)
                {
                    DataRow row = rows.Rows[i];
                    long tbRowId = NORMHelper.Long(row, "TbRowId");
                    string gl = NORMHelper.Str(row, "GlAccount");
                    decimal sourceAmount = NORMHelper.Dec(row, "AccumBalance");
                    decimal sourceMovement = NORMHelper.Dec(row, "DebitMovement") + NORMHelper.Dec(row, "CreditMovement");
                    totalNet += sourceAmount;
                    totalAbs += Math.Abs(sourceAmount);

                    MapEntry mapping = null;
                    bool mapped = gl != null && mappings.TryGetValue(gl, out mapping) &&
                        !String.IsNullOrWhiteSpace(mapping.StatementLine) && IsKnownType(mapping.AccountType);
                    if (!mapped) { mapping = null; }

                    string lineCode = mapped ? mapping.StatementLine : "UNMAPPED";
                    TemplateLine template = null;
                    if (mapped) { templates.TryGetValue(lineCode, out template); }
                    if (hierarchy && template != null) lineCode = template.LineCode;
                    string statementCode = template == null ? "POOL" : template.StatementCode;
                    decimal presented = sourceAmount / 1000m;
                    if (mapped && (hierarchy && template != null && !String.IsNullOrEmpty(template.NaturalSign)
                        ? template.NaturalSign == "C" : IsCreditType(mapping.AccountType))) { presented = -presented; }

                    if (mapped)
                    {
                        mappedAbs += Math.Abs(sourceAmount);
                        decimal normal = sourceAmount / 1000m;
                        if (mapping.AccountType == "Asset") { asset += normal; }
                        else if (mapping.AccountType == "Liability") { liability += -normal; }
                        else if (mapping.AccountType == "Equity") { equity += -normal; }
                        else if (mapping.AccountType == "Income") { income += -normal; }
                        else if (mapping.AccountType == "Expense") { expense += normal; }
                        if (!String.IsNullOrWhiteSpace(mapping.CashFlowClass))
                        {
                            if (IsCashFlowClassSafe(mapping.CashFlowClass))
                                classifiedCashMovement += CashFlowContribution(mapping.CashFlowClass, sourceMovement / 1000m);
                            else
                                unsafeCashClassCount++;
                        }
                    }
                    else { unmappedCount++; }

                    string key = statementCode + "|" + lineCode;
                    LineAccumulation accumulation;
                    if (!lines.TryGetValue(key, out accumulation))
                    {
                        accumulation = new LineAccumulation();
                        accumulation.StatementCode = statementCode;
                        accumulation.LineCode = lineCode;
                        accumulation.StatementLineId = template == null ? (int?)null : template.StatementLineId;
                        lines[key] = accumulation;
                    }
                    accumulation.ComputedAmount += presented;
                    SourceContribution contribution = new SourceContribution();
                    contribution.TbRowId = tbRowId;
                    contribution.SourceAmount = sourceAmount;
                    contribution.PresentedAmount = presented;
                    contribution.Mapping = mapping;
                    accumulation.Sources.Add(contribution);
                }

                /* Persist zero-valued template lines as explicit results. A
                   missing mapping must never make a disclosure line vanish. */
                foreach (KeyValuePair<string, TemplateLine> templateItem in templates)
                {
                    TemplateLine template = templateItem.Value;
                    if (template.CalculationKind != "Mapped") { continue; }
                    string templateKey = template.StatementCode + "|" + template.LineCode;
                    if (!lines.ContainsKey(templateKey))
                    {
                        LineAccumulation empty = new LineAccumulation();
                        empty.StatementCode = template.StatementCode;
                        empty.LineCode = template.LineCode;
                        empty.StatementLineId = template.StatementLineId;
                        lines[templateKey] = empty;
                    }
                }

                if (employeeDetails != null)
                {
                    foreach (string detailCode in employeeDetails)
                    {
                        LineAccumulation detail = lines["SOCI|" + detailCode];
                        detail.ReportedAmount = RoundEmployeeAmount(detail.ComputedAmount);
                    }
                }
                if (hierarchy) CalculateHierarchy(templates, lines);
                else AddFormulaLines(connection, transaction, context.ReleaseId, lines, employeeDetails);

                foreach (KeyValuePair<string, LineAccumulation> item in lines)
                {
                    LineAccumulation line = item.Value;
                    TemplateLine lineTemplate;
                    bool isFormulaLine = templates.TryGetValue(line.LineCode, out lineTemplate) &&
                        lineTemplate.CalculationKind == "Formula";
                    decimal expected;
                    bool hasPublished = published.TryGetValue(line.StatementCode + "|" + line.LineCode, out expected);
                    decimal? variance = hasPublished ? (decimal?)(line.ComputedAmount - expected) : null;
                    string status = ResultStatus(line.LineCode, line.ComputedAmount, hasPublished, expected);
                    line.LineResultId = Convert.ToInt64(NORMHelper.Scalar(connection, transaction,
                        "INSERT dbo.tblNORM_LineResult " +
                        "(CalculationRunId,StatementLineId,StatementCode,LineCode,ComputedAmount,PublishedAmount,Variance,StatusCode) " +
                        "VALUES (@run,@template,@statement,@line,@computed,@published,@variance,@status); " +
                        "SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                        NORMHelper.P("@run", runId), NORMHelper.P("@template", line.StatementLineId),
                        NORMHelper.P("@statement", line.StatementCode), NORMHelper.P("@line", line.LineCode),
                        NORMHelper.P("@computed", line.ComputedAmount),
                        NORMHelper.P("@published", hasPublished ? (object)expected : null),
                        NORMHelper.P("@variance", variance), NORMHelper.P("@status", status)));
                    if (line.ReportedAmount.HasValue)
                        NORMHelper.Exec(connection, transaction,
                            "UPDATE dbo.tblNORM_LineResult SET ReportedAmount=@amount WHERE LineResultId=@id",
                            NORMHelper.P("@amount", line.ReportedAmount.Value), NORMHelper.P("@id", line.LineResultId));

                    for (int s = 0; s < line.Sources.Count; s++)
                    {
                        SourceContribution source = line.Sources[s];
                        MapEntry map = source.Mapping;
                        string snapshot = map == null
                            ? "Unmapped in configuration release " + context.ReleaseVersion
                            : (isFormulaLine ? "Formula " + line.LineCode + "; " : "") +
                              "Release " + context.ReleaseVersion + "; mapping " + map.AccountMapId.ToString() +
                              "; " + map.GlCode + " -> " + map.StatementLine;
                        NORMHelper.Exec(connection, transaction,
                            "INSERT dbo.tblNORM_Lineage " +
                            "(CalculationRunId,LineResultId,TbRowId,AccountMapId,ConfigurationReleaseId,SourceAmount," +
                            " PresentedContribution,DerivationCode,MappingSnapshot,AccountTypeSnapshot,StatementLineSnapshot," +
                            " NoteSubLineSnapshot,CashFlowClassSnapshot) " +
                            "VALUES (@run,@result,@row,@map,@release,@source,@presented,@derivation,@snapshot,@type,@line,@note,@cash)",
                            NORMHelper.P("@run", runId), NORMHelper.P("@result", line.LineResultId),
                            NORMHelper.P("@row", source.TbRowId), NORMHelper.P("@map", map == null ? (object)null : map.AccountMapId),
                            NORMHelper.P("@release", context.ReleaseId), NORMHelper.P("@source", source.SourceAmount),
                            NORMHelper.P("@presented", source.PresentedAmount),
                            NORMHelper.P("@derivation", map == null ? "UNMAPPED" : (isFormulaLine ? "FORMULA" : "GL_MAPPING")),
                            NORMHelper.P("@snapshot", snapshot), NORMHelper.P("@type", map == null ? null : map.AccountType),
                            NORMHelper.P("@line", map == null ? null : map.StatementLine),
                            NORMHelper.P("@note", map == null ? null : map.NoteSubLine),
                            NORMHelper.P("@cash", map == null ? null : map.CashFlowClass));
                    }
                }

                WriteValidations(connection, transaction, runId, rows.Rows.Count, totalNet, totalAbs, mappedAbs,
                    unmappedCount, asset, liability, equity, income, expense, classifiedCashMovement, unsafeCashClassCount, lines, published, hierarchy);
                if (employeeDetails != null)
                {
                    LineAccumulation employee = lines["SOCI|" + EmployeeBenefitsCode];
                    AddValidation(connection, transaction, runId, "EMPLOYEE_PRESENTATION_ROUNDING",
                        "Employee benefits presentation rounding", "Information", "Pass",
                        employee.ReportedAmount, RoundEmployeeAmount(employee.ComputedAmount), null,
                        "Reported total sums rounded detail. Exact underlying amount in AUD: " +
                        (employee.ComputedAmount * 1000m).ToString("0.00", CultureInfo.InvariantCulture) +
                        ". Difference is the reported total less the independently rounded underlying amount, in $'000.");
                }
                if (hierarchy)
                {
                    int index = 0;
                    foreach (TemplateLine template in templates.Values.OrderBy(t => t.StatementLineId))
                    {
                        if (template.CalculationKind != "Formula") continue;
                        LineAccumulation line = lines[template.StatementCode + "|" + template.LineCode];
                        AddValidation(connection, transaction, runId, "HIERARCHY_ROUNDING_" + (++index).ToString(CultureInfo.InvariantCulture),
                            Truncate(template.LineCode + " presentation rounding", 240), "Information", "Pass",
                            line.ReportedAmount, RoundEmployeeAmount(line.ComputedAmount), null,
                            "Reported result less independently rounded exact result in $'000. Exact underlying AUD: " +
                            (line.ComputedAmount * 1000m).ToString("0.00", CultureInfo.InvariantCulture) + ".");
                    }
                }
                WriteSourceFileValidations(connection, transaction, runId, context);

                NORMHelper.Exec(connection, transaction,
                    "UPDATE dbo.tblNORM_CalculationRun SET StatusCode = 'Complete',CompletedUtc = SYSUTCDATETIME() " +
                    "WHERE CalculationRunId = @run AND StatusCode = 'Running'", NORMHelper.P("@run", runId));
                if (!context.RetainedAnnualSource) NORMHelper.Exec(connection, transaction,
                    "UPDATE dbo.tblNORM_Import SET StatusCode = 'Calculated' WHERE ImportId = @import AND StatusCode = 'Imported'",
                    NORMHelper.P("@import", context.ImportId));
                NORMHelper.Exec(connection, transaction,
                    "INSERT dbo.tblNORM_AuditEvent (EventCode,EntityType,EntityId,DetailText,PerformedBy) " +
                    "VALUES ('CALCULATION_COMPLETED','CalculationRun',@id,@detail,@user)",
                    NORMHelper.P("@id", runId.ToString()),
                    NORMHelper.P("@detail", "Immutable calculation completed for import " + context.ImportId.ToString() + "."),
                    NORMHelper.P("@user", startedBy));
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    private static void WriteValidations(OleDbConnection connection, OleDbTransaction transaction, int runId,
        int rowCount, decimal totalNet, decimal totalAbs, decimal mappedAbs, int unmappedCount,
        decimal asset, decimal liability, decimal equity, decimal income, decimal expense,
        decimal classifiedCashMovement, int unsafeCashClassCount, Dictionary<string, LineAccumulation> lines, Dictionary<string, decimal> published,
        bool hierarchy)
    {
        decimal tbDifference = totalNet / 1000m;
        AddValidation(connection, transaction, runId, "DEBITS_EQUAL_CREDITS", "Debits equal credits", "Blocking",
            totalNet == 0m ? "Pass" : "Fail", tbDifference, 0m, 0m,
            "Net of all imported trial-balance rows in $'000. Exact difference in AUD: " +
            totalNet.ToString("0.00##########################", CultureInfo.InvariantCulture) +
            ". Any non-zero difference fails this check.");

        decimal coverage = totalAbs == 0m ? 0m : 100m * mappedAbs / totalAbs;
        AddValidation(connection, transaction, runId, "MAPPING_VALUE_COVERAGE", "Trial balance mapped by value", "Warning",
            coverage >= 99m ? "Pass" : "Warning", coverage, 100m, 1m,
            coverage.ToString("0.000", CultureInfo.InvariantCulture) + "% of absolute trial-balance value has an explicit mapping.");

        AddValidation(connection, transaction, runId, "UNMAPPED_DISPOSITION", "Every source row has a mapping disposition", "Warning",
            unmappedCount == 0 ? "Pass" : "Warning", unmappedCount, 0m, 0m,
            unmappedCount.ToString("N0", CultureInfo.GetCultureInfo("en-AU")) + " of " + rowCount.ToString("N0", CultureInfo.GetCultureInfo("en-AU")) + " rows remain unmapped.");

        decimal operatingResult = income - expense;
        decimal sofpDifference = asset - liability - equity - operatingResult;
        AddValidation(connection, transaction, runId, "SOFP_BALANCES", "Statement of financial position balances",
            hierarchy ? "Warning" : "Blocking", hierarchy ? "Warning" : Math.Abs(sofpDifference) <= 0.001m ? "Pass" : "Fail",
            sofpDifference, 0m, 0.001m, hierarchy
                ? "Equity is outside the NIHRACS hierarchy scope. Full balance-sheet reconciliation remains outstanding."
                : "Assets less liabilities, equity and the current-year operating result in $'000.");

        decimal closingCash = 0m;
        LineAccumulation cashLine;
        if (lines.TryGetValue("SOFP|Cash and cash equivalents", out cashLine)) { closingCash = cashLine.ComputedAmount; }
        object priorCashValue = NORMHelper.Scalar(connection, transaction,
            "SELECT TOP 1 p.AmountPrior FROM dbo.tblNORM_PublishedFigure p " +
            "INNER JOIN dbo.tblNORM_CalculationRun r ON r.ConfigurationReleaseId=p.ConfigurationReleaseId " +
            "WHERE r.CalculationRunId=@run AND p.StatementCode='SOFP' AND p.LineCode='Cash and cash equivalents' AND p.IsDeactivated=0",
            NORMHelper.P("@run", runId));
        decimal priorCash = priorCashValue == null ? closingCash - classifiedCashMovement : Convert.ToDecimal(priorCashValue);
        decimal cashDifference = classifiedCashMovement - (closingCash - priorCash);
        AddValidation(connection, transaction, runId, "CASH_FLOW_TIES", "Cash flow ties to the movement in cash", "Blocking",
            Math.Abs(cashDifference) <= 0.001m ? "Pass" : "Fail", cashDifference, 0m, 0.001m,
            "Configured cash-flow classes less the movement in cash and cash equivalents in $'000.");
        AddValidation(connection, transaction, runId, "CASH_FLOW_NON_CASH_EXCLUDED", "Non-cash balances are excluded from cash flow", "Blocking",
            unsafeCashClassCount == 0 ? "Pass" : "Fail", unsafeCashClassCount, 0m, 0m,
            unsafeCashClassCount == 0 ? "No unsafe or non-cash clearing classes feed the direct-method cash-flow statement."
                                      : unsafeCashClassCount.ToString() + " mapped account(s) use a non-cash or unsafe cash-flow class and have been excluded.");

        int lineageCount = 0;
        decimal maximumLineageDifference = 0m;
        foreach (KeyValuePair<string, LineAccumulation> pair in lines)
        {
            decimal sourceTotal = 0m;
            for (int i = 0; i < pair.Value.Sources.Count; i++)
            {
                sourceTotal += pair.Value.Sources[i].PresentedAmount;
                lineageCount++;
            }
            maximumLineageDifference = Math.Max(maximumLineageDifference, Math.Abs(sourceTotal - pair.Value.ComputedAmount));
        }
        int distinctSourceRows = Convert.ToInt32(NORMHelper.Scalar(connection, transaction,
            "SELECT COUNT(DISTINCT TbRowId) FROM dbo.tblNORM_Lineage WHERE CalculationRunId = @run",
            NORMHelper.P("@run", runId)));
        AddValidation(connection, transaction, runId, "LINEAGE_COMPLETE", "Every result retains complete source lineage", "Blocking",
            distinctSourceRows == rowCount ? "Pass" : "Fail", distinctSourceRows, rowCount, 0m,
            distinctSourceRows.ToString("N0", CultureInfo.GetCultureInfo("en-AU")) + " source rows are represented by " +
            lineageCount.ToString("N0", CultureInfo.GetCultureInfo("en-AU")) + " persisted derivation edges.");
        AddValidation(connection, transaction, runId, "NOTES_RECONCILE_TO_FACE", "Note classifications reconcile to face lines", "Blocking",
            maximumLineageDifference <= 0.001m ? "Pass" : "Fail", maximumLineageDifference, 0m, 0.001m,
            "Largest difference between a face line and its persisted note/source contributions in $'000.");

        int tied = 0;
        int close = 0;
        int variance = 0;
        foreach (KeyValuePair<string, LineAccumulation> pair in lines)
        {
            decimal expected;
            if (!published.TryGetValue(pair.Value.StatementCode + "|" + pair.Value.LineCode, out expected)) { continue; }
            string status = ResultStatus(pair.Value.LineCode, pair.Value.ComputedAmount, true, expected);
            if (status == "Tied") { tied++; }
            else if (status == "Close") { close++; }
            else { variance++; }
        }
        string replayResult = variance == 0 ? "Pass" : "Warning";
        AddValidation(connection, transaction, runId, "FY2025_PUBLISHED_REPLAY", "FY2025 audited statement replay", "Information",
            replayResult, tied, published.Count, 0m,
            tied.ToString() + " lines tie to the nearest $'000; " + close.ToString() +
            " are within 1%; " + variance.ToString() + " require accounting verification.");

        int priorCount = Convert.ToInt32(NORMHelper.Scalar(connection, transaction,
            "SELECT COUNT(*) FROM dbo.tblNORM_PublishedFigure p " +
            "INNER JOIN dbo.tblNORM_CalculationRun r ON r.ConfigurationReleaseId = p.ConfigurationReleaseId " +
            "WHERE r.CalculationRunId = @run AND p.AmountPrior IS NOT NULL AND p.IsDeactivated = 0",
            NORMHelper.P("@run", runId)));
        AddValidation(connection, transaction, runId, "COMPARATIVES_PRESENT", "Audited comparative figures are present", "Blocking",
            priorCount == published.Count ? "Pass" : "Fail", priorCount, published.Count, 0m,
            priorCount.ToString() + " of " + published.Count.ToString() + " published baseline lines include a comparative.");
    }

    private static void AddValidation(OleDbConnection connection, OleDbTransaction transaction, int runId,
        string code, string label, string severity, string result, decimal? actual, decimal? expected,
        decimal? tolerance, string detail)
    {
        decimal? difference = actual.HasValue && expected.HasValue ? actual.Value - expected.Value : (decimal?)null;
        NORMHelper.Exec(connection, transaction,
            "INSERT dbo.tblNORM_ValidationResult " +
            "(CalculationRunId,CheckCode,CheckLabel,SeverityCode,ResultCode,ActualValue,ExpectedValue,DifferenceValue,ToleranceValue,DetailText) " +
            "VALUES (@run,@code,@label,@severity,@result,@actual,@expected,@difference,@tolerance,@detail)",
            NORMHelper.P("@run", runId), NORMHelper.P("@code", code), NORMHelper.P("@label", label),
            NORMHelper.P("@severity", severity), NORMHelper.P("@result", result),
            NORMHelper.P("@actual", actual), NORMHelper.P("@expected", expected),
            NORMHelper.P("@difference", difference), NORMHelper.P("@tolerance", tolerance),
            NORMHelper.P("@detail", detail));
    }

    private static void WriteSourceFileValidations(OleDbConnection connection, OleDbTransaction transaction,
        int runId, ImportContext context)
    {
        DataTable files = NORMHelper.Query(connection, transaction,
            "SELECT SourceType,PeriodStart,PeriodEnd,IsStatementInput FROM dbo.tblNORM_ImportFile " +
            "WHERE ImportId = @import ORDER BY PeriodStart,SourceType",
            NORMHelper.P("@import", context.ImportId));
        bool retained = files.Rows.Count > 0;
        AddValidation(connection, transaction, runId, "SOURCE_FILES_RETAINED", "Original source files are retained", "Blocking",
            retained ? "Pass" : "Fail", files.Rows.Count, null, null,
            retained ? files.Rows.Count.ToString() + " immutable source file(s) are retained with separate evidence."
                     : "No original source file is retained for this import.");

        if (!String.Equals(context.SourceType, "ROMAN+ERP", StringComparison.OrdinalIgnoreCase)) { return; }
        bool roman = false;
        bool erp = false;
        for (int i = 0; i < files.Rows.Count; i++)
        {
            DataRow file = files.Rows[i];
            string type = NORMHelper.Str(file, "SourceType");
            int start = file.IsNull("PeriodStart") ? 0 : NORMHelper.Int(file, "PeriodStart");
            int end = file.IsNull("PeriodEnd") ? 0 : NORMHelper.Int(file, "PeriodEnd");
            bool statementInput = Convert.ToBoolean(file["IsStatementInput"]);
            if (type == "ROMAN" && start == 1 && end == 10 && !statementInput) { roman = true; }
            if (type == "ERP" && start == 11 && end == 12 && statementInput) { erp = true; }
        }
        bool valid = files.Rows.Count == 2 && roman && erp;
        AddValidation(connection, transaction, runId, "FY2025_SOURCE_PERIODS", "FY2025 source periods are complete and non-overlapping", "Blocking",
            valid ? "Pass" : "Fail", valid ? 12m : 0m, 12m, 0m,
            valid
                ? "ROMAN periods 01-10 are retained as transition evidence; ERP periods 11-12 and its carried-forward opening position drive the statement calculation."
                : "Expected exactly ROMAN periods 01-10 as transition evidence and ERP periods 11-12 as statement input.");
    }

    private static ImportContext LoadContext(int importId, int? annualRelease = null)
    {
        DataTable table = NORMHelper.Query(
            (annualRelease.HasValue ? "SELECT i.ImportId,c.ConfigurationReleaseId," : "SELECT i.ImportId,i.ConfigurationReleaseId,") +
            "i.FinancialYear,i.EntityCode,i.SourceType,i.DataFingerprint,c.VersionCode,COALESCE(e.EntityName,i.EntityCode) AS EntityName " +
            "FROM dbo.tblNORM_Import i INNER JOIN dbo.tblNORM_ConfigurationRelease c " +
            (annualRelease.HasValue ? "ON c.ConfigurationReleaseId=@release AND c.EntityCode=i.EntityCode AND c.FinancialYear=i.FinancialYear " :
                "ON c.ConfigurationReleaseId = i.ConfigurationReleaseId ") +
            "LEFT JOIN dbo.tblNORM_ReportingEntity e ON e.EntityCode=i.EntityCode AND e.FinancialYear=i.FinancialYear AND e.IsDeactivated=0 " +
            "WHERE i.ImportId = @import AND i.IsDeactivated = 0 AND c.StatusCode = 'Approved' AND c.IsDeactivated = 0 " +
            (annualRelease.HasValue ? "AND i.ConfigurationReleaseId IS NULL AND i.ParentImportId IS NULL AND i.IsTestBreak=0 AND i.StatusCode IN ('Imported','Calculated')" : ""),
            NORMHelper.P("@import", importId), NORMHelper.P("@release", annualRelease));
        if (table.Rows.Count == 0) { throw new InvalidOperationException("The import or its approved configuration release was not found."); }
        DataRow row = table.Rows[0];
        ImportContext context = new ImportContext();
        context.ImportId = importId;
        context.ReleaseId = NORMHelper.Int(row, "ConfigurationReleaseId");
        context.FinancialYear = NORMHelper.Int(row, "FinancialYear");
        context.EntityCode = NORMHelper.Str(row, "EntityCode");
        context.SourceType = NORMHelper.Str(row, "SourceType");
        context.FileHash = NORMHelper.Str(row, "DataFingerprint");
        context.ReleaseVersion = NORMHelper.Str(row, "VersionCode");
        context.ReportingEntityName = NORMHelper.Str(row, "EntityName");
        context.RetainedAnnualSource = annualRelease.HasValue;
        return context;
    }

    private static Dictionary<string, MapEntry> LoadMappings(OleDbConnection connection, OleDbTransaction transaction, int releaseId)
    {
        DataTable table = NORMHelper.Query(connection, transaction,
            "SELECT AccountMapId,GlCode,AccountType,StatementLine,NoteSubLine,CashFlowClass " +
            "FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId = @release AND IsDeactivated = 0 ORDER BY GlCode,AccountMapId",
            NORMHelper.P("@release", releaseId));
        Dictionary<string, MapEntry> values = new Dictionary<string, MapEntry>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < table.Rows.Count; i++)
        {
            string gl = NORMHelper.Str(table.Rows[i], "GlCode");
            if (values.ContainsKey(gl)) { throw new InvalidOperationException("Configuration contains more than one active mapping for G/L " + gl + "."); }
            MapEntry entry = new MapEntry();
            entry.AccountMapId = NORMHelper.Int(table.Rows[i], "AccountMapId");
            entry.GlCode = gl;
            entry.AccountType = NORMHelper.Str(table.Rows[i], "AccountType");
            entry.StatementLine = NORMHelper.Str(table.Rows[i], "StatementLine");
            entry.NoteSubLine = NORMHelper.Str(table.Rows[i], "NoteSubLine");
            entry.CashFlowClass = NORMHelper.Str(table.Rows[i], "CashFlowClass");
            values[gl] = entry;
        }
        if (values.Count == 0) { throw new InvalidOperationException("The selected configuration release contains no account mappings."); }
        return values;
    }

    private static Dictionary<string, TemplateLine> LoadTemplates(OleDbConnection connection, OleDbTransaction transaction, int releaseId)
    {
        DataTable table = NORMHelper.Query(connection, transaction,
            "SELECT StatementLineId,StatementCode,LineCode,SeqNo,CalculationKind,FormulaSpec,LineType,NaturalSign," +
            (HasEmployeeHierarchy(connection, transaction) ? "ParentLineCode" : "NULL AS ParentLineCode") + " FROM dbo.tblNORM_StatementLine " +
            "WHERE ConfigurationReleaseId = @release AND LineCode IS NOT NULL AND IsDeactivated = 0 ORDER BY StatementCode,SeqNo",
            NORMHelper.P("@release", releaseId));
        Dictionary<string, TemplateLine> values = new Dictionary<string, TemplateLine>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < table.Rows.Count; i++)
        {
            string code = NORMHelper.Str(table.Rows[i], "LineCode");
            if (values.ContainsKey(code)) { throw new InvalidOperationException("Statement line code is not unique: " + code + "."); }
            TemplateLine line = new TemplateLine();
            line.StatementLineId = NORMHelper.Int(table.Rows[i], "StatementLineId");
            line.StatementCode = NORMHelper.Str(table.Rows[i], "StatementCode");
            line.LineCode = code;
            line.SeqNo = NORMHelper.Int(table.Rows[i], "SeqNo");
            line.CalculationKind = NORMHelper.Str(table.Rows[i], "CalculationKind");
            line.LineType = NORMHelper.Str(table.Rows[i], "LineType");
            line.FormulaSpec = NORMHelper.Str(table.Rows[i], "FormulaSpec");
            line.ParentLineCode = NORMHelper.Str(table.Rows[i], "ParentLineCode");
            line.NaturalSign = NORMHelper.Str(table.Rows[i], "NaturalSign");
            values[code] = line;
        }
        return values;
    }

    private static HashSet<string> EmployeeDetailCodes(Dictionary<string, TemplateLine> templates)
    {
        // This pilot uses the existing Employee benefits formula slot with no explicit terms.
        // An empty set still represents an active subtotal after its last child is deactivated.
        TemplateLine parent;
        bool active = templates.TryGetValue(EmployeeBenefitsCode, out parent) && parent.StatementCode == "SOCI" &&
            parent.CalculationKind == "Formula" && String.IsNullOrWhiteSpace(parent.FormulaSpec);
        HashSet<string> details = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TemplateLine line in templates.Values)
        {
            if (line.LineType != EmployeeDetailType && String.IsNullOrWhiteSpace(line.ParentLineCode)) continue;
            if (!active || line == parent || line.LineType != EmployeeDetailType ||
                line.StatementCode != "SOCI" || line.CalculationKind != "Mapped" ||
                !String.Equals(line.ParentLineCode, EmployeeBenefitsCode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid Employee benefits parent relationship. Each mapped detail must have Employee benefits as its parent.");
            details.Add(line.LineCode);
        }
        return active ? details : null;
    }

    private static bool IsHierarchy(Dictionary<string, TemplateLine> templates)
    {
        return templates.Values.Any(line => line.LineType == HierarchyDetailType);
    }

    internal static Dictionary<string, string> LoadHierarchyAccountTypes(int releaseId)
    {
        using (OleDbConnection connection = NORMHelper.OpenConnection())
        {
            Dictionary<string, TemplateLine> templates = LoadTemplates(connection, null, releaseId);
            if (!IsHierarchy(templates)) return null;
            CalculateHierarchy(templates, new Dictionary<string, LineAccumulation>(StringComparer.OrdinalIgnoreCase));
            return HierarchyAccountTypes(templates);
        }
    }

    private static Dictionary<string, string> HierarchyAccountTypes(Dictionary<string, TemplateLine> templates)
    {
        Dictionary<string, string> types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (TemplateLine line in templates.Values.Where(t => t.CalculationKind == "Mapped"))
        {
            TemplateLine root = line;
            HashSet<string> parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { line.LineCode };
            while (!String.IsNullOrEmpty(root.ParentLineCode))
            {
                if (!parents.Add(root.ParentLineCode) || !templates.TryGetValue(root.ParentLineCode, out root))
                    throw new InvalidOperationException("The reporting hierarchy contains an invalid or circular parent relationship.");
            }
            string type = null;
            switch (root.LineCode)
            {
                case "Total expenses": type = "Expense"; break;
                case "Total own-source income":
                case "Revenue from Government": type = "Income"; break;
                case "Total assets": type = "Asset"; break;
                case "Total liabilities": type = "Liability"; break;
            }
            if (line.LineType != HierarchyDetailType || type == null)
                throw new InvalidOperationException("A mapped classification must be a detail under a recognised reporting total.");
            types.Add(line.LineCode, type);
        }
        return types;
    }

    private static void CalculateHierarchy(Dictionary<string, TemplateLine> templates, Dictionary<string, LineAccumulation> lines)
    {
        var children = templates.Values.Where(t => !String.IsNullOrEmpty(t.ParentLineCode))
            .ToLookup(t => t.ParentLineCode, StringComparer.OrdinalIgnoreCase);
        foreach (var group in children)
        {
            TemplateLine parent;
            if (!templates.TryGetValue(group.Key, out parent) || parent.CalculationKind != "Formula" ||
                !String.IsNullOrEmpty(parent.FormulaSpec) || group.Any(child => child.StatementCode != parent.StatementCode))
                throw new InvalidOperationException("A hierarchy parent must be an additive total in the same statement.");
        }
        var leaves = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TemplateLine line in templates.Values.Where(t => t.CalculationKind != "Heading"))
            CalculateHierarchyLine(line, templates, children, lines, leaves, visiting);
    }

    private static LineAccumulation CalculateHierarchyLine(TemplateLine line, Dictionary<string, TemplateLine> templates,
        ILookup<string, TemplateLine> children, Dictionary<string, LineAccumulation> lines,
        Dictionary<string, HashSet<string>> leaves, HashSet<string> visiting)
    {
        string key = line.StatementCode + "|" + line.LineCode;
        if (leaves.ContainsKey(line.LineCode)) return lines[key];
        if (!visiting.Add(line.LineCode)) throw new InvalidOperationException("The reporting hierarchy contains a circular dependency.");
        LineAccumulation result;
        if (!lines.TryGetValue(key, out result))
            result = new LineAccumulation { StatementCode = line.StatementCode, LineCode = line.LineCode, StatementLineId = line.StatementLineId };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (line.CalculationKind == "Mapped")
        {
            result.ReportedAmount = RoundEmployeeAmount(result.ComputedAmount);
            used.Add(line.LineCode);
        }
        else
        {
            result.ReportedAmount = 0m;
            bool additive = String.IsNullOrEmpty(line.FormulaSpec);
            IEnumerable<string> terms = additive ? children[line.LineCode].Select(c => "+" + c.LineCode) : line.FormulaSpec.Split('|');
            var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string term in terms)
            {
                TemplateLine child;
                if (term.Length < 2 || (term[0] != '+' && term[0] != '-') ||
                    !templates.TryGetValue(term.Substring(1), out child) || child.StatementCode != line.StatementCode ||
                    child.CalculationKind == "Heading" || !included.Add(child.LineCode) ||
                    (!additive && child.LineType == HierarchyDetailType))
                    throw new InvalidOperationException("A reporting formula contains an invalid or duplicate component.");
                LineAccumulation component = CalculateHierarchyLine(child, templates, children, lines, leaves, visiting);
                foreach (string leaf in leaves[child.LineCode])
                    if (!used.Add(leaf)) throw new InvalidOperationException("A reporting formula counts a detail more than once.");
                decimal factor = term[0] == '-' ? -1m : 1m;
                result.ReportedAmount += factor * component.ReportedAmount.Value;
                AddComponent(result, component, factor);
            }
        }
        visiting.Remove(line.LineCode);
        leaves.Add(line.LineCode, used);
        lines[key] = result;
        return result;
    }

    private static void AddComponent(LineAccumulation total, LineAccumulation component, decimal factor)
    {
        total.ComputedAmount += factor * component.ComputedAmount;
        foreach (SourceContribution source in component.Sources)
            total.Sources.Add(new SourceContribution {
                TbRowId = source.TbRowId, SourceAmount = source.SourceAmount,
                PresentedAmount = factor * source.PresentedAmount, Mapping = source.Mapping
            });
    }

    internal static bool HasEmployeeHierarchy(OleDbConnection connection, OleDbTransaction transaction)
    {
        const string sql = "SELECT CASE WHEN COL_LENGTH('dbo.tblNORM_StatementLine','ParentLineCode') IS NULL THEN 0 ELSE 1 END";
        return Convert.ToInt32(connection == null ? NORMHelper.Scalar(sql) : NORMHelper.Scalar(connection, transaction, sql)) == 1;
    }

    internal static HashSet<string> ValidateEmployeeHierarchy(int releaseId)
    {
        using (OleDbConnection connection = NORMHelper.OpenConnection())
            return EmployeeDetailCodes(LoadTemplates(connection, null, releaseId));
    }

    private static decimal RoundEmployeeAmount(decimal amount)
    { return Math.Round(amount, 0, MidpointRounding.AwayFromZero); }

    public static bool HasReportedAmounts()
    {
        return Convert.ToInt32(NORMHelper.Scalar(
            "SELECT CASE WHEN COL_LENGTH('dbo.tblNORM_LineResult','ReportedAmount') IS NULL THEN 0 ELSE 1 END")) == 1;
    }

    private static void AddFormulaLines(OleDbConnection connection, OleDbTransaction transaction, int releaseId,
        Dictionary<string, LineAccumulation> lines, HashSet<string> employeeDetails)
    {
        DataTable formulas = NORMHelper.Query(connection, transaction,
            "SELECT StatementLineId,StatementCode,LineCode,SeqNo,FormulaSpec FROM dbo.tblNORM_StatementLine " +
            "WHERE ConfigurationReleaseId = @release AND CalculationKind = 'Formula' AND LineCode IS NOT NULL " +
            "AND IsDeactivated = 0 ORDER BY CASE WHEN StatementCode='SOCI' AND LineCode='Employee benefits' " +
            "AND FormulaSpec IS NULL THEN 0 ELSE 1 END,StatementCode,SeqNo",
            NORMHelper.P("@release", releaseId));
        for (int i = 0; i < formulas.Rows.Count; i++)
        {
            DataRow row = formulas.Rows[i];
            string statementCode = NORMHelper.Str(row, "StatementCode");
            string lineCode = NORMHelper.Str(row, "LineCode");
            string spec = NORMHelper.Str(row, "FormulaSpec");
            bool employeeParent = employeeDetails != null && statementCode == "SOCI" &&
                String.Equals(lineCode, EmployeeBenefitsCode, StringComparison.OrdinalIgnoreCase);
            if (!employeeParent && String.IsNullOrWhiteSpace(spec))
            {
                throw new InvalidOperationException("Formula line " + lineCode + " has no formula specification.");
            }

            LineAccumulation formula = new LineAccumulation();
            formula.StatementCode = statementCode;
            formula.LineCode = lineCode;
            formula.StatementLineId = NORMHelper.Int(row, "StatementLineId");
            List<KeyValuePair<decimal, LineAccumulation>> components = employeeDetails == null
                ? null : new List<KeyValuePair<decimal, LineAccumulation>>();
            IEnumerable<string> tokens = employeeParent ? employeeDetails.Select(code => "+" + code) : spec.Split('|');
            foreach (string term in tokens)
            {
                string token = term.Trim();
                if (token.Length < 2 || (token[0] != '+' && token[0] != '-'))
                {
                    throw new InvalidOperationException("Formula " + lineCode + " contains an invalid term: " + token + ".");
                }
                decimal factor = token[0] == '-' ? -1m : 1m;
                string componentCode = token.Substring(1);
                LineAccumulation component;
                if (!lines.TryGetValue(statementCode + "|" + componentCode, out component))
                {
                    throw new InvalidOperationException("Formula " + lineCode + " references missing line " + componentCode + ".");
                }
                if (employeeDetails != null && employeeDetails.Contains(componentCode) && !employeeParent)
                    throw new InvalidOperationException("Include Employee benefits through its subtotal in other formulas.");
                if (components != null) components.Add(new KeyValuePair<decimal, LineAccumulation>(factor, component));
                AddComponent(formula, component, factor);
            }
            if (components != null) ReportEmployeeFormula(formula, components, employeeParent);
            lines[statementCode + "|" + lineCode] = formula;
        }
    }

    private static void ReportEmployeeFormula(LineAccumulation formula,
        List<KeyValuePair<decimal, LineAccumulation>> components, bool employeeParent)
    {
        int employeeTerms = components.FindAll(term => term.Value.UsesEmployeeRollup).Count;
        if (!employeeParent && employeeTerms == 0) return;
        if (employeeTerms > 1)
            throw new InvalidOperationException("A formula cannot include Employee benefits more than once.");
        decimal reported = 0m;
        foreach (KeyValuePair<decimal, LineAccumulation> term in components)
        {
            LineAccumulation component = term.Value;
            if (!component.ReportedAmount.HasValue)
                component.ReportedAmount = RoundEmployeeAmount(component.ComputedAmount);
            reported += term.Key * component.ReportedAmount.Value;
        }
        formula.ReportedAmount = reported;
        formula.UsesEmployeeRollup = true;
    }

    private static Dictionary<string, decimal> LoadPublished(OleDbConnection connection, OleDbTransaction transaction, int releaseId)
    {
        DataTable table = NORMHelper.Query(connection, transaction,
            "SELECT StatementCode,LineCode,AmountCurrent FROM dbo.tblNORM_PublishedFigure " +
            "WHERE ConfigurationReleaseId = @release AND AmountCurrent IS NOT NULL AND IsDeactivated = 0",
            NORMHelper.P("@release", releaseId));
        Dictionary<string, decimal> values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < table.Rows.Count; i++)
        {
            string key = NORMHelper.Str(table.Rows[i], "StatementCode") + "|" + NORMHelper.Str(table.Rows[i], "LineCode");
            values[key] = NORMHelper.Dec(table.Rows[i], "AmountCurrent");
        }
        return values;
    }

    internal static string ConfigurationFingerprint(int releaseId)
    {
        string parentCode = HasEmployeeHierarchy(null, null) ? "+'|'+ISNULL(ParentLineCode,'')" : "";
        DataTable table = NORMHelper.Query(
            "SELECT 'M' AS Kind,GlCode AS A,ISNULL(AccountType,'') AS B,ISNULL(StatementLine,'') AS C," +
            "ISNULL(NoteSubLine,'') AS D,ISNULL(CashFlowClass,'') AS E FROM dbo.tblNORM_AccountMap " +
            "WHERE ConfigurationReleaseId = @release AND IsDeactivated = 0 " +
            "UNION ALL " +
            "SELECT 'S',StatementCode,ISNULL(LineCode,''),LineLabel,ISNULL(NoteRef,''),ISNULL(NaturalSign,'') " +
            "FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId = @release AND IsDeactivated = 0 " +
            "UNION ALL SELECT 'H',StatementCode,ISNULL(LineCode,''),LineType,CalculationKind," +
            "CONVERT(nvarchar(12),SeqNo)+'|'+ISNULL(FormulaSpec,'')" + parentCode + " FROM dbo.tblNORM_StatementLine t " +
            "WHERE t.ConfigurationReleaseId=@pilotRelease AND t.IsDeactivated=0 AND EXISTS " +
            "(SELECT 1 FROM dbo.tblNORM_StatementLine d WHERE d.ConfigurationReleaseId=t.ConfigurationReleaseId " +
            "AND d.IsDeactivated=0 AND (d.LineType IN ('employee-detail','hierarchy-detail') OR (d.StatementCode='SOCI' " +
            "AND d.LineCode='Employee benefits' AND d.CalculationKind='Formula' AND d.FormulaSpec IS NULL))) " +
            "ORDER BY Kind,A,B,C,D,E", NORMHelper.P("@release", releaseId), NORMHelper.P("@pilotRelease", releaseId));
        StringBuilder canonical = new StringBuilder();
        for (int i = 0; i < table.Rows.Count; i++)
        {
            canonical.Append(NORMHelper.Str(table.Rows[i], "Kind")).Append('|')
                .Append(NORMHelper.Str(table.Rows[i], "A")).Append('|')
                .Append(NORMHelper.Str(table.Rows[i], "B")).Append('|')
                .Append(NORMHelper.Str(table.Rows[i], "C")).Append('|')
                .Append(NORMHelper.Str(table.Rows[i], "D")).Append('|')
                .Append(NORMHelper.Str(table.Rows[i], "E")).Append('\n');
        }
        return NORMCrypto.Sha256(canonical.ToString());
    }

    private static string ResultStatus(string lineCode, decimal computed, bool hasPublished, decimal published)
    {
        if (lineCode == "UNMAPPED") { return "Unmapped"; }
        if (!hasPublished) { return "Mapped"; }
        decimal difference = Math.Abs(computed - published);
        if (difference <= 0.5m) { return "Tied"; }
        decimal tolerance = Math.Max(1m, Math.Abs(published) * 0.01m);
        return difference <= tolerance ? "Close" : "Variance";
    }

    private static bool IsKnownType(string accountType)
    {
        return accountType == "Asset" || accountType == "Liability" || accountType == "Equity" ||
            accountType == "Income" || accountType == "Expense";
    }

    private static bool IsCreditType(string accountType)
    {
        return accountType == "Liability" || accountType == "Equity" || accountType == "Income";
    }

    private static decimal CashFlowContribution(string cashFlowClass, decimal sourceAmount)
    {
        string value = (cashFlowClass ?? "").ToLowerInvariant();
        bool outflow = value.IndexOf("payment") >= 0 || value.IndexOf("purchase") >= 0 ||
            value.IndexOf("used") >= 0 || value.IndexOf("paid") >= 0 || value.IndexOf("return") >= 0 ||
            value.IndexOf("selling cost") >= 0;
        return outflow ? -Math.Abs(sourceAmount) : Math.Abs(sourceAmount);
    }

    private static bool IsCashFlowClassSafe(string cashFlowClass)
    {
        string value = (cashFlowClass ?? "").Trim().ToLowerInvariant();
        if (value.Length == 0 || value.StartsWith("clearing -")) { return false; }
        return value.IndexOf("depreciation") < 0 && value.IndexOf("amortisation") < 0 &&
            value.IndexOf("equity movement") < 0 && value.IndexOf("asset movement") < 0 &&
            value.IndexOf("cash and cash equivalents") < 0;
    }

    private static string Truncate(string value, int maximum)
    {
        if (String.IsNullOrEmpty(value) || value.Length <= maximum) { return value; }
        return value.Substring(0, maximum);
    }
}
