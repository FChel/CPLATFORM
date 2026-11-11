using System.Collections.Generic;

/// <summary>Canonical row produced by each supported trial-balance parser.</summary>
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
    public List<NORMTrialBalanceRow> Rows = new List<NORMTrialBalanceRow>();
    public List<string> Warnings = new List<string>();
    public int FinancialYear;
    public int PeriodStart;
    public int PeriodEnd;
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
    public string Entity { get; internal set; }
    public int FinancialYear { get; internal set; }
    public string Companies { get; internal set; }
    public string Ledgers { get; internal set; }
    public string ReportingStreams { get; internal set; }
    public int PeriodStart { get; internal set; }
    public int PeriodEnd { get; internal set; }
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
