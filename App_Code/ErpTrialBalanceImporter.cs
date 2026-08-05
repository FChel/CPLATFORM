using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;

/// <summary>Strict parser for the ERP trial-balance workbook used by DFG.</summary>
public static class ErpTrialBalanceImporter
{
    public static NORMParsedImport Parse(byte[] content, IList<string> allowedLedgers, int expectedFinancialYear)
    {
        NORMParsedImport parsed = new NORMParsedImport();
        using (MemoryStream stream = new MemoryStream(content, false))
        using (ExcelPackage package = new ExcelPackage(stream))
        {
            if (package.Workbook.Worksheets.Count == 0)
            {
                throw new InvalidDataException("The trial balance workbook does not contain a worksheet.");
            }
            ExcelWorksheet sheet = package.Workbook.Worksheets[1];
            if (sheet.Dimension == null) { throw new InvalidDataException("The trial balance worksheet is empty."); }

            int headerRow = FindHeaderRow(sheet);
            if (headerRow < 1)
            {
                throw new InvalidDataException("The workbook does not contain the expected trial-balance columns.");
            }
            ValidateFinancialYear(sheet, headerRow, expectedFinancialYear);
            ReadPeriodRange(sheet, headerRow, expectedFinancialYear, parsed);

            for (int rowNo = headerRow + 1; rowNo <= sheet.Dimension.End.Row; rowNo++)
            {
                string ledger = Text(sheet.Cells[rowNo, 1].Value);
                if (!IsCompanyCode(ledger)) { continue; }
                if (allowedLedgers != null && allowedLedgers.Count > 0 && !Contains(allowedLedgers, ledger)) { continue; }

                string gl = ExtractGl(Text(sheet.Cells[rowNo, 3].Value));
                if (String.IsNullOrWhiteSpace(gl))
                {
                    throw new InvalidDataException("Row " + rowNo.ToString() + " has no G/L account.");
                }

                decimal ending;
                if (!TryDecimal(sheet.Cells[rowNo, 8].Value, out ending))
                {
                    throw new InvalidDataException("Row " + rowNo.ToString() + " has an invalid ending balance.");
                }

                decimal opening;
                decimal debitMovement;
                decimal creditMovement;
                if (!TryDecimal(sheet.Cells[rowNo, 5].Value, out opening) ||
                    !TryDecimal(sheet.Cells[rowNo, 6].Value, out debitMovement) ||
                    !TryDecimal(sheet.Cells[rowNo, 7].Value, out creditMovement))
                {
                    throw new InvalidDataException("Row " + rowNo.ToString() +
                        " does not contain valid opening, debit and credit movement amounts.");
                }
                decimal movementDifference = opening + debitMovement + creditMovement - ending;
                if (Math.Abs(movementDifference) > 0.01m)
                {
                    throw new InvalidDataException("Row " + rowNo.ToString() +
                        " does not reconcile: opening balance plus movements differs from ending balance by $" +
                        movementDifference.ToString("N2", CultureInfo.GetCultureInfo("en-AU")) + ".");
                }

                NORMTrialBalanceRow item = new NORMTrialBalanceRow();
                item.SourceRowNo = rowNo;
                item.SourceLedger = ledger;
                item.GlAccount = gl;
                item.GlText = Text(sheet.Cells[rowNo, 4].Value);
                item.OpeningBalance = opening;
                item.DebitMovement = debitMovement;
                item.CreditMovement = creditMovement;
                item.AccumBalance = ending;
                item.RowHash = NORMCrypto.Sha256(rowNo.ToString(CultureInfo.InvariantCulture) + "|" + ledger + "|" + gl + "|" +
                    (item.GlText ?? "") + "|" + opening.ToString("0.00", CultureInfo.InvariantCulture) + "|" +
                    debitMovement.ToString("0.00", CultureInfo.InvariantCulture) + "|" +
                    creditMovement.ToString("0.00", CultureInfo.InvariantCulture) + "|" +
                    ending.ToString("0.00", CultureInfo.InvariantCulture));
                parsed.Rows.Add(item);
            }
        }

        if (parsed.Rows.Count == 0)
        {
            throw new InvalidDataException("No rows matched the configured source ledgers for this reporting entity.");
        }
        return parsed;
    }

