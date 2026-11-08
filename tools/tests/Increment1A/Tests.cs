using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;

public static class Tests
{
    static byte[] original;
    static int passed;
    static void Assert(bool ok, string message) { if (!ok) { throw new Exception(message); } }
    static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    static NORMParsedImport Inspect(byte[] bytes) { return ErpTrialBalanceImporter.ParseForInspection(bytes, new string[] { "1000" }, 2026); }
    static byte[] Change(Action<ExcelPackage> change)
    {
        using (ExcelPackage p = new ExcelPackage(new MemoryStream(original))) { change(p); return p.GetAsByteArray(); }
    }
    static void Reject(byte[] bytes, string reason)
    {
        try { Inspect(bytes); }
        catch (InvalidDataException e) { Assert(e.Message.IndexOf(reason, StringComparison.OrdinalIgnoreCase) >= 0, e.Message); return; }
        throw new Exception("Expected rejection: " + reason);
    }
    static byte[] Legacy()
    {
        using (ExcelPackage p = new ExcelPackage())
        {
            ExcelWorksheet s = p.Workbook.Worksheets.Add("Legacy");
            s.Cells[1,1].Value = "Fiscal Year"; s.Cells[1,2].Value = 2026;
            s.Cells[2,1].Value = "Posting Date From"; s.Cells[2,2].Value = "01.07.2025";
            s.Cells[3,1].Value = "Posting Date To"; s.Cells[3,2].Value = "30.06.2026";
            s.Cells[4,1].Value = "Company Code"; s.Cells[4,3].Value = "G/L Account";
            s.Cells[4,8].Value = "Ending Balance in Company Code Currency";
            for (int row = 5; row <= 6; row++)
            {
                s.Cells[row,1].Value = "1000"; s.Cells[row,3].Value = "GL/" + row;
                s.Cells[row,4].Value = "Legacy account";
                s.Cells[row,5].Value = row == 5 ? "100.00" : "-100.00";
                s.Cells[row,6].Value = "0.00"; s.Cells[row,7].Value = "0.00";
                s.Cells[row,8].Value = row == 5 ? "100.00" : "-100.00";
            }
            return p.GetAsByteArray();
        }
    }
    public static int Main(string[] args)
    {
        try
        {
            original = File.ReadAllBytes(args[0]);
            Test("original Defence workbook", delegate {
                NORMParsedImport parsed = Inspect(original);
                decimal opening = 0m, debit = 0m, credit = 0m, ending = 0m, positive = 0m, negative = 0m;
                foreach (NORMTrialBalanceRow row in parsed.Rows)
                {
                    Assert(row.SourceCompanyCode == "1000" && row.SourceLedger == "0L", "Company/ledger conflation");
                    Assert(row.GlAccount.StartsWith("DCOA/", StringComparison.Ordinal), "Source account identifier lost");
                    Assert(row.AccumBalance == Decimal.Round(row.AccumBalance, 2) &&
                        row.OpeningBalance == Decimal.Round(row.OpeningBalance.Value, 2) &&
                        row.DebitMovement == Decimal.Round(row.DebitMovement.Value, 2) &&
                        row.CreditMovement == Decimal.Round(row.CreditMovement.Value, 2), "Sample conversion contains fractional cents");
                    opening += row.OpeningBalance.Value; debit += row.DebitMovement.Value;
                    credit += row.CreditMovement.Value; ending += row.AccumBalance;
                    if (row.AccumBalance >= 0) { positive += row.AccumBalance; } else { negative -= row.AccumBalance; }
                }
                Console.WriteLine("RESULT rows=" + parsed.Rows.Count + " year=" + parsed.FinancialYear + " periods=" + parsed.PeriodStart + "-" + parsed.PeriodEnd);
                Console.WriteLine("RESULT opening=" + opening + " debitMovement=" + debit + " creditMovement=" + credit + " ending=" + ending + " positiveEnding=" + positive + " negativeEnding=" + negative);
                Assert(parsed.Rows.Count == 984 && parsed.FinancialYear == 2026 && parsed.PeriodStart == 1 && parsed.PeriodEnd == 12, "Population/year/period mismatch");
                Assert(opening == 0m && debit == 4466384209424.24m && credit == -4466384209424.24m && ending == 0m, "Control totals mismatch");
                Assert(positive == 268992152391.65m && negative == positive, "Exact-zero ending balance failure");
                NORMTrialBalanceRow first = parsed.Rows[0];
                Assert(first.SourceRowNo == 28 && first.GlAccount == "DCOA/100000" && first.GlText == "Rcpts-Ratns,qrts,mls" &&
                    first.DebitMovement == 92585468.06m && first.CreditMovement == -172344690.28m && first.AccumBalance == -79759222.22m, "First account/sign/precision mismatch");
                Assert(parsed.Rows[983].SourceRowNo == 1011 && parsed.Rows[983].GlAccount == "DCOA/843050", "Totals entered detail population");
                // Independent traversal of original cells, without parser row predicates.
                using (ExcelPackage p = new ExcelPackage(new MemoryStream(original)))
                {
                    ExcelWorksheet s = p.Workbook.Worksheets[1];
                    decimal[] sums = new decimal[4];
                    for (int row = 28; row <= 1011; row++)
                    {
                        NORMTrialBalanceRow actual = parsed.Rows[row - 28];
                        Assert(actual.GlAccount == s.Cells[row,3].Text.Trim() && actual.GlText == s.Cells[row,4].Text, "Account/description changed at row " + row);
                        decimal[] values = { actual.OpeningBalance.Value, actual.DebitMovement.Value, actual.CreditMovement.Value, actual.AccumBalance };
                        for (int column = 5; column <= 8; column++)
                        {
                            decimal expected = Convert.ToDecimal(s.Cells[row,column].Value, CultureInfo.InvariantCulture);
                            Assert(values[column - 5] == expected, "Monetary value changed"); sums[column - 5] += expected;
                        }
                    }
                    for (int column = 5; column <= 8; column++)
                    {
                        Assert(sums[column - 5] == Convert.ToDecimal(s.Cells[1012,column].Value, CultureInfo.InvariantCulture), "Independent company control mismatch");
                        Assert(sums[column - 5] == Convert.ToDecimal(s.Cells[1013,column].Value, CultureInfo.InvariantCulture), "Independent grand control mismatch");
                    }
                }
            });
            Test("legacy format retains existing contract", delegate {
                NORMParsedImport direct = ErpTrialBalanceImporter.Parse(Legacy(), new string[] { "1000" }, 2026);
                NORMParsedImport detected = Inspect(Legacy());
                Assert(direct.Rows.Count == 2 && detected.Rows.Count == 2 && detected.Warnings.Count == 1, "Legacy detection changed");
                for (int i = 0; i < 2; i++)
                {
                    Assert(direct.Rows[i].RowHash == detected.Rows[i].RowHash && detected.Rows[i].SourceLedger == "1000" &&
                        detected.Rows[i].SourceCompanyCode == null && detected.Rows[i].GlAccount == (i + 5).ToString(), "Legacy consumer semantics changed");
                }
            });
            Test("legacy persistence entry explicitly blocks Defence", delegate {
                try { ErpTrialBalanceImporter.Parse(original, new string[] { "1000" }, 2026); }
                catch (InvalidDataException e) { Assert(e.Message.Contains("inspection only"), "Unclear persistence safeguard"); return; }
                throw new Exception("Defence reached legacy persistence contract");
            });
            Test("service safeguard prevents writes and calculation (existing FR016 substitutes)", delegate {
                NORMHelper.Reset();
                try { NORMImportService.Import(original, "arbitrary-name.xlsx", "ERP", 1, "isolated-test"); }
                catch (InvalidDataException e)
                {
                    Assert(e.Message.Contains("inspection only"), "Wrong service safeguard error");
                    Assert(NORMHelper.Committed.Count == 0 && NORMHelper.Opens == 0 && NORMHelper.Runs == 0, "Defence reached persistence/calculation");
                    return;
                }
                throw new Exception("Service accepted Defence layout");
            });
            Test("nonworkbook content rejected clearly", delegate { Reject(new byte[] { 1, 2, 3 }, "Unsupported ERP content"); });
            Test("empty content rejected clearly", delegate { Reject(new byte[0], "non-empty"); });
            Test("no filename or extension needed", delegate { Assert(Inspect((byte[])original.Clone()).Rows.Count == 984, "Content detection failed"); });
            Test("partial-year period remains partial", delegate {
                byte[] b = Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[15,2].Value = "31.03.2026"; p.Workbook.Worksheets[1].Cells[23,2].Value = "31.03.2026"; });
                Assert(Inspect(b).PeriodEnd == 9, "Partial year represented as full year");
            });
            Test("expected year mismatch", delegate {
                try { ErpTrialBalanceImporter.ParseForInspection(original, new string[] { "1000" }, 2025); }
                catch (InvalidDataException e) { Assert(e.Message.Contains("expected financial year"), "Wrong mismatch error"); return; }
                throw new Exception("Year mismatch accepted");
            });
            Test("unauthorised company rejected instead of silently filtered", delegate {
                try { ErpTrialBalanceImporter.ParseForInspection(original, new string[] { "2000" }, 2026); }
                catch (InvalidDataException e) { Assert(e.Message.Contains("not authorised"), "Wrong permission error"); return; }
                throw new Exception("Unauthorised company accepted");
            });
            Test("missing permission population fails closed", delegate {
                try { ErpTrialBalanceImporter.ParseForInspection(original, null, 2026); }
                catch (InvalidDataException e) { Assert(e.Message.Contains("not authorised"), "Wrong permission error"); return; }
                throw new Exception("Missing company configuration accepted");
            });
            Test("changed report identifier rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[2,2].Value = "Different Report"; }), "Unsupported"); });
            Test("changed provider rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[3,2].Value = "Different Cube"; }), "Unsupported"); });
            Test("wrong movement header rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[27,7].Value = "Debit"; }), "Unsupported"); });
            Test("missing year rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[9,2].Value = null; p.Workbook.Worksheets[1].Cells[24,2].Value = null; }), "Unsupported"); });
            Test("conflicting years rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[24,2].Value = 2025; }), "conflicting"); });
            Test("conflicting legacy year rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[26,1].Value = "Fiscal Year"; p.Workbook.Worksheets[1].Cells[26,2].Value = 2025; }), "conflicting"); });
            Test("missing dates rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[13,2].Value = null; p.Workbook.Worksheets[1].Cells[21,2].Value = null; }), "posting-date"); });
            Test("out-of-year dates rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[13,2].Value = "01.07.2024"; p.Workbook.Worksheets[1].Cells[21,2].Value = "01.07.2024"; }), "posting-date range"); });
            Test("missing ledger rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[12,2].Value = null; p.Workbook.Worksheets[1].Cells[20,2].Value = null; }), "identity is missing"); });
            Test("different source ledger rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[11,2].Value = "2L"; p.Workbook.Worksheets[1].Cells[22,2].Value = "2L"; }), "identity is missing"); });
            Test("missing description rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[28,4].Value = null; }), "incomplete"); });
            Test("missing account rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[28,3].Value = null; }), "incomplete"); });
            Test("missing amount rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[28,8].Value = null; }), "invalid Defence ERP amount"); });
            Test("malformed numeric data rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[28,6].Value = "not an amount"; }), "invalid Defence ERP amount"); });
            Test("unexpected company row rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[28,1].Value = "2000"; }), "incomplete"); });
            Test("duplicate account rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[29,3].Value = "DCOA/100000"; }), "Duplicate"); });
            Test("missing subtotal rejected", delegate { Reject(Change(delegate(ExcelPackage p) { for (int c = 1; c <= 8; c++) { p.Workbook.Worksheets[1].Cells[1012,c].Value = null; } }), "misplaced grand total"); });
            Test("missing grand total rejected", delegate { Reject(Change(delegate(ExcelPackage p) { for (int c = 1; c <= 8; c++) { p.Workbook.Worksheets[1].Cells[1013,c].Value = null; } }), "grand total are required"); });
            foreach (int controlRow in new int[] { 1012, 1013 })
            {
                foreach (int col in new int[] { 5, 6, 7, 8 })
                {
                    int r = controlRow, c = col;
                    Test("independent control mismatch row " + r + " column " + c, delegate {
                        Reject(Change(delegate(ExcelPackage p) { ExcelWorksheet s = p.Workbook.Worksheets[1]; s.Cells[r,c].Value = Convert.ToDecimal(s.Cells[r,c].Value) + 0.01m; }), "controls disagree");
                    });
                }
            }
            Test("unreconciled movements rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets[1].Cells[28,6].Value = "92585468.07"; }), "movements do not reconcile exactly"); });
            Test("ambiguous duplicated data sheet rejected", delegate { Reject(Change(delegate(ExcelPackage p) { p.Workbook.Worksheets.Add("Another TB", p.Workbook.Worksheets[1]); }), "Ambiguous"); });
            Test("Defence data detected beyond first worksheet and legacy entry still blocked", delegate {
                byte[] b = Change(delegate(ExcelPackage p) { p.Workbook.Worksheets.Add("Cover").Cells[1,1].Value = "Cover sheet"; p.Workbook.Worksheets.MoveToStart("Cover"); });
                Assert(Inspect(b).Rows.Count == 984, "Detection depended on worksheet order");
                try { ErpTrialBalanceImporter.Parse(b, new string[] { "1000" }, 2026); }
                catch (InvalidDataException e) { Assert(e.Message.Contains("inspection only"), "Guard inspected only first worksheet"); return; }
                throw new Exception("Worksheet order bypassed guard");
            });
            Test("malformed Defence markers cannot fall back to legacy sheet", delegate {
                byte[] b = Change(delegate(ExcelPackage p) {
                    p.Workbook.Worksheets[1].Cells[27,7].Value = "Wrong header";
                    using (ExcelPackage legacy = new ExcelPackage(new MemoryStream(Legacy())))
                    { p.Workbook.Worksheets.Add("Legacy", legacy.Workbook.Worksheets[1]); }
                });
                Reject(b, "Unsupported");
            });
            Test("unrelated workbook rejected", delegate {
                using (ExcelPackage p = new ExcelPackage()) { p.Workbook.Worksheets.Add("Unsupported").Cells[1,1].Value = "G/L Account"; Reject(p.GetAsByteArray(), "Unsupported"); }
            });
            Test("fractional cents are preserved without rounding", delegate {
                byte[] b = Change(delegate(ExcelPackage p) {
                    ExcelWorksheet s = p.Workbook.Worksheets[1];
                    s.Cells[28,5].Value = "0.001"; s.Cells[28,8].Value = "-79759222.219";
                    s.Cells[29,5].Value = "-0.001"; s.Cells[29,8].Value = "-1293824.121";
                });
                NORMParsedImport parsed = Inspect(b);
                Assert(parsed.Rows[0].OpeningBalance == 0.001m && parsed.Rows[0].AccumBalance == -79759222.219m &&
                    parsed.Rows[1].AccumBalance == -1293824.121m, "Parser rounded source precision");
                Assert(parsed.Rows[0].RowHash != Inspect(original).Rows[0].RowHash, "Precision missing from row hash");
                // Parsing is not storage acceptance: FR-016 still rejects fractional cents.
            });
            Test("nonzero TB rejected even when report controls agree", delegate {
                Reject(Change(delegate(ExcelPackage p) {
                    ExcelWorksheet s = p.Workbook.Worksheets[1];
                    s.Cells[28,5].Value = "0.001"; s.Cells[28,8].Value = "-79759222.219";
                    foreach (int row in new int[] { 1012, 1013 }) { s.Cells[row,5].Value = "0.001"; s.Cells[row,8].Value = "0.001"; }
                }), "exactly zero");
            });
            Console.WriteLine("TOTAL PASS " + passed); return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
    }
}
