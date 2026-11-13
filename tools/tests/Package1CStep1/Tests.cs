using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using CPlatform.NORM;

public static partial class Tests
{
    sealed class Release
    {
        public int Id, Year;
        public string Entity = "CONTROLLED", Status = "Approved";
        public bool Inactive;
        public List<string[]> Populations = new List<string[]>();
    }
    static readonly List<Release> releases = new List<Release>();
    static int passed, candidateReads;
    static string root;
    static byte[] aag2019, aag2020, defence;
    static void Assert(bool condition, string reason) { if (!condition) { throw new Exception(reason); } }
    static DataTable Table(string columns)
    { DataTable t = new DataTable(); foreach (string column in columns.Split(',')) { t.Columns.Add(column, typeof(object)); } return t; }
    static Release Add(int id, int year, string company, string ledger, string stream)
    {
        Release r = new Release { Id = id, Year = year };
        r.Populations.Add(new[] { company, ledger, stream }); releases.Add(r); return r;
    }
    static Release Find(int id) { return releases.Find(delegate(Release r) { return r.Id == id; }); }
    static DataTable Query(NORMHelper.Call call)
    {
        string sql = call.Sql;
        if (sql.Contains("FROM dbo.tblNORM_ConfigurationRelease"))
        {
            Assert(sql.Contains("StatusCode = 'Approved'") || sql.Contains("StatusCode='Approved'"), "Release approval filter lost");
            Assert(sql.Contains("IsDeactivated = 0") || sql.Contains("IsDeactivated=0"), "Release active filter lost");
            DataTable t = Table("ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode,DisplayLabel");
            if (call.Values.ContainsKey("@entity"))
            {
                candidateReads++;
                Assert(call.ParameterNames.Count == 2 && call.ParameterNames[0] == "@entity" && call.ParameterNames[1] == "@fy", "OleDb candidate parameter order changed");
                Assert(!sql.Contains("TOP 1") && !sql.Contains("DESC"), "Candidate query silently selects latest");
            }
            foreach (Release r in releases)
            {
                if (r.Status != "Approved" || r.Inactive) { continue; }
                if (call.Values.ContainsKey("@release") && r.Id != (int)call["@release"]) { continue; }
                if (call.Values.ContainsKey("@entity") && (r.Entity != (string)call["@entity"] || r.Year != (int)call["@fy"])) { continue; }
                t.Rows.Add(r.Id, r.Year, r.Entity, "controlled-v" + r.Id, r.Entity + " FY" + r.Year + " release " + r.Id);
            }
            return t;
        }
        if (sql.Contains("FROM dbo.tblNORM_EntityLedger"))
        {
            DataTable t = Table("SourceCompanyCode,SourceLedger,ReportingStream,IsRequired");
            foreach (string[] population in Find((int)call["@release"]).Populations)
            { t.Rows.Add(population[0], (object)population[1] ?? DBNull.Value, population[2], true); }
            return t;
        }
        if (sql.Contains("FROM dbo.tblNORM_AccountMap")) { return Table("GlCode,AccountType,StatementLine"); }
        if (sql.Contains("FROM dbo.tblNORM_StatementLine")) { return Table("LineCode,StatementCode"); }
        throw new Exception("Unexpected query: " + sql);
    }
    static void Test(string name, Action action)
    {
        NORMHelper.Reset(); releases.Clear(); candidateReads = 0;
        Add(1, 2020, "150", null, "Departmental"); Add(2, 2019, "150", null, "Departmental");
        NORMHelper.CurrentYear = 2020; NORMHelper.QueryCallResponse = Query;
        action(); passed++; Console.WriteLine("PASS " + name);
    }
    static void NoAcceptance()
    {
        Assert(NORMHelper.Runs == 0 && !NORMHelper.Committed.Exists(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_Import ") || c.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow"); }), "Unexpected acceptance/calculation");
    }
    static void Fails(Action action, string expected)
    {
        try { action(); }
        catch (Exception error)
        {
            if (!(error is InvalidOperationException) && !(error is InvalidDataException) && !(error is UnauthorizedAccessException)) { throw; }
            Assert(error.Message.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0, "Wrong failure: " + error.Message);
            NoAcceptance(); return;
        }
        throw new Exception("Expected failure containing " + expected);
    }
    static NORMImportPreview Inspect(byte[] bytes, params int[] explicitIds)
    { return NORMImportService.InspectForAgency(bytes, "CONTROLLED-context.source", 1, explicitIds, "isolated-test"); }
    static void Context(NORMImportPreview p, int year, int release, string company, string ledger, string stream)
    {
        Assert(p.CanAccept && p.Population != null, "Population not validated");
        Assert(p.Population.Entity == "CONTROLLED" && p.Entity == "CONTROLLED" && p.Population.FinancialYear == year && p.FinancialYear == year, "Wrong agency/year");
        Assert(p.Population.Company == company && p.Population.Ledger == ledger && p.Population.ReportingStream == stream, "Wrong five-part identity");
        Assert(p.ReleaseId == release && p.ConfigurationVersion == "controlled-v" + release && p.ApprovedConfigurationFingerprint.Length == 64, "Wrong release/fingerprint");
        Assert(p.FileHash.Length == 64 && p.Population.SourceFileName == p.FileName && p.Token != null && p.CoverageStart.HasValue && p.CoverageEnd.HasValue, "Original/coverage context missing");
    }
    static byte[] ControlledDefence(string company, string ledger, string stream)
    {
        using (ExcelPackage p = new ExcelPackage(new MemoryStream(defence)))
        {
            ExcelWorksheet s = p.Workbook.Worksheets[1];
            for (int row = 1; row <= s.Dimension.End.Row; row++)
            {
                if (s.Cells[row, 1].Text == "1000") { s.Cells[row, 1].Value = company; }
                if (row < 26 && s.Cells[row, 1].Text == "Company Code") { s.Cells[row, 2].Value = company; }
                if (s.Cells[row, 1].Text == "Ledger" || s.Cells[row, 1].Text == "Source Ledger") { s.Cells[row, 2].Value = ledger; }
            }
            s.Cells[26, 1].Value = "Reporting Stream"; s.Cells[26, 2].Value = stream;
            return p.GetAsByteArray();
        }
    }
    static byte[] ControlledLegacy(bool twoPopulations, bool balanced)
    {
        using (ExcelPackage p = new ExcelPackage())
        {
            ExcelWorksheet s = p.Workbook.Worksheets.Add("CONTROLLED mechanics only");
            s.Cells[1, 1].Value = "Fiscal Year"; s.Cells[1, 2].Value = 2020;
            s.Cells[2, 1].Value = "Posting Date From"; s.Cells[2, 2].Value = "01.07.2019";
            s.Cells[3, 1].Value = "Posting Date To"; s.Cells[3, 2].Value = "30.06.2020";
            s.Cells[4, 1].Value = "Company Code"; s.Cells[4, 3].Value = "G/L Account"; s.Cells[4, 8].Value = "Ending Balance in Company Code Currency";
            for (int row = 5; row <= (twoPopulations ? 8 : 6); row++)
            {
                s.Cells[row, 1].Value = row < 7 ? "1000" : "2000"; s.Cells[row, 3].Value = "GL/" + row;
                decimal balance = row % 2 == 1 ? 100m : balanced ? -100m : -99.99m;
                s.Cells[row, 5].Value = balance; s.Cells[row, 6].Value = 0m; s.Cells[row, 7].Value = 0m; s.Cells[row, 8].Value = balance;
            }
            return p.GetAsByteArray();
        }
    }
    public static int Main(string[] args)
    {
        try
        {
            root = args[0]; string fixtures = Path.Combine(root, "docs/Trial Balance Examples");
            aag2019 = File.ReadAllBytes(Path.Combine(fixtures, "AAG_TrialBalanceFinStats150_201819.csv"));
            aag2020 = File.ReadAllBytes(Path.Combine(fixtures, "AAG_TrialBalanceFinStats150.csv"));
            defence = File.ReadAllBytes(Path.Combine(fixtures, "Defence TB - ERP Format FY26.xlsx"));
            Test("existing single-year preparation and confirmation unchanged", delegate {
                NORMImportPreview p = Inspect(aag2020); Context(p, 2020, 1, "150", null, "Departmental");
                Assert(candidateReads == 0 && NORMHelper.Opens == 0, "Primary year was not explicitly bound/read-only");
                NORMImportService.Confirm(p, "isolated-test");
                Assert(NORMHelper.Committed.FindAll(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow"); }).Count == 409, "Single-year persistence changed");
            });
            Test("primary release requires explicit choice rather than newest default", delegate {
                Assert(new NORM_Import().DefaultReleaseSelectionForTest() == "", "Primary release silently defaulted");
            });
            Test("two genuine years bind independently in one controlled agency", delegate {
                NORMImportPreview a = Inspect(aag2019), b = Inspect(aag2020);
                Context(a, 2019, 2, "150", null, "Departmental"); Context(b, 2020, 1, "150", null, "Departmental");
                NORMImportService.ValidateBatch(new[] { a, b });
                Assert(a.ApprovedConfigurationFingerprint != b.ApprovedConfigurationFingerprint && candidateReads == 1 && NORMHelper.Opens == 0, "Wrong member fingerprints/inspection writes");
            });
            Test("ambiguous approved releases never choose newest", delegate {
                Add(9, 2019, "150", null, "Departmental");
                Fails(delegate { Inspect(aag2019); }, "More than one approved");
            });
            Test("explicit older approved release resolves ambiguity", delegate {
                Add(9, 2019, "150", null, "Departmental");
                Context(Inspect(aag2019, 2), 2019, 2, "150", null, "Departmental");
                Assert(candidateReads == 0, "Explicit choice was ignored");
            });
            Test("primary selection also resolves same-year release ambiguity", delegate {
                Add(8, 2020, "150", null, "Departmental");
                Context(Inspect(aag2020), 2020, 1, "150", null, "Departmental");
            });
            Test("unresolved year release rejects clearly", delegate {
                releases.Remove(Find(2)); Fails(delegate { Inspect(aag2019); }, "No active approved");
            });
            Test("inactive candidates cannot auto-bind", delegate {
                Find(2).Inactive = true; Fails(delegate { Inspect(aag2019); }, "No active approved");
            });
            Test("draft candidates cannot auto-bind", delegate {
                Find(2).Status = "Draft"; Fails(delegate { Inspect(aag2019); }, "No active approved");
            });
            Test("explicit inactive release rejects", delegate {
                Find(2).Inactive = true; Fails(delegate { Inspect(aag2019, 2); }, "approved configuration");
            });
            Test("explicit unapproved release rejects", delegate {
                Find(2).Status = "Draft"; Fails(delegate { Inspect(aag2019, 2); }, "approved configuration");
            });
            Test("explicit unavailable release rejects", delegate {
                Fails(delegate { Inspect(aag2019, 999); }, "approved configuration");
            });
            Test("wrong-year explicit inspection cannot authorise a source", delegate {
                Fails(delegate { NORMImportService.Inspect(aag2019, "genuine.csv", 1, "isolated-test"); }, "year/month metadata");
            });
            Test("wrong-year candidates never satisfy detected year", delegate {
                Find(2).Year = 2018; Fails(delegate { Inspect(aag2019, 2); }, "No active approved");
            });
            Test("two explicit releases for one year reject", delegate {
                Add(9, 2019, "150", null, "Departmental"); Fails(delegate { Inspect(aag2019, 2, 9); }, "only one approved");
            });
            Test("cross-agency explicit release rejects even with access to both", delegate {
                Find(2).Entity = "OTHER"; Fails(delegate { Inspect(aag2019, 2); }, "same agency");
            });
            Test("other-agency release cannot auto-bind", delegate {
                Find(2).Entity = "OTHER"; Fails(delegate { Inspect(aag2019); }, "No active approved");
            });
            Test("source company remains subject to permitted configuration", delegate {
                Find(2).Populations[0][0] = "999"; Fails(delegate { Inspect(aag2019); }, "not authorised");
            });
            Test("ambiguous configured population is not guessed", delegate {
                Find(2).Populations.Add(new[] { "150", "0L", "Administered" }); Fails(delegate { Inspect(aag2019); }, "ambiguous");
            });
            Test("duplicate identity rejected across different releases", delegate {
                NORMImportPreview a = Inspect(aag2019, 2); Add(9, 2019, "150", null, "Departmental");
                NORMImportPreview b = Inspect(aag2019, 9);
                Fails(delegate { NORMImportService.ValidateBatch(new[] { a, b }); }, "same agency, financial year");
            });
            Test("batch-level cross-agency contexts rejected", delegate {
                NORMImportPreview a = Inspect(aag2020); Find(1).Entity = "OTHER";
                NORMImportPreview b = NORMImportService.Inspect(aag2020, "other.csv", 1, "isolated-test");
                Fails(delegate { NORMImportService.ValidateBatch(new[] { a, b }); }, "one agency");
            });
            Test("changed same-release configuration still invalidates staged batch", delegate {
                NORMImportPreview a = Inspect(aag2020); Find(1).Populations[0][1] = "0L";
                NORMImportPreview b = Inspect(aag2020);
                Fails(delegate { NORMImportService.ValidateBatch(new[] { a, b }); }, "configuration changed");
            });
            Test("mixed-release confirmation uses one transaction and own contexts", delegate {
                NORMImportPreview a = Inspect(aag2019), b = Inspect(aag2020);
                var outcomes = NORMImportService.ConfirmBatch(new[] { a, b }, "isolated-test", true);
                Assert(outcomes.Count == 2 && NORMHelper.Commits == 1 && a.Consumed && b.Consumed && NORMHelper.Runs == 2, "Mixed batch was not atomic");
            });
            Test("multiple companies ledgers streams and NULL versus 0L remain separate", delegate {
                Add(5, 2026, "1000", "0L", "Departmental");
                Find(5).Populations.Add(new[] { "1000", null, "Departmental" });
                Find(5).Populations.Add(new[] { "2000", "2L", "Administered" });
                Find(5).Populations.Add(new[] { "1000", "0L", "Administered" });
                NORMHelper.CurrentYear = 2026;
                NORMImportPreview a = Inspect(ControlledDefence("1000", "0L", "Departmental")),
                    b = Inspect(ControlledDefence("1000", null, "Departmental")),
                    c = Inspect(ControlledDefence("2000", "2L", "Administered")),
                    d = Inspect(ControlledDefence("1000", "0L", "Administered"));
                Context(a, 2026, 5, "1000", "0L", "Departmental"); Context(b, 2026, 5, "1000", null, "Departmental");
                Context(c, 2026, 5, "2000", "2L", "Administered"); Context(d, 2026, 5, "1000", "0L", "Administered");
                NORMImportService.ValidateBatch(new[] { a, b, c, d });
                Assert(NORMImportService.BatchConfirmationBlockReason(new[] { a, b, c, d }) == null, "Single-release multi-scope path blocked");
            });
            Test("mixed-year page upload preserves contexts when first file differs from primary", delegate {
                NORM_Import page = new NORM_Import();
                page.InspectUploadsForTest(1, new int[0], new[] { aag2019, aag2020 }, new[] { "genuine2019.csv", "genuine2020.csv" });
                Assert(page.ErrorForTest == "" && page.PendingForTest, "Real page inspection failed: " + page.ErrorForTest);
                Context(page.PreviewsForTest[0], 2019, 2, "150", null, "Departmental"); Context(page.PreviewsForTest[1], 2020, 1, "150", null, "Departmental");
                Assert(page.PreviewForTest.Contains("controlled-v2 (release 2)") && page.PreviewForTest.Contains("controlled-v1 (release 1)") && page.PreviewForTest.Contains("2018-07-01 to 2019-06-30") && page.PreviewForTest.Contains("2019-07-01 to 2020-06-30"), "Preview inherited first context");
                Assert(page.ConfirmationEnabledForTest, "Validated mixed confirmation disabled");
                page.ClearUploadedFilesForTest(); page.ConfirmForTest();
                Assert(page.ErrorForTest == "" && page.ResultForTest.Contains("Source accepted") && NORMHelper.Commits == 1, "Mixed page confirmation failed: " + page.ErrorForTest);
            });
            Test("page release list resolves ambiguity through explicit selection", delegate {
                Add(9, 2019, "150", null, "Departmental"); NORM_Import page = new NORM_Import();
                page.InspectUploadsForTest(1, new[] { 2 }, new[] { aag2019, aag2020 }, new[] { "2019.csv", "2020.csv" });
                Assert(page.ErrorForTest == "" && page.PreviewsForTest[0].ReleaseId == 2 && page.ConfirmationEnabledForTest, "Explicit page selection failed: " + page.ErrorForTest);
            });
            Test("one automatically bound year can still use unchanged acceptance", delegate {
                NORM_Import page = new NORM_Import(); page.InspectUploadsForTest(1, new int[0], new[] { aag2019 }, new[] { "genuine2019.csv" });
                Assert(page.ErrorForTest == "" && page.ConfirmationEnabledForTest && page.PreviewsForTest[0].ReleaseId == 2, "Single bound year preview failed");
                page.ClearUploadedFilesForTest(); page.ConfirmForTest();
                Assert(page.ErrorForTest == "" && page.ResultForTest.Contains("Source accepted"), "Page compared first member release with primary selection: " + page.ErrorForTest);
                NORMHelper.Call import = NORMHelper.Committed.Find(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_Import "); });
                Assert((int)import["@release"] == 2 && (int)import["@fy"] == 2019, "Existing transaction received wrong bound context");
            });
            Test("changed additional selection invalidates confirmation", delegate {
                NORM_Import page = new NORM_Import(); page.StageForTest(Inspect(aag2020), true);
                page.ChangeAdditionalForTest(2); page.ConfirmForTest();
                Assert(page.ErrorForTest.Contains("configuration changed") && NORMHelper.Opens == 0, "Changed selection accepted");
            });
            Test("genuine Defence discovery preserves all native facts", delegate {
                Add(5, 2026, "1000", "0L", "Departmental"); NORMHelper.CurrentYear = 2026;
                NORMImportPreview p = Inspect(defence); Context(p, 2026, 5, "1000", "0L", "Departmental");
                Assert(p.RowCount == 984 && p.Net == 0m && p.Debit == 268992152391.65m && p.Credit == p.Debit && p.ProfileVersion == "DEFENCE-ERP/1" && p.CoverageStart == new DateTime(2025, 7, 1) && p.CoverageEnd == new DateTime(2026, 6, 30), "Defence facts changed");
                Assert(p.FileHash == NORMCrypto.Sha256(defence), "Defence original changed");
            });
            Test("genuine CSV discovery preserves counts balance and coverage", delegate {
                NORMImportPreview p = Inspect(aag2019); Assert(p.RowCount == 386 && p.Debit == 11270618549.82m && p.Credit == p.Debit && p.Net == 0m && p.IsFullYear == true && p.ProfileVersion == "ACTEWAGL-FINSTATS/1" && p.FileHash == NORMCrypto.Sha256(aag2019), "CSV facts changed");
            });
            Test("genuine Icon discovery preserves signs controls and partial coverage", delegate {
                Add(4, 2016, "86", null, "Departmental"); NORMHelper.CurrentYear = 2016;
                byte[] bytes = File.ReadAllBytes(Path.Combine(fixtures, "coy 86.txt")); NORMImportPreview p = Inspect(bytes);
                Context(p, 2016, 4, "86", null, "Departmental");
                Assert(p.RowCount == 18 && p.Debit == 382097954m && p.Credit == p.Debit && p.Net == 0m && p.ProfileVersion == "ICON-WATER-FIXED/1" && p.CoverageStart == new DateTime(2015, 7, 1) && p.CoverageEnd == new DateTime(2016, 3, 31) && p.IsFullYear == false, "Icon facts changed");
                NORMParsedImport parsed = NORMTextTrialBalanceImporter.DiscoverForInspection(bytes);
                foreach (NORMTrialBalanceRow row in parsed.Rows) { Assert(row.CreditMovement <= 0m && row.OpeningBalance + row.DebitMovement + row.CreditMovement == row.AccumBalance, "Icon signed movements changed"); }
            });
            Test("non-current partial year remains ineligible after binding", delegate {
                Add(4, 2016, "86", null, "Departmental");
                Fails(delegate { Inspect(File.ReadAllBytes(Path.Combine(fixtures, "coy 86.txt"))); }, "complete financial year");
            });
            Test("legacy source-year discovery preserves inspection identity", delegate {
                Find(1).Populations[0][0] = "1000";
                Context(Inspect(ControlledLegacy(false, true)), 2020, 1, "1000", null, "Departmental");
            });
            Test("Step 1 continues to reject multi-population files", delegate {
                Find(1).Populations[0][0] = "1000";
                Find(1).Populations.Add(new[] { "2000", null, "Departmental" });
                Fails(delegate { Inspect(ControlledLegacy(true, true)); }, "multiple populations");
            });
            Test("nonzero discovered legacy source still disables acceptance", delegate {
                Find(1).Populations[0][0] = "1000";
                NORMImportPreview p = Inspect(ControlledLegacy(false, false));
                Assert(!p.CanAccept && p.Net == 0.01m && p.Errors.Exists(delegate(string e) { return e.Contains("exactly zero"); }), "Exact-zero preview validation bypassed");
                Fails(delegate { NORMImportService.Confirm(p, "isolated-test"); }, "invalid");
            });
            Console.WriteLine("Package 1C Step 1: " + passed + " Passed / 0 Failed");
#if STEP2
            RunStep2(fixtures);
#endif
#if STEP3
            RunStep3(fixtures);
#endif
#if STEP4
            RunStep4(fixtures);
#endif
#if CORRECTION
            RunUatCorrection();
#endif
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
