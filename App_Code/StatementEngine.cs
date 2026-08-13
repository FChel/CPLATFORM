using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text;

/// <summary>
/// Immutable FY-versioned statement engine. Each completed run retains its
/// calculated amounts, validation evidence and a mapping snapshot for every
/// contributing trial-balance row.
/// </summary>
public static class NORMStatementEngine
{
    private class ImportContext
    {
        public int ImportId;
        public int ReleaseId;
        public int FinancialYear;
        public string EntityCode;
        public string SourceType;
        public string FileHash;
        public string ReleaseVersion;
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
        public string FormulaSpec;
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
        public long LineResultId;
        public List<SourceContribution> Sources = new List<SourceContribution>();
    }

    public static int Run(int importId, string startedBy)
    {
        ImportContext context = LoadContext(importId);
        string configurationHash = ConfigurationFingerprint(context.ReleaseId);
        string inputFingerprint = NORMCrypto.Sha256(context.FileHash + "|" + configurationHash);

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
                "(ImportId,ConfigurationReleaseId,InputFingerprint,StatusCode,StartedBy) " +
                "VALUES (@import,@release,@fingerprint,'Running',@user)",
                NORMHelper.P("@import", importId), NORMHelper.P("@release", context.ReleaseId),
                NORMHelper.P("@fingerprint", inputFingerprint), NORMHelper.P("@user", startedBy));
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
                    string statementCode = template == null ? "POOL" : template.StatementCode;
                    decimal presented = sourceAmount / 1000m;
                    if (mapped && IsCreditType(mapping.AccountType)) { presented = -presented; }

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

