using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>Strict parser for historical ROMAN pipe-framed trial balances.</summary>
public static class RomanTrialBalanceImporter
{
    private static readonly Regex LinePattern = new Regex(
        @"^\|\s*(\d{4})\s+(\w+)\s+(.{1,20})\s+AUD\s+(.*)\|\s*$", RegexOptions.Compiled);
    private static readonly Regex NumberPattern = new Regex(@"[\d,]+\.\d{2}-?", RegexOptions.Compiled);
    private static readonly Regex PeriodPattern = new Regex(
        @"Carryforward\s+Periods\s+\d{2}-\d{2}\s+(\d{4})\s+Reporting\s+Periods\s+(\d{2})-(\d{2})\s+(\d{4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static NORMParsedImport Parse(byte[] content, IList<string> allowedLedgers, int expectedFinancialYear)
    {
        NORMParsedImport parsed = new NORMParsedImport();
        using (MemoryStream stream = new MemoryStream(content, false))
        using (StreamReader reader = new StreamReader(stream, true))
        {
            string line;
            int rowNo = 0;
            while ((line = reader.ReadLine()) != null)
            {
                rowNo++;
                Match periodMatch = PeriodPattern.Match(line);
                if (periodMatch.Success)
                {
                    int carryYear = Int32.Parse(periodMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    int periodStart = Int32.Parse(periodMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                    int periodEnd = Int32.Parse(periodMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                    int reportingYear = Int32.Parse(periodMatch.Groups[4].Value, CultureInfo.InvariantCulture);
                    if (carryYear != reportingYear || reportingYear != expectedFinancialYear)
                    {
                        throw new InvalidDataException("The ROMAN trial balance is for FY" + reportingYear.ToString() +
                            " but the selected configuration is FY" + expectedFinancialYear.ToString() + ".");
                    }
                    if (periodStart < 1 || periodEnd > 16 || periodStart > periodEnd)
                    {
                        throw new InvalidDataException("The ROMAN trial balance contains an invalid reporting-period range.");
                    }
                    if (parsed.FinancialYear != 0 &&
                        (parsed.FinancialYear != reportingYear || parsed.PeriodStart != periodStart || parsed.PeriodEnd != periodEnd))
                    {
                        throw new InvalidDataException("The ROMAN trial balance contains conflicting reporting-period headers.");
                    }
                    parsed.FinancialYear = reportingYear;
                    parsed.PeriodStart = periodStart;
                    parsed.PeriodEnd = periodEnd;
                }
                Match match = LinePattern.Match(line);
                if (!match.Success) { continue; }
                string ledger = match.Groups[1].Value;
                if (allowedLedgers != null && allowedLedgers.Count > 0 && !Contains(allowedLedgers, ledger)) { continue; }
                MatchCollection amounts = NumberPattern.Matches(match.Groups[4].Value);
                if (amounts.Count == 0)
                {
                    throw new InvalidDataException("ROMAN row " + rowNo.ToString() + " does not contain an accumulated balance.");
                }
                decimal accumulated = ParseAccountingDecimal(amounts[amounts.Count - 1].Value);
                NORMTrialBalanceRow item = new NORMTrialBalanceRow();
                item.SourceRowNo = rowNo;
                item.SourceLedger = ledger;
                item.GlAccount = match.Groups[2].Value.Trim();
                item.GlText = match.Groups[3].Value.Trim();
                item.AccumBalance = accumulated;
                item.RowHash = NORMCrypto.Sha256(ledger + "|" + item.GlAccount + "|" +
                    accumulated.ToString("0.00", CultureInfo.InvariantCulture));
                parsed.Rows.Add(item);
            }
        }
        if (parsed.Rows.Count == 0)
        {
            throw new InvalidDataException("No ROMAN rows matched the configured source ledgers.");
        }
        if (parsed.FinancialYear == 0)
        {
            throw new InvalidDataException("The ROMAN trial balance does not identify its financial year and reporting periods.");
        }
        return parsed;
    }

    public static decimal ParseAccountingDecimal(string token)
    {
        bool credit = token.EndsWith("-", StringComparison.Ordinal);
        decimal value = Decimal.Parse(token.Replace("-", "").Replace(",", ""),
            NumberStyles.Number, CultureInfo.InvariantCulture);
        return credit ? -value : value;
    }

    private static bool Contains(IList<string> values, string value)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (String.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) { return true; }
        }
        return false;
    }
}
