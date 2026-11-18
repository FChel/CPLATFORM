using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;

/// <summary>NIHRACS consolidated annual trial balance. One population per worksheet.</summary>
public static class NihracsTrialBalanceImporter
{
    public const string Profile = "NIHRACS-XLSX/1";
    public const string Agency = "NIHRACS";
    public const string Company = "NIHRA";
    public const string Stream = "Departmental";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] Headings = { "Entity Code", "Account", "Centre", "Description", "Centre Desc" };

    public static List<NORMParsedImport> TryParse(byte[] content)
    {
        if (content.Length < 4 || content[0] != 0x50 || content[1] != 0x4b || content[2] != 3 || content[3] != 4)
        { return null; }
        using (MemoryStream stream = new MemoryStream(content, false))
        using (ExcelPackage package = new ExcelPackage(stream))
        {
            bool identified = false;
            foreach (ExcelWorksheet sheet in package.Workbook.Worksheets)
            {
                identified |= Text(sheet, 3, 1) == Agency && Text(sheet, 7, 1) == Headings[0];
            }
            if (!identified) { return null; }
            if (package.Workbook.Worksheets.Count != 3)
            { throw new InvalidDataException("The NIHRACS profile requires the 2024, 2025 and 2026 worksheets."); }

            List<NORMParsedImport> results = new List<NORMParsedImport>();
            for (int year = 2024; year <= 2026; year++)
            {
                ExcelWorksheet sheet = package.Workbook.Worksheets[year.ToString(Invariant)];
                if (sheet == null) { throw new InvalidDataException("NIHRACS worksheet " + year + " is missing."); }
                results.Add(ParseSheet(sheet, year));
            }
            return results;
        }
    }

    private static NORMParsedImport ParseSheet(ExcelWorksheet sheet, int year)
    {
        string caption = "NI Health & Residential Aged Care - Financial Year : " + year.ToString(Invariant);
        if (year == 2026) { caption += " Period 12"; }
        if (Text(sheet, 1, 1) != "Trial Balance" || Text(sheet, 3, 1) != Agency || Text(sheet, 4, 1) != caption ||
            sheet.Dimension == null || sheet.Dimension.End.Column != 7)
        { throw Invalid(sheet, 4, "Unexpected report identity, financial year or layout."); }
        for (int column = 1; column <= Headings.Length; column++)
        {
            if (Text(sheet, 7, column) != Headings[column - 1])
            { throw Invalid(sheet, 7, "Unexpected column headings."); }
        }
        int debitColumn = Text(sheet, 7, 6) == "Debit" ? 6 : 7;
        int creditColumn = debitColumn == 6 ? 7 : 6;
        if (Text(sheet, 7, debitColumn) != "Debit" || Text(sheet, 7, creditColumn) != "Credit")
        { throw Invalid(sheet, 7, "Debit and Credit headings are required."); }

        NORMParsedImport parsed = new NORMParsedImport {
            ProfileVersion = Profile, FinancialYear = year, PeriodStart = 1, PeriodEnd = 12,
            CoverageStart = new DateTime(year - 1, 7, 1), CoverageEnd = new DateTime(year, 6, 30)
        };
        NORMImportFileContext counts = parsed.SourceFile;
        counts.RecordUnit = "Worksheet rows";
        counts.RecordCount = sheet.Dimension.End.Row;
        counts.MetadataRowCount = 5;
        counts.BlankRowCount = 2;
        int last = counts.RecordCount;
        if (year == 2026) { counts.ControlRowCount = 4; last -= 4; }
        decimal debitTotal = 0m, creditTotal = 0m;
        for (int row = 8; row <= last; row++)
        {
            string gl = Text(sheet, row, 2), centre = Text(sheet, row, 3);
            string description = Convert.ToString(sheet.Cells[row, 4].Value, Invariant);
            if (Text(sheet, row, 1) != Company || String.IsNullOrEmpty(gl) || gl.Length > 30 ||
                (centre != null && centre.Length > 30) || String.IsNullOrWhiteSpace(description) ||
                description.Length > 300 || Text(sheet, row, 5) != null)
            { throw Invalid(sheet, row, "Unsupported company, account, centre or description."); }
            decimal debit = Amount(sheet, row, debitColumn), credit = Amount(sheet, row, creditColumn);
            debitTotal += debit;
            creditTotal += credit;
            parsed.Rows.Add(new NORMTrialBalanceRow {
                SourceRowNo = row, SourceCompanyCode = Company, ReportingStream = Stream,
                GlAccount = gl, GlText = description, SourceCostCentre = centre,
                SourceDebit = debit, SourceCredit = credit, AccumBalance = debit - credit,
                RowHash = NORMCrypto.Sha256(sheet.Name + "|" + row + "|" + Company + "|" +
                    gl.Length + ":" + gl + "|" + (centre == null ? "-1:" : centre.Length + ":" + centre) + "|" +
                    description.Length + ":" + description + "|" + debit.ToString(Invariant) + "|" + credit.ToString(Invariant))
            });
        }
        if (parsed.Rows.Count == 0) { throw Invalid(sheet, 8, "No detail rows were found."); }
        if (year == 2026) { VerifyControls(sheet, last + 1, debitColumn, creditColumn, debitTotal, creditTotal); }
        counts.DetailRowCount = parsed.Rows.Count;
        return parsed;
    }

    private static void VerifyControls(ExcelWorksheet sheet, int first, int debitColumn, int creditColumn,
        decimal debitTotal, decimal creditTotal)
    {
        for (int row = first; row < first + 4; row++)
        {
            for (int column = 1; column <= 4; column++)
            {
                if (Text(sheet, row, column) != null)
                { throw Invalid(sheet, row, "Unexpected data in the footer."); }
            }
            if (row > first && Text(sheet, row, 7) != null)
            { throw Invalid(sheet, row, "Unexpected amount in the footer."); }
        }
        if (Text(sheet, first, 5) != "Totals" ||
            Amount(sheet, first, debitColumn) != debitTotal || Amount(sheet, first, creditColumn) != creditTotal ||
            Text(sheet, first + 1, 5) != "Credits" || Amount(sheet, first + 1, 6) != creditTotal ||
            Text(sheet, first + 2, 5) != "Debits" || Amount(sheet, first + 2, 6) != debitTotal ||
            Text(sheet, first + 3, 5) != "Up / (Down)" || Amount(sheet, first + 3, 6) != 0m)
        { throw Invalid(sheet, first, "Footer controls do not reconcile to the detail."); }
    }

    private static decimal Amount(ExcelWorksheet sheet, int row, int column)
    {
        var cell = sheet.Cells[row, column];
        object raw = cell.Value;
        if (!String.IsNullOrEmpty(cell.Formula) || !(raw is double || raw is int || raw is decimal))
        { throw Invalid(sheet, row, "A numeric amount is required in " + cell.Address + "."); }
        double value = Convert.ToDouble(raw, Invariant);
        if (Double.IsInfinity(value) || Double.IsNaN(value) || value < 0 || value >= 10000000000000d)
        { throw Invalid(sheet, row, "A non-negative amount within the supported precision is required in " + cell.Address + "."); }

        // Require both a small number of adjacent floating-point representations
        // and at most a billionth of a dollar. Balance validation remains exact.
        decimal cents = Math.Round(Convert.ToDecimal(value), 2, MidpointRounding.ToEven);
        long distance = Math.Abs(BitConverter.DoubleToInt64Bits(value == 0d ? 0d : value) -
            BitConverter.DoubleToInt64Bits((double)cents));
        if (distance > 16 || Math.Abs(value - (double)cents) > 0.000000001d)
        { throw Invalid(sheet, row, "A genuine sub-cent amount cannot be stored in " + cell.Address + "."); }
        return cents;
    }

    private static string Text(ExcelWorksheet sheet, int row, int column)
    {
        string text = Convert.ToString(sheet.Cells[row, column].Value, Invariant).Trim();
        return text.Length == 0 ? null : text;
    }

    private static InvalidDataException Invalid(ExcelWorksheet sheet, int row, string reason)
    {
        return new InvalidDataException("NIHRACS worksheet " + sheet.Name + ", row " + row + ". " + reason);
    }
}
