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
    private static readonly string[] ControlLabels = { "Totals", "Credits", "Debits", "Up / (Down)" };

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
            List<NORMParsedImport> results = new List<NORMParsedImport>();
            foreach (ExcelWorksheet sheet in package.Workbook.Worksheets)
            {
                int year;
                if (sheet.Name.Length != 4 || !Int32.TryParse(sheet.Name, NumberStyles.None, Invariant, out year) ||
                    year < 1900 || year > 2999)
                { throw new InvalidDataException("Use a four-digit financial year as each NIHRACS worksheet name."); }
                results.Add(ParseSheet(sheet, year));
            }
            results.Sort((left, right) => left.FinancialYear.CompareTo(right.FinancialYear));
            return results;
        }
    }

    private static NORMParsedImport ParseSheet(ExcelWorksheet sheet, int year)
    {
        string caption = "NI Health & Residential Aged Care - Financial Year : " + year.ToString(Invariant);
        if (Text(sheet, 1, 1) != "Trial Balance") { throw InvalidCell(sheet, 1, 1, "Expected the Trial Balance heading."); }
        if (Text(sheet, 3, 1) != Agency) { throw InvalidCell(sheet, 3, 1, "Expected the NIHRACS report identity."); }
        string heading = Text(sheet, 4, 1);
        if (heading != caption && heading != caption + " Period 12")
        { throw InvalidCell(sheet, 4, 1, "Expected the worksheet financial year in the annual report heading."); }
        if (sheet.Dimension == null || sheet.Dimension.End.Column != 7)
        { throw Invalid(sheet, 7, "Expected seven source columns."); }
        for (int column = 1; column <= Headings.Length; column++)
        {
            if (Text(sheet, 7, column) != Headings[column - 1])
            { throw InvalidCell(sheet, 7, column, "Unexpected column heading."); }
        }
        int debitColumn = Text(sheet, 7, 6) == "Debit" ? 6 : 7;
        int creditColumn = debitColumn == 6 ? 7 : 6;
        if (Text(sheet, 7, debitColumn) != "Debit" || Text(sheet, 7, creditColumn) != "Credit")
        { throw InvalidCell(sheet, 7, Text(sheet, 7, 6) == "Debit" || Text(sheet, 7, 6) == "Credit" ? 7 : 6,
            "Expected one Debit and one Credit heading."); }

        NORMParsedImport parsed = new NORMParsedImport {
            ProfileVersion = Profile, FinancialYear = year, PeriodStart = 1, PeriodEnd = 12,
            CoverageStart = new DateTime(year - 1, 7, 1), CoverageEnd = new DateTime(year, 6, 30)
        };
        NORMImportFileContext counts = parsed.SourceFile;
        counts.RecordUnit = "Worksheet rows";
        counts.RecordCount = sheet.Dimension.End.Row;
        counts.MetadataRowCount = 5;
        counts.BlankRowCount = 2;
        decimal debitTotal = 0m, creditTotal = 0m;
        for (int row = 8; row <= counts.RecordCount; row++)
        {
            string centreDescription = Text(sheet, row, 5);
            if (Array.IndexOf(ControlLabels, centreDescription) >= 0)
            {
                VerifyControls(sheet, row, debitColumn, creditColumn, debitTotal, creditTotal);
                counts.ControlRowCount = ControlLabels.Length;
                break;
            }
            string gl = Text(sheet, row, 2), centre = Text(sheet, row, 3);
            string description = Convert.ToString(sheet.Cells[row, 4].Value, Invariant);
            if (Text(sheet, row, 1) != Company)
            { throw InvalidCell(sheet, row, 1, "Expected a NIHRA detail row or a labelled control footer."); }
            if (String.IsNullOrEmpty(gl) || gl.Length > 30)
            { throw InvalidCell(sheet, row, 2, "A GL account of up to 30 characters is required."); }
            if (centre != null && centre.Length > 30)
            { throw InvalidCell(sheet, row, 3, "A cost centre must be no more than 30 characters."); }
            if (String.IsNullOrWhiteSpace(description) || description.Length > 300)
            { throw InvalidCell(sheet, row, 4, "A description of up to 300 characters is required."); }
            if (centreDescription != null)
            { throw InvalidCell(sheet, row, 5, "Expected a blank centre description."); }
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
        counts.DetailRowCount = parsed.Rows.Count;
        return parsed;
    }

    private static void VerifyControls(ExcelWorksheet sheet, int first, int debitColumn, int creditColumn,
        decimal debitTotal, decimal creditTotal)
    {
        for (int index = 0; index < ControlLabels.Length; index++)
        {
            int row = first + index;
            if (Text(sheet, row, 5) != ControlLabels[index])
            { throw InvalidCell(sheet, row, 5, "Expected the " + ControlLabels[index] + " footer label."); }
            for (int column = 1; column <= 4; column++)
            {
                if (Text(sheet, row, column) != null)
                { throw InvalidCell(sheet, row, column, "Unexpected data in the footer."); }
            }
            if (row > first && Text(sheet, row, 7) != null)
            { throw InvalidCell(sheet, row, 7, "Unexpected amount in the footer."); }
        }
        if (sheet.Dimension.End.Row >= first + ControlLabels.Length)
        { throw Invalid(sheet, first + ControlLabels.Length, "Unexpected data after the control footer."); }

        VerifyControlAmount(sheet, first, debitColumn, debitTotal);
        VerifyControlAmount(sheet, first, creditColumn, creditTotal);
        VerifyControlAmount(sheet, first + 1, 6, creditTotal);
        VerifyControlAmount(sheet, first + 2, 6, debitTotal);
        VerifyControlAmount(sheet, first + 3, 6, 0m);
    }

    private static void VerifyControlAmount(ExcelWorksheet sheet, int row, int column, decimal expected)
    {
        if (Amount(sheet, row, column) != expected)
        { throw InvalidCell(sheet, row, column, "Footer amount does not match the calculated control."); }
    }

    private static decimal Amount(ExcelWorksheet sheet, int row, int column)
    {
        var cell = sheet.Cells[row, column];
        object raw = cell.Value;
        if (!String.IsNullOrEmpty(cell.Formula))
        { throw InvalidCell(sheet, row, column, "Excel formulas are not accepted. Supply a fixed numeric amount."); }
        if (!(raw is double || raw is int || raw is decimal))
        { throw InvalidCell(sheet, row, column, "A numeric amount is required."); }
        double value = Convert.ToDouble(raw, Invariant);
        if (Double.IsInfinity(value) || Double.IsNaN(value) || value < 0 || value >= 10000000000000d)
        { throw InvalidCell(sheet, row, column, "A non-negative amount within the supported precision is required."); }

        // Require both a small number of adjacent floating-point representations
        // and at most a billionth of a dollar. Balance validation remains exact.
        decimal cents = Math.Round(Convert.ToDecimal(value), 2, MidpointRounding.ToEven);
        long distance = Math.Abs(BitConverter.DoubleToInt64Bits(value == 0d ? 0d : value) -
            BitConverter.DoubleToInt64Bits((double)cents));
        if (distance > 16 || Math.Abs(value - (double)cents) > 0.000000001d)
        { throw InvalidCell(sheet, row, column, "A genuine sub-cent amount cannot be stored."); }
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

    private static InvalidDataException InvalidCell(ExcelWorksheet sheet, int row, int column, string reason)
    {
        return new InvalidDataException("NIHRACS worksheet " + sheet.Name + ", cell " + sheet.Cells[row, column].Address + ". " + reason);
    }
}
