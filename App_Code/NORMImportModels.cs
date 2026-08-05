using System.Collections.Generic;

/// <summary>Canonical row produced by each supported trial-balance parser.</summary>
public class NORMTrialBalanceRow
{
    public int SourceRowNo;
    public string SourceLedger;
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
    public int RowCount;
    public decimal TotalDebit;
    public decimal TotalCredit;
    public decimal NetBalance;
}
