using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;

/// <summary>Creates immutable, transactional trial-balance imports.</summary>
public static class NORMImportService
{
    public static NORMImportOutcome Import(byte[] content, string fileName, string sourceType,
        int configurationReleaseId, string importedBy)
    {
        DataRow release = LoadApprovedRelease(configurationReleaseId);
        int financialYear = NORMHelper.Int(release, "FinancialYear");
        if (financialYear == 2025)
        {
            throw new InvalidOperationException("FY2025 requires the controlled ROMAN periods 01-10 and ERP periods 11-12 source pair.");
        }
        string entityCode = NORMHelper.Str(release, "EntityCode");
        List<string> ledgers = LoadLedgers(financialYear, entityCode);
        string normalisedType = (sourceType ?? "").Trim().ToUpperInvariant();
        ValidateContent(content, "trial balance");

        NORMParsedImport parsed;
        if (normalisedType == "ERP") { parsed = ErpTrialBalanceImporter.Parse(content, ledgers, financialYear); }
        else if (normalisedType == "ROMAN") { parsed = RomanTrialBalanceImporter.Parse(content, ledgers, financialYear); }
        else { throw new InvalidDataException("Select ERP or ROMAN as the source format."); }

        string fileHash = NORMCrypto.Sha256(content);
        NORMImportSource source = new NORMImportSource();
        source.Content = content;
        source.FileName = Path.GetFileName(fileName);
        source.FileHash = fileHash;
        source.SourceType = normalisedType;
        source.Parsed = parsed;
        source.IsStatementInput = true;
        List<NORMImportSource> sources = new List<NORMImportSource>();
        sources.Add(source);
        return CommitImport(release, normalisedType, source.FileName, fileHash, parsed, sources, importedBy,
            "The retained source covers reporting periods " + PeriodLabel(parsed) + ".");
    }

    public static NORMImportOutcome ImportFy2025Transition(byte[] romanContent, string romanFileName,
        byte[] erpContent, string erpFileName, int configurationReleaseId, string importedBy)
    {
        DataRow release = LoadApprovedRelease(configurationReleaseId);
        int financialYear = NORMHelper.Int(release, "FinancialYear");
        if (financialYear != 2025)
        {
            throw new InvalidOperationException("The two-file ROMAN/ERP transition is available only for FY2025.");
        }
        ValidateContent(romanContent, "ROMAN trial balance");
        ValidateContent(erpContent, "ERP trial balance");
        string entityCode = NORMHelper.Str(release, "EntityCode");
        List<string> ledgers = LoadLedgers(financialYear, entityCode);
        NORMParsedImport roman = RomanTrialBalanceImporter.Parse(romanContent, ledgers, financialYear);
        NORMParsedImport erp = ErpTrialBalanceImporter.Parse(erpContent, ledgers, financialYear);

        if (roman.PeriodStart != 1 || roman.PeriodEnd != 10)
        {
            throw new InvalidDataException("The FY2025 ROMAN file must identify reporting periods 01-10; this file identifies " +
                PeriodLabel(roman) + ".");
        }
        if (erp.PeriodStart != 11 || erp.PeriodEnd != 12)
        {
            throw new InvalidDataException("The FY2025 ERP file must identify reporting periods 11-12; this file identifies " +
                PeriodLabel(erp) + ".");
        }
        if (roman.PeriodEnd >= erp.PeriodStart)
        {
            throw new InvalidDataException("The ROMAN and ERP reporting periods overlap. No period may be supplied by both files.");
        }
        if (roman.PeriodEnd + 1 != erp.PeriodStart)
        {
            throw new InvalidDataException("There is a gap between the ROMAN and ERP reporting periods.");
        }

        NORMImportSource romanSource = BuildSource(romanContent, romanFileName, "ROMAN", roman, false);
        NORMImportSource erpSource = BuildSource(erpContent, erpFileName, "ERP", erp, true);
        List<NORMImportSource> sources = new List<NORMImportSource>();
        sources.Add(romanSource);
        sources.Add(erpSource);
        string combinedHash = NORMCrypto.Sha256("FY2025|ROMAN|" + romanSource.FileHash + "|01-10|ERP|" +
            erpSource.FileHash + "|11-12");
        string combinedName = CombinedFileName(romanSource.FileName, erpSource.FileName);
        return CommitImport(release, "ROMAN+ERP", combinedName, combinedHash, erp, sources, importedBy,
            "FY2025 transition pair validated with no overlap: ROMAN periods 01-10 and ERP periods 11-12. " +
            "ERP ending balances are the statement input because their starting balances carry the migrated ROMAN year-to-date position.");
    }

