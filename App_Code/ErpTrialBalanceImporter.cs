using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;

/// <summary>Strict parser for the ERP trial-balance workbook used by DFG.</summary>
public static class ErpTrialBalanceImporter
{
    /// <summary>
    /// Content-detected parsing only. Defence rows retain actual company/ledger;
    /// legacy results preserve their historical SourceLedger contract. No persistence.
    /// </summary>
    public static NORMParsedImport ParseForInspection(byte[] content, IList<string> allowedCompanyCodes,
        int expectedFinancialYear)
    {
        if (content == null || content.Length == 0) { throw new InvalidDataException("Choose a non-empty ERP workbook."); }
        if (content.Length < 4 || content[0] != 0x50 || content[1] != 0x4b || content[2] != 0x03 || content[3] != 0x04)
        { throw new InvalidDataException("Unsupported ERP content: expected an XLSX workbook."); }
        using (MemoryStream stream = new MemoryStream(content, false))
        using (ExcelPackage package = new ExcelPackage(stream))
        {
            ExcelWorksheet selected = null;
            int header = -1;
            bool defence = false;
            bool defenceMetadata = false;
            int matches = 0;
            foreach (ExcelWorksheet sheet in package.Workbook.Worksheets)
            {
                if (sheet.Dimension == null) { continue; }
                bool marked = HasDefenceMetadata(sheet);
                defenceMetadata |= marked;
                for (int row = 1; row <= sheet.Dimension.End.Row; row++)
                {
                    if (!IsHeader(sheet, row)) { continue; }
                    bool isDefence = marked && IsDefenceHeader(sheet, row) &&
                        Same(Text(FindMetadataValue(sheet, row, "Query Name")), "2CCFITRIALBALQ0001 / Trial Balance") &&
                        Same(Text(FindMetadataValue(sheet, row, "InfoProvider Name")), "2CIFIGLBALCUBE / G/L Account Balance - Cube") &&
                        FindMetadataValue(sheet, row, "Funds of Flow Ledger Fiscal Year") != null;
                    bool isLegacy = !marked && FindMetadataValue(sheet, row, "Fiscal Year") != null &&
                        FindMetadataValue(sheet, row, "Posting Date From") != null &&
                        FindMetadataValue(sheet, row, "Posting Date To") != null;
                    if (!isDefence && !isLegacy) { continue; }
                    selected = sheet; header = row; defence = isDefence; matches++;
                }
            }
            if (matches > 1) { throw new InvalidDataException("Ambiguous ERP workbook: more than one supported layout or data header."); }
            if (matches == 0 || (defenceMetadata && !defence))
            {
                throw new InvalidDataException("Unsupported or malformed ERP layout; no unambiguous supported report signature.");
            }
            if (defence) { return ParseDefence(selected, header, allowedCompanyCodes, expectedFinancialYear); }
            // The legacy parser reads worksheet one. Do not detect another sheet and
            // then accidentally parse a different population through that entry point.
            if (selected != package.Workbook.Worksheets[1])
            {
                throw new InvalidDataException("The legacy ERP data must be on the first worksheet.");
            }
        }
        NORMParsedImport legacy = Parse(content, allowedCompanyCodes, expectedFinancialYear);
        legacy.Warnings.Add("Legacy ERP inspection preserves the historical company-in-SourceLedger contract; actual ledger identity is not established.");
        return legacy;
    }

    private static bool HasDefenceMetadata(ExcelWorksheet sheet)
    {
        if (sheet.Dimension == null) { return false; }
        for (int row = 1; row <= sheet.Dimension.End.Row; row++)
        {
            string label = Text(sheet.Cells[row, 1].Value);
            string value = Text(sheet.Cells[row, 2].Value) ?? "";
            if (Same(label, "Funds of Flow Ledger Fiscal Year") ||
                (Same(label, "Query Name") && value.StartsWith("2CCFITRIALBALQ0001", StringComparison.OrdinalIgnoreCase))) { return true; }
        }
        return false;
    }

    private static bool IsHeader(ExcelWorksheet sheet, int row)
    {
        return Same(Text(sheet.Cells[row, 1].Value), "Company Code") &&
            Same(Text(sheet.Cells[row, 3].Value), "G/L Account") &&
            Same(Text(sheet.Cells[row, 8].Value), "Ending Balance in Company Code Currency");
    }

