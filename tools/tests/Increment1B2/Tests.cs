using System;
using System.Data;
using System.IO;
using System.Text;
using System.Web;
using OfficeOpenXml;
using CPlatform.NORM;

public static class Tests
{
    static byte[] original;
    static int passed, year, snapshotReads, release;
    static bool approved, changeAtCommit, dualStream;
    static string mappingLine, version, ledger;
    static DataTable Table(string columns)
    { DataTable t = new DataTable(); foreach (string c in columns.Split(',')) { t.Columns.Add(c, typeof(object)); } return t; }
    static void Assert(bool condition, string message) { if (!condition) { throw new Exception(message); } }
    static void Test(string name, Action action)
    {
        NORMHelper.Reset(); year = 2026; release = 1; approved = true; changeAtCommit = false; dualStream = false; snapshotReads = 0;
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
            if (approved) { t.Rows.Add(release, year, "DEPT", version, "approved-hash"); } return t;
        }
        if (sql.Contains("FROM dbo.tblNORM_EntityLedger"))
        {
            if (sql.Contains("SELECT SourceLedger")) { t = Table("SourceLedger"); t.Rows.Add("1000"); return t; }
            t = Table("SourceCompanyCode,SourceLedger,ReportingStream,IsRequired");
            if (!sql.Contains("IsRequired=1")) { t.Rows.Add("1000", ledger, "Departmental", false); }
            t.Rows.Add("1000", "2L", "Departmental", true);
            t.Rows.Add("1000", DBNull.Value, "Departmental", true);
            if (dualStream) { t.Rows.Add("1000", ledger, "Administered", true); }
            return t;
        }
        if (sql.Contains("FROM dbo.tblNORM_AccountMap")) { t = Table("GlCode,AccountType,StatementLine"); t.Rows.Add("100000", "Income", mappingLine); return t; }
        if (sql.Contains("FROM dbo.tblNORM_StatementLine")) { t = Table("LineCode,StatementCode"); t.Rows.Add(mappingLine, "SOCI"); return t; }
        throw new Exception("Unexpected query: " + sql);
    }
    static NORMImportPreview Inspect(byte[] bytes) { return NORMImportService.Inspect(bytes, "source & file.arbitrary", release, "isolated-test"); }
    static int AcceptedRows() { return NORMHelper.Committed.FindAll(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow"); }).Count; }
    static void NoAcceptance() { Assert(AcceptedRows() == 0 && NORMHelper.Runs == 0 && !NORMHelper.Committed.Exists(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_Import "); }), "Unexpected acceptance"); }
    static void Fails(Action action)
    {
        try { action(); } catch (UnauthorizedAccessException) { NoAcceptance(); return; } catch (InvalidOperationException) { NoAcceptance(); return; } catch (InvalidDataException) { NoAcceptance(); return; }
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
    static NORMImportPreview OtherLedger()
    {
        using (ExcelPackage package = new ExcelPackage(new MemoryStream(original)))
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets[1];
            for (int row = 1; row < 28; row++)
            {
                for (int column = 1; column <= 8; column++)
                {
                    if (sheet.Cells[row,column].Text == "0L") { sheet.Cells[row,column].Value = "2L"; }
                }
            }
            return NORMImportService.Inspect(package.GetAsByteArray(), "other-ledger.xlsx", 1, "isolated-test");
        }
    }

    static byte[] WithStream(string stream)
    {
        using (ExcelPackage package = new ExcelPackage(new MemoryStream(original)))
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets[1];
            sheet.Cells[26,1].Value = "Reporting Stream";
            sheet.Cells[26,2].Value = stream;
            return package.GetAsByteArray();
        }
    }

    // Controlled copies are generated only in memory. The genuine fixture stays unchanged.
    static byte[] WithCoverage(DateTime start, DateTime end)
    {
        using (ExcelPackage package = new ExcelPackage(new MemoryStream(original)))
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets[1];
            for (int row = 1; row < 28; row++)
            {
                if (sheet.Cells[row,1].Text == "Posting Date From")
                { sheet.Cells[row,2].Value = start.ToString("dd.MM.yyyy"); }
                if (sheet.Cells[row,1].Text == "Posting Date To")
                { sheet.Cells[row,2].Value = end.ToString("dd.MM.yyyy"); }
            }
            return package.GetAsByteArray();
        }
    }

    static int ExistingScopedSource(NORMHelper.Call call)
    {
        Assert(call.Sql.Contains("i.EntityCode=@entity AND i.FinancialYear=@fy") &&
            call.Sql.Contains("tb.SourceLedger=@ledger") && call.Sql.Contains("tb.SourceCompanyCode=@company") &&
            call.Sql.Contains("i.ParentImportId IS NULL"), "Source lookup lost its exact scope or included calculation copies");
        Assert((string)call["@entity"] == "DEPT" && (int)call["@fy"] == 2026 && (string)call["@company"] == "1000",
            "Source lookup supplied wrong scope");
        return (string)call["@ledger"] == "0L" ? 2 : 0;
    }

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
            Test("existing source warning and cancellation preserve prior source", delegate {
                NORMHelper.CurrentSource = ExistingScopedSource;
                NORM_Import page = new NORM_Import(); page.StageForTest(Inspect(original), true);
                Assert(page.PreviewForTest.Contains("<div><dt>Configuration</dt><dd>test-v1 (release 1)</dd></div>") &&
                    page.PreviewForTest.Contains("<h3>Existing trial balance will be replaced</h3><p class=\"norm-import-existing-source\">") &&
                    page.PreviewForTest.Contains("2026 figures for Company 1000, Ledger 0L, Departmental already exist. They were imported on 09/11/2026 from prior &amp; original.xlsx.") &&
                    page.PreviewForTest.Contains("Confirming this replacement will replace the existing figures with those from the newly uploaded file. The previous import will be retained as historical evidence.") &&
                    page.PreviewForTest.Contains("<small>The preview expires after 15 minutes.") &&
                    HttpUtility.HtmlDecode(page.PreviewForTest).Contains("01\u201312"), "Replacement wording or period display was incorrect");
                Assert(page.ReplacementLabelForTest == "I confirm replacement of the existing figures." &&
                    page.ConfirmLabelForTest == "Confirm replacement", "Replacement controls were unclear");
                page.CheckReplacementForTest();
                page.CancelForTest();
                Assert(!page.PendingForTest && !page.ReviewVisibleForTest && !page.ReplacementCheckedForTest,
                    "Cancel did not return to file selection and clear the staged replacement");
                page.ConfirmForTest(); NoAcceptance();
                Assert(NORMHelper.Committed.Count == 0, "Cancel modified source or audit history");
            });
            Test("multi-file replacement lists each ledger and uses plural wording", delegate {
                NORMHelper.CurrentSource = call => ExistingScopedSource(call) == 2 ? 2 : 3;
                NORM_Import page = new NORM_Import(); page.StageForTest(Inspect(original), true, OtherLedger());
                Assert(page.PreviewForTest.Contains("Ledger 0L, Departmental already exist") && page.PreviewForTest.Contains("Ledger 2L, Departmental already exist") &&
                    page.PreviewForTest.Contains("Confirming these replacements") &&
                    page.ReplacementLabelForTest == "I confirm replacement of the existing figures." &&
                    page.ConfirmLabelForTest == "Confirm replacement", "Batch replacement wording was unclear");
                NoAcceptance();
            });
            Test("unchecked replacement retains preview and allows retry without upload", delegate {
                NORMHelper.CurrentSource = ExistingScopedSource;
                NORM_Import page = new NORM_Import(); page.StageForTest(Inspect(original), true);
                string preview = page.PreviewForTest;
                int reads = snapshotReads;
                for (int i = 0; i < 2; i++)
                {
                    page.ConfirmForTest(); NoAcceptance();
                    Assert(page.ReplacementErrorForTest.Contains("Please tick the checkbox to confirm replacement.") &&
                        page.PendingForTest && page.ReplacementControlsForTest && page.PreviewForTest == preview,
                        "Checkbox validation discarded the staged preview");
                    Assert(snapshotReads == reads && NORMHelper.Committed.Count == 0, "Unchecked confirmation inspected or wrote data");
                }
                page.CheckReplacementForTest(); page.ConfirmForTest();
                Assert(AcceptedRows() == 984 && !page.PendingForTest && page.ReplacementErrorForTest == "",
                    "Retry did not accept the staged source");
            });
            Test("expired replacement requires inspection even with checkbox unchecked", delegate {
                NORMHelper.CurrentSource = ExistingScopedSource;
                NORMImportPreview preview = Inspect(original);
                NORM_Import page = new NORM_Import(); page.StageForTest(preview, true);
                page.ConfirmForTest();
                preview.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
                page.ConfirmForTest(); NoAcceptance();
                Assert(!page.PendingForTest && page.ErrorForTest.Contains("Inspect the batch again") &&
                    page.ReplacementErrorForTest == "", "Expired preview was treated as recoverable");
            });
            Test("checked replacement retains source and writes linked audit", delegate {
                NORMHelper.CurrentSource = ExistingScopedSource;
                NORM_Import page = new NORM_Import(); page.StageForTest(Inspect(original), true);
                page.CheckReplacementForTest(); page.ConfirmForTest();
                Assert(AcceptedRows() == 984 && page.ResultForTest.Contains("Source accepted"), "Replacement failed: " + page.ErrorForTest);
                var audit = NORMHelper.Committed.Find(c => c.Values.ContainsKey("@event") && (string)c["@event"] == "IMPORT_SOURCE_REPLACED");
                Assert(audit != null && audit.Values.ContainsValue("42") &&
                    audit.Values.ContainsValue("Prior import=2; entity=DEPT; financial year=2026; company=1000; ledger=0L; stream=Departmental"),
                    "Missing replacement link");
                Assert(!NORMHelper.Committed.Exists(c => c.Sql.Contains("DELETE") || c.Values.ContainsValue(2)),
                    "Replacement attempted to mutate historical data");
            });
            Test("replacement and new ledger commit as a single multi-file batch", delegate {
                NORMHelper.CurrentSource = ExistingScopedSource;
                NORMImportPreview first = Inspect(original), second = OtherLedger();
                Assert(first.HasReplacement && !second.HasReplacement && second.Ledgers == "2L", "Replacement crossed ledger boundary");
                var outcomes = NORMImportService.ConfirmBatch(new[] { first, second }, "isolated-test", true);
                Assert(outcomes.Count == 2 && outcomes[0].ImportId != outcomes[1].ImportId &&
                    AcceptedRows() == 1968 && NORMHelper.Runs == 2, "Batch was not accepted");
                Assert(NORMHelper.Committed.FindAll(c => c.Values.ContainsKey("@event") &&
                    (string)c["@event"] == "IMPORT_SOURCE_REPLACED").Count == 1, "Unrelated ledger was marked replaced");
            });
            Test("invalid file leaves the entire replacement batch unchanged", delegate {
                NORMHelper.CurrentSource = ExistingScopedSource;
                var first = Inspect(original);
                NORMHelper.CurrentSource = null;
                var invalid = Inspect(Legacy(false));
                Fails(delegate { NORMImportService.ConfirmBatch(new[] { first, invalid }, "isolated-test", true); });
                Assert(NORMHelper.Opens == 0, "Invalid batch opened a write transaction");
            });
            Test("two files for the same scope are rejected", delegate {
                Fails(delegate { NORMImportService.ValidateBatch(new[] { Inspect(original), Inspect(original) }); });
            });
            Test("failure writing second file rolls back first file and replacement audit", delegate {
                NORMHelper.CurrentSource = ExistingScopedSource;
                var first = Inspect(original); var second = OtherLedger();
                NORMHelper.FailImportFileName = second.FileName;
                Fails(delegate { NORMImportService.ConfirmBatch(new[] { first, second }, "isolated-test", true); });
                Assert(NORMHelper.Committed.Count == 0, "Failed batch retained partial evidence");
            });
            Test("another confirmation changing a prior import invalidates the replacement", delegate {
                NORMHelper.CurrentSource = ExistingScopedSource;
                var first = Inspect(original);
                NORMHelper.CurrentSource = call => {
                    Assert(call.Sql.Contains("UPDLOCK,HOLDLOCK"), "Confirmation lookup lacked transaction locks");
                    return 3;
                };
                Fails(delegate { NORMImportService.ConfirmBatch(new[] { first }, "isolated-test", true); });
            });
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
            Test("ROMAN without identifiable company rejected", delegate { Fails(delegate { Inspect(Roman()); }); });
            Test("FY2025 legacy pair requirement remains explicit", delegate {
                year = 2025; NORMImportPreview erp = Inspect(Legacy(true));
                Assert(!erp.CanAccept, "FY2025 transition bypassed"); Fails(delegate { Inspect(Roman()); });
            });
            Test("missing required populations are independent of optional acceptance", delegate {
                NORMHelper.CurrentSource = delegate(NORMHelper.Call call) {
                    Assert(call.Sql.Contains("tb.ReportingStream=@stream") && !call.Sql.Contains("i.ConfigurationReleaseId"), "Lookup ignores stream or depends on release");
                    return call.Sql.Contains("tb.SourceLedger IS NULL") ? 23 : 0;
                };
                var missing = NORMImportService.MissingRequiredPopulations(1);
                Assert(missing.Count == 1 && missing[0].Ledger == "2L", "Wrong missing required set");
                NORMImportPreview preview = Inspect(original);
                Assert(!preview.HasReplacement, "Absent ledger matched 0L");
                NORMImportService.Confirm(preview, "isolated-test");
                Assert(AcceptedRows() == 984, "Missing required source blocked acceptance");
            });
            Test("absent ledger persists as null and uses IS NULL lookup", delegate {
                NORMHelper.CurrentSource = delegate(NORMHelper.Call call) {
                    Assert(call.Sql.Contains("tb.SourceLedger IS NULL") && !call.Values.ContainsKey("@ledger"), "Wrong absent ledger lookup");
                    return 0;
                };
                NORMImportService.Confirm(Inspect(Legacy(true)), "isolated-test");
                NORMHelper.Call row = NORMHelper.Committed.Find(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow"); });
                Assert(row["@ledger"] == DBNull.Value && (string)row["@stream"] == "Departmental", "Absent ledger or stream lost");
            });
            Test("ambiguous configured streams require distinguishing evidence", delegate {
                dualStream = true;
                Fails(delegate { Inspect(original); });
            });
            Test("same company and ledger can identify both streams in one existing batch", delegate {
                dualStream = true;
                NORMImportPreview departmental = Inspect(WithStream("Departmental"));
                NORMImportPreview administered = Inspect(WithStream("Administered"));
                NORMImportService.ConfirmBatch(new[] { departmental, administered }, "isolated-test", false);
                Assert(AcceptedRows() == 1968 && departmental.ReportingStreams == "Departmental" && administered.ReportingStreams == "Administered", "Streams were merged");
            });
            Test("entity assignment is required for direct release access", delegate {
                NORMHelper.EntityAccess = delegate(string entity) { return entity == "OTHER"; };
                Fails(delegate { Inspect(original); });
                Fails(delegate { NORMImportService.MissingRequiredPopulations(1); });
            });
            Test("assignment revoked after inspection prevents confirmation", delegate {
                NORMImportPreview preview = Inspect(original);
                NORMHelper.EntityAccess = delegate { return false; };
                Fails(delegate { NORMImportService.Confirm(preview, "isolated-test"); });
            });
            Test("confirmation checks access again inside the write transaction", delegate {
                NORMImportPreview preview = Inspect(original);
                int checks = 0;
                NORMHelper.EntityAccess = delegate { return ++checks == 1; };
                Fails(delegate { NORMImportService.Confirm(preview, "isolated-test"); });
                Assert(checks == 2 && NORMHelper.Opens == 1, "Missing transactional recheck");
            });
            Test("unassigned users cannot read a source file via forged run/file IDs", delegate {
                NORMHelper.EntityAccess = delegate(string entity) { return entity == "OTHER"; };
                NORMHelper.QueryResponse = delegate(string sql) {
                    Assert(sql.Contains("r.CalculationRunId = @run AND f.ImportFileId = @file") && !sql.Contains("FileContent"),
                        "Authorisation must use the stored run/file link before reading bytes");
                    DataTable evidence = Table("ImportFileId,SourceType,SourceFileName,SourceFileHash,EntityCode");
                    evidence.Rows.Add(91, "ERP", "protected.xlsx", "hash", "DEPT");
                    return evidence;
                };
                StringWriter output = new StringWriter();
                HttpContext context = new HttpContext(
                    new HttpRequest("", "http://localhost/NORM/NORM_SourceFile.ashx", "run=42&file=91&entity=OTHER"),
                    new HttpResponse(output));
                context.User = new System.Security.Principal.GenericPrincipal(
                    new System.Security.Principal.GenericIdentity("isolated-test", "Windows"), new string[0]);
                new NORMSourceFileHandler().ProcessRequest(context);
                Assert(context.Response.StatusCode == 403 && !output.ToString().Contains("protected.xlsx"), "Cross-entity evidence was exposed");
            });
            Test("forged import IDs cannot create a cross-entity test break", delegate {
                NORMHelper.QueryResponse = delegate(string sql) {
                    Assert(sql.Contains("ImportId = @import"), "Import ID did not scope lookup");
                    DataTable imports = Table("EntityCode");
                    imports.Rows.Add("DEPT");
                    return imports;
                };
                NORMHelper.EntityAccess = delegate { return false; };
                Fails(delegate { NORMImportService.CreateTestBreak(91, 1m, "isolated-test"); });
                Assert(NORMHelper.Opens == 0, "Unauthorised test break reached the write transaction");
            });
            Test("new approved release keeps the prior source current", delegate {
                NORMHelper.CurrentSource = ExistingScopedSource;
                Assert(Inspect(original).HasReplacement, "Baseline did not identify the current scope");
                release = 2;
                version = "test-v2";
                NORMImportPreview next = Inspect(original);
                Assert(next.ReleaseId == 2 && next.HasReplacement, "Approval created a new current scope");
                Fails(delegate { NORMImportService.Confirm(next, "isolated-test"); });
            });
            Test("exact full-year boundaries survive preview", delegate {
                NORMImportPreview preview = Inspect(original);
                Assert(preview.CoverageStart == new DateTime(2025,7,1) &&
                    preview.CoverageEnd == new DateTime(2026,6,30) && preview.IsFullYear == true &&
                    preview.ConfiguredFinancialYear == 2026, "Exact full-year metadata lost");
            });
            Test("configured current year accepts partial coverage", delegate {
                NORMImportPreview preview = Inspect(WithCoverage(new DateTime(2025,8,3), new DateTime(2025,12,19)));
                Assert(preview.IsFullYear == false && preview.CoverageStart == new DateTime(2025,8,3) &&
                    preview.CoverageEnd == new DateTime(2025,12,19), "Partial boundaries were expanded");
                NORMImportService.Confirm(preview, "isolated-test");
                Assert(AcceptedRows() == 984, "Configured current-year partial source rejected");
            });
            Test("same partial source rejected when another year is designated", delegate {
                NORMHelper.CurrentYear = 2027;
                Fails(delegate { Inspect(WithCoverage(new DateTime(2025,7,1), new DateTime(2025,12,31))); });
            });
            Test("non-current full year accepted independently of calendar date", delegate {
                NORMHelper.CurrentYear = 2030;
                NORMImportPreview preview = Inspect(original);
                NORMImportService.Confirm(preview, "isolated-test");
                Assert(preview.ConfiguredFinancialYear == 2030 && AcceptedRows() == 984, "System date determined eligibility");
            });
            Test("periods 01-12 are insufficient for full-year coverage", delegate {
                byte[] bytes = WithCoverage(new DateTime(2025,7,2), new DateTime(2026,6,29));
                NORMImportPreview current = Inspect(bytes);
                Assert(current.PeriodStart == 1 && current.PeriodEnd == 12 && current.IsFullYear == false,
                    "Numeric periods were treated as full coverage");
                NORMHelper.CurrentYear = 2027;
                Fails(delegate { Inspect(bytes); });
            });
            Test("reversed boundaries within the same month are rejected", delegate {
                Fails(delegate { Inspect(WithCoverage(new DateTime(2025,7,19), new DateTime(2025,7,3))); });
            });
            Test("reversed boundaries across months are rejected", delegate {
                Fails(delegate { Inspect(WithCoverage(new DateTime(2025,12,1), new DateTime(2025,8,31))); });
            });
            Test("coverage outside the identified financial year is rejected", delegate {
                Fails(delegate { Inspect(WithCoverage(new DateTime(2025,6,30), new DateTime(2026,6,30))); });
                Fails(delegate { Inspect(WithCoverage(new DateTime(2025,7,1), new DateTime(2026,7,1))); });
            });
            Test("missing year designation cannot fall back to release or calendar", delegate {
                NORMHelper.CurrentYear = null;
                Fails(delegate { Inspect(original); });
                Fails(delegate { NORMImportService.ConfiguredCurrentYear("DEPT"); });
            });
            Test("year change between inspection and confirmation rejects even a full source", delegate {
                NORMImportPreview preview = Inspect(original);
                NORMHelper.CurrentYear = 2027;
                Fails(delegate { NORMImportService.Confirm(preview, "isolated-test"); });
            });
            Test("year designation is locked and rechecked inside acceptance transaction", delegate {
                NORMImportPreview preview = Inspect(WithCoverage(new DateTime(2025,7,1), new DateTime(2025,12,31)));
                int reads = 0;
                NORMHelper.ReadYear = delegate { return ++reads == 1 ? 2026 : 2027; };
                Fails(delegate { NORMImportService.Confirm(preview, "isolated-test"); });
                Assert(reads == 2 && NORMHelper.Committed.Count == 0, "Acceptance failed to recheck year transactionally");
            });
            Test("exact metadata and coverage audit are persisted with retained bytes", delegate {
                byte[] bytes = WithCoverage(new DateTime(2025,8,3), new DateTime(2025,12,19));
                NORMImportService.Confirm(Inspect(bytes), "isolated-test");
                NORMHelper.Call file = NORMHelper.Committed.Find(delegate(NORMHelper.Call call) {
                    return call.Sql.Contains("INSERT dbo.tblNORM_ImportFile");
                });
                Assert(file.Sql.Contains("CoverageStart,CoverageEnd,IsFullYear") &&
                    (DateTime)file["@coverageStart"] == new DateTime(2025,8,3) &&
                    (DateTime)file["@coverageEnd"] == new DateTime(2025,12,19) &&
                    (bool)file["@isFullYear"] == false &&
                    NORMCrypto.Sha256((byte[])file["@content"]) == NORMCrypto.Sha256(bytes), "Coverage persistence parameters or original changed");
                Assert(NORMHelper.Committed.Exists(delegate(NORMHelper.Call call) {
                    return call.Values.ContainsKey("@event") &&
                        (string)call["@event"] == "IMPORT_COVERAGE_VALIDATION" &&
                        ((string)call["@detail"]).Contains("Coverage=2025-08-03/2025-12-19;Partial") &&
                        ((string)call["@detail"]).Contains("ConfiguredCurrentYear=2026;Result=Pass");
                }), "Exact coverage validation evidence missing");
            });
            Test("replacement changes coverage without changing scope or appending history", delegate {
                NORMHelper.CurrentSource = ExistingScopedSource;
                NORMImportPreview full = Inspect(original);
                NORMImportPreview partial = Inspect(WithCoverage(new DateTime(2025,7,1), new DateTime(2025,12,31)));
                Assert(full.Scopes.Count == 1 && partial.Scopes.Count == 1, "Unexpected source scope count");
                foreach (string key in full.Scopes.Keys) {
                    Assert(partial.Scopes.ContainsKey(key) && partial.Scopes[key].ImportId == full.Scopes[key].ImportId,
                        "Coverage changed the replacement identity");
                }
                NORMImportService.ConfirmBatch(new[] { partial }, "isolated-test", true);
                Assert(NORMHelper.Committed.FindAll(delegate(NORMHelper.Call call) {
                    return call.Sql.Contains("INSERT dbo.tblNORM_ImportFile");
                }).Count == 1, "Replacement appended extra evidence");
                Assert(!NORMHelper.Committed.Exists(delegate(NORMHelper.Call call) {
                    return call.Sql.StartsWith("DELETE") || call.Sql.Contains("UPDATE dbo.tblNORM_ImportFile");
                }), "Replacement altered historical evidence");
            });
            Test("coverage write failure rolls back acceptance", delegate {
                NORMHelper.FailAuditCode = "IMPORT_COVERAGE_VALIDATION";
                bool failed = false;
                try { NORMImportService.Confirm(Inspect(original), "isolated-test"); }
                catch (Exception error) {
                    Assert(error.Message == "private connection detail", "Unexpected injected failure");
                    failed = true;
                }
                Assert(failed && NORMHelper.Committed.Count == 0, "Coverage audit failure left partial acceptance");
                NoAcceptance();
            });
            Test("invalid source date is rejected", delegate {
                using (ExcelPackage package = new ExcelPackage(new MemoryStream(original)))
                {
                    ExcelWorksheet sheet = package.Workbook.Worksheets[1];
                    for (int row = 1; row < 28; row++) {
                        if (sheet.Cells[row,1].Text == "Posting Date From") {
                            sheet.Cells[row,2].Value = "31.02.2026";
                        }
                    }
                    byte[] invalid = package.GetAsByteArray();
                    Fails(delegate { Inspect(invalid); });
                }
            });
            Test("numeric-only historical evidence retains unknown exact coverage", delegate {
                NORMParsedImport historical = RomanTrialBalanceImporter.Parse(Roman(), new[] { "1000" }, 2026);
                Assert(historical.PeriodStart == 1 && historical.PeriodEnd == 12 &&
                    historical.CoverageStart == null && historical.CoverageEnd == null &&
                    historical.IsFullYear == null, "Numeric periods invented full-year dates");
            });
            Console.WriteLine("TOTAL PASS " + passed + " (real page handlers/session objects; substituted database/identity hosting)"); return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
    }
}