    private static NORMImportOutcome CommitImport(DataRow release, string sourceType, string sourceFileName,
        string sourceHash, NORMParsedImport statementInput, IList<NORMImportSource> sources, string importedBy,
        string auditDetail)
    {
        int configurationReleaseId = NORMHelper.Int(release, "ConfigurationReleaseId");
        int financialYear = NORMHelper.Int(release, "FinancialYear");
        string entityCode = NORMHelper.Str(release, "EntityCode");
        object duplicate = NORMHelper.Scalar(
            "SELECT TOP 1 ImportId FROM dbo.tblNORM_Import " +
            "WHERE ConfigurationReleaseId = @release AND SourceFileHash = @hash AND SourceType = @type " +
            "AND IsTestBreak = 0 AND IsDeactivated = 0 ORDER BY ImportId DESC",
            NORMHelper.P("@release", configurationReleaseId),
            NORMHelper.P("@hash", sourceHash),
            NORMHelper.P("@type", sourceType));
        if (duplicate != null)
        {
            throw new InvalidOperationException("This exact source file set has already been imported as import " +
                Convert.ToInt32(duplicate).ToString() + ". Open the existing run instead of creating a duplicate.");
        }

        decimal debit = 0m;
        decimal credit = 0m;
        for (int i = 0; i < statementInput.Rows.Count; i++)
        {
            decimal amount = statementInput.Rows[i].AccumBalance;
            if (amount >= 0m) { debit += amount; }
            else { credit += -amount; }
        }
        decimal net = debit - credit;
        long totalBytes = 0L;
        for (int i = 0; i < sources.Count; i++) { totalBytes += sources[i].Content.LongLength; }

        int importId;
        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            try
            {
                importId = NORMHelper.InsertId(connection, transaction,
                    "INSERT dbo.tblNORM_Import " +
                    "(ConfigurationReleaseId,FinancialYear,EntityCode,SourceType,SourceFileName,SourceFileHash,DataFingerprint,SourceFileBytes," +
                    " [RowCount],TotalDebit,TotalCredit,NetBalance,StatusCode,ImportedBy) " +
                    "VALUES (@release,@fy,@entity,@type,@file,@hash,@hash,@bytes,@rows,@debit,@credit,@net,'Importing',@user)",
                    NORMHelper.P("@release", configurationReleaseId),
                    NORMHelper.P("@fy", financialYear),
                    NORMHelper.P("@entity", entityCode),
                    NORMHelper.P("@type", sourceType),
                    NORMHelper.P("@file", sourceFileName),
                    NORMHelper.P("@hash", sourceHash),
                    NORMHelper.P("@bytes", totalBytes),
                    NORMHelper.P("@rows", statementInput.Rows.Count),
                    NORMHelper.P("@debit", debit),
                    NORMHelper.P("@credit", credit),
                    NORMHelper.P("@net", net),
                    NORMHelper.P("@user", importedBy));

                for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    NORMImportSource source = sources[sourceIndex];
                    OleDbParameter contentParameter = NORMHelper.P("@content", source.Content);
                    contentParameter.OleDbType = OleDbType.LongVarBinary;
                    NORMHelper.Exec(connection, transaction,
                        "INSERT dbo.tblNORM_ImportFile " +
                        "(ImportId,SourceType,SourceFileName,SourceFileHash,SourceFileBytes,PeriodStart,PeriodEnd,[RowCount],IsStatementInput,FileContent) " +
                        "VALUES (@import,@type,@file,@hash,@bytes,@periodStart,@periodEnd,@rows,@input,@content)",
                        NORMHelper.P("@import", importId), NORMHelper.P("@type", source.SourceType),
                        NORMHelper.P("@file", source.FileName), NORMHelper.P("@hash", source.FileHash),
                        NORMHelper.P("@bytes", source.Content.LongLength),
                        NORMHelper.P("@periodStart", source.Parsed.PeriodStart),
                        NORMHelper.P("@periodEnd", source.Parsed.PeriodEnd),
                        NORMHelper.P("@rows", source.Parsed.Rows.Count),
                        NORMHelper.P("@input", source.IsStatementInput), contentParameter);
                }

                for (int i = 0; i < statementInput.Rows.Count; i++)
                {
                    NORMTrialBalanceRow row = statementInput.Rows[i];
                    NORMHelper.Exec(connection, transaction,
                        "INSERT dbo.tblNORM_TrialBalanceRow " +
                        "(ImportId,SourceRowNo,SourceLedger,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement," +
                        " AccumBalance,RowHash,IsSynthetic) " +
                        "VALUES (@import,@row,@ledger,@gl,@text,@opening,@debit,@credit,@balance,@hash,0)",
                        NORMHelper.P("@import", importId),
                        NORMHelper.P("@row", row.SourceRowNo),
                        NORMHelper.P("@ledger", row.SourceLedger),
                        NORMHelper.P("@gl", row.GlAccount),
                        NORMHelper.P("@text", row.GlText),
                        NORMHelper.P("@opening", row.OpeningBalance),
                        NORMHelper.P("@debit", row.DebitMovement),
                        NORMHelper.P("@credit", row.CreditMovement),
                        NORMHelper.P("@balance", row.AccumBalance),
                        NORMHelper.P("@hash", row.RowHash));
                }

                NORMHelper.Exec(connection, transaction,
                    "UPDATE dbo.tblNORM_Import SET StatusCode = 'Imported' WHERE ImportId = @import AND StatusCode = 'Importing'",
                    NORMHelper.P("@import", importId));
                WriteAudit(connection, transaction, "IMPORT_CREATED", "Import", importId.ToString(),
                    statementInput.Rows.Count.ToString("N0", CultureInfo.GetCultureInfo("en-AU")) +
                    " statement-input rows; " + sources.Count.ToString() + " retained source file(s); SHA-256 " +
                    sourceHash + ". " + auditDetail, importedBy);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        int runId = NORMStatementEngine.Run(importId, importedBy);
        NORMImportOutcome outcome = new NORMImportOutcome();
        outcome.ImportId = importId;
        outcome.CalculationRunId = runId;
        outcome.RowCount = statementInput.Rows.Count;
        outcome.TotalDebit = debit;
        outcome.TotalCredit = credit;
        outcome.NetBalance = net;
        return outcome;
    }