    private static bool IsDefenceHeader(ExcelWorksheet sheet, int row)
    {
        return IsHeader(sheet, row) && Same(Text(sheet.Cells[row, 2].Value), "Company Code") &&
            Same(Text(sheet.Cells[row, 4].Value), "G/L Account") &&
            Same(Text(sheet.Cells[row, 5].Value), "Starting Balance in Company Code Crcy") &&
            Same(Text(sheet.Cells[row, 6].Value), "Debit Balance in Company Code Currency") &&
            Same(Text(sheet.Cells[row, 7].Value), "Credit Balance in Company Code Currency");
    }

    private static NORMParsedImport ParseDefence(ExcelWorksheet sheet, int header,
        IList<string> allowedCompanyCodes, int expectedFinancialYear)
    {
        NORMParsedImport parsed = new NORMParsedImport();
        int year;
        if (!Int32.TryParse(Text(FindMetadataValue(sheet, header, "Funds of Flow Ledger Fiscal Year")), out year))
        { throw new InvalidDataException("The Defence ERP fiscal year is missing or invalid."); }
        object legacyYear = FindMetadataValue(sheet, header, "Fiscal Year");
        if (legacyYear != null && !Same(Text(legacyYear), year.ToString(CultureInfo.InvariantCulture)))
        { throw new InvalidDataException("The ERP workbook contains conflicting fiscal-year labels."); }
        if (year != expectedFinancialYear)
        { throw new InvalidDataException("The Defence ERP financial year does not match the expected financial year."); }
        ReadPeriodRange(sheet, header, year, parsed);
        ValidateDefenceUnits(sheet, header);
        string company = Text(FindMetadataValue(sheet, header, "Company Code"));
        string ledger = Text(FindMetadataValue(sheet, header, "Ledger"));
        string sourceLedger = Text(FindMetadataValue(sheet, header, "Source Ledger"));
        if (!IsCompanyCode(company) || String.IsNullOrWhiteSpace(ledger) || !Same(ledger, sourceLedger))
        { throw new InvalidDataException("The Defence ERP company or consistent Ledger/Source Ledger identity is missing."); }
        if (allowedCompanyCodes == null || allowedCompanyCodes.Count == 0 || !Contains(allowedCompanyCodes, company))
        { throw new InvalidDataException("The Defence ERP company is not authorised for inspection."); }

        decimal[] detail = new decimal[4];
        decimal[] subtotal = null;
        decimal[] grandTotal = null;
        HashSet<string> accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int row = header + 1; row <= sheet.Dimension.End.Row; row++)
        {
            bool empty = true;
            for (int column = 1; column <= 8; column++)
            { if (!String.IsNullOrWhiteSpace(Text(sheet.Cells[row, column].Value))) { empty = false; break; } }
            if (empty) { continue; }
            string rowCompany = Text(sheet.Cells[row, 1].Value);
            string account = Text(sheet.Cells[row, 3].Value);
            string description = Convert.ToString(sheet.Cells[row, 4].Value, CultureInfo.InvariantCulture);
            bool companyTotal = Same(rowCompany, company) && Same(account, "Total") && Same(description, "Total");
            bool grand = Same(rowCompany, "Grand Total") && Same(Text(sheet.Cells[row, 2].Value), "Grand Total") &&
                Same(account, "Grand Total") && Same(description, "Grand Total");
            if (companyTotal || grand)
            {
                decimal[] control = ReadAmounts(sheet, row);
                if (companyTotal)
                {
                    if (subtotal != null || grandTotal != null) { throw new InvalidDataException("Duplicate or misplaced company subtotal."); }
                    subtotal = control;
                }
                else
                {
                    if (grandTotal != null || subtotal == null) { throw new InvalidDataException("Duplicate or misplaced grand total."); }
                    grandTotal = control;
                }
                continue;
            }
            if (subtotal != null || grandTotal != null || !Same(rowCompany, company) ||
                String.IsNullOrWhiteSpace(account) || !account.StartsWith("DCOA/", StringComparison.Ordinal) ||
                !IsDigits(account.Substring(5)) || String.IsNullOrWhiteSpace(description))
            { throw new InvalidDataException("Unrecognised or incomplete Defence ERP detail row " + row.ToString() + "."); }
            if (!accounts.Add(account)) { throw new InvalidDataException("Duplicate Defence ERP account " + account + "."); }
            decimal[] amounts = ReadAmounts(sheet, row);
            if (amounts[0] + amounts[1] + amounts[2] != amounts[3])
            { throw new InvalidDataException("Defence ERP movements do not reconcile exactly at row " + row.ToString() + "."); }
            for (int index = 0; index < 4; index++) { detail[index] += amounts[index]; }
            NORMTrialBalanceRow item = new NORMTrialBalanceRow();
            item.SourceRowNo = row;
            item.SourceCompanyCode = company;
            item.SourceLedger = ledger;
            // Keep the complete source identifier; no mapping/persistence occurs here.
            item.GlAccount = account;
            item.GlText = description;
            item.OpeningBalance = amounts[0]; item.DebitMovement = amounts[1];
            item.CreditMovement = amounts[2]; item.AccumBalance = amounts[3];
            item.RowHash = NORMCrypto.Sha256(row.ToString(CultureInfo.InvariantCulture) + "|" + company + "|" + ledger + "|" +
                account + "|" + description + "|" + amounts[0].ToString("G29", CultureInfo.InvariantCulture) + "|" +
                amounts[1].ToString("G29", CultureInfo.InvariantCulture) + "|" + amounts[2].ToString("G29", CultureInfo.InvariantCulture) + "|" +
                amounts[3].ToString("G29", CultureInfo.InvariantCulture));
            parsed.Rows.Add(item);
        }
        if (parsed.Rows.Count == 0 || subtotal == null || grandTotal == null)
        { throw new InvalidDataException("Defence ERP account detail, company subtotal and grand total are required."); }
        for (int index = 0; index < 4; index++)
        {
            if (detail[index] != subtotal[index] || detail[index] != grandTotal[index])
            { throw new InvalidDataException("Defence ERP report controls disagree with extracted detail in column " + (index + 5).ToString() + "."); }
        }
        if (detail[3] != 0m) { throw new InvalidDataException("The Defence ERP trial balance must sum to exactly zero."); }
        return parsed;
    }

    private static decimal[] ReadAmounts(ExcelWorksheet sheet, int row)
    {
        decimal[] amounts = new decimal[4];
        for (int index = 0; index < 4; index++)
        {
            if (!TryDecimal(sheet.Cells[row, index + 5].Value, out amounts[index]))
            { throw new InvalidDataException("Missing or invalid Defence ERP amount at row " + row.ToString() + ", column " + (index + 5).ToString() + "."); }
        }
        return amounts;
    }

    private static void ValidateDefenceUnits(ExcelWorksheet sheet, int header)
    {
        // NORM's supported contract is unscaled Australian dollars. Missing
        // indicators do not introduce another currency; explicit conflicts fail.
        for (int row = 1; row < header; row++)
        {
            string label = Text(sheet.Cells[row, 1].Value);
            string value = Text(sheet.Cells[row, 2].Value);
            if (Same(label, "Currency") || Same(label, "Currency Code") || Same(label, "Company Code Currency") ||
                Same(label, "Display Currency"))
            {
                if (!Same(value, "AUD")) { throw new InvalidDataException("The Defence ERP import supports Australian dollars (AUD) only."); }
            }
            if (Same(label, "Units") || Same(label, "Unit") || Same(label, "Scale") || Same(label, "Scaling Factor"))
            {
                if (!Same(value, "AUD") && !Same(value, "Dollars") && !Same(value, "$") && !Same(value, "1"))
                { throw new InvalidDataException("The Defence ERP import requires unscaled dollar units; thousands or other scales are unsupported."); }
            }
        }
    }

    private static bool IsDigits(string value)
    {
        if (value.Length == 0) { return false; }
        for (int index = 0; index < value.Length; index++) { if (value[index] < '0' || value[index] > '9') { return false; } }
        return true;
    }

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

            // This legacy entry point feeds persistence, whose schema cannot retain
            // company and ledger independently. Inspection must not enable import.
            foreach (ExcelWorksheet candidate in package.Workbook.Worksheets)
            {
                if (HasDefenceMetadata(candidate))
                {
                    throw new InvalidDataException("The Defence ERP layout is available for parsing inspection only through this route. Inspect the file and explicitly confirm acceptance.");
                }
            }

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
