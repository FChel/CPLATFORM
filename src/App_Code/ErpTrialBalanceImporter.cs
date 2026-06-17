using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using OfficeOpenXml;

/// <summary>
/// Imports an ERP (S/4HANA) trial balance exported from a BEx query. Layout:
///   CompanyCode | CompanyName | G/L Account ("DCOA/100000") | Description |
///   StartingBalance | Debit | Credit | EndingBalance
/// A block of report headers precedes the data, so the first data row is found
/// by scanning for the first row whose first cell is a four-digit company code.
/// The ERP export uses the standard sign convention (negative = credit), which
/// already matches NORM's storage (debit positive, credit negative), so the
/// EndingBalance is stored as-is. EndingBalance is the year-to-date closing
/// balance (opening was migrated from ROMAN at cutover).
///
/// EPPlus 4.5.3.3: worksheets and cells are 1-based; no LicenseContext required
/// (that applies to EPPlus 5+).
/// </summary>
public class ErpTrialBalanceImporter
{
    public class TbRow
    {
        public string SourceLedger;
        public string GlAccount;
        public string GlText;
        public decimal AccumBalance;
    }

    private const int COL_COMPANY = 1;
    private const int COL_GL = 3;
    private const int COL_DESC = 4;
    private const int COL_ENDING = 8;

    public static List<TbRow> Parse(string filePath, List<string> sourceLedgers)
    {
        List<TbRow> rows = new List<TbRow>();
        FileInfo fi = new FileInfo(filePath);
        using (ExcelPackage pkg = new ExcelPackage(fi))
        {
            ExcelWorksheet ws = pkg.Workbook.Worksheets[1];
            if (ws == null || ws.Dimension == null) { return rows; }

            int lastRow = ws.Dimension.End.Row;
            int firstData = FindFirstDataRow(ws, lastRow);
            if (firstData < 1) { return rows; }

            for (int r = firstData; r <= lastRow; r++)
            {
                string cc = CellText(ws, r, COL_COMPANY);
                if (cc == null || cc.Length == 0) { continue; }
                if (!IsCompanyCode(cc)) { continue; }
                if (sourceLedgers != null && sourceLedgers.Count > 0 && !sourceLedgers.Contains(cc))
                {
                    continue;
                }

                object endVal = ws.Cells[r, COL_ENDING].Value;
                if (endVal == null) { continue; }

                decimal bal;
                if (!TryDecimal(endVal, out bal)) { continue; }

                TbRow row = new TbRow();
                row.SourceLedger = cc;
                row.GlAccount = ExtractGl(CellText(ws, r, COL_GL));
                row.GlText = CellText(ws, r, COL_DESC);
                row.AccumBalance = bal;
                rows.Add(row);
            }
        }
        return rows;
    }

    private static int FindFirstDataRow(ExcelWorksheet ws, int lastRow)
    {
        for (int r = 1; r <= lastRow; r++)
        {
            string cc = CellText(ws, r, COL_COMPANY);
            if (cc != null && IsCompanyCode(cc))
            {
                return r;
            }
        }
        return -1;
    }

    private static bool IsCompanyCode(string s)
    {
        s = s.Trim();
        if (s.Length != 4) { return false; }
        for (int i = 0; i < s.Length; i++)
        {
            if (!char.IsDigit(s[i])) { return false; }
        }
        return true;
    }

    /// <summary>"DCOA/100000" => "100000"; bare values pass through.</summary>
    public static string ExtractGl(string raw)
    {
        if (raw == null) { return null; }
        raw = raw.Trim();
        int slash = raw.LastIndexOf('/');
        if (slash >= 0 && slash < raw.Length - 1)
        {
            return raw.Substring(slash + 1).Trim();
        }
        return raw;
    }

    private static string CellText(ExcelWorksheet ws, int r, int c)
    {
        object v = ws.Cells[r, c].Value;
        if (v == null) { return null; }
        return Convert.ToString(v).Trim();
    }

    private static bool TryDecimal(object v, out decimal d)
    {
        d = 0m;
        if (v == null) { return false; }
        if (v is double) { d = Convert.ToDecimal((double)v); return true; }
        if (v is decimal) { d = (decimal)v; return true; }
        if (v is int) { d = (int)v; return true; }
        string s = Convert.ToString(v).Replace(",", "").Trim();
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d);
    }

    public static int Import(string filePath, int financialYear, string entityCode,
                             string mapVersion, string importedBy)
    {
        List<string> ledgers = RomanTrialBalanceImporter.LoadEntityLedgers(financialYear, entityCode);
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
            NORMHelper.P("SourceLabel", "ERP"),
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
}