    private static NORMImportSource BuildSource(byte[] content, string fileName, string sourceType,
        NORMParsedImport parsed, bool isStatementInput)
    {
        NORMImportSource source = new NORMImportSource();
        source.Content = content;
        source.FileName = Path.GetFileName(fileName);
        source.FileHash = NORMCrypto.Sha256(content);
        source.SourceType = sourceType;
        source.Parsed = parsed;
        source.IsStatementInput = isStatementInput;
        return source;
    }

    private static void ValidateContent(byte[] content, string label)
    {
        if (content == null || content.Length == 0) { throw new InvalidDataException("Choose a non-empty " + label + " file."); }
        int maximum = NORMHelper.SettingInt("NORM.MaxUploadBytes", 104857600);
        if (content.Length > maximum) { throw new InvalidDataException("The " + label + " file exceeds the configured upload limit."); }
    }

    private static string PeriodLabel(NORMParsedImport parsed)
    {
        return parsed.PeriodStart.ToString("00", CultureInfo.InvariantCulture) + "-" +
            parsed.PeriodEnd.ToString("00", CultureInfo.InvariantCulture);
    }

    private static string CombinedFileName(string first, string second)
    {
        string value = first + " + " + second;
        return value.Length <= 260 ? value : value.Substring(0, 257) + "...";
    }

