using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// Imports a ROMAN trial balance (SAP transaction RFSSLD00, "G/L Account
/// Balances" layout). The dump is pipe-framed, fixed-ish width:
///   | {CompanyCode} {GLAccount} {Description up to 20} AUD {period columns...} |
/// The accumulated (year-to-date) balance is the LAST numeric token on the line.
/// A trailing minus denotes a credit, e.g. "35,840,570.00-".
/// Debit balances are stored positive, credits negative.
/// Only rows whose company code is one of the entity's source ledgers are kept.
/// </summary>
public class RomanTrialBalanceImporter
{
    // Captures: 1=company code, 2=GL account, 3=description, 4=remainder (period figures)
    private static readonly Regex LineRx =
        new Regex(@"^\|\s*(\d{4})\s+(\w+)\s+(.{1,20})\s+AUD\s+(.*)\|\s*$", RegexOptions.Compiled);
    private static readonly Regex NumRx =
        new Regex(@"[\d,]+\.\d{2}-?", RegexOptions.Compiled);

    public class TbRow
    {
        public string SourceLedger;
        public string GlAccount;
        public string GlText;
        public decimal AccumBalance;
    }

    /// <summary>Parse a file into rows, keeping only the supplied source ledgers.</summary>
    public static List<TbRow> Parse(string filePath, List<string> sourceLedgers)
    {
        List<TbRow> rows = new List<TbRow>();
        using (StreamReader sr = new StreamReader(filePath))
        {
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                Match m = LineRx.Match(line);
                if (!m.Success) { continue; }

                string cc = m.Groups[1].Value;
                if (sourceLedgers != null && sourceLedgers.Count > 0 && !sourceLedgers.Contains(cc))
                {
                    continue;
                }

                string tail = m.Groups[4].Value;
                MatchCollection nums = NumRx.Matches(tail);
                if (nums.Count == 0) { continue; }

                string token = nums[nums.Count - 1].Value;
                decimal val = ParseAccountingDecimal(token);

                TbRow r = new TbRow();
                r.SourceLedger = cc;
                r.GlAccount = m.Groups[2].Value.Trim();
                r.GlText = m.Groups[3].Value.Trim();
                r.AccumBalance = val;
                rows.Add(r);
            }
        }
        return rows;
    }

    /// <summary>"35,840,570.00-" => -35840570.00 (trailing minus = credit).</summary>
    public static decimal ParseAccountingDecimal(string token)
    {
        bool negative = token.EndsWith("-");
        string body = token.Replace("-", "").Replace(",", "");
        decimal d = decimal.Parse(body, NumberStyles.Number, CultureInfo.InvariantCulture);
        if (negative) { d = -d; }
        return d;
    }

    /// <summary>
    /// Full import: create the import header, persist trial-balance rows, set totals.
    /// Returns the new ImportId. Caller then runs StatementEngine.Run(importId).
    /// </summary>
    public static int Import(string filePath, int financialYear, string entityCode,
                             string mapVersion, string importedBy)
    {
        List<string> ledgers = LoadEntityLedgers(financialYear, entityCode);
        List<TbRow> rows = Parse(filePath, ledgers);

        decimal debit = 0m;
        decimal credit = 0m;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].AccumBalance >= 0) { debit += rows[i].AccumBalance; }
            else { credit += rows[i].AccumBalance; }
        }

        int importId = NORMHelper.ExecReturnId(
            "INSERT INTO dbo.tblNORM_Import " +
            "(FinancialYear,EntityCode,SourceLabel,SourceFileName,MapVersion,TotalDebit,TotalCredit,ImportedBy,ImportedUtc,StatusCode) " +
            "VALUES (?,?,?,?,?,?,?,?,?,?)",
            NORMHelper.P("FinancialYear", financialYear),
            NORMHelper.P("EntityCode", entityCode),
            NORMHelper.P("SourceLabel", "ROMAN"),
            NORMHelper.P("SourceFileName", Path.GetFileName(filePath)),
            NORMHelper.P("MapVersion", mapVersion),
            NORMHelper.P("TotalDebit", debit),
            NORMHelper.P("TotalCredit", credit),
            NORMHelper.P("ImportedBy", importedBy),
            NORMHelper.P("ImportedUtc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            NORMHelper.P("StatusCode", "Imported"));

        for (int i = 0; i < rows.Count; i++)
        {
            NORMHelper.Exec(
                "INSERT INTO dbo.tblNORM_TrialBalanceRow " +
                "(ImportId,SourceLedger,GlAccount,GlText,AccumBalance) VALUES (?,?,?,?,?)",
                NORMHelper.P("ImportId", importId),
                NORMHelper.P("SourceLedger", rows[i].SourceLedger),
                NORMHelper.P("GlAccount", rows[i].GlAccount),
                NORMHelper.P("GlText", rows[i].GlText),
                NORMHelper.P("AccumBalance", rows[i].AccumBalance));
        }
        return importId;
    }

    public static List<string> LoadEntityLedgers(int financialYear, string entityCode)
    {
        List<string> ledgers = new List<string>();
        DataTable dt = NORMHelper.Query(
            "SELECT SourceLedger FROM dbo.tblNORM_EntityLedger " +
            "WHERE FinancialYear = ? AND EntityCode = ? AND IsDeactivated = 0",
            NORMHelper.P("FinancialYear", financialYear),
            NORMHelper.P("EntityCode", entityCode));
        for (int i = 0; i < dt.Rows.Count; i++)
        {
            ledgers.Add(NORMHelper.Str(dt.Rows[i], "SourceLedger"));
        }
        return ledgers;
    }
}
