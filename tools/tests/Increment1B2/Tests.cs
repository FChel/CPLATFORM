using System;
using System.Data;
using System.IO;
using System.Text;
using OfficeOpenXml;
using CPlatform.NORM;

public static class Tests
{
    static byte[] original;
    static int passed, year, snapshotReads;
    static bool approved, changeAtCommit;
    static string mappingLine, version, ledger;
    static DataTable Table(string columns)
    { DataTable t = new DataTable(); foreach (string c in columns.Split(',')) { t.Columns.Add(c, typeof(object)); } return t; }
    static void Assert(bool condition, string message) { if (!condition) { throw new Exception(message); } }
    static void Test(string name, Action action)
    {
        NORMHelper.Reset(); year = 2026; approved = true; changeAtCommit = false; snapshotReads = 0;
        mappingLine = "Revenue"; version = "test-v1"; ledger = "0L";
        NORMHelper.QueryResponse = Query; action(); passed++; Console.WriteLine("PASS " + name);
    }
    static DataTable Query(string sql)
    {
        DataTable t;
        if (sql.Contains("FROM dbo.tblNORM_ConfigurationRelease"))
        {
            t = Table("ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode,ContentHash");
            if (sql.Contains("HOLDLOCK")) { snapshotReads++; if (changeAtCommit && snapshotReads == 3) { mappingLine = "Changed during acceptance"; } }
            if (approved) { t.Rows.Add(1, year, "DEPT", version, "approved-hash"); } return t;
        }
        if (sql.Contains("FROM dbo.tblNORM_EntityLedger"))
        {
            if (sql.Contains("SELECT SourceLedger")) { t = Table("SourceLedger"); t.Rows.Add("1000"); return t; }
            t = Table("SourceCompanyCode,SourceLedger"); t.Rows.Add("1000", ledger); t.Rows.Add(DBNull.Value, "1000"); return t;
        }
        if (sql.Contains("FROM dbo.tblNORM_AccountMap")) { t = Table("GlCode,AccountType,StatementLine"); t.Rows.Add("100000", "Income", mappingLine); return t; }
        if (sql.Contains("FROM dbo.tblNORM_StatementLine")) { t = Table("LineCode,StatementCode"); t.Rows.Add(mappingLine, "SOCI"); return t; }
        throw new Exception("Unexpected query: " + sql);
    }
    static NORMImportPreview Inspect(byte[] bytes) { return NORMImportService.Inspect(bytes, "source & file.arbitrary", 1, "isolated-test"); }
    static int AcceptedRows() { return NORMHelper.Committed.FindAll(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow"); }).Count; }
    static void NoAcceptance() { Assert(AcceptedRows() == 0 && NORMHelper.Runs == 0 && !NORMHelper.Committed.Exists(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_Import "); }), "Unexpected acceptance"); }
    static void Fails(Action action)
    {
        try { action(); } catch (InvalidOperationException) { NoAcceptance(); return; } catch (InvalidDataException) { NoAcceptance(); return; }
        throw new Exception("Expected failure");
    }
    static byte[] Legacy(bool balanced)
    {
        using (ExcelPackage p = new ExcelPackage())
        {
            ExcelWorksheet s = p.Workbook.Worksheets.Add("Legacy");
            s.Cells[1,1].Value = "Fiscal Year"; s.Cells[1,2].Value = year;
            s.Cells[2,1].Value = "Posting Date From"; s.Cells[2,2].Value = "01.07." + (year - 1);
            s.Cells[3,1].Value = "Posting Date To"; s.Cells[3,2].Value = "30.06." + year;
            s.Cells[4,1].Value = "Company Code"; s.Cells[4,3].Value = "G/L Account"; s.Cells[4,8].Value = "Ending Balance in Company Code Currency";
            for (int r = 5; r <= 6; r++)
            {
                s.Cells[r,1].Value = "1000"; s.Cells[r,3].Value = "GL/" + r;
                s.Cells[r,5].Value = r == 5 ? "100.00" : balanced ? "-100.00" : "-99.99";
                s.Cells[r,6].Value = "0"; s.Cells[r,7].Value = "0"; s.Cells[r,8].Value = s.Cells[r,5].Value;
            }
            return p.GetAsByteArray();
        }
    }
    static byte[] Roman() { return Encoding.UTF8.GetBytes("Carryforward Periods 00-00 " + year + " Reporting Periods 01-12 " + year + "\n|1000 100 Cash AUD 100.00|\n|1000 200 Equity AUD 100.00-|\n"); }
    public static int Main(string[] args)
    {
        try
        {
            original = File.ReadAllBytes(args[0]);
            Test("Defence preview: 984 rows, identity, exact totals, unmapped warning, zero writes", delegate {
                NORMImportPreview p = Inspect(original);
                Assert(p.CanAccept && p.Layout == "Defence ERP" && p.Entity == "DEPT" && p.FinancialYear == 2026 && p.Companies == "1000" && p.Ledgers == "0L" && p.PeriodStart == 1 && p.PeriodEnd == 12, "Wrong preview identity");
                Assert(p.RowCount == 984 && p.UnmappedAccounts == 983 && p.Debit == 268992152391.65m && p.Credit == p.Debit && p.Net == 0, "Wrong preview controls");
                Assert(NORMHelper.Opens == 0 && p.PrecisionValid, "Preview opened reporting transaction"); NoAcceptance();
            });
            Test("confirmation accepts retained bytes, company/ledger and source hash", delegate {
                NORMImportPreview p = Inspect(original); NORMImportOutcome o = NORMImportService.Confirm(p, "isolated-test");
                Assert(o.ImportId == 42 && o.CalculationRunId == 24 && AcceptedRows() == 984, "Acceptance failed");
                var source = NORMHelper.Committed.Find(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_ImportFile"); });
                Assert(NORMCrypto.Sha256((byte[])source["@content"]) == NORMCrypto.Sha256(original), "Source switched");
            });
            Test("cancel handler removes pending bytes and prevents confirmation", delegate {
                NORM_Import page = new NORM_Import(); page.StageForTest(Inspect(original), true); page.CancelForTest();
                Assert(!page.PendingForTest, "Cancel retained pending source"); page.ConfirmForTest(); NoAcceptance();
            });
            Test("page confirms using server preview and HTML-encodes source values", delegate {
                NORM_Import page = new NORM_Import(); page.StageForTest(Inspect(original), true);
                Assert(page.PreviewForTest.Contains("source &amp; file") && !page.PreviewForTest.Contains("source & file"), "Preview markup injection");
                page.ConfirmForTest(); Assert(AcceptedRows() == 984 && page.ResultForTest.Contains("Source accepted") && !page.PendingForTest, "Page acceptance failed: " + page.ErrorForTest);
                int calls = NORMHelper.Committed.Count; page.ConfirmForTest(); Assert(NORMHelper.Committed.Count == calls, "Page replay accepted");
            });
            Test("changed dropdown configuration rejected by page", delegate {
                NORM_Import page = new NORM_Import(); page.StageForTest(Inspect(original), true); page.SwitchReleaseForTest(); page.ConfirmForTest(); NoAcceptance();
                Assert(page.ErrorForTest.Contains("configuration changed"), "No configuration-switch message");
            });
            Test("stale tab token rejected by page", delegate { NORM_Import page = new NORM_Import(); page.StageForTest(Inspect(original), false); page.ConfirmForTest(); NoAcceptance(); });
            Test("forged browser preview fields are ignored", delegate {
                NORM_Import page = new NORM_Import(); page.StageForTest(Inspect(original), true); page.ForgeSummaryForTest(); page.ConfirmForTest();
                Assert(AcceptedRows() == 984, "Browser fields replaced staged summary");
                var row = NORMHelper.Committed.Find(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow"); });
                Assert((string)row["@company"] == "1000", "Browser changed source identity");
            });
            Test("preparer access rechecked on confirmation", delegate { NORM_Import page = new NORM_Import(); page.StageForTest(Inspect(original), true); NORMHelper.PrepareAccess = false; page.ConfirmForTest(); NoAcceptance(); });
            Test("unbalanced legacy preview cannot confirm", delegate { NORMImportPreview p = Inspect(Legacy(false)); Assert(!p.CanAccept && p.Net == 0.01m, "Imbalance hidden"); Fails(delegate { NORMImportService.Confirm(p, "isolated-test"); }); });
            Test("unsupported content cannot preview", delegate { Fails(delegate { Inspect(new byte[] { 1, 2, 3 }); }); });
            Test("owner mismatch rejected", delegate { NORMImportPreview p = Inspect(original); Fails(delegate { NORMImportService.Confirm(p, "another-user"); }); });
            Test("expired preview rejected", delegate { NORMImportPreview p = Inspect(original); p.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1); Fails(delegate { NORMImportService.Confirm(p, "isolated-test"); }); });
            Test("caller buffer cannot change staged bytes", delegate { byte[] b = (byte[])original.Clone(); NORMImportPreview p = Inspect(b); b[0] = 0; Assert(p.Content[0] == 0x50, "Staged content aliases upload buffer"); });
            Test("changed staged bytes rejected", delegate { NORMImportPreview p = Inspect(original); p.Content[0] = 0; Fails(delegate { NORMImportService.Confirm(p, "isolated-test"); }); });
            Test("changed staged identity rejected", delegate { NORMImportPreview p = Inspect(original); p.IdentityFingerprint = "forged"; Fails(delegate { NORMImportService.Confirm(p, "isolated-test"); }); });
            Test("fractional cents cannot be confirmed", delegate {
                byte[] b;
                using (ExcelPackage p = new ExcelPackage(new MemoryStream(original)))
                {
                    ExcelWorksheet s = p.Workbook.Worksheets[1];
                    s.Cells[28,5].Value = "0.001"; s.Cells[28,8].Value = "-79759222.219";
                    s.Cells[29,5].Value = "-0.001"; s.Cells[29,8].Value = "-1293824.121"; b = p.GetAsByteArray();
                }
                NORMImportPreview preview = Inspect(b); Assert(!preview.CanAccept && !preview.PrecisionValid && preview.Net == 0m, "Precision rounded away");
                Fails(delegate { NORMImportService.Confirm(preview, "isolated-test"); });
            });
            Test("changed approved configuration rejected", delegate { NORMImportPreview p = Inspect(original); version = "changed"; Fails(delegate { NORMImportService.Confirm(p, "isolated-test"); }); });
            Test("changed mapping without updated release hash rejected", delegate { NORMImportPreview p = Inspect(original); mappingLine = "Changed"; Fails(delegate { NORMImportService.Confirm(p, "isolated-test"); }); });
            Test("withdrawn approval rejected", delegate { NORMImportPreview p = Inspect(original); approved = false; Fails(delegate { NORMImportService.Confirm(p, "isolated-test"); }); });
            Test("changed permitted ledger rejected", delegate { NORMImportPreview p = Inspect(original); ledger = "2L"; Fails(delegate { NORMImportService.Confirm(p, "isolated-test"); }); });
            Test("configuration rechecked inside acceptance transaction", delegate { NORMImportPreview p = Inspect(original); changeAtCommit = true; Fails(delegate { NORMImportService.Confirm(p, "isolated-test"); }); Assert(snapshotReads == 3, "Transaction snapshot not read"); });
            Test("replacement rejected after preview", delegate { NORMImportPreview p = Inspect(original); NORMHelper.ExistingCompany = true; Fails(delegate { NORMImportService.Confirm(p, "isolated-test"); }); });
            Test("service replay cannot create another import", delegate {
                NORMImportPreview p = Inspect(original); NORMImportService.Confirm(p, "isolated-test"); int count = NORMHelper.Committed.Count;
                try { NORMImportService.Confirm(p, "isolated-test"); } catch (InvalidOperationException) { Assert(NORMHelper.Committed.Count == count, "Replay wrote data"); return; }
                throw new Exception("Replay accepted");
            });
            Test("calculation failure reports accepted source through page", delegate {
                NORM_Import page = new NORM_Import(); page.StageForTest(Inspect(original), true); NORMHelper.FailCalculation = true; page.ConfirmForTest();
                Assert(AcceptedRows() == 984 && page.ResultForTest.Contains("Source accepted") && page.ResultForTest.Contains("calculation failed") && page.ErrorForTest == "", "Accepted source misreported as rejected");
            });
            Test("legacy ERP auto recognition and confirmed acceptance", delegate { NORMImportPreview p = Inspect(Legacy(true)); Assert(p.Layout == "Legacy ERP", "Legacy layout lost"); NORMImportService.Confirm(p, "isolated-test"); Assert(AcceptedRows() == 2, "Legacy acceptance lost"); });
            Test("ROMAN auto recognition and confirmed acceptance", delegate { NORMImportPreview p = Inspect(Roman()); Assert(p.Layout == "ROMAN", "ROMAN layout lost"); NORMImportService.Confirm(p, "isolated-test"); Assert(AcceptedRows() == 2, "ROMAN acceptance lost"); });
            Test("FY2025 legacy pair requirement remains explicit", delegate {
                year = 2025; NORMImportPreview erp = Inspect(Legacy(true)), roman = Inspect(Roman());
                Assert(!erp.CanAccept && !roman.CanAccept, "FY2025 transition bypassed"); NoAcceptance();
            });
            Console.WriteLine("TOTAL PASS " + passed + " (real page handlers/session objects; substituted database/identity hosting)"); return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
    }
}
