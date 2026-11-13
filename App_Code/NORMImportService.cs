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
    /// <summary>Prepare one file within the agency established by an approved
    /// configuration. Explicit selections override ambiguity, never authorisation.</summary>
    public static NORMImportPreview InspectForAgency(byte[] content, string fileName, int agencyReleaseId,
        IList<int> selectedReleaseIds, string user)
    {
        return Inspect(content, fileName, ResolveAgencyRelease(content, fileName, agencyReleaseId, selectedReleaseIds, user), user);
    }

    public static List<NORMImportPreview> InspectFileForAgency(byte[] content, string fileName, int agencyReleaseId,
        IList<int> selectedReleaseIds, string user)
    {
        int releaseId = ResolveAgencyRelease(content, fileName, agencyReleaseId, selectedReleaseIds, user);
        NORMParsedImport parsed;
        List<NORMImportPreview> previews;
        bool text = content.Length < 4 || content[0] != 0x50 || content[1] != 0x4b || content[2] != 3 || content[3] != 4;
        try { previews = InspectPopulations(content, fileName, releaseId, user, true, true, out parsed); }
        catch (InvalidDataException error)
        {
            if (text) { RecordInspectionRejection(content, fileName, releaseId, user, error.Message); }
            throw;
        }
        if (text)
        {
            foreach (NORMImportPreview preview in previews)
            { if (!preview.CanAccept) { RecordInspectionRejection(content, fileName, releaseId, user, String.Join(" ", preview.Errors.ToArray()), preview); } }
        }
        return previews;
    }

    private static int ResolveAgencyRelease(byte[] content, string fileName, int agencyReleaseId,
        IList<int> selectedReleaseIds, string user)
    {
        ValidateContent(content, "trial balance");
        DataRow anchor = LoadApprovedRelease(agencyReleaseId);
        string entity = NORMHelper.Str(anchor, "EntityCode");
        Dictionary<int, int> selections = new Dictionary<int, int>();
        selections.Add(NORMHelper.Int(anchor, "FinancialYear"), agencyReleaseId);
        if (selectedReleaseIds != null)
        {
            foreach (int selectedId in selectedReleaseIds)
            {
                if (selectedId == agencyReleaseId) { continue; }
                DataRow selected = LoadApprovedRelease(selectedId);
                if (!String.Equals(entity, NORMHelper.Str(selected, "EntityCode"), StringComparison.OrdinalIgnoreCase))
                { throw new InvalidDataException("All selected configuration releases must belong to the same agency."); }
                int selectedYear = NORMHelper.Int(selected, "FinancialYear");
                int existing;
                if (selections.TryGetValue(selectedYear, out existing) && existing != selectedId)
                { throw new InvalidDataException("Select only one approved configuration release per financial year. Change the primary selection for its year."); }
                selections[selectedYear] = selectedId;
            }
        }

        bool workbook = content.Length >= 4 && content[0] == 0x50 && content[1] == 0x4b && content[2] == 3 && content[3] == 4;
        NORMParsedImport discovered;
        try
        {
            discovered = workbook ? ErpTrialBalanceImporter.DiscoverForInspection(content) :
                NORMTextTrialBalanceImporter.DiscoverForInspection(content);
        }
        catch (InvalidDataException error)
        {
            if (!workbook) { RecordInspectionRejection(content, fileName, agencyReleaseId, user, error.Message); }
            throw;
        }
        int releaseId;
        if (!selections.TryGetValue(discovered.FinancialYear, out releaseId))
        {
            DataTable candidates = NORMHelper.Query(
                "SELECT ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode FROM dbo.tblNORM_ConfigurationRelease " +
                "WHERE EntityCode=@entity AND FinancialYear=@fy AND StatusCode='Approved' AND IsDeactivated=0 ORDER BY ConfigurationReleaseId",
                NORMHelper.P("@entity", entity), NORMHelper.P("@fy", discovered.FinancialYear));
            if (candidates.Rows.Count == 0)
            { throw new InvalidDataException("No active approved configuration release is available for " + entity + " FY" + discovered.FinancialYear + "."); }
            if (candidates.Rows.Count != 1)
            { throw new InvalidDataException("More than one approved configuration release is available for " + entity + " FY" + discovered.FinancialYear + ". Select its applicable release in Additional approved configurations and inspect again."); }
            releaseId = NORMHelper.Int(candidates.Rows[0], "ConfigurationReleaseId");
        }
        // Repeat the normal approved-release and source/population checks. The
        // discovery parse never authorises a population or enables acceptance.
        DataRow bound = LoadApprovedRelease(releaseId);
        if (!String.Equals(entity, NORMHelper.Str(bound, "EntityCode"), StringComparison.OrdinalIgnoreCase) ||
            NORMHelper.Int(bound, "FinancialYear") != discovered.FinancialYear)
        { throw new InvalidDataException("The selected release does not match the source agency and financial year. Inspect again."); }
        return releaseId;
    }

    public static NORMImportPreview Inspect(byte[] content, string fileName, int releaseId, string user)
    {
        NORMParsedImport parsed;
        NORMImportPreview preview;
        try
        {
            preview = InspectSource(content, fileName, releaseId, user, true, out parsed);
        }
        catch (InvalidDataException error)
        {
            if (content != null && (content.Length < 4 || content[0] != 0x50 || content[1] != 0x4b || content[2] != 3 || content[3] != 4))
            { RecordInspectionRejection(content, fileName, releaseId, user, error.Message); }
            throw;
        }
        if (!preview.CanAccept && (parsed.ProfileVersion == NORMTextTrialBalanceImporter.CsvProfile || parsed.ProfileVersion == NORMTextTrialBalanceImporter.FixedProfile))
        { RecordInspectionRejection(content, fileName, releaseId, user, String.Join(" ", preview.Errors.ToArray()), preview); }
        return preview;
    }

    private static void RecordInspectionRejection(byte[] content, string fileName, int releaseId, string user, string reason, NORMImportPreview preview = null)
    {
        // Authorisation failures never expose or write another agency's identity.
        DataRow release = preview == null ? LoadApprovedRelease(releaseId) : null;
        string entity = preview == null ? NORMHelper.Str(release, "EntityCode") : preview.Entity;
        int financialYear = preview == null ? NORMHelper.Int(release, "FinancialYear") : preview.FinancialYear;
        string availableScope = "";
        if (content != null)
        {
            string prefix = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 4096));
            System.Text.RegularExpressions.Match company = System.Text.RegularExpressions.Regex.Match(prefix, "(?:Company : \",\"|Company:\\s+)([0-9]+)");
            if (company.Success) { availableScope = ";SourceCompany=" + company.Groups[1].Value; }
        }
        if (preview != null)
        {
            availableScope += ";Profile=" + preview.ProfileVersion + ";Company=" + Uri.EscapeDataString(preview.Companies ?? "") +
                ";Ledger=" + Uri.EscapeDataString(preview.Ledgers ?? "") + ";Stream=" + Uri.EscapeDataString(preview.ReportingStreams ?? "") +
                ";Coverage=" + (preview.CoverageStart.HasValue ? preview.CoverageStart.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "Unknown") +
                "/" + (preview.CoverageEnd.HasValue ? preview.CoverageEnd.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "Unknown");
        }
        string detail = "Outcome=Rejected;Release=" + releaseId + ";Agency=" + Uri.EscapeDataString(entity) + availableScope +
            ";FY=" + financialYear + ";File=" + Uri.EscapeDataString(Path.GetFileName(fileName ?? "")) +
            ";SHA256=" + (content == null ? "Unavailable" : NORMCrypto.Sha256(content)) + ";Reason=" + Uri.EscapeDataString(reason);
        if (detail.Length > 2000) { detail = detail.Substring(0, 2000); }
        try
        {
            using (OleDbConnection connection = NORMHelper.OpenConnection())
            using (OleDbTransaction transaction = connection.BeginTransaction())
            {
                NORMHelper.RequireEntityAccess(entity, connection, transaction);
                WriteAudit(connection, transaction, "IMPORT_REJECTED_SOURCE", "ImportAttempt", Guid.NewGuid().ToString("N"), detail, user);
                transaction.Commit();
            }
        }
        catch { throw new InvalidDataException("Source rejected. Rejection audit evidence could not be saved; contact support. " + reason); }
    }

    private static NORMImportPreview InspectSource(byte[] content, string fileName, int releaseId, string user,
        bool loadCurrentSources, out NORMParsedImport parsed)
    {
        List<NORMImportPreview> populations = InspectPopulations(content, fileName, releaseId, user, loadCurrentSources, false, out parsed);
        if (populations.Count != 1)
        { throw new InvalidDataException("This source file contains multiple populations. Use file inspection to review every population; acceptance is not yet enabled."); }
        return populations[0];
    }

    private static List<NORMImportPreview> InspectPopulations(byte[] content, string fileName, int releaseId, string user,
        bool loadCurrentSources, bool retainValidationErrors, out NORMParsedImport parsed)
    {
        ValidateContent(content, "trial balance");
        byte[] bytes = (byte[])content.Clone();
        DataRow release = LoadApprovedRelease(releaseId);
        DataTable permitted, mappings;
        int currentYear;
        string configuration = PreviewConfiguration(releaseId, null, null, out permitted, out mappings, out currentYear);
        List<string> companies = new List<string>();
        foreach (DataRow population in permitted.Rows)
        { companies.Add(NORMHelper.Str(population, "SourceCompanyCode")); }
        int year = NORMHelper.Int(release, "FinancialYear");
        bool workbook = bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4b && bytes[2] == 3 && bytes[3] == 4;
        parsed = workbook ? ErpTrialBalanceImporter.ParseForInspection(bytes, companies, year) :
            NORMTextTrialBalanceImporter.Parse(bytes, year);
        bool defence = workbook && parsed.Rows[0].SourceCompanyCode != null;
        bool text = !workbook;
        string layout = text ? (parsed.ProfileVersion == NORMTextTrialBalanceImporter.CsvProfile ? "ActewAGL FinStats CSV" : "Icon Water fixed-width text") : defence ? "Defence ERP" : "Legacy ERP";
        if (workbook) { parsed.ProfileVersion = defence ? "DEFENCE-ERP/1" : "LEGACY-ERP/1"; }
        // Existing SQL constrains the storage source category to ERP/ROMAN.
        // ERP remains the TB source category; exact native format/version is
        // retained separately in IMPORT_SOURCE_PROFILE audit, never called XLSX.
        Dictionary<int, string> resolutionErrors = new Dictionary<int, string>();
        foreach (NORMTrialBalanceRow row in parsed.Rows)
        {
            if (!defence && !text)
            {
                // This workbook column is labelled Company Code. It supplies no ledger.
                row.SourceCompanyCode = row.SourceLedger;
                row.SourceLedger = null;
            }
            try
            {
                if (text) { ResolveTextPopulation(row, permitted); }
                else { ResolvePopulation(row, permitted); }
            }
            catch (InvalidDataException error)
            {
                if (!retainValidationErrors) { throw; }
                resolutionErrors[row.SourceRowNo] = error.Message;
            }
            if (defence) { row.GlAccount = ErpTrialBalanceImporter.ExtractGl(row.GlAccount); }
        }

        // Only existing row evidence is partitioned. Native profiles still return
        // their single documented population; no hypothetical report blocks.
        Dictionary<string, List<NORMTrialBalanceRow>> grouped = new Dictionary<string, List<NORMTrialBalanceRow>>(StringComparer.OrdinalIgnoreCase);
        List<List<NORMTrialBalanceRow>> groups = new List<List<NORMTrialBalanceRow>>();
        foreach (NORMTrialBalanceRow row in parsed.Rows)
        {
            string key = ScopeKey(row);
            List<NORMTrialBalanceRow> rows;
            if (!grouped.TryGetValue(key, out rows)) { rows = new List<NORMTrialBalanceRow>(); grouped.Add(key, rows); groups.Add(rows); }
            rows.Add(row);
        }
        NORMImportFileContext file = parsed.SourceFile;
        file.Token = Guid.NewGuid().ToString("N"); file.FileName = Path.GetFileName(fileName); file.FileHash = NORMCrypto.Sha256(bytes);
        file.PopulationCount = groups.Count;
        if (!file.RowsReconciled || file.DetailRowCount != parsed.Rows.Count)
        { throw new InvalidDataException("Source detail and excluded record counts do not reconcile. No population can be accepted."); }
        List<NORMImportPreview> results = new List<NORMImportPreview>();
        foreach (List<NORMTrialBalanceRow> rows in groups)
        {
            NORMParsedImport population = new NORMParsedImport {
                Rows = rows, ProfileVersion = parsed.ProfileVersion, FinancialYear = parsed.FinancialYear,
                PeriodStart = parsed.PeriodStart, PeriodEnd = parsed.PeriodEnd, CoverageStart = parsed.CoverageStart,
                CoverageEnd = parsed.CoverageEnd, SourceFile = file
            };
            population.Warnings.AddRange(parsed.Warnings);
            NORMImportPreview preview = BuildPopulationPreview(bytes, releaseId, user, loadCurrentSources, retainValidationErrors,
                population, release, configuration, mappings, currentYear, layout);
            foreach (NORMTrialBalanceRow row in rows)
            {
                string error;
                if (resolutionErrors.TryGetValue(row.SourceRowNo, out error))
                {
                    if (!preview.Errors.Contains(error)) { preview.Errors.Add(error); }
                    preview.InvalidSourceRows.Add(row.SourceRowNo);
                }
            }
            results.Add(preview);
        }
        file.PopulationFingerprint = FilePopulationFingerprint(results);
        foreach (NORMImportPreview preview in results)
        { preview.ConfirmationFingerprint = PopulationEvidence(preview, true); }
        return results;
    }

    private static NORMImportPreview BuildPopulationPreview(byte[] bytes, int releaseId, string user,
        bool loadCurrentSources, bool retainValidationErrors, NORMParsedImport parsed, DataRow release,
        string configuration, DataTable mappings, int currentYear, string layout)
    {
        int year = NORMHelper.Int(release, "FinancialYear");
        NORMImportPreview preview = new NORMImportPreview();
        preview.Content = bytes; preview.Token = Guid.NewGuid().ToString("N"); preview.Owner = user;
        preview.ExpiresUtc = DateTime.UtcNow.AddMinutes(15); preview.ReleaseId = releaseId;
        preview.ConfigurationVersion = NORMHelper.Str(release, "VersionCode");
        preview.SourceFile = parsed.SourceFile;
        preview.FileName = parsed.SourceFile.FileName;
        if (String.IsNullOrEmpty(preview.FileName) || preview.FileName.Length > 260)
        { preview.Errors.Add("The source filename cannot be retained within the supported storage limit."); }
        preview.FileHash = NORMCrypto.Sha256(bytes);
        preview.Population = new NORMSourceScope {
            Entity = NORMHelper.Str(release, "EntityCode"), FinancialYear = parsed.FinancialYear,
            Company = parsed.Rows[0].SourceCompanyCode, Ledger = parsed.Rows[0].SourceLedger,
            ReportingStream = parsed.Rows[0].ReportingStream, SourceFileName = preview.FileName
        };
        preview.ConfigurationFingerprint = configuration; preview.SourceType = "ERP"; preview.Layout = layout;
        preview.ProfileVersion = parsed.ProfileVersion;
        preview.Entity = NORMHelper.Str(release, "EntityCode"); preview.FinancialYear = parsed.FinancialYear;
        preview.CoverageStart = parsed.CoverageStart; preview.CoverageEnd = parsed.CoverageEnd;
        preview.IsFullYear = parsed.IsFullYear; preview.ConfiguredFinancialYear = currentYear;
        try { ValidateCoverage(parsed, currentYear); }
        catch (InvalidDataException error)
        {
            if (!retainValidationErrors) { throw; }
            preview.Errors.Add(error.Message);
            foreach (NORMTrialBalanceRow row in parsed.Rows) { preview.InvalidSourceRows.Add(row.SourceRowNo); }
        }
        preview.PeriodStart = parsed.PeriodStart; preview.PeriodEnd = parsed.PeriodEnd; preview.RowCount = parsed.Rows.Count;
        preview.PopulationRows.AddRange(parsed.Rows);
        preview.TotalsAvailable = true;
        // D3 excludes new historical ROMAN/ERP transition acceptance, not ordinary
        // standalone FY2025 sources. This governed path uses supported profiles
        // and the normal authorised population/full-year coverage checks above;
        // it does not invoke the legacy transition-pair workflow.
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
        HashSet<string> streams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool precision = true;
        Dictionary<string, int> accounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (NORMTrialBalanceRow row in parsed.Rows)
            {
                companyValues.Add(row.SourceCompanyCode);
                ledgerValues.Add(row.SourceLedger ?? "Not applicable");
                streams.Add(row.ReportingStream);
                identity.Append(row.SourceRowNo).Append('|').Append(row.SourceCompanyCode).Append('|').Append(row.SourceLedger).Append('|').Append(row.ReportingStream).Append('|').Append(row.RowHash).Append('\n');
                if (row.AccumBalance >= 0m) { preview.Debit += row.AccumBalance; } else { preview.Credit -= row.AccumBalance; }
                bool rowPrecision = FitsStoredAmount(row.AccumBalance) && (!row.OpeningBalance.HasValue || FitsStoredAmount(row.OpeningBalance.Value)) &&
                    (!row.DebitMovement.HasValue || FitsStoredAmount(row.DebitMovement.Value)) && (!row.CreditMovement.HasValue || FitsStoredAmount(row.CreditMovement.Value));
                precision &= rowPrecision;
                if (!rowPrecision) { preview.InvalidSourceRows.Add(row.SourceRowNo); }
                int previousRow;
                if (accounts.TryGetValue(row.GlAccount, out previousRow))
                {
                    preview.InvalidSourceRows.Add(previousRow); preview.InvalidSourceRows.Add(row.SourceRowNo);
                    string duplicate = "Duplicate source account " + row.GlAccount + " within this population.";
                    if (!preview.Errors.Contains(duplicate)) { preview.Errors.Add(duplicate); }
                }
                else { accounts.Add(row.GlAccount, row.SourceRowNo); }
                if (!mapped.Contains(row.GlAccount)) { unmapped.Add(row.GlAccount); }
            }
            preview.Net = preview.Debit - preview.Credit;
            precision &= FitsStoredAmount(preview.Debit) && FitsStoredAmount(preview.Credit) && FitsStoredAmount(preview.Net);
            if (preview.Net != 0m)
            {
                preview.Errors.Add("Debits and credits must differ by exactly zero AUD. Actual: " + ExactAmount(preview.Net) + ".");
                foreach (NORMTrialBalanceRow row in parsed.Rows) { preview.InvalidSourceRows.Add(row.SourceRowNo); }
            }
        }
        catch (OverflowException)
        {
            precision = false; preview.TotalsAvailable = false; preview.Errors.Add("Amount totals exceed the supported decimal range.");
            foreach (NORMTrialBalanceRow row in parsed.Rows) { preview.InvalidSourceRows.Add(row.SourceRowNo); }
        }
        preview.PrecisionValid = precision;
        if (!precision) { preview.Errors.Add("Amounts must fit existing storage without precision loss."); }
        List<string> orderedCompanies = new List<string>(companyValues), orderedLedgers = new List<string>(ledgerValues);
        orderedCompanies.Sort(StringComparer.Ordinal); orderedLedgers.Sort(StringComparer.Ordinal);
        preview.Companies = String.Join(", ", orderedCompanies.ToArray()); preview.Ledgers = String.Join(", ", orderedLedgers.ToArray());
        preview.ReportingStreams = String.Join(", ", streams);
        preview.UnmappedAccounts = unmapped.Count; preview.Warnings.AddRange(parsed.Warnings);
        if (unmapped.Count > 0) { preview.Warnings.Add(unmapped.Count.ToString() + " unmapped accounts remain eligible for acceptance and the existing mapping workflow."); }
        preview.IdentityFingerprint = NORMCrypto.Sha256(parsed.ProfileVersion + "|" + preview.Entity + "|" + year + "|" + parsed.PeriodStart + "|" + parsed.PeriodEnd + "|" + CoverageLabel(parsed) + "|" + identity);
        if (loadCurrentSources)
        {
            foreach (NORMTrialBalanceRow row in parsed.Rows)
            {
                string company = row.SourceCompanyCode;
                string ledger = row.SourceLedger;
                string key = ScopeKey(row);
                if (row.ReportingStream != null && !preview.Scopes.ContainsKey(key))
                {
                    preview.Scopes.Add(key, FindCurrentSource(preview, company, ledger, row.ReportingStream, null, null));
                }
            }
        }
        return preview;
    }

    public static NORMImportOutcome Confirm(NORMImportPreview preview, string user)
    {
        return ConfirmBatch(new[] { preview }, user, false)[0];
    }

    public static void ValidateBatch(IList<NORMImportPreview> previews)
    {
        try
        {
            ValidatePreparationBatch(previews);
            string batch = BatchEvidence(previews);
            foreach (NORMImportPreview preview in previews) { preview.BatchFingerprint = batch; }
        }
        catch (Exception error)
        {
            if (previews != null && (error is InvalidDataException || error is InvalidOperationException))
            {
                foreach (NORMImportPreview p in previews)
                {
                    if (p != null && (p.ProfileVersion == NORMTextTrialBalanceImporter.CsvProfile || p.ProfileVersion == NORMTextTrialBalanceImporter.FixedProfile))
                    { RecordInspectionRejection(p.Content, p.FileName, p.ReleaseId, p.Owner, error.Message, p); }
                }
            }
            throw;
        }
    }

    private static string IdentityPart(string value)
    {
        return value == null ? "-1:" : value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
    }

    private static string SourceIdentity(NORMSourceScope scope)
    {
        return IdentityPart(scope.Entity) + IdentityPart(scope.FinancialYear.ToString(CultureInfo.InvariantCulture)) +
            IdentityPart(scope.Company) + IdentityPart(scope.Ledger) + IdentityPart(scope.ReportingStream);
    }

    private static string PopulationEvidence(NORMImportPreview preview, bool includePredecessor)
    {
        NORMImportFileContext file = preview.SourceFile;
        StringBuilder text = new StringBuilder();
        foreach (string value in new[] { SourceIdentity(preview.Population), preview.Entity, preview.FinancialYear.ToString(CultureInfo.InvariantCulture),
            preview.ReleaseId.ToString(CultureInfo.InvariantCulture), preview.ConfigurationVersion, preview.ConfigurationFingerprint,
            preview.IdentityFingerprint, preview.FileName, preview.FileHash, preview.SourceType, preview.Layout, preview.ProfileVersion,
            preview.Companies, preview.Ledgers, preview.ReportingStreams, preview.Population.SourceFileName,
            preview.PeriodStart.ToString(CultureInfo.InvariantCulture), preview.PeriodEnd.ToString(CultureInfo.InvariantCulture),
            preview.CoverageStart.HasValue ? preview.CoverageStart.Value.ToString("O", CultureInfo.InvariantCulture) : null,
            preview.CoverageEnd.HasValue ? preview.CoverageEnd.Value.ToString("O", CultureInfo.InvariantCulture) : null,
            preview.IsFullYear.ToString(), preview.ConfiguredFinancialYear.ToString(CultureInfo.InvariantCulture),
            preview.RowCount.ToString(CultureInfo.InvariantCulture), preview.UnmappedAccounts.ToString(CultureInfo.InvariantCulture),
            ExactAmount(preview.Debit), ExactAmount(preview.Credit), ExactAmount(preview.Net), preview.TotalsAvailable.ToString(), preview.PrecisionValid.ToString(),
            file.FileName, file.FileHash, file.PopulationCount.ToString(CultureInfo.InvariantCulture), file.RecordUnit,
            file.RecordCount.ToString(CultureInfo.InvariantCulture), file.DetailRowCount.ToString(CultureInfo.InvariantCulture),
            file.MetadataRowCount.ToString(CultureInfo.InvariantCulture), file.ControlRowCount.ToString(CultureInfo.InvariantCulture), file.BlankRowCount.ToString(CultureInfo.InvariantCulture) })
        { text.Append(IdentityPart(value)); }
        foreach (NORMTrialBalanceRow row in preview.SourceRows)
        {
            foreach (string value in new[] { row.SourceRowNo.ToString(CultureInfo.InvariantCulture), row.SourceCompanyCode, row.SourceLedger,
                row.ReportingStream, row.GlAccount, row.GlText, row.OpeningBalance.HasValue ? ExactAmount(row.OpeningBalance.Value) : null,
                row.DebitMovement.HasValue ? ExactAmount(row.DebitMovement.Value) : null, row.CreditMovement.HasValue ? ExactAmount(row.CreditMovement.Value) : null,
                ExactAmount(row.AccumBalance), row.RowHash, row.IsSynthetic.ToString() })
            { text.Append(IdentityPart(value)); }
        }
        text.Append("ERRORS"); foreach (string error in preview.Errors) { text.Append(IdentityPart(error)); }
        text.Append("WARNINGS"); foreach (string warning in preview.Warnings) { text.Append(IdentityPart(warning)); }
        if (includePredecessor)
        {
            text.Append(IdentityPart(preview.Token)).Append(IdentityPart(preview.Owner)).Append(IdentityPart(preview.ExpiresUtc.ToString("O", CultureInfo.InvariantCulture)))
                .Append(IdentityPart(file.Token)).Append(IdentityPart(file.PopulationFingerprint));
            List<string> scopes = new List<string>();
            foreach (NORMSourceScope scope in preview.SourceScopes)
            { scopes.Add(SourceIdentity(scope) + IdentityPart(scope.ImportId.ToString(CultureInfo.InvariantCulture)) + IdentityPart(scope.SourceFileName) + IdentityPart(scope.ImportedUtc.ToString("O", CultureInfo.InvariantCulture))); }
            scopes.Sort(StringComparer.Ordinal);
            foreach (string scope in scopes) { text.Append(IdentityPart(scope)); }
        }
        return NORMCrypto.Sha256(text.ToString());
    }

    private static string FilePopulationFingerprint(IList<NORMImportPreview> populations)
    {
        List<string> members = new List<string>();
        foreach (NORMImportPreview population in populations) { members.Add(PopulationEvidence(population, false)); }
        members.Sort(StringComparer.Ordinal);
        return NORMCrypto.Sha256(String.Join("|", members.ToArray()));
    }

    private static string BatchEvidence(IList<NORMImportPreview> previews)
    {
        List<string> members = new List<string>();
        foreach (NORMImportPreview preview in previews)
        { members.Add(IdentityPart(preview.Token) + IdentityPart(preview.ConfirmationFingerprint)); }
        members.Sort(StringComparer.Ordinal);
        return NORMCrypto.Sha256(String.Join("|", members.ToArray()));
    }

    private static void VerifyBatchMembership(IList<NORMImportPreview> previews)
    {
        string batch = BatchEvidence(previews);
        foreach (NORMImportPreview preview in previews)
        {
            if (preview.BatchFingerprint != null && preview.BatchFingerprint != batch)
            { throw new InvalidOperationException("The staged batch population set changed. Inspect the complete batch again."); }
        }
    }

    private static void ValidatePreparationBatch(IList<NORMImportPreview> previews)
    {
        if (previews == null || previews.Count == 0 || previews[0] == null)
        { throw new InvalidOperationException("Inspect the source files before confirming acceptance."); }
        string entity = previews[0].Entity;
        long bytes = 0;
        HashSet<string> files = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, int> fileRows = new Dictionary<string, int>();
        Dictionary<string, int> filePopulations = new Dictionary<string, int>();
        Dictionary<string, HashSet<int>> sourceRows = new Dictionary<string, HashSet<int>>();
        HashSet<string> identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, string> snapshots = new Dictionary<int, string>();
        foreach (NORMImportPreview preview in previews)
        {
            if (preview == null || preview.Content == null || preview.Population == null)
            { throw new InvalidOperationException("A source population has no resolved context. Inspect again."); }
            if (!String.Equals(preview.Entity, entity, StringComparison.OrdinalIgnoreCase))
            { throw new InvalidDataException("An upload batch must belong to one agency. Cross-agency batches are not supported."); }
            string snapshot;
            if (snapshots.TryGetValue(preview.ReleaseId, out snapshot) && snapshot != preview.ConfigurationFingerprint)
            { throw new InvalidOperationException("The approved configuration changed between files. Inspect the batch again."); }
            snapshots[preview.ReleaseId] = preview.ConfigurationFingerprint;
            NORMImportFileContext file = preview.SourceFile;
            if (file == null || !file.RowsReconciled || String.IsNullOrEmpty(file.Token))
            { throw new InvalidDataException("Source record accounting is unavailable. Inspect again."); }
            if (preview.RowCount != preview.SourceRows.Count)
            { throw new InvalidDataException("Population detail row count does not match its source rows. Inspect again."); }
            if (files.Add(file.Token))
            {
                bytes += preview.Content.LongLength;
                fileRows.Add(file.Token, 0); filePopulations.Add(file.Token, 0); sourceRows.Add(file.Token, new HashSet<int>());
            }
            fileRows[file.Token] += preview.RowCount;
            filePopulations[file.Token]++;
            foreach (NORMTrialBalanceRow row in preview.SourceRows)
            {
                if (!sourceRows[file.Token].Add(row.SourceRowNo))
                { throw new InvalidDataException("A source detail row was assigned more than once. Inspect again."); }
            }
            NORMSourceScope scope = preview.Population;
            string key = SourceIdentity(scope);
            if (!identities.Add(key))
            { throw new InvalidDataException("More than one file supplies the same agency, financial year, company, ledger and reporting stream. Select one authoritative file per source scope."); }
        }
        foreach (NORMImportPreview preview in previews)
        {
            NORMImportFileContext file = preview.SourceFile;
            if (fileRows[file.Token] != file.DetailRowCount || sourceRows[file.Token].Count != file.DetailRowCount ||
                filePopulations[file.Token] != file.PopulationCount)
            { throw new InvalidDataException("Not every source detail row and population is accounted for. Inspect the complete file again."); }
        }
        if (bytes > NORMHelper.SettingInt("NORM.MaxUploadBytes", 104857600))
        { throw new InvalidDataException("The combined files exceed the configured upload limit."); }
    }

    public static string BatchConfirmationBlockReason(IList<NORMImportPreview> previews)
    {
        try { ValidatePreparationBatch(previews); }
        catch (InvalidDataException error) { return error.Message; }
        catch (InvalidOperationException error) { return error.Message; }
        return null;
    }

    private static void ValidateBatchCore(IList<NORMImportPreview> previews)
    {
        ValidatePreparationBatch(previews);
    }

    public static List<NORMImportOutcome> ConfirmBatch(IList<NORMImportPreview> previews, string user, bool replaceExisting)
    {
        // Routine unchecked acknowledgement and expiry are intentionally not audited.
        bool auditFailure = previews != null && previews.Count > 0;
        if (auditFailure)
        {
            foreach (NORMImportPreview p in previews)
            {
                if (p == null || p.Owner != user || p.ExpiresUtc <= DateTime.UtcNow || (p.HasReplacement && !replaceExisting))
                { auditFailure = false; break; }
            }
        }
        string attempt = Guid.NewGuid().ToString("N");
        bool accepted = false;
        try { return ConfirmBatchCore(previews, user, replaceExisting, ref accepted); }
        catch (Exception error)
        {
            if (auditFailure && !accepted && !(error is UnauthorizedAccessException) && !(error is RoutineConfirmationException) &&
                NORMHelper.HasPrepareAccess() && String.Equals(user, NORMHelper.CurrentUserId(), StringComparison.Ordinal))
            {
                RecordBatchAcceptanceRejection(previews, NORMHelper.CurrentUserId(), attempt, error);
            }
            throw;
        }
    }

    // A typed routine outcome avoids suppressing a real conflict when the clock
    // crosses preview expiry while its transaction is unwinding.
    private sealed class RoutineConfirmationException : InvalidOperationException
    {
        public RoutineConfirmationException(string message) : base(message) { }
    }

    private static string AuditField(string value, int limit)
    {
        string escaped = Uri.EscapeDataString(value ?? "(NULL)");
        if (escaped.Length <= limit) { return escaped; }
        int length = limit - 12;
        // Do not split an escaped byte triplet.
        while (length > 0 && (escaped[length - 1] == '%' || (length > 1 && escaped[length - 2] == '%'))) { length--; }
        return escaped.Substring(0, length) + "[truncated]";
    }

    private static string RejectionScope(NORMImportPreview p)
    {
        NORMSourceScope scope = p.Population;
        return "Agency=" + AuditField(p.Entity, 100) + ";FY=" + p.FinancialYear.ToString(CultureInfo.InvariantCulture) +
            ";Company=" + AuditField(scope == null ? null : scope.Company, 80) +
            ";Ledger=" + AuditField(scope == null ? null : scope.Ledger, 80) +
            ";Stream=" + AuditField(scope == null ? null : scope.ReportingStream, 80) +
            ";Release=" + p.ReleaseId.ToString(CultureInfo.InvariantCulture);
    }

    private static string RejectionScopeEvidence(NORMImportPreview p)
    {
        try { VerifyRetainedPreview(p); return "Retained"; }
        catch (InvalidOperationException) { return "ChangedOrUnavailable"; }
        catch (NullReferenceException) { return "ChangedOrUnavailable"; }
    }

    private static void RecordBatchAcceptanceRejection(IList<NORMImportPreview> previews, string actor, string attempt, Exception error)
    {
        string reason = error is InvalidDataException || error is InvalidOperationException ? error.Message : "Acceptance failed; no source accepted.";
        // One event per original, not repeated identical failure events for its
        // populations. Distinct scopes are associated within that event.
        Dictionary<string, List<NORMImportPreview>> files = new Dictionary<string, List<NORMImportPreview>>(StringComparer.Ordinal);
        foreach (NORMImportPreview p in previews)
        {
            string key = p.SourceFile == null ? p.FileHash ?? "Unavailable" : p.SourceFile.Token;
            List<NORMImportPreview> members;
            if (!files.TryGetValue(key, out members)) { members = new List<NORMImportPreview>(); files.Add(key, members); }
            members.Add(p);
        }
        try
        {
            using (OleDbConnection connection = NORMHelper.OpenConnection())
            using (OleDbTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    foreach (List<NORMImportPreview> members in files.Values)
                    {
                        SortedSet<string> scopes = new SortedSet<string>(StringComparer.Ordinal);
                        foreach (NORMImportPreview p in members)
                        {
                            NORMHelper.RequireEntityAccess(p.Entity, connection, transaction);
                            scopes.Add(RejectionScope(p) + ";ScopeEvidence=" + RejectionScopeEvidence(p));
                        }
                        NORMImportPreview first = members[0];
                        string detail = "Phase=Acceptance;Outcome=Rejected;Reason=" + AuditField(reason, 400) +
                            ";Conflict=" + AuditField(Convert.ToString(error.Data["NORMConflict"], CultureInfo.InvariantCulture), 450) +
                            ";Batch=" + AuditField(BatchEvidence(previews), 64) +
                            ";Sources=" + previews.Count.ToString(CultureInfo.InvariantCulture) +
                            ";ScopeCount=" + scopes.Count.ToString(CultureInfo.InvariantCulture) +
                            ";SHA256=" + (first.Content == null ? "Unavailable" : NORMCrypto.Sha256(first.Content)) +
                            ";Profile=" + AuditField(first.ProfileVersion, 80) +
                            ";Coverage=" + (first.CoverageStart.HasValue ? first.CoverageStart.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "Unknown") +
                            "/" + (first.CoverageEnd.HasValue ? first.CoverageEnd.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "Unknown");
                        // Reserve the reason, conflict IDs and hash before optional
                        // metadata. ScopeCount makes a bounded scope list explicit.
                        string scopeText = String.Join("|", new List<string>(scopes).ToArray());
                        int scopeLimit = Math.Min(900, 2000 - detail.Length - 120);
                        detail += ";Scopes=" + AuditField(scopeText, scopeLimit);
                        detail += ";File=" + AuditField(Path.GetFileName(first.FileName ?? ""), Math.Min(260, 2000 - detail.Length - 6));
                        WriteAudit(connection, transaction, "IMPORT_REJECTED_SOURCE", "ImportAttempt", attempt, detail, actor);
                    }
                    transaction.Commit();
                }
                catch { transaction.Rollback(); throw; }
            }
        }
        catch { throw new InvalidDataException("Source rejected. Rejection audit evidence could not be saved; contact support. " + reason); }
    }

    private static List<NORMImportOutcome> ConfirmBatchCore(IList<NORMImportPreview> previews, string user, bool replaceExisting, ref bool accepted)
    {
        if (previews == null || previews.Count == 0)
        { throw new InvalidOperationException("Inspect the source files before confirming acceptance."); }
        previews = new List<NORMImportPreview>(previews);
        if (!NORMHelper.HasPrepareAccess() || !String.Equals(user, NORMHelper.CurrentUserId(), StringComparison.Ordinal))
        { throw new UnauthorizedAccessException("The authenticated user does not have authority to confirm this batch."); }
        // Check all acknowledgements before consuming any member, so an unchecked
        // replacement can be corrected without partially consuming the batch.
        foreach (NORMImportPreview preview in previews)
        {
            if (preview == null) { throw new InvalidOperationException("An inspected population is missing. Inspect again."); }
            lock (preview)
            {
                if (DateTime.UtcNow >= preview.ExpiresUtc)
                { throw new RoutineConfirmationException("This preview is invalid, expired or already consumed. Inspect the files again."); }
                if (!String.Equals(preview.Owner, user, StringComparison.Ordinal) || !preview.CanAccept)
                { throw new InvalidOperationException("This preview is invalid, expired or already consumed. Inspect the files again."); }
                if (preview.HasReplacement && !replaceExisting)
                { throw new RoutineConfirmationException("Explicit replacement confirmation is required."); }
                VerifyRetainedPreview(preview);
            }
        }
        VerifyBatchMembership(previews);
        foreach (NORMImportPreview preview in previews)
        {
            lock (preview)
            {
                if (DateTime.UtcNow >= preview.ExpiresUtc)
                { throw new RoutineConfirmationException("This preview is invalid, expired or already consumed. Inspect again."); }
                if (!preview.CanAccept) { throw new InvalidOperationException("This preview is invalid, expired or already consumed. Inspect again."); }
                preview.Consumed = true;
            }
        }
        // Reparse each entire original once, including populations omitted by a
        // caller. Fresh evidence, not caller-owned rows, supplies persistence.
        List<NORMImportPreview> validated = ReinspectBatchFiles(previews, user);
        ValidateBatchCore(previews);
        List<NORMImportSource> sources = new List<NORMImportSource>();
        foreach (NORMImportPreview preview in validated)
        { sources.Add(BuildSource(preview.Content, preview.FileName, preview.SourceType, ParsedPopulation(preview), true)); }

        List<int> order = new List<int>();
        for (int i = 0; i < previews.Count; i++) { order.Add(i); }
        order.Sort(delegate(int a, int b) { return StringComparer.OrdinalIgnoreCase.Compare(SourceIdentity(previews[a].Population), SourceIdentity(previews[b].Population)); });
        SortedDictionary<int, string> releases = new SortedDictionary<int, string>();
        foreach (NORMImportPreview preview in previews) { releases[preview.ReleaseId] = preview.ConfigurationFingerprint; }
        int[] importIds = new int[previews.Count];
        string entity = previews[0].Entity;
        string batchReference = Guid.NewGuid().ToString("N");

        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            try
            {
                if (!String.Equals(user, NORMHelper.CurrentUserId(), StringComparison.Ordinal))
                { throw new UnauthorizedAccessException("The authenticated batch owner changed. Inspect again."); }
                NORMHelper.RequireEntityAccess(entity, connection, transaction);
                Dictionary<int, int> currentYears = new Dictionary<int, int>();
                foreach (KeyValuePair<int, string> release in releases)
                {
                    DataTable permitted, mappings;
                    int currentYear;
                    if (PreviewConfiguration(release.Key, connection, transaction, out permitted, out mappings, out currentYear) != release.Value)
                    { throw new InvalidOperationException("Configuration changed before acceptance. Inspect again."); }
                    currentYears.Add(release.Key, currentYear);
                }
                ValidateBatchCore(previews);
                VerifyBatchMembership(previews);
                HashSet<byte[]> checkedContent = new HashSet<byte[]>();
                foreach (int i in order)
                {
                    NORMImportPreview original = previews[i];
                    if (original.Owner != user) { throw new UnauthorizedAccessException("This preview is invalid or expired. Inspect again."); }
                    if (DateTime.UtcNow >= original.ExpiresUtc)
                    { throw new RoutineConfirmationException("This preview is invalid or expired. Inspect again."); }
                    VerifyRetainedPreview(original);
                    if (checkedContent.Add(original.Content) && NORMCrypto.Sha256(original.Content) != original.FileHash)
                    { throw new InvalidDataException("The inspected source bytes changed. Inspect again."); }
                    ValidateCoverage(sources[i].Parsed, currentYears[original.ReleaseId]);
                    NORMSourceScope scope = ExpectedPredecessor(original);
                    NORMSourceScope current = FindCurrentSource(original, scope.Company, scope.Ledger, scope.ReportingStream, connection, transaction);
                    if (current.ImportId != scope.ImportId)
                    {
                        InvalidOperationException conflict = new InvalidOperationException("The accepted source changed after inspection. Inspect the batch again.");
                        conflict.Data["NORMConflict"] = "SOURCE_PREDECESSOR_CHANGED;ExpectedImport=" + scope.ImportId.ToString(CultureInfo.InvariantCulture) +
                            ";ObservedImport=" + current.ImportId.ToString(CultureInfo.InvariantCulture) + ";" + RejectionScope(original);
                        throw conflict;
                    }
                }
                foreach (int i in order)
                {
                    NORMImportSource source = sources[i];
                    NORMImportPreview preview = validated[i];
                    NORMImportFileContext file = preview.SourceFile;
                    int importId = PersistImport(connection, transaction, preview.ReleaseId, preview.FinancialYear, preview.Entity,
                        source.SourceType, source.FileName, source.FileHash, source.Parsed, new[] { source }, user,
                        "Confirmed " + preview.Layout + "; company=" + preview.Companies + ";ledger=" + preview.Ledgers +
                        ";stream=" + preview.ReportingStreams + ";coverage=" + CoverageLabel(source.Parsed) + ";periods=" + PeriodLabel(source.Parsed) + ";unmapped=" + preview.UnmappedAccounts + ";units=AUD." +
                        ";entity=" + preview.Entity + ";financialYear=" + preview.FinancialYear + ";release=" + preview.ReleaseId +
                        ";batch=" + batchReference + ";fileReference=" + previews[i].SourceFile.Token + ";filePopulations=" + file.PopulationCount +
                        ";fileRecords=" + file.RecordCount + ";fileDetail=" + file.DetailRowCount + ";metadata=" + file.MetadataRowCount +
                        ";controls=" + file.ControlRowCount + ";blank=" + file.BlankRowCount + ";populationAccepted=" + preview.RowCount + ";populationInvalid=0",
                        source.Content.LongLength, preview.Debit, preview.Credit, preview.Net);
                    importIds[i] = importId;
                    foreach (NORMSourceScope scope in previews[i].Scopes.Values)
                    {
                        if (scope.ImportId == 0) { continue; }
                        WriteAudit(connection, transaction, "IMPORT_SOURCE_REPLACED", "Import", importId.ToString(CultureInfo.InvariantCulture),
                            "Prior import=" + scope.ImportId.ToString(CultureInfo.InvariantCulture) + "; entity=" + preview.Entity +
                            "; financial year=" + preview.FinancialYear + "; company=" + (scope.Company ?? "(legacy)") +
                            "; ledger=" + (scope.Ledger ?? "(absent)") + "; stream=" + scope.ReportingStream, user);
                    }
                }
                transaction.Commit();
                accepted = true;
            }
            catch { transaction.Rollback(); throw; }
        }

        List<NORMImportOutcome> outcomes = new List<NORMImportOutcome>();
        for (int i = 0; i < previews.Count; i++)
        {
            NORMImportPreview preview = validated[i];
            outcomes.Add(Calculate(importIds[i], preview.RowCount, preview.Debit, preview.Credit, preview.Net, user));
        }
        return outcomes;
    }

    private static void VerifyRetainedPreview(NORMImportPreview preview)
    {
        if (preview.SourceFile == null || preview.Population == null || preview.Content == null ||
            preview.ConfirmationFingerprint != PopulationEvidence(preview, true))
        { throw new InvalidOperationException("The retained source identity, rows or confirmation context changed. Inspect again."); }
    }

    private static NORMSourceScope ExpectedPredecessor(NORMImportPreview preview)
    {
        if (preview.Scopes.Count == 1)
        {
            foreach (NORMSourceScope scope in preview.SourceScopes)
            { if (SourceIdentity(scope) == SourceIdentity(preview.Population)) { return scope; } }
        }
        throw new InvalidOperationException("The source predecessor context is incomplete. Inspect again.");
    }

    private static List<NORMImportPreview> ReinspectBatchFiles(IList<NORMImportPreview> previews, string user)
    {
        Dictionary<string, List<NORMImportPreview>> files = new Dictionary<string, List<NORMImportPreview>>(StringComparer.Ordinal);
        foreach (NORMImportPreview preview in previews)
        {
            List<NORMImportPreview> members;
            if (!files.TryGetValue(preview.SourceFile.Token, out members))
            { members = new List<NORMImportPreview>(); files.Add(preview.SourceFile.Token, members); }
            members.Add(preview);
        }
        Dictionary<NORMImportPreview, NORMImportPreview> matches = new Dictionary<NORMImportPreview, NORMImportPreview>();
        foreach (List<NORMImportPreview> members in files.Values)
        {
            NORMImportPreview first = members[0];
            if (NORMCrypto.Sha256(first.Content) != first.FileHash)
            { throw new InvalidDataException("The inspected source bytes changed. Inspect again."); }
            NORMParsedImport parsed;
            List<NORMImportPreview> fresh = InspectPopulations(first.Content, first.FileName, first.ReleaseId, user, false, false, out parsed);
            if (fresh.Count != members.Count || FilePopulationFingerprint(fresh) != first.SourceFile.PopulationFingerprint)
            { throw new InvalidOperationException("The complete original file population set or validation changed. Inspect every population again."); }
            foreach (NORMImportPreview member in fresh)
            { if (!member.CanAccept) { throw new InvalidOperationException("A source population is invalid. No population from the batch can be accepted."); } }
            Dictionary<string, NORMImportPreview> byIdentity = new Dictionary<string, NORMImportPreview>(StringComparer.OrdinalIgnoreCase);
            foreach (NORMImportPreview member in fresh) { byIdentity.Add(SourceIdentity(member.Population), member); }
            foreach (NORMImportPreview member in members)
            {
                NORMImportPreview match;
                if (member.ReleaseId != first.ReleaseId || member.FileHash != first.FileHash ||
                    (!Object.ReferenceEquals(member.Content, first.Content) && NORMCrypto.Sha256(member.Content) != first.FileHash) ||
                    !byIdentity.TryGetValue(SourceIdentity(member.Population), out match) || PopulationEvidence(member, false) != PopulationEvidence(match, false) ||
                    matches.ContainsKey(member))
                { throw new InvalidOperationException("The configuration, source identity or validation changed. Inspect again."); }
                matches.Add(member, match);
            }
        }
        List<NORMImportPreview> validated = new List<NORMImportPreview>();
        foreach (NORMImportPreview preview in previews) { validated.Add(matches[preview]); }
        return validated;
    }

    private static NORMParsedImport ParsedPopulation(NORMImportPreview preview)
    {
        return new NORMParsedImport { Rows = new List<NORMTrialBalanceRow>(preview.SourceRows), ProfileVersion = preview.ProfileVersion,
            FinancialYear = preview.FinancialYear, PeriodStart = preview.PeriodStart, PeriodEnd = preview.PeriodEnd,
            CoverageStart = preview.CoverageStart, CoverageEnd = preview.CoverageEnd, SourceFile = preview.SourceFile };
    }

    private static string ScopeKey(NORMTrialBalanceRow row)
    {
        string company = row.SourceCompanyCode;
        string ledger = row.SourceLedger ?? "";
        return company.Length + ":" + company + ledger.Length + ":" + ledger + row.ReportingStream;
    }

    private static void ResolvePopulation(NORMTrialBalanceRow row, DataTable permitted)
    {
        string resolvedStream = null;
        foreach (DataRow population in permitted.Rows)
        {
            string stream = NORMHelper.Str(population, "ReportingStream");
            if (!String.Equals(row.SourceCompanyCode, NORMHelper.Str(population, "SourceCompanyCode"), StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(row.SourceLedger, NORMHelper.Str(population, "SourceLedger"), StringComparison.OrdinalIgnoreCase) ||
                (row.ReportingStream != null && !String.Equals(row.ReportingStream, stream, StringComparison.OrdinalIgnoreCase)))
            { continue; }
            if (resolvedStream != null)
            { throw new InvalidDataException("The source population is ambiguous. Supply reporting stream evidence."); }
            resolvedStream = stream;
        }
        if (resolvedStream == null)
        { throw new InvalidDataException("The detected company/ledger pair and reporting stream are not authorised."); }
        row.ReportingStream = resolvedStream;
    }

    private static void ResolveTextPopulation(NORMTrialBalanceRow row, DataTable permitted)
    {
        DataRow match = null;
        foreach (DataRow population in permitted.Rows)
        {
            if (row.SourceCompanyCode != null && !String.Equals(row.SourceCompanyCode, NORMHelper.Str(population, "SourceCompanyCode"), StringComparison.OrdinalIgnoreCase)) { continue; }
            if (row.SourceLedger != null && !String.Equals(row.SourceLedger, NORMHelper.Str(population, "SourceLedger"), StringComparison.OrdinalIgnoreCase)) { continue; }
            if (row.ReportingStream != null && !String.Equals(row.ReportingStream, NORMHelper.Str(population, "ReportingStream"), StringComparison.OrdinalIgnoreCase)) { continue; }
            if (match != null) { throw new InvalidDataException("The source company has ambiguous configured ledger/reporting stream populations."); }
            match = population;
        }
        if (match == null) { throw new InvalidDataException("The source company is not authorised by the active agency configuration."); }
        row.SourceCompanyCode = NORMHelper.Str(match, "SourceCompanyCode");
        row.SourceLedger = NORMHelper.Str(match, "SourceLedger");
        row.ReportingStream = NORMHelper.Str(match, "ReportingStream");
    }

    public static IList<NORMSourceScope> MissingRequiredPopulations(int releaseId)
    {
        DataRow release = LoadApprovedRelease(releaseId);
        NORMImportPreview scope = new NORMImportPreview {
            Entity = NORMHelper.Str(release, "EntityCode"), FinancialYear = NORMHelper.Int(release, "FinancialYear")
        };
        DataTable required = NORMHelper.Query(
            "SELECT * FROM dbo.tblNORM_EntityLedger WHERE ConfigurationReleaseId=@release AND IsRequired=1 AND IsDeactivated=0",
            NORMHelper.P("@release", releaseId));
        List<NORMSourceScope> missing = new List<NORMSourceScope>();
        foreach (DataRow population in required.Rows)
        {
            NORMSourceScope current = FindCurrentSource(scope, NORMHelper.Str(population, "SourceCompanyCode"),
                NORMHelper.Str(population, "SourceLedger"), NORMHelper.Str(population, "ReportingStream"), null, null);
            if (current.ImportId == 0) { missing.Add(current); }
        }
        return missing;
    }

    public static int? ConfiguredCurrentYear(string entity)
    {
        NORMHelper.RequireEntityAccess(entity);
        return ReadConfiguredCurrentYear(entity, null, null);
    }

    private static int ReadConfiguredCurrentYear(string entity, OleDbConnection connection, OleDbTransaction transaction)
    {
        string sql = "SELECT CurrentFinancialYear FROM dbo.tblNORM_YearSetup WITH (HOLDLOCK) " +
            "WHERE EntityCode=@entity AND IsCurrent=1 AND IsDeactivated=0";
        OleDbParameter parameter = NORMHelper.P("@entity", entity);
        DataTable years = connection == null ? NORMHelper.Query(sql, parameter) :
            NORMHelper.Query(connection, transaction, sql, parameter);
        if (years.Rows.Count != 1)
        { throw new InvalidOperationException("Save an explicit current financial year for this entity in Year Setup before importing."); }
        return NORMHelper.Int(years.Rows[0], "CurrentFinancialYear");
    }

    private static void ValidateCoverage(NORMParsedImport parsed, int currentYear)
    {
        if (!parsed.CoverageStart.HasValue || !parsed.CoverageEnd.HasValue)
        { throw new InvalidDataException("The source does not establish exact reporting-period boundaries. Numeric period labels alone cannot establish coverage."); }
        DateTime start = parsed.CoverageStart.Value, end = parsed.CoverageEnd.Value;
        if (start > end || start < new DateTime(parsed.FinancialYear - 1, 7, 1) ||
            end > new DateTime(parsed.FinancialYear, 6, 30))
        { throw new InvalidDataException("The source reporting-period boundaries must be ordered and within the identified financial year."); }
        if (parsed.FinancialYear != currentYear && parsed.IsFullYear != true)
        { throw new InvalidDataException("A source outside the configured current financial year must cover the complete financial year."); }
    }

    private static string CoverageLabel(NORMParsedImport parsed)
    {
        if (!parsed.CoverageStart.HasValue || !parsed.CoverageEnd.HasValue) { return "Unknown"; }
        return parsed.CoverageStart.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/" +
            parsed.CoverageEnd.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
            (parsed.IsFullYear == true ? ";Full" : ";Partial");
    }

    private static NORMSourceScope FindCurrentSource(NORMImportPreview preview, string company, string ledger, string stream,
        OleDbConnection connection, OleDbTransaction transaction)
    {
        // Accepted imports are immutable. The newest original import supplying this exact
        // scope is current. Recalculation copies and test breaks never become source versions.
        string hints = transaction == null ? "" : " WITH (UPDLOCK,HOLDLOCK)";
        string sql = "SELECT TOP 1 i.ImportId,i.SourceFileName,i.SourceFileHash,i.ImportedUtc,COUNT(*) AS ScopeRows " +
            "FROM dbo.tblNORM_Import i" + hints + " INNER JOIN dbo.tblNORM_TrialBalanceRow tb" + hints + " ON tb.ImportId=i.ImportId " +
            "WHERE i.EntityCode=@entity AND i.FinancialYear=@fy AND i.IsTestBreak=0 AND i.IsDeactivated=0 " +
            "AND i.ParentImportId IS NULL AND i.StatusCode IN ('Imported','Calculated') AND tb.IsDeactivated=0 " +
            "AND " + (ledger == null ? "tb.SourceLedger IS NULL " : "tb.SourceLedger=@ledger ") +
            "AND tb.SourceCompanyCode=@company AND tb.ReportingStream=@stream " +
            "GROUP BY i.ImportId,i.SourceFileName,i.SourceFileHash,i.ImportedUtc ORDER BY i.ImportId DESC";
        List<OleDbParameter> parameters = new List<OleDbParameter> {
            NORMHelper.P("@entity", preview.Entity), NORMHelper.P("@fy", preview.FinancialYear)
        };
        if (ledger != null) { parameters.Add(NORMHelper.P("@ledger", ledger)); }
        parameters.Add(NORMHelper.P("@company", company));
        parameters.Add(NORMHelper.P("@stream", stream));
        DataTable current = connection == null ? NORMHelper.Query(sql, parameters.ToArray()) :
            NORMHelper.Query(connection, transaction, sql, parameters.ToArray());
        NORMSourceScope scope = new NORMSourceScope {
            Entity = preview.Entity, FinancialYear = preview.FinancialYear,
            Company = company, Ledger = ledger, ReportingStream = stream
        };
        if (current.Rows.Count == 1)
        {
            DataRow row = current.Rows[0];
            scope.ImportId = NORMHelper.Int(row, "ImportId");
            scope.SourceFileName = NORMHelper.Str(row, "SourceFileName");
            scope.ImportedUtc = (DateTime)row["ImportedUtc"];
        }
        return scope;
    }

    private static string PreviewConfiguration(int releaseId, OleDbConnection connection, OleDbTransaction transaction,
        out DataTable permitted, out DataTable mappings, out int currentYear)
    {
        DataTable release = PreviewQuery(connection, transaction,
            "SELECT * FROM dbo.tblNORM_ConfigurationRelease WITH (HOLDLOCK) WHERE ConfigurationReleaseId=@release AND StatusCode='Approved' AND IsDeactivated=0", releaseId);
        if (release.Rows.Count != 1) { throw new InvalidOperationException("The approved configuration is no longer available."); }
        string ledgerSql = "SELECT * FROM dbo.tblNORM_EntityLedger WITH (HOLDLOCK) WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 ORDER BY EntityLedgerId";
        OleDbParameter[] args = { NORMHelper.P("@release", releaseId) };
        permitted = connection == null ? NORMHelper.Query(ledgerSql, args) : NORMHelper.Query(connection, transaction, ledgerSql, args);
        mappings = PreviewQuery(connection, transaction, "SELECT * FROM dbo.tblNORM_AccountMap WITH (HOLDLOCK) WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 ORDER BY AccountMapId", releaseId);
        DataTable lines = PreviewQuery(connection, transaction, "SELECT * FROM dbo.tblNORM_StatementLine WITH (HOLDLOCK) WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 ORDER BY StatementLineId", releaseId);
        string entity = NORMHelper.Str(release.Rows[0], "EntityCode");
        currentYear = ReadConfiguredCurrentYear(entity, connection, transaction);
        StringBuilder snapshot = new StringBuilder();
        snapshot.Append(currentYear).Append(';');
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
        ValidateContent(content, "Defence trial balance");
        DataTable permitted = NORMHelper.Query(
            "SELECT SourceCompanyCode,SourceLedger,ReportingStream FROM dbo.tblNORM_EntityLedger " +
            "WHERE ConfigurationReleaseId=@release AND IsDeactivated=0",
            NORMHelper.P("@release", configurationReleaseId));
        List<string> companies = new List<string>();
        foreach (DataRow population in permitted.Rows)
        { companies.Add(NORMHelper.Str(population, "SourceCompanyCode")); }
        NORMParsedImport parsed = ErpTrialBalanceImporter.ParseForInspection(content, companies, year);
        foreach (NORMTrialBalanceRow row in parsed.Rows)
        {
            if (row.SourceCompanyCode == null) { throw new InvalidDataException("A Defence ERP source is required."); }
            ResolvePopulation(row, permitted);
            row.GlAccount = ErpTrialBalanceImporter.ExtractGl(row.GlAccount);
        }
        string hash = NORMCrypto.Sha256(content);
        NORMImportSource source = BuildSource(content, fileName, "ERP", parsed, true);
        return CommitImport(release, "ERP", source.FileName, hash, parsed,
            new List<NORMImportSource> { source }, importedBy,
            "Defence ERP; Company=" + parsed.Rows[0].SourceCompanyCode + ";Ledger=" + parsed.Rows[0].SourceLedger +
            ";Stream=" + parsed.Rows[0].ReportingStream + ";Periods=" + PeriodLabel(parsed) + ";Units=AUD;AccountKey=DCOA suffix; full identifiers retained in source evidence.");
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
                NORMHelper.RequireEntityAccess(entityCode, connection, transaction);
                HashSet<string> populations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                NORMImportPreview scope = new NORMImportPreview { Entity = entityCode, FinancialYear = financialYear };
                foreach (NORMTrialBalanceRow row in statementInput.Rows)
                {
                    if (row.SourceCompanyCode == null || !populations.Add(ScopeKey(row))) { continue; }
                    if (FindCurrentSource(scope, row.SourceCompanyCode, row.SourceLedger, row.ReportingStream, connection, transaction).ImportId != 0)
                    { throw new InvalidOperationException("Confirmed replacement is not available through this entry point. Use the import preview."); }
                }
                importId = PersistImport(connection, transaction, configurationReleaseId, financialYear, entityCode,
                    sourceType, sourceFileName, sourceHash, statementInput, sources, importedBy, auditDetail, totalBytes, debit, credit, net);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        return Calculate(importId, statementInput.Rows.Count, debit, credit, net, importedBy);
    }

    private static int PersistImport(OleDbConnection connection, OleDbTransaction transaction,
        int configurationReleaseId, int financialYear, string entityCode, string sourceType, string sourceFileName,
        string sourceHash, NORMParsedImport statementInput, IList<NORMImportSource> sources, string importedBy,
        string auditDetail, long totalBytes, decimal debit, decimal credit, decimal net)
    {
        // D3 preserves the historical FY2025 transition contract.
        // Governed source acceptance requires exact coverage.
        int? coverageYear = null;
        if (sourceType != "ROMAN+ERP")
        {
            coverageYear = ReadConfiguredCurrentYear(entityCode, connection, transaction);
            foreach (NORMImportSource source in sources) { ValidateCoverage(source.Parsed, coverageYear.Value); }
        }

        int importId = NORMHelper.InsertId(connection, transaction,
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
                "(ImportId,SourceType,SourceFileName,SourceFileHash,SourceFileBytes,PeriodStart,PeriodEnd,CoverageStart,CoverageEnd,IsFullYear,[RowCount],IsStatementInput,FileContent) " +
                "VALUES (@import,@type,@file,@hash,@bytes,@periodStart,@periodEnd,@coverageStart,@coverageEnd,@isFullYear,@rows,@input,@content)",
                NORMHelper.P("@import", importId), NORMHelper.P("@type", source.SourceType),
                NORMHelper.P("@file", source.FileName), NORMHelper.P("@hash", source.FileHash),
                NORMHelper.P("@bytes", source.Content.LongLength),
                NORMHelper.P("@periodStart", source.Parsed.PeriodStart),
                NORMHelper.P("@periodEnd", source.Parsed.PeriodEnd),
                NORMHelper.P("@coverageStart", source.Parsed.CoverageStart),
                NORMHelper.P("@coverageEnd", source.Parsed.CoverageEnd),
                NORMHelper.P("@isFullYear", source.Parsed.IsFullYear),
                NORMHelper.P("@rows", source.Parsed.Rows.Count),
                NORMHelper.P("@input", source.IsStatementInput), contentParameter);
            WriteAudit(connection, transaction, "IMPORT_COVERAGE_VALIDATION", "Import",
                importId.ToString(CultureInfo.InvariantCulture),
                "Source=" + source.SourceType + ";SHA256=" + source.FileHash +
                ";FinancialYear=" + financialYear.ToString(CultureInfo.InvariantCulture) +
                ";Coverage=" + CoverageLabel(source.Parsed) +
                ";ConfiguredCurrentYear=" + (coverageYear.HasValue ? coverageYear.Value.ToString(CultureInfo.InvariantCulture) : "Historical transition") +
                ";Result=" + (coverageYear.HasValue ? "Pass" : "Not evaluated"), importedBy);
            if (source.Parsed.ProfileVersion != null)
            {
                WriteAudit(connection, transaction, "IMPORT_SOURCE_PROFILE", "Import", importId.ToString(CultureInfo.InvariantCulture),
                    "Profile=" + source.Parsed.ProfileVersion + ";SHA256=" + source.FileHash +
                    ";Currency=AUD;Units=Dollars;Multiplier=1;BalanceSign=DebitPositive;CreditMovement=SignedNegative;Rows=" + source.Parsed.Rows.Count,
                    importedBy);
            }
        }

        for (int i = 0; i < statementInput.Rows.Count; i++)
        {
            NORMTrialBalanceRow row = statementInput.Rows[i];
            NORMHelper.Exec(connection, transaction,
                "INSERT dbo.tblNORM_TrialBalanceRow " +
                "(ImportId,SourceRowNo,SourceCompanyCode,SourceLedger,ReportingStream,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement," +
                " AccumBalance,RowHash,IsSynthetic) " +
                "VALUES (@import,@row,@company,@ledger,@stream,@gl,@text,@opening,@debit,@credit,@balance,@hash,0)",
                NORMHelper.P("@import", importId),
                NORMHelper.P("@row", row.SourceRowNo),
                NORMHelper.P("@company", (object)row.SourceCompanyCode ?? DBNull.Value),
                NORMHelper.P("@ledger", row.SourceLedger),
                NORMHelper.P("@stream", row.ReportingStream),
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
        return importId;
    }

    private static NORMImportOutcome Calculate(int importId, int rowCount, decimal debit, decimal credit, decimal net, string importedBy)
    {
        NORMImportOutcome outcome = new NORMImportOutcome();
        outcome.ImportId = importId;
        outcome.RowCount = rowCount;
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
        NORMHelper.RequireEntityAccess(NORMHelper.Str(parent, "EntityCode"));
        int importId;

        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            try
            {
                NORMHelper.RequireEntityAccess(NORMHelper.Str(parent, "EntityCode"), connection, transaction);
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
                    "(ImportId,SourceType,SourceFileName,SourceFileHash,SourceFileBytes,PeriodStart,PeriodEnd,CoverageStart,CoverageEnd,IsFullYear,[RowCount],IsStatementInput,FileContent) " +
                    "SELECT @newImport,SourceType,SourceFileName,SourceFileHash,SourceFileBytes,PeriodStart,PeriodEnd,CoverageStart,CoverageEnd,IsFullYear,[RowCount],IsStatementInput,FileContent " +
                    "FROM dbo.tblNORM_ImportFile WHERE ImportId = @parent",
                    NORMHelper.P("@newImport", importId), NORMHelper.P("@parent", parentImportId));
                NORMHelper.Exec(connection, transaction,
                    "INSERT dbo.tblNORM_TrialBalanceRow " +
                    "(ImportId,SourceRowNo,SourceCompanyCode,SourceLedger,ReportingStream,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement,AccumBalance,RowHash,IsSynthetic) " +
                    "SELECT @newImport,SourceRowNo,SourceCompanyCode,SourceLedger,ReportingStream,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement,AccumBalance,RowHash,IsSynthetic " +
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
        NORMHelper.RequireEntityAccess(NORMHelper.Str(table.Rows[0], "EntityCode"));
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
