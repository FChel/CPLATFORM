using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>Creates immutable, transactional trial-balance imports.</summary>
public static class NORMImportService
{
    public static NORMImportPreview Inspect(byte[] content, string fileName, int releaseId, string user)
    {
        ValidateContent(content, "trial balance");
        byte[] bytes = (byte[])content.Clone();
        DataRow release = LoadApprovedRelease(releaseId);
        DataTable permitted, mappings;
        string configuration = PreviewConfiguration(releaseId, null, null, out permitted, out mappings);
        List<string> companies = new List<string>(), legacyCodes = new List<string>();
        foreach (DataRow pair in permitted.Rows)
        {
            string company = NORMHelper.Str(pair, "SourceCompanyCode");
            if (String.IsNullOrWhiteSpace(company)) { company = NORMHelper.Str(pair, "SourceLedger"); legacyCodes.Add(company); }
            companies.Add(company);
        }
        int year = NORMHelper.Int(release, "FinancialYear");
        NORMParsedImport parsed;
        string type, layout;
        bool workbook = bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4b && bytes[2] == 3 && bytes[3] == 4;
        if (workbook)
        {
            parsed = ErpTrialBalanceImporter.ParseForInspection(bytes, companies, year);
            type = "ERP";
            if (!String.IsNullOrWhiteSpace(parsed.Rows[0].SourceCompanyCode))
            {
                layout = "Defence ERP";
                foreach (NORMTrialBalanceRow row in parsed.Rows)
                {
                    bool allowed = false;
                    foreach (DataRow pair in permitted.Rows)
                    {
                        if (String.Equals(row.SourceCompanyCode, NORMHelper.Str(pair, "SourceCompanyCode"), StringComparison.OrdinalIgnoreCase) &&
                            String.Equals(row.SourceLedger, NORMHelper.Str(pair, "SourceLedger"), StringComparison.OrdinalIgnoreCase)) { allowed = true; break; }
                    }
                    if (!allowed) { throw new InvalidDataException("The detected company/ledger pair is not authorised."); }
                    row.GlAccount = ErpTrialBalanceImporter.ExtractGl(row.GlAccount);
                }
            }
            else
            {
                if (legacyCodes.Count == 0) { throw new InvalidDataException("No legacy source population is authorised."); }
                parsed = ErpTrialBalanceImporter.Parse(bytes, legacyCodes, year);
                layout = "Legacy ERP";
            }
        }
        else if (RomanTrialBalanceImporter.Recognises(bytes))
        {
            if (legacyCodes.Count == 0) { throw new InvalidDataException("No ROMAN source population is authorised."); }
            parsed = RomanTrialBalanceImporter.Parse(bytes, legacyCodes, year); type = "ROMAN"; layout = "ROMAN";
        }
        else { throw new InvalidDataException("Unsupported source layout. No recognised ERP or ROMAN content signature was found."); }

        NORMImportPreview preview = new NORMImportPreview();
        preview.Content = bytes; preview.Token = Guid.NewGuid().ToString("N"); preview.Owner = user;
        preview.ExpiresUtc = DateTime.UtcNow.AddMinutes(15); preview.ReleaseId = releaseId;
        preview.ConfigurationVersion = NORMHelper.Str(release, "VersionCode");
        preview.FileName = Path.GetFileName(fileName); preview.FileHash = NORMCrypto.Sha256(bytes);
        preview.ConfigurationFingerprint = configuration; preview.SourceType = type; preview.Layout = layout;
        preview.Entity = NORMHelper.Str(release, "EntityCode"); preview.FinancialYear = parsed.FinancialYear;
        preview.PeriodStart = parsed.PeriodStart; preview.PeriodEnd = parsed.PeriodEnd; preview.RowCount = parsed.Rows.Count;
        preview.TotalsAvailable = true;
        if (year == 2025) { preview.Errors.Add("FY2025 requires the existing controlled ROMAN 01-10 / ERP 11-12 pair. Single-file acceptance is unavailable for this year."); }
        HashSet<string> companyValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> ledgerValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase), unmapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow mapping in mappings.Rows)
        {
            string accountType = NORMHelper.Str(mapping, "AccountType");
            if ((accountType == "Asset" || accountType == "Liability" || accountType == "Equity" || accountType == "Income" || accountType == "Expense") &&
                !String.IsNullOrWhiteSpace(NORMHelper.Str(mapping, "StatementLine"))) { mapped.Add(NORMHelper.Str(mapping, "GlCode")); }
        }
        StringBuilder identity = new StringBuilder();
        bool precision = true;
        try
        {
            foreach (NORMTrialBalanceRow row in parsed.Rows)
            {
                companyValues.Add(row.SourceCompanyCode ?? (type == "ERP" ? row.SourceLedger : "Unknown (legacy ROMAN)"));
                ledgerValues.Add(row.SourceCompanyCode == null ? "Unknown (legacy source " + row.SourceLedger + ")" : row.SourceLedger);
                identity.Append(row.SourceRowNo).Append('|').Append(row.SourceCompanyCode).Append('|').Append(row.SourceLedger).Append('|').Append(row.RowHash).Append('\n');
                if (row.AccumBalance >= 0m) { preview.Debit += row.AccumBalance; } else { preview.Credit -= row.AccumBalance; }
                precision &= FitsStoredAmount(row.AccumBalance) && (!row.OpeningBalance.HasValue || FitsStoredAmount(row.OpeningBalance.Value)) &&
                    (!row.DebitMovement.HasValue || FitsStoredAmount(row.DebitMovement.Value)) && (!row.CreditMovement.HasValue || FitsStoredAmount(row.CreditMovement.Value));
                if (!mapped.Contains(row.GlAccount)) { unmapped.Add(row.GlAccount); }
            }
            preview.Net = preview.Debit - preview.Credit;
            precision &= FitsStoredAmount(preview.Debit) && FitsStoredAmount(preview.Credit) && FitsStoredAmount(preview.Net);
            if (preview.Net != 0m) { preview.Errors.Add("Debits and credits must differ by exactly zero AUD. Actual: " + ExactAmount(preview.Net) + "."); }
        }
        catch (OverflowException) { precision = false; preview.TotalsAvailable = false; preview.Errors.Add("Amount totals exceed the supported decimal range."); }
        preview.PrecisionValid = precision;
        if (!precision) { preview.Errors.Add("Amounts must fit existing storage without precision loss."); }
        List<string> orderedCompanies = new List<string>(companyValues), orderedLedgers = new List<string>(ledgerValues);
        orderedCompanies.Sort(StringComparer.Ordinal); orderedLedgers.Sort(StringComparer.Ordinal);
        preview.Companies = String.Join(", ", orderedCompanies.ToArray()); preview.Ledgers = String.Join(", ", orderedLedgers.ToArray());
        preview.UnmappedAccounts = unmapped.Count; preview.Warnings.AddRange(parsed.Warnings);
        if (unmapped.Count > 0) { preview.Warnings.Add(unmapped.Count.ToString() + " unmapped accounts remain eligible for acceptance and the existing mapping workflow."); }
        preview.IdentityFingerprint = NORMCrypto.Sha256(preview.Entity + "|" + year + "|" + parsed.PeriodStart + "|" + parsed.PeriodEnd + "|" + identity);
        return preview;
    }

    public static NORMImportOutcome Confirm(NORMImportPreview preview, string user)
    {
        if (preview == null) { throw new InvalidOperationException("Inspect a file before confirming acceptance."); }
        lock (preview)
        {
            if (!String.Equals(preview.Owner, user, StringComparison.Ordinal) || !preview.CanAccept)
            { throw new InvalidOperationException("This preview is invalid, expired or already consumed. Inspect the file again."); }
            preview.Consumed = true;
        }
        if (NORMCrypto.Sha256(preview.Content) != preview.FileHash) { throw new InvalidDataException("The inspected source bytes changed. Inspect again."); }
        NORMImportPreview fresh = Inspect(preview.Content, preview.FileName, preview.ReleaseId, user);
        if (!fresh.CanAccept || fresh.ConfigurationFingerprint != preview.ConfigurationFingerprint || fresh.IdentityFingerprint != preview.IdentityFingerprint || fresh.Layout != preview.Layout)
        { throw new InvalidOperationException("The configuration, source identity or validation changed. Inspect again."); }
        DataRow release = LoadApprovedRelease(preview.ReleaseId);
        List<string> codes = new List<string>(fresh.Companies.Split(new string[] { ", " }, StringSplitOptions.None));
        NORMParsedImport parsed = fresh.SourceType == "ROMAN"
            ? RomanTrialBalanceImporter.Parse(preview.Content, LoadLedgers(fresh.FinancialYear, fresh.Entity), fresh.FinancialYear)
            : ErpTrialBalanceImporter.ParseForInspection(preview.Content, codes, fresh.FinancialYear);
        if (fresh.Layout == "Defence ERP")
        { foreach (NORMTrialBalanceRow row in parsed.Rows) { row.GlAccount = ErpTrialBalanceImporter.ExtractGl(row.GlAccount); } }
        NORMImportSource source = BuildSource(preview.Content, preview.FileName, fresh.SourceType, parsed, true);
        return CommitImport(release, fresh.SourceType, source.FileName, source.FileHash, parsed,
            new List<NORMImportSource> { source }, user, "Confirmed " + fresh.Layout + "; company=" + fresh.Companies +
            ";ledger=" + fresh.Ledgers + ";periods=" + PeriodLabel(parsed) + ";unmapped=" + fresh.UnmappedAccounts + ";units=AUD.", fresh.ConfigurationFingerprint);
    }

    private static string PreviewConfiguration(int releaseId, OleDbConnection connection, OleDbTransaction transaction,
        out DataTable permitted, out DataTable mappings)
    {
        DataTable release = PreviewQuery(connection, transaction,
            "SELECT * FROM dbo.tblNORM_ConfigurationRelease WITH (HOLDLOCK) WHERE ConfigurationReleaseId=@release AND StatusCode='Approved' AND IsDeactivated=0", releaseId);
        if (release.Rows.Count != 1) { throw new InvalidOperationException("The approved configuration is no longer available."); }
        DataRow scope = release.Rows[0];
        string ledgerSql = "SELECT * FROM dbo.tblNORM_EntityLedger WITH (HOLDLOCK) WHERE FinancialYear=@fy AND EntityCode=@entity AND IsDeactivated=0 ORDER BY EntityLedgerId";
        OleDbParameter[] args = { NORMHelper.P("@fy", NORMHelper.Int(scope, "FinancialYear")), NORMHelper.P("@entity", NORMHelper.Str(scope, "EntityCode")) };
        permitted = connection == null ? NORMHelper.Query(ledgerSql, args) : NORMHelper.Query(connection, transaction, ledgerSql, args);
        mappings = PreviewQuery(connection, transaction, "SELECT * FROM dbo.tblNORM_AccountMap WITH (HOLDLOCK) WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 ORDER BY AccountMapId", releaseId);
        DataTable lines = PreviewQuery(connection, transaction, "SELECT * FROM dbo.tblNORM_StatementLine WITH (HOLDLOCK) WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 ORDER BY StatementLineId", releaseId);
        StringBuilder snapshot = new StringBuilder();
        foreach (DataTable table in new DataTable[] { release, permitted, mappings, lines })
        {
            snapshot.Append("TABLE:").Append(table.Columns.Count).Append(':').Append(table.Rows.Count).Append(';');
            foreach (DataColumn column in table.Columns) { snapshot.Append(column.ColumnName.Length).Append(':').Append(column.ColumnName); }
            foreach (DataRow row in table.Rows)
            {
                foreach (object value in row.ItemArray)
                {
                    string text = value is DateTime ? ((DateTime)value).ToString("O", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture);
                    snapshot.Append(value == DBNull.Value ? -1 : text.Length).Append(':').Append(text);
                }
            }
        }
        return NORMCrypto.Sha256(snapshot.ToString());
    }

    private static DataTable PreviewQuery(OleDbConnection connection, OleDbTransaction transaction, string sql, int releaseId)
    {
        OleDbParameter parameter = NORMHelper.P("@release", releaseId);
        return connection == null ? NORMHelper.Query(sql, parameter) : NORMHelper.Query(connection, transaction, sql, parameter);
    }

    /// <summary>Direct Defence backend entry point retained for isolated testing.
    /// Browser acceptance uses Inspect/Confirm with session-bound confirmation.</summary>
    public static NORMImportOutcome ImportDefence(byte[] content, string fileName,
        int configurationReleaseId, string importedBy)
    {
        DataRow release = LoadApprovedRelease(configurationReleaseId);
        int year = NORMHelper.Int(release, "FinancialYear");
        string entity = NORMHelper.Str(release, "EntityCode");
        ValidateContent(content, "Defence trial balance");
        DataTable permitted = NORMHelper.Query(
            "SELECT SourceCompanyCode,SourceLedger FROM dbo.tblNORM_EntityLedger " +
            "WHERE FinancialYear=@fy AND EntityCode=@entity AND SourceCompanyCode IS NOT NULL AND IsDeactivated=0",
            NORMHelper.P("@fy", year), NORMHelper.P("@entity", entity));
        List<string> companies = new List<string>();
        foreach (DataRow pair in permitted.Rows) { companies.Add(NORMHelper.Str(pair, "SourceCompanyCode")); }
        NORMParsedImport parsed = ErpTrialBalanceImporter.ParseForInspection(content, companies, year);
        foreach (NORMTrialBalanceRow row in parsed.Rows)
        {
            bool allowed = false;
            foreach (DataRow pair in permitted.Rows)
            {
                if (String.Equals(row.SourceCompanyCode, NORMHelper.Str(pair, "SourceCompanyCode"), StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(row.SourceLedger, NORMHelper.Str(pair, "SourceLedger"), StringComparison.OrdinalIgnoreCase)) { allowed = true; break; }
            }
            if (String.IsNullOrWhiteSpace(row.SourceCompanyCode) || !allowed)
            { throw new InvalidDataException("The detected company/ledger pair is not authorised for this entity and financial year."); }
            // Match the existing GL mapping key; the retained workbook and row hash
            // preserve the original DCOA/... identifier and amounts.
            row.GlAccount = ErpTrialBalanceImporter.ExtractGl(row.GlAccount);
        }
        string hash = NORMCrypto.Sha256(content);
        NORMImportSource source = BuildSource(content, fileName, "ERP", parsed, true);
        return CommitImport(release, "ERP", source.FileName, hash, parsed,
            new List<NORMImportSource> { source }, importedBy,
            "Defence ERP; Company=" + parsed.Rows[0].SourceCompanyCode + ";Ledger=" + parsed.Rows[0].SourceLedger +
            ";Periods=" + PeriodLabel(parsed) + ";Units=AUD;AccountKey=DCOA suffix; full identifiers retained in source evidence.");
    }

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
        string auditDetail, string confirmationFingerprint = null)
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
        string storageError = null;
        bool totalsAvailable = true;
        for (int i = 0; i < statementInput.Rows.Count; i++)
        {
            NORMTrialBalanceRow row = statementInput.Rows[i];
            decimal amount = row.AccumBalance;
            if (totalsAvailable)
            {
                try
                {
                    if (amount >= 0m) { debit += amount; }
                    else { credit += -amount; }
                }
                catch (OverflowException) { totalsAvailable = false; }
            }
            if (storageError == null)
            {
                if (!FitsStoredAmount(row.AccumBalance)) { storageError = StorageError(row.SourceRowNo, "AccumBalance", row.AccumBalance); }
                else if (row.OpeningBalance.HasValue && !FitsStoredAmount(row.OpeningBalance.Value)) { storageError = StorageError(row.SourceRowNo, "OpeningBalance", row.OpeningBalance.Value); }
                else if (row.DebitMovement.HasValue && !FitsStoredAmount(row.DebitMovement.Value)) { storageError = StorageError(row.SourceRowNo, "DebitMovement", row.DebitMovement.Value); }
                else if (row.CreditMovement.HasValue && !FitsStoredAmount(row.CreditMovement.Value)) { storageError = StorageError(row.SourceRowNo, "CreditMovement", row.CreditMovement.Value); }
            }
        }
        decimal net = totalsAvailable ? debit - credit : 0m;
        if (!totalsAvailable && storageError == null) { storageError = "The trial balance totals exceed the supported amount range."; }
        if (storageError == null && (!FitsStoredAmount(debit) || !FitsStoredAmount(credit) || !FitsStoredAmount(net)))
        {
            storageError = "The trial balance totals exceed the supported amount range.";
        }
        if (net != 0m || storageError != null)
        {
            string attemptId = Guid.NewGuid().ToString("N");
            string failure = net != 0m
                ? "Trial balance rejected: debits and credits differ by AUD " + ExactAmount(net) + ". The required difference is exactly zero."
                : "Trial balance rejected: an amount cannot be stored without loss.";
            try
            {
                using (OleDbConnection connection = NORMHelper.OpenConnection())
                using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    try
                    {
                        WriteAudit(connection, transaction, "IMPORT_REJECTED", "ImportAttempt", attemptId,
                            "Release=" + configurationReleaseId.ToString(CultureInfo.InvariantCulture) +
                            ";FinancialYear=" + financialYear.ToString(CultureInfo.InvariantCulture) +
                            ";Entity=" + Uri.EscapeDataString(entityCode) + ";SourceType=" + sourceType + ";SHA256=" + sourceHash +
                            ";Rows=" + statementInput.Rows.Count.ToString(CultureInfo.InvariantCulture), importedBy);
                        WriteBalanceAudit(connection, transaction, "ImportAttempt", attemptId, debit, credit, net, storageError, importedBy, totalsAvailable);
                        for (int i = 0; i < sources.Count; i++)
                        {
                            NORMImportSource source = sources[i];
                            string fileName = source.FileName;
                            if (String.IsNullOrEmpty(fileName) || fileName.Length > 260)
                            {
                                throw new InvalidDataException("The source filename cannot be retained within the supported audit contract.");
                            }
                            // Escape field delimiters and line breaks; retain Unicode and spaces unchanged.
                            // At most 780 characters for a supported 260-character filename.
                            string escapedName = fileName.Replace("%", "%25").Replace(";", "%3B").Replace("=", "%3D")
                                .Replace("\r", "%0D").Replace("\n", "%0A");
                            WriteAudit(connection, transaction, "IMPORT_REJECTED_SOURCE", "ImportAttempt", attemptId,
                                "SourceType=" + source.SourceType + ";SHA256=" + source.FileHash +
                                ";Bytes=" + source.Content.LongLength.ToString(CultureInfo.InvariantCulture) +
                                ";PeriodStart=" + source.Parsed.PeriodStart.ToString(CultureInfo.InvariantCulture) +
                                ";PeriodEnd=" + source.Parsed.PeriodEnd.ToString(CultureInfo.InvariantCulture) +
                                ";StatementInput=" + source.IsStatementInput.ToString() + ";FileName=" + escapedName, importedBy);
                        }
                        transaction.Commit();
                    }
                    catch { transaction.Rollback(); throw; }
                }
            }
            catch
            {
                throw new InvalidDataException(failure + " No reporting source or calculation was created. Rejection audit evidence could not be saved; contact support.");
            }
            throw new InvalidDataException(failure + " No reporting source or calculation was created. Rejection evidence saved. Attempt reference: " + attemptId + ".");
        }
        long totalBytes = 0L;
        for (int i = 0; i < sources.Count; i++) { totalBytes += sources[i].Content.LongLength; }

        int importId;
        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            try
            {
                if (confirmationFingerprint != null)
                {
                    DataTable permitted, mappings;
                    if (PreviewConfiguration(configurationReleaseId, connection, transaction, out permitted, out mappings) != confirmationFingerprint)
                    { throw new InvalidOperationException("Configuration changed before acceptance. Inspect again."); }
                }
                HashSet<string> populations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (NORMTrialBalanceRow row in statementInput.Rows)
                {
                    string company = row.SourceCompanyCode ?? row.SourceLedger;
                    // Preserve existing FY2025 pair/recalculation paths; new browser
                    // confirmations also protect legacy source populations.
                    if (row.SourceCompanyCode == null && confirmationFingerprint == null) { continue; }
                    if (!populations.Add(company)) { continue; }
                    object existing = NORMHelper.Scalar(connection, transaction,
                        "SELECT TOP 1 i.ImportId FROM dbo.tblNORM_Import i WITH (UPDLOCK,HOLDLOCK) " +
                        "INNER JOIN dbo.tblNORM_TrialBalanceRow tb WITH (UPDLOCK,HOLDLOCK) ON tb.ImportId=i.ImportId " +
                        "WHERE i.EntityCode=@entity AND i.FinancialYear=@fy AND i.IsTestBreak=0 AND i.IsDeactivated=0 " +
                        "AND tb.IsDeactivated=0 AND (tb.SourceCompanyCode=@company OR " +
                        "(tb.SourceCompanyCode IS NULL AND tb.SourceLedger=@company AND i.SourceType IN ('ERP','ROMAN+ERP','ROMAN')))",
                        NORMHelper.P("@entity", entityCode), NORMHelper.P("@fy", financialYear),
                        NORMHelper.P("@company", company));
                    if (existing != null)
                    { throw new InvalidOperationException("This company/year already has retained reporting data. Confirmed replacement is not available."); }
                }
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
                        "(ImportId,SourceRowNo,SourceCompanyCode,SourceLedger,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement," +
                        " AccumBalance,RowHash,IsSynthetic) " +
                        "VALUES (@import,@row,@company,@ledger,@gl,@text,@opening,@debit,@credit,@balance,@hash,0)",
                        NORMHelper.P("@import", importId),
                        NORMHelper.P("@row", row.SourceRowNo),
                        NORMHelper.P("@company", (object)row.SourceCompanyCode ?? DBNull.Value),
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
                try
                {
                    WriteBalanceAudit(connection, transaction, "Import", importId.ToString(CultureInfo.InvariantCulture),
                        debit, credit, net, null, importedBy);
                }
                catch
                {
                    throw new InvalidDataException("Import not accepted: validation evidence could not be saved. No calculation was created; contact support.");
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        NORMImportOutcome outcome = new NORMImportOutcome();
        outcome.ImportId = importId;
        outcome.RowCount = statementInput.Rows.Count;
        outcome.TotalDebit = debit;
        outcome.TotalCredit = credit;
        outcome.NetBalance = net;
        try { outcome.CalculationRunId = NORMStatementEngine.Run(importId, importedBy); }
        catch (Exception)
        {
            // The accepted source transaction has already committed. Do not report
            // rejection or encourage re-upload when subsequent calculation fails.
            outcome.CalculationError = "The source was accepted, but calculation failed. Review the failed calculation before retrying; do not upload the source again.";
        }
        return outcome;
    }

    private static bool FitsStoredAmount(decimal amount)
    {
        return amount >= -99999999999999999.99m && amount <= 99999999999999999.99m &&
            Decimal.Round(amount, 2) == amount;
    }

    private static string ExactAmount(decimal amount)
    {
        return amount.ToString("0.00##########################", CultureInfo.InvariantCulture);
    }

    private static string StorageError(int rowNo, string field, decimal amount)
    {
        return "SourceRow=" + rowNo.ToString(CultureInfo.InvariantCulture) + ";Field=" + field + ";Amount=" + ExactAmount(amount);
    }

    private static void WriteBalanceAudit(OleDbConnection connection, OleDbTransaction transaction,
        string entityType, string entityId, decimal debit, decimal credit, decimal net, string storageError, string user,
        bool totalsAvailable = true)
    {
        WriteAudit(connection, transaction, "IMPORT_BALANCE_VALIDATION", entityType, entityId,
            totalsAvailable ? "Check=DEBITS_EQUAL_CREDITS;Result=" + (net == 0m ? "Pass" : "Fail") +
            ";Units=AUD;Actual=" + ExactAmount(net) + ";Expected=0;Difference=" + ExactAmount(net) +
            ";Tolerance=0;Debit=" + ExactAmount(debit) + ";Credit=" + ExactAmount(credit)
            : "Check=DEBITS_EQUAL_CREDITS;Result=NotRun;Units=AUD;Expected=0;Tolerance=0;Reason=Decimal aggregate overflow", user);
        WriteAudit(connection, transaction, "IMPORT_STORAGE_VALIDATION", entityType, entityId,
            "Check=LOSSLESS_DECIMAL_19_2;Result=" + (storageError == null ? "Pass" : "Fail") +
            (storageError == null ? "" : ";" + storageError), user);
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
                    "(ImportId,SourceRowNo,SourceCompanyCode,SourceLedger,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement,AccumBalance,RowHash,IsSynthetic) " +
                    "SELECT @newImport,SourceRowNo,SourceCompanyCode,SourceLedger,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement,AccumBalance,RowHash,IsSynthetic " +
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
            "WHERE FinancialYear = @fy AND EntityCode = @entity AND SourceCompanyCode IS NULL AND IsDeactivated = 0 ORDER BY SourceLedger",
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