    private static void ReadPeriodRange(ExcelWorksheet sheet, int headerRow, int financialYear,
        NORMParsedImport parsed)
    {
        object fromValue = FindMetadataValue(sheet, headerRow, "Posting Date From");
        object toValue = FindMetadataValue(sheet, headerRow, "Posting Date To");
        DateTime from;
        DateTime to;
        if (!TryDate(fromValue, out from) || !TryDate(toValue, out to))
        {
            throw new InvalidDataException("The ERP trial balance does not contain valid posting-date boundaries.");
        }
        int periodStart = FiscalPeriod(from, financialYear);
        int periodEnd = FiscalPeriod(to, financialYear);
        if (periodStart == 0 || periodEnd == 0 || periodStart > periodEnd)
        {
            throw new InvalidDataException("The ERP posting-date range does not fall within FY" +
                financialYear.ToString() + ".");
        }
        parsed.FinancialYear = financialYear;
        parsed.PeriodStart = periodStart;
        parsed.PeriodEnd = periodEnd;
    }

    private static object FindMetadataValue(ExcelWorksheet sheet, int headerRow, string label)
    {
        object found = null;
        for (int row = 1; row < headerRow; row++)
        {
            if (!Same(Text(sheet.Cells[row, 1].Value), label)) { continue; }
            object value = sheet.Cells[row, 2].Value;
            if (found != null && !Same(Text(found), Text(value)))
            {
                throw new InvalidDataException("The ERP trial balance contains conflicting " + label + " values.");
            }
            found = value;
        }
        return found;
    }

    private static bool TryDate(object value, out DateTime result)
    {
        result = DateTime.MinValue;
        if (value == null) { return false; }
        if (value is DateTime) { result = ((DateTime)value).Date; return true; }
        if (value is double)
        {
            try { result = DateTime.FromOADate((double)value).Date; return true; }
            catch (ArgumentException) { return false; }
        }
        string text = Convert.ToString(value).Trim();
        return DateTime.TryParseExact(text, new string[] { "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    private static int FiscalPeriod(DateTime date, int financialYear)
    {
        if (date.Year == financialYear - 1 && date.Month >= 7) { return date.Month - 6; }
        if (date.Year == financialYear && date.Month <= 6) { return date.Month + 6; }
        return 0;
    }

    public static string ExtractGl(string raw)
    {
        if (raw == null) { return null; }
        int slash = raw.LastIndexOf('/');
        return (slash >= 0 && slash < raw.Length - 1 ? raw.Substring(slash + 1) : raw).Trim();
    }

    private static int FindHeaderRow(ExcelWorksheet sheet)
    {
        for (int row = 1; row <= sheet.Dimension.End.Row; row++)
        {
            if (Same(Text(sheet.Cells[row, 1].Value), "Company Code") &&
                Same(Text(sheet.Cells[row, 3].Value), "G/L Account") &&
                Same(Text(sheet.Cells[row, 8].Value), "Ending Balance in Company Code Currency"))
            {
                return row;
            }
        }
        return -1;
    }

    private static void ValidateFinancialYear(ExcelWorksheet sheet, int headerRow, int expectedFinancialYear)
    {
        bool found = false;
        for (int row = 1; row < headerRow; row++)
        {
            if (!Same(Text(sheet.Cells[row, 1].Value), "Fiscal Year")) { continue; }
            found = true;
            int detected;
            if (!Int32.TryParse(Text(sheet.Cells[row, 2].Value), out detected))
            {
                throw new InvalidDataException("The trial balance contains an invalid fiscal-year value.");
            }
            if (detected != expectedFinancialYear)
            {
                throw new InvalidDataException("The trial balance is for FY" + detected.ToString() +
                    " but the selected configuration is FY" + expectedFinancialYear.ToString() + ".");
            }
        }
        if (!found)
        {
            throw new InvalidDataException("The trial balance does not identify its fiscal year.");
        }
    }

    private static bool Same(string left, string right)
    {
        return String.Equals((left ?? "").Trim(), right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(IList<string> values, string value)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (String.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) { return true; }
        }
        return false;
    }

    private static bool IsCompanyCode(string value)
    {
        if (value == null || value.Length != 4) { return false; }
        for (int i = 0; i < value.Length; i++) { if (!Char.IsDigit(value[i])) { return false; } }
        return true;
    }

    private static string Text(object value)
    {
        return value == null ? null : Convert.ToString(value).Trim();
    }

    private static bool TryDecimal(object value, out decimal result)
    {
        result = 0m;
        if (value == null) { return false; }
        if (value is decimal) { result = (decimal)value; return true; }
        if (value is double || value is float || value is int || value is long)
        {
            result = Convert.ToDecimal(value); return true;
        }
        string text = Convert.ToString(value).Replace(",", "").Trim();
        return Decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out result);
    }
}