    public static NORMImportOutcome CreateTestBreak(int parentImportId, decimal breakAmount, string importedBy)
    {
        if (breakAmount == 0m) { throw new ArgumentException("The test-break amount must be non-zero."); }
        DataTable parentTable = NORMHelper.Query(
            "SELECT TOP 1 * FROM dbo.tblNORM_Import WHERE ImportId = @import AND IsDeactivated = 0",
            NORMHelper.P("@import", parentImportId));
        if (parentTable.Rows.Count == 0) { throw new InvalidOperationException("The source import was not found."); }
        DataRow parent = parentTable.Rows[0];
        int importId;

        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            try
            {
                string dataFingerprint = NORMCrypto.Sha256(NORMHelper.Str(parent, "DataFingerprint") + "|TEST|" +
                    breakAmount.ToString("0.00", CultureInfo.InvariantCulture) + "|" + DateTime.UtcNow.Ticks.ToString());
                importId = NORMHelper.InsertId(connection, transaction,
                    "INSERT dbo.tblNORM_Import " +
                    "(ConfigurationReleaseId,FinancialYear,EntityCode,SourceType,SourceFileName,SourceFileHash,DataFingerprint,SourceFileBytes," +
                    " [RowCount],TotalDebit,TotalCredit,NetBalance,StatusCode,IsTestBreak,ParentImportId,ImportedBy) " +
                    "VALUES (@release,@fy,@entity,'TestBreak',@file,@fileHash,@dataHash,@bytes,@rows,@debit,@credit,@net,'Imported',1,@parent,@user)",
                    NORMHelper.P("@release", NORMHelper.Int(parent, "ConfigurationReleaseId")),
                    NORMHelper.P("@fy", NORMHelper.Int(parent, "FinancialYear")),
                    NORMHelper.P("@entity", NORMHelper.Str(parent, "EntityCode")),
                    NORMHelper.P("@file", "TEST BREAK - " + NORMHelper.Str(parent, "SourceFileName")),
                    NORMHelper.P("@fileHash", NORMHelper.Str(parent, "SourceFileHash")),
                    NORMHelper.P("@dataHash", dataFingerprint),
                    NORMHelper.P("@bytes", NORMHelper.Long(parent, "SourceFileBytes")),
                    NORMHelper.P("@rows", NORMHelper.Int(parent, "RowCount") + 1),
                    NORMHelper.P("@debit", NORMHelper.Dec(parent, "TotalDebit") + (breakAmount > 0m ? breakAmount : 0m)),
                    NORMHelper.P("@credit", NORMHelper.Dec(parent, "TotalCredit") + (breakAmount < 0m ? -breakAmount : 0m)),
                    NORMHelper.P("@net", NORMHelper.Dec(parent, "NetBalance") + breakAmount),
                    NORMHelper.P("@parent", parentImportId),
                    NORMHelper.P("@user", importedBy));

                NORMHelper.Exec(connection, transaction,
                    "INSERT dbo.tblNORM_ImportFile " +
                    "(ImportId,SourceType,SourceFileName,SourceFileHash,SourceFileBytes,PeriodStart,PeriodEnd,[RowCount],IsStatementInput,FileContent) " +
                    "SELECT @newImport,SourceType,SourceFileName,SourceFileHash,SourceFileBytes,PeriodStart,PeriodEnd,[RowCount],IsStatementInput,FileContent " +
                    "FROM dbo.tblNORM_ImportFile WHERE ImportId = @parent",
                    NORMHelper.P("@newImport", importId), NORMHelper.P("@parent", parentImportId));
                NORMHelper.Exec(connection, transaction,
                    "INSERT dbo.tblNORM_TrialBalanceRow " +
                    "(ImportId,SourceRowNo,SourceLedger,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement,AccumBalance,RowHash,IsSynthetic) " +
                    "SELECT @newImport,SourceRowNo,SourceLedger,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement,AccumBalance,RowHash,IsSynthetic " +
                    "FROM dbo.tblNORM_TrialBalanceRow WHERE ImportId = @parent AND IsDeactivated = 0",
                    NORMHelper.P("@newImport", importId), NORMHelper.P("@parent", parentImportId));
                int nextRow = Convert.ToInt32(NORMHelper.Scalar(connection, transaction,
                    "SELECT ISNULL(MAX(SourceRowNo),0) + 1 FROM dbo.tblNORM_TrialBalanceRow WHERE ImportId = @import",
                    NORMHelper.P("@import", importId)));
                NORMHelper.Exec(connection, transaction,
                    "INSERT dbo.tblNORM_TrialBalanceRow " +
                    "(ImportId,SourceRowNo,SourceLedger,GlAccount,GlText,AccumBalance,RowHash,IsSynthetic) " +
                    "VALUES (@import,@row,'1000','NORM-TEST-BREAK','Deliberate validation test break',@amount,@hash,1)",
                    NORMHelper.P("@import", importId), NORMHelper.P("@row", nextRow),
                    NORMHelper.P("@amount", breakAmount), NORMHelper.P("@hash", NORMCrypto.Sha256(dataFingerprint + "|ROW")));
                WriteAudit(connection, transaction, "TEST_BREAK_CREATED", "Import", importId.ToString(),
                    "Created from import " + parentImportId.ToString() + " with a deliberate " +
                    breakAmount.ToString("N2", CultureInfo.GetCultureInfo("en-AU")) + " imbalance.", importedBy);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        int runId = NORMStatementEngine.Run(importId, importedBy);
        NORMImportOutcome outcome = new NORMImportOutcome();
        outcome.ImportId = importId;
        outcome.CalculationRunId = runId;
        outcome.RowCount = NORMHelper.Int(parent, "RowCount") + 1;
        outcome.TotalDebit = NORMHelper.Dec(parent, "TotalDebit") + (breakAmount > 0m ? breakAmount : 0m);
        outcome.TotalCredit = NORMHelper.Dec(parent, "TotalCredit") + (breakAmount < 0m ? -breakAmount : 0m);
        outcome.NetBalance = NORMHelper.Dec(parent, "NetBalance") + breakAmount;
        return outcome;
    }

    private static DataRow LoadApprovedRelease(int releaseId)
    {
        DataTable table = NORMHelper.Query(
            "SELECT ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode FROM dbo.tblNORM_ConfigurationRelease " +
            "WHERE ConfigurationReleaseId = @release AND StatusCode = 'Approved' AND IsDeactivated = 0",
            NORMHelper.P("@release", releaseId));
        if (table.Rows.Count == 0) { throw new InvalidOperationException("Select an approved configuration release."); }
        return table.Rows[0];
    }

    private static List<string> LoadLedgers(int financialYear, string entityCode)
    {
        DataTable table = NORMHelper.Query(
            "SELECT SourceLedger FROM dbo.tblNORM_EntityLedger " +
            "WHERE FinancialYear = @fy AND EntityCode = @entity AND IsDeactivated = 0 ORDER BY SourceLedger",
            NORMHelper.P("@fy", financialYear), NORMHelper.P("@entity", entityCode));
        List<string> values = new List<string>();
        for (int i = 0; i < table.Rows.Count; i++) { values.Add(NORMHelper.Str(table.Rows[i], "SourceLedger")); }
        if (values.Count == 0) { throw new InvalidOperationException("The selected configuration has no source ledgers."); }
        return values;
    }

    private static void WriteAudit(OleDbConnection connection, OleDbTransaction transaction,
        string eventCode, string entityType, string entityId, string detail, string user)
    {
        NORMHelper.Exec(connection, transaction,
            "INSERT dbo.tblNORM_AuditEvent (EventCode,EntityType,EntityId,DetailText,PerformedBy) " +
            "VALUES (@event,@type,@id,@detail,@user)",
            NORMHelper.P("@event", eventCode), NORMHelper.P("@type", entityType),
            NORMHelper.P("@id", entityId), NORMHelper.P("@detail", detail), NORMHelper.P("@user", user));
    }

    private sealed class NORMImportSource
    {
        public byte[] Content;
        public string FileName;
        public string FileHash;
        public string SourceType;
        public NORMParsedImport Parsed;
        public bool IsStatementInput;
    }
}