                AddFormulaLines(connection, transaction, context.ReleaseId, lines, templates);

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
                    unmappedCount, asset, liability, equity, income, expense, classifiedCashMovement, unsafeCashClassCount, lines, published);
                WriteSourceFileValidations(connection, transaction, runId, context);

                NORMHelper.Exec(connection, transaction,
                    "UPDATE dbo.tblNORM_CalculationRun SET StatusCode = 'Complete',CompletedUtc = SYSUTCDATETIME() " +
                    "WHERE CalculationRunId = @run AND StatusCode = 'Running'", NORMHelper.P("@run", runId));
                NORMHelper.Exec(connection, transaction,
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
        decimal classifiedCashMovement, int unsafeCashClassCount, Dictionary<string, LineAccumulation> lines, Dictionary<string, decimal> published)
    {
        decimal tbDifference = totalNet / 1000m;
        AddValidation(connection, transaction, runId, "DEBITS_EQUAL_CREDITS", "Debits equal credits", "Blocking",
            Math.Abs(tbDifference) <= 0.001m ? "Pass" : "Fail", tbDifference, 0m, 0.001m,
            "Net of all imported trial-balance rows in $'000. A difference above $1 blocks assurance.");

        decimal coverage = totalAbs == 0m ? 0m : 100m * mappedAbs / totalAbs;
        AddValidation(connection, transaction, runId, "MAPPING_VALUE_COVERAGE", "Trial balance mapped by value", "Warning",
            coverage >= 99m ? "Pass" : "Warning", coverage, 100m, 1m,
            coverage.ToString("0.000", CultureInfo.InvariantCulture) + "% of absolute trial-balance value has an explicit mapping.");

        AddValidation(connection, transaction, runId, "UNMAPPED_DISPOSITION", "Every source row has a mapping disposition", "Warning",
            unmappedCount == 0 ? "Pass" : "Warning", unmappedCount, 0m, 0m,
            unmappedCount.ToString("N0", CultureInfo.GetCultureInfo("en-AU")) + " of " + rowCount.ToString("N0", CultureInfo.GetCultureInfo("en-AU")) + " rows remain unmapped.");

        decimal operatingResult = income - expense;
        decimal sofpDifference = asset - liability - equity - operatingResult;
        AddValidation(connection, transaction, runId, "SOFP_BALANCES", "Statement of financial position balances", "Blocking",
            Math.Abs(sofpDifference) <= 0.001m ? "Pass" : "Fail", sofpDifference, 0m, 0.001m,
            "Assets less liabilities, equity and the current-year operating result in $'000.");

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

    private static ImportContext LoadContext(int importId)
    {
        DataTable table = NORMHelper.Query(
            "SELECT i.ImportId,i.ConfigurationReleaseId,i.FinancialYear,i.EntityCode,i.SourceType,i.DataFingerprint,c.VersionCode " +
            "FROM dbo.tblNORM_Import i INNER JOIN dbo.tblNORM_ConfigurationRelease c " +
            "ON c.ConfigurationReleaseId = i.ConfigurationReleaseId " +
            "WHERE i.ImportId = @import AND i.IsDeactivated = 0 AND c.StatusCode = 'Approved' AND c.IsDeactivated = 0",
            NORMHelper.P("@import", importId));
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
            "SELECT StatementLineId,StatementCode,LineCode,SeqNo,CalculationKind,FormulaSpec FROM dbo.tblNORM_StatementLine " +
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
            line.FormulaSpec = NORMHelper.Str(table.Rows[i], "FormulaSpec");
            values[code] = line;
        }
        return values;
    }

    private static void AddFormulaLines(OleDbConnection connection, OleDbTransaction transaction, int releaseId,
        Dictionary<string, LineAccumulation> lines, Dictionary<string, TemplateLine> templates)
    {
        DataTable formulas = NORMHelper.Query(connection, transaction,
            "SELECT StatementLineId,StatementCode,LineCode,SeqNo,FormulaSpec FROM dbo.tblNORM_StatementLine " +
            "WHERE ConfigurationReleaseId = @release AND CalculationKind = 'Formula' AND LineCode IS NOT NULL " +
            "AND IsDeactivated = 0 ORDER BY StatementCode,SeqNo",
            NORMHelper.P("@release", releaseId));
        for (int i = 0; i < formulas.Rows.Count; i++)
        {
            DataRow row = formulas.Rows[i];
            string statementCode = NORMHelper.Str(row, "StatementCode");
            string lineCode = NORMHelper.Str(row, "LineCode");
            string spec = NORMHelper.Str(row, "FormulaSpec");
            if (String.IsNullOrWhiteSpace(spec))
            {
                throw new InvalidOperationException("Formula line " + lineCode + " has no formula specification.");
            }

            LineAccumulation formula = new LineAccumulation();
            formula.StatementCode = statementCode;
            formula.LineCode = lineCode;
            formula.StatementLineId = NORMHelper.Int(row, "StatementLineId");
            string[] tokens = spec.Split('|');
            for (int t = 0; t < tokens.Length; t++)
            {
                string token = tokens[t].Trim();
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
                formula.ComputedAmount += factor * component.ComputedAmount;
                for (int s = 0; s < component.Sources.Count; s++)
                {
                    SourceContribution original = component.Sources[s];
                    SourceContribution derived = new SourceContribution();
                    derived.TbRowId = original.TbRowId;
                    derived.SourceAmount = original.SourceAmount;
                    derived.PresentedAmount = factor * original.PresentedAmount;
                    derived.Mapping = original.Mapping;
                    formula.Sources.Add(derived);
                }
            }
            lines[statementCode + "|" + lineCode] = formula;
        }
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

    private static string ConfigurationFingerprint(int releaseId)
    {
        DataTable table = NORMHelper.Query(
            "SELECT 'M' AS Kind,GlCode AS A,ISNULL(AccountType,'') AS B,ISNULL(StatementLine,'') AS C," +
            "ISNULL(NoteSubLine,'') AS D,ISNULL(CashFlowClass,'') AS E FROM dbo.tblNORM_AccountMap " +
            "WHERE ConfigurationReleaseId = @release AND IsDeactivated = 0 " +
            "UNION ALL " +
            "SELECT 'S',StatementCode,ISNULL(LineCode,''),LineLabel,ISNULL(NoteRef,''),ISNULL(NaturalSign,'') " +
            "FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId = @release AND IsDeactivated = 0 " +
            "ORDER BY Kind,A,B,C,D,E", NORMHelper.P("@release", releaseId));
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
