using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using OfficeOpenXml;

public static class Tests
{
    static byte[] original;
    static int passed;
    static void Assert(bool condition, string message) { if (!condition) { throw new Exception(message); } }
    static void Test(string name, Action action) { NORMHelper.Reset(); action(); passed++; Console.WriteLine("PASS " + name); }
    static NORMImportOutcome Import(byte[] content) { return NORMImportService.ImportDefence(content, "Defence original.xlsx", 1, "isolated-test"); }
    static byte[] Change(Action<ExcelWorksheet> action)
    { using (ExcelPackage p = new ExcelPackage(new MemoryStream(original))) { action(p.Workbook.Worksheets[1]); return p.GetAsByteArray(); } }
    static List<NORMHelper.Call> Rows()
    { return NORMHelper.Committed.FindAll(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow"); }); }
    static void Rejected(byte[] bytes, string reason)
    {
        try { Import(bytes); }
        catch (InvalidDataException e) { Assert(e.Message.Contains(reason), e.Message); NoSource(); return; }
        catch (InvalidOperationException e) { Assert(e.Message.Contains(reason), e.Message); NoSource(); return; }
        throw new Exception("Expected rejection: " + reason);
    }
    static void NoSource()
    {
        Assert(Rows().Count == 0 && NORMHelper.Runs == 0 && !NORMHelper.Committed.Exists(delegate(NORMHelper.Call c) {
            return c.Sql.Contains("INSERT dbo.tblNORM_Import ") || c.Sql.Contains("INSERT dbo.tblNORM_ImportFile"); }), "Rejected input reached reporting");
    }
    public static int Main(string[] args)
    {
        try
        {
            original = File.ReadAllBytes(args[0]);
            Test("original: 984 separate company/ledger rows, exact amounts, hashes and retained bytes", delegate {
                NORMParsedImport parsed = ErpTrialBalanceImporter.ParseForInspection(original, new string[] { "1000" }, 2026);
                NORMImportOutcome outcome = Import(original);
                List<NORMHelper.Call> rows = Rows();
                Assert(outcome.RowCount == 984 && outcome.NetBalance == 0 && rows.Count == 984 && NORMHelper.Runs == 1, "Import population mismatch");
                decimal net = 0m;
                for (int i = 0; i < rows.Count; i++)
                {
                    NORMHelper.Call row = rows[i];
                    Assert((string)row["@company"] == "1000" && (string)row["@ledger"] == "0L", "Company/ledger lost");
                    Assert((string)row["@gl"] == ErpTrialBalanceImporter.ExtractGl(parsed.Rows[i].GlAccount), "Mapping key lost");
                    Assert((string)row["@hash"] == parsed.Rows[i].RowHash && (decimal)row["@balance"] == parsed.Rows[i].AccumBalance, "Source hash/amount changed");
                    net += (decimal)row["@balance"];
                }
                Assert(net == 0m, "Net changed");
                NORMHelper.Call file = NORMHelper.Committed.Find(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_ImportFile"); });
                Assert(NORMCrypto.Sha256((byte[])file["@content"]) == NORMCrypto.Sha256(original) && (int)file["@periodStart"] == 1 && (int)file["@periodEnd"] == 12, "Evidence/period changed");
                Assert(!NORMHelper.Queries.Exists(delegate(string sql) { return sql.Contains("AccountMap"); }), "Mapping completion became an acceptance gate");
            });
            Test("wrong actual ledger rejected", delegate { NORMHelper.PermittedPairs[0][1] = "2L"; Rejected(original, "pair is not authorised"); });
            Test("wrong company rejected", delegate { NORMHelper.PermittedPairs[0][0] = "2000"; Rejected(original, "not authorised"); });
            Test("no permitted pairs rejected", delegate { NORMHelper.PermittedPairs.Clear(); Rejected(original, "not authorised"); });
            Test("same ledger allowed for another company", delegate {
                NORMHelper.PermittedPairs.Add(new string[] { "2000", "0L" });
                byte[] bytes = Change(delegate(ExcelWorksheet s) {
                    s.Cells[10,2].Value = "2000"; s.Cells[19,2].Value = "2000";
                    for (int r = 28; r <= 1012; r++) { s.Cells[r,1].Value = "2000"; }
                });
                Import(bytes);
                Assert(Rows().Count == 984 && (string)Rows()[0]["@company"] == "2000" && (string)Rows()[0]["@ledger"] == "0L", "Pair collision");
            });
            Test("replacement not silently appended", delegate { NORMHelper.ExistingCompany = true; Rejected(original, "replacement is not available"); });
            Test("explicit USD rejected", delegate { Rejected(Change(delegate(ExcelWorksheet s) { s.Cells[26,1].Value = "Currency"; s.Cells[26,2].Value = "USD"; }), "AUD"); });
            Test("explicit thousands rejected", delegate { Rejected(Change(delegate(ExcelWorksheet s) { s.Cells[26,1].Value = "Units"; s.Cells[26,2].Value = "AUD k"; }), "unscaled"); });
            Test("explicit AUD accepted", delegate { Import(Change(delegate(ExcelWorksheet s) { s.Cells[26,1].Value = "Currency"; s.Cells[26,2].Value = "AUD"; })); Assert(Rows().Count == 984, "AUD rejected"); });
            Test("exact-zero required", delegate { Rejected(Change(delegate(ExcelWorksheet s) {
                s.Cells[28,5].Value = "0.01"; s.Cells[28,8].Value = "-79759222.21";
                foreach (int r in new int[] { 1012, 1013 }) { s.Cells[r,5].Value = "0.01"; s.Cells[r,8].Value = "0.01"; }
            }), "exactly zero"); });
            Test("balanced fractional cents reach unchanged FR016 storage rejection", delegate {
                Rejected(Change(delegate(ExcelWorksheet s) {
                    s.Cells[28,5].Value = "0.001"; s.Cells[28,8].Value = "-79759222.219";
                    s.Cells[29,5].Value = "-0.001"; s.Cells[29,8].Value = "-1293824.121";
                }), "without loss");
                Assert(NORMHelper.Committed.Exists(delegate(NORMHelper.Call c) { return c.Values.ContainsKey("@event") && (string)c["@event"] == "IMPORT_REJECTED"; }), "FR016 rejection audit missing");
            });
            Test("late audit failure rolls back source rows", delegate { NORMHelper.FailAuditCode = "IMPORT_STORAGE_VALIDATION"; Rejected(original, "could not be saved"); Assert(NORMHelper.Committed.Count == 0, "Partial acceptance"); });
            Test("legacy browser/service route stays blocked", delegate {
                try { NORMImportService.Import(original, "original.xlsx", "ERP", 1, "test"); }
                catch (InvalidDataException e) { Assert(e.Message.Contains("inspection only"), "Guard removed"); NoSource(); return; }
                throw new Exception("Browser path enabled");
            });
            Test("FY2025 transition keeps historical source codes and null company", delegate {
                NORMHelper.QueryResponse = delegate(string sql) {
                    DataTable t = new DataTable();
                    if (sql.Contains("SELECT SourceLedger"))
                    {
                        Assert(sql.Contains("SourceCompanyCode IS NULL"), "Legacy filter includes actual-ledger configuration");
                        t.Columns.Add("SourceLedger"); t.Rows.Add("1000"); return t;
                    }
                    t.Columns.Add("ConfigurationReleaseId", typeof(int)); t.Columns.Add("FinancialYear", typeof(int));
                    t.Columns.Add("EntityCode"); t.Columns.Add("VersionCode"); t.Rows.Add(1, 2025, "DEPT", "legacy"); return t;
                };
                byte[] erp;
                using (ExcelPackage p = new ExcelPackage())
                {
                    ExcelWorksheet s = p.Workbook.Worksheets.Add("Legacy");
                    s.Cells[1,1].Value = "Fiscal Year"; s.Cells[1,2].Value = 2025;
                    s.Cells[2,1].Value = "Posting Date From"; s.Cells[2,2].Value = "01.05.2025";
                    s.Cells[3,1].Value = "Posting Date To"; s.Cells[3,2].Value = "30.06.2025";
                    s.Cells[4,1].Value = "Company Code"; s.Cells[4,3].Value = "G/L Account"; s.Cells[4,8].Value = "Ending Balance in Company Code Currency";
                    for (int r = 5; r <= 6; r++)
                    { s.Cells[r,1].Value = "1000"; s.Cells[r,3].Value = "GL/" + r; s.Cells[r,5].Value = r == 5 ? "100" : "-100";
                      s.Cells[r,6].Value = "0"; s.Cells[r,7].Value = "0"; s.Cells[r,8].Value = r == 5 ? "100" : "-100"; }
                    erp = p.GetAsByteArray();
                }
                byte[] roman = System.Text.Encoding.UTF8.GetBytes("Carryforward Periods 00-00 2025 Reporting Periods 01-10 2025\n|1000 100 Cash AUD 100.00|\n|1000 200 Equity AUD 100.00-|\n");
                NORMImportService.ImportFy2025Transition(roman, "legacy.txt", erp, "legacy.xlsx", 1, "test");
                Assert(Rows().Count == 2, "Transition changed population");
                foreach (NORMHelper.Call row in Rows()) { Assert(row["@company"] == DBNull.Value && (string)row["@ledger"] == "1000", "Historical identities rewritten"); }
                Assert(NORMHelper.Committed.FindAll(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_ImportFile"); }).Count == 2, "Transition evidence lost");
            });
            Console.WriteLine("TOTAL PASS " + passed + " (database substitutes only)"); return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
    }
}
