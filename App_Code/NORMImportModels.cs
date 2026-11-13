using System.Collections.Generic;

/// <summary>Canonical row produced by each supported trial-balance parser.</summary>
[System.Serializable]
public class NORMTrialBalanceRow
{
    public int SourceRowNo;
    public string SourceCompanyCode;
    public string SourceLedger;
    public string ReportingStream;
    public string GlAccount;
    public string GlText;
    public decimal? OpeningBalance;
    public decimal? DebitMovement;
    public decimal? CreditMovement;
    public decimal AccumBalance;
    public string RowHash;
    public bool IsSynthetic;
}

public class NORMParsedImport
{
    public NORMImportFileContext SourceFile = new NORMImportFileContext();
    public string ProfileVersion;
    public List<NORMTrialBalanceRow> Rows = new List<NORMTrialBalanceRow>();
    public List<string> Warnings = new List<string>();
    public int FinancialYear;
    public int PeriodStart;
    public int PeriodEnd;
    public System.DateTime? CoverageStart;
    public System.DateTime? CoverageEnd;
    public bool? IsFullYear
    {
        get
        {
            if (!CoverageStart.HasValue || !CoverageEnd.HasValue) { return null; }
            return CoverageStart.Value == new System.DateTime(FinancialYear - 1, 7, 1) &&
                CoverageEnd.Value == new System.DateTime(FinancialYear, 6, 30);
        }
    }
}

[System.Serializable]
public sealed class NORMImportFileContext
{
    internal string PopulationFingerprint = null;
    public string Token { get; internal set; }
    public string FileName { get; internal set; }
    public string FileHash { get; internal set; }
    public int PopulationCount { get; internal set; }
    public string RecordUnit { get; internal set; }
    public int RecordCount { get; internal set; }
    public int DetailRowCount { get; internal set; }
    public int MetadataRowCount { get; internal set; }
    public int ControlRowCount { get; internal set; }
    public int BlankRowCount { get; internal set; }
    public bool RowsReconciled { get { return RecordCount == DetailRowCount + MetadataRowCount + ControlRowCount + BlankRowCount; } }
}

public class NORMImportOutcome
{
    public int ImportId;
    public int CalculationRunId;
    public string CalculationError;
    public int RowCount;
    public decimal TotalDebit;
    public decimal TotalCredit;
    public decimal NetBalance;
}

[System.Serializable]
public sealed class NORMSourceScope
{
    public string Entity { get; internal set; }
    public int FinancialYear { get; internal set; }
    public string Company { get; internal set; }
    public string Ledger { get; internal set; }
    public string ReportingStream { get; internal set; }
    public int ImportId { get; internal set; }
    public string SourceFileName { get; internal set; }
    public System.DateTime ImportedUtc { get; internal set; }
}

/// <summary>One short-lived server-session preview. Never serialised into ViewState.</summary>
[System.Serializable]
public sealed class NORMImportPreview
{
    internal byte[] Content = null;
    internal string ConfigurationFingerprint = null;
    internal string IdentityFingerprint = null;
    internal string ConfirmationFingerprint = null;
    internal string BatchFingerprint = null;
    internal bool Consumed = false;
    public string Token { get; internal set; }
    public string Owner { get; internal set; }
    public System.DateTime ExpiresUtc { get; internal set; }
    public int ReleaseId { get; internal set; }
    public string ConfigurationVersion { get; internal set; }
    public string FileName { get; internal set; }
    public string FileHash { get; internal set; }
    public string SourceType { get; internal set; }
    public string Layout { get; internal set; }
    public string ProfileVersion { get; internal set; }
    public string Entity { get; internal set; }
    public int FinancialYear { get; internal set; }
    public string Companies { get; internal set; }
    // One preview per identified population; several may share an original file.
    public NORMSourceScope Population { get; internal set; }
    public NORMImportFileContext SourceFile { get; internal set; }
    internal readonly List<NORMTrialBalanceRow> PopulationRows = new List<NORMTrialBalanceRow>();
    public IList<NORMTrialBalanceRow> SourceRows { get { return PopulationRows.AsReadOnly(); } }
    internal readonly HashSet<int> InvalidSourceRows = new HashSet<int>();
    public int InvalidRowCount { get { return InvalidSourceRows.Count; } }
    public int ValidatedRowCount { get { return RowCount - InvalidRowCount; } }
    public string ApprovedConfigurationFingerprint { get { return ConfigurationFingerprint; } }
    public string Ledgers { get; internal set; }
    public string ReportingStreams { get; internal set; }
    public int PeriodStart { get; internal set; }
    public int PeriodEnd { get; internal set; }
    public System.DateTime? CoverageStart { get; internal set; }
    public System.DateTime? CoverageEnd { get; internal set; }
    public bool? IsFullYear { get; internal set; }
    public int ConfiguredFinancialYear { get; internal set; }
    public int RowCount { get; internal set; }
    public int UnmappedAccounts { get; internal set; }
    public decimal Debit { get; internal set; }
    public decimal Credit { get; internal set; }
    public decimal Net { get; internal set; }
    public bool TotalsAvailable { get; internal set; }
    public bool PrecisionValid { get; internal set; }
    internal readonly Dictionary<string, NORMSourceScope> Scopes =
        new Dictionary<string, NORMSourceScope>(System.StringComparer.OrdinalIgnoreCase);
    public IEnumerable<NORMSourceScope> SourceScopes { get { return Scopes.Values; } }
    public bool HasReplacement
    {
        get
        {
            foreach (NORMSourceScope scope in Scopes.Values) { if (scope.ImportId != 0) { return true; } }
            return false;
        }
    }
    public List<string> Errors = new List<string>();
    public List<string> Warnings = new List<string>();
    public bool CanAccept { get { return !Consumed && Errors.Count == 0 && System.DateTime.UtcNow < ExpiresUtc; } }
}
