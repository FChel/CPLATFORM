using System;
using System.Data;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Principal;
using System.Web;
using System.Web.Hosting;
using CPlatform.NORM;

public static class Tests
{
    sealed class DownloadRequest : SimpleWorkerRequest
    {
        public MemoryStream Body = new MemoryStream();
        public DownloadRequest() : base("/", Path.GetTempPath(), "source.ashx", "run=24&file=42", new StringWriter()) { }
        public override void SendResponseFromMemory(byte[] data, int length) { Body.Write(data, 0, length); }
    }
    static int passed, year;
    static string company, ledger;
    static bool ambiguous;
    static byte[] retained;
    static string retainedName, retainedHash;
    static void Assert(bool value, string message) { if (!value) { throw new Exception(message); } }
    static DataTable Table(string cols) { DataTable t = new DataTable(); foreach (string c in cols.Split(',')) { t.Columns.Add(c, typeof(object)); } return t; }
    static DataTable Query(string sql)
    {
        DataTable t;
        if (sql.Contains("FROM dbo.tblNORM_ConfigurationRelease")) { t = Table("ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode,ContentHash"); t.Rows.Add(1, year, "ISOLATED-AGENCY", "test-v1", "hash"); return t; }
        if (sql.Contains("FROM dbo.tblNORM_EntityLedger"))
        {
            t = Table("SourceCompanyCode,SourceLedger,ReportingStream,IsRequired"); t.Rows.Add(company, (object)ledger ?? DBNull.Value, "Departmental", true);
            if (ambiguous) { t.Rows.Add(company, "OTHER", "Administered", false); } return t;
        }
        if (sql.Contains("FROM dbo.tblNORM_AccountMap")) { return Table("GlCode,AccountType,StatementLine"); }
        if (sql.Contains("FROM dbo.tblNORM_StatementLine")) { return Table("LineCode,StatementCode"); }
        if (sql.Contains("SELECT TOP 1 f.ImportFileId")) { t = Table("ImportFileId,SourceType,SourceFileName,SourceFileHash,EntityCode"); t.Rows.Add(42, "ERP", retainedName, retainedHash, "ISOLATED-AGENCY"); return t; }
        throw new Exception("Unexpected query " + sql);
    }
    static void Setup(int fy, string coy)
    {
        NORMHelper.Reset(); NORMHelper.ScalarResponse = null; NORMHelper.ExecResponse = null;
        year = fy; company = coy; ledger = null; ambiguous = false;
        NORMHelper.CurrentYear = fy; NORMHelper.QueryResponse = Query;
    }
    static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    static byte[] Change(byte[] b, string from, string to) { string s = Encoding.UTF8.GetString(b); Assert(s.Contains(from), "Mutation target missing"); return Encoding.UTF8.GetBytes(s.Replace(from, to)); }
    static NORMImportPreview Inspect(byte[] b) { return NORMImportService.Inspect(b, "extension.arbitrary", 1, "isolated-test"); }
    static List<NORMHelper.Call> Calls(string text) { return NORMHelper.Committed.FindAll(delegate(NORMHelper.Call c) { return c.Sql.Contains(text); }); }
    static void Rejected(byte[] b)
    {
        bool failed = false;
        try { NORMImportPreview p = Inspect(b); failed = !p.CanAccept; } catch (InvalidDataException) { failed = true; }
        Assert(failed && Calls("INSERT dbo.tblNORM_TrialBalanceRow").Count == 0 && NORMHelper.Runs == 0, "Invalid source accepted");
        Assert(NORMHelper.Committed.Exists(delegate(NORMHelper.Call c) { return c.Values.ContainsKey("@event") && (string)c["@event"] == "IMPORT_REJECTED_SOURCE" && ((string)c["@detail"]).Contains("SHA256=") && ((string)c["@detail"]).Contains("Outcome=Rejected"); }), "Rejection evidence missing");
    }
    static void Accept(byte[] b, int rows, string profile, DateTime start, DateTime end, decimal positive)
    {
        NORMImportPreview p = Inspect(b);
        Assert(p.CanAccept && p.RowCount == rows && p.Net == 0 && p.Debit == positive && p.Credit == positive && p.ProfileVersion == profile, "Wrong amounts/profile/count");
        Assert(p.Entity == "ISOLATED-AGENCY" && p.Companies == company && p.ReportingStreams == "Departmental" && p.CoverageStart == start && p.CoverageEnd == end, "Scope/coverage mismatch");
        Assert(NORMHelper.Committed.Count == 0, "Inspection wrote accepted data");
        NORMImportService.Confirm(p, "isolated-test");
        Assert(Calls("INSERT dbo.tblNORM_TrialBalanceRow").Count == rows, "Row persistence mismatch");
        NORMHelper.Call file = Calls("INSERT dbo.tblNORM_ImportFile")[0];
        byte[] actual = (byte[])file["@content"];
        Assert(Convert.ToBase64String(actual) == Convert.ToBase64String(b) && (string)file["@hash"] == NORMCrypto.Sha256(b), "Original not retained");
        Assert(NORMHelper.Committed.Exists(delegate(NORMHelper.Call c) { return c.Values.ContainsKey("@event") && (string)c["@event"] == "IMPORT_SOURCE_PROFILE" && ((string)c["@detail"]).Contains(profile); }), "Profile provenance not retained");
        retained = actual; retainedName = (string)file["@file"]; retainedHash = (string)file["@hash"];
        Retrieve();
    }
    static void Retrieve()
    {
        NORMHelper.ScalarResponse = delegate { return retained; }; NORMHelper.ExecResponse = delegate { };
        DownloadRequest request = new DownloadRequest(); HttpContext ctx = new HttpContext(request);
        ctx.User = new GenericPrincipal(new GenericIdentity("isolated-test", "test"), new string[0]);
        new NORMSourceFileHandler().ProcessRequest(ctx); ctx.Response.Flush();
        Assert(NORMCrypto.Sha256(request.Body.ToArray()) == retainedHash, "Download hash mismatch");
    }
    public static int Main(string[] args)
    {
        try
        {
            string dir = Path.Combine(args[0], "docs/Trial Balance Examples");
            string[] csvNames = { "AAG_TrialBalanceFinStats150_201718.csv", "AAG_TrialBalanceFinStats150_201819.csv", "AAG_TrialBalanceFinStats150.csv" };
            int[] counts = { 379, 386, 409 }; decimal[] pos = { 10626766953.52m, 11270618549.82m, 11925983562.62m };
            for (int i = 0; i < 3; i++)
            {
                int n = i;
                Test("Genuine CSV " + csvNames[i] + " inspect/confirm/original/profile", delegate { Setup(2018 + n, "150"); Accept(File.ReadAllBytes(Path.Combine(dir, csvNames[n])), counts[n], NORMTextTrialBalanceImporter.CsvProfile, new DateTime(2017 + n, 7, 1), new DateTime(2018 + n, 6, 30), pos[n]); });
            }
            string[] names = { "coy 86.txt", "coy 87.txt", "coy 80.txt" }; string[] companies = { "86", "87", "80" };
            int[] iconRows = { 18, 17, 412 };
            decimal[] iconPositive = { 382097954.00m, 1421192159.48m, 17634789869.35m };
            for (int i = 0; i < 3; i++)
            {
                int n = i;
                Test("Genuine Icon " + names[i] + " all rows/controls/confirmation", delegate
                {
                    Setup(2016, companies[n]); byte[] b = File.ReadAllBytes(Path.Combine(dir, names[n]));
                    NORMParsedImport raw = NORMTextTrialBalanceImporter.Parse(b, 2016);
                    foreach (NORMTrialBalanceRow r in raw.Rows) { Assert(r.OpeningBalance + r.DebitMovement + r.CreditMovement == r.AccumBalance, "Canonical credit sign mismatch"); }
                    Assert(raw.Rows[0].GlText == (n == 2 ? "Petty Cash Floats" : "Intercompany Receivable/(Payabl"), "Supplied description changed");
                    Accept(b, iconRows[n], NORMTextTrialBalanceImporter.FixedProfile, new DateTime(2015, 7, 1), new DateTime(2016, 3, 31), iconPositive[n]);
                });
            }
            byte[] csv = File.ReadAllBytes(Path.Combine(dir, csvNames[2])), icon = File.ReadAllBytes(Path.Combine(dir, names[0]));
            Test("Missing ledger/stream uniquely configured", delegate { Setup(2020, "150"); ledger = "CONFIGURED"; Assert(Inspect(csv).Ledgers == "CONFIGURED", "Missing ledger not resolved"); });
            Test("Ambiguous population rejects with audit", delegate { Setup(2020, "150"); ambiguous = true; Rejected(csv); });
            Test("Company conflict rejects with audit", delegate { Setup(2020, "999"); Rejected(csv); });
            Test("CSV year mismatch rejects", delegate { Setup(2021, "150"); Rejected(csv); });
            Test("CSV header year mismatch rejects", delegate { Setup(2020, "150"); Rejected(Change(csv, "Jun 2019/20", "Jun 2020/21")); });
            Test("CSV malformed prior amount rejects despite CY-only", delegate { Setup(2020, "150"); Rejected(Change(csv, "\"-135000\",\"0\"", "\"-135000\",\"bad\"")); });
            Test("CSV nonzero exact balance rejects", delegate { Setup(2020, "150"); Rejected(Change(csv, "\"-135000\"", "\"-135000.01\"")); });
            Test("CSV balanced fractional cents cannot confirm", delegate { Setup(2020, "150"); byte[] b = Change(Change(csv, "\"-135000\"", "\"-135000.001\""), "\"-14030361.52\"", "\"-14030361.519\""); Rejected(b); });
            Test("CSV duplicate account rejects", delegate { Setup(2020, "150"); Rejected(Change(csv, "\"22511\",", "\"22615\",")); });
            Test("CSV unclosed quote rejects", delegate { Setup(2020, "150"); Rejected(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(csv) + "\"unclosed")); });
            Test("CSV BOM and quoted description edge syntax", delegate { Setup(2020, "150"); byte[] b = Change(csv, "22615 - Accrued Expenses - Capex", "22615 - \"\"quoted\"\", description\ncontinued"); b = Encoding.UTF8.GetBytes("\uFEFF" + Encoding.UTF8.GetString(b)); Assert(Inspect(b).CanAccept, "Quoted CSV/BOM failed"); });
            Test("CSV named map accepts column reordering", delegate {
                Setup(2020, "150"); string[] lines = Encoding.UTF8.GetString(csv).Replace("\r\n", "\n").Split('\n');
                for (int j = 6; j < lines.Length; j++)
                {
                    if (lines[j].StartsWith("\""))
                    {
                        string[] fields = lines[j].Split(new[] { "\",\"" }, StringSplitOptions.None);
                        Assert(fields.Length == 4, "Unexpected fixture quoting");
                        lines[j] = "\"" + fields[1] + "\",\"" + fields[0].Substring(1) + "\",\"" + fields[2] + "\",\"" + fields[3];
                    }
                }
                NORMImportPreview p = Inspect(Encoding.UTF8.GetBytes(String.Join("\n", lines)));
                Assert(p.CanAccept && p.RowCount == 409 && p.Debit == pos[2], "Named mapping changed population");
            });
            Test("CSV decimal precision is never rounded", delegate { Setup(2020, "150"); Rejected(Change(csv, "\"-135000\"", "\"-135000.00000000000000000000000000001\"")); });
            Test("CSV unexpected record rejects", delegate { Setup(2020, "150"); Rejected(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(csv) + "\nCurrency,USD\n")); });
            Test("Unsupported content rejects with audit", delegate { Setup(2020, "150"); Rejected(Encoding.UTF8.GetBytes("Not a trial balance")); });
            Test("Invalid UTF8 rejects", delegate { Setup(2020, "150"); Rejected(new byte[] { 255, 255 }); });
            Test("Icon non-AUD rejects", delegate { Setup(2016, "86"); Rejected(Change(icon, "Currency:  AUD", "Currency:  USD")); });
            Test("Icon absent currency defaults to approved AUD", delegate { Setup(2016, "86"); Assert(Inspect(Change(icon, "Currency:  AUD", "")).CanAccept, "Approved AUD default not applied"); });
            Test("Icon changed period rejects", delegate { Setup(2016, "86"); Rejected(Change(icon, "MAR-16", "APR-16")); });
            Test("Icon row movement mismatch rejects", delegate { Setup(2016, "86"); Rejected(Change(icon, "-12,562.00", "-12,562.01")); });
            Test("Icon footer mismatch rejects", delegate { Setup(2016, "86"); Rejected(Change(icon, "161,053,185.12", "161,053,185.13")); });
            Test("Icon missing footer rejects", delegate { Setup(2016, "86"); Rejected(Change(icon, "161,053,185.12", "")); });
            Test("Icon inconsistent page rejects", delegate { Setup(2016, "80"); byte[] b = File.ReadAllBytes(Path.Combine(dir, names[2])); Rejected(Change(b, "2  of       9", "3  of       9")); });
            Test("Icon unknown line rejects", delegate { Setup(2016, "86"); Rejected(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(icon) + "\nunknown detail\n")); });
            Test("Icon partial completed year rejects", delegate { Setup(2016, "86"); NORMHelper.CurrentYear = 2026; Rejected(icon); });
            Test("Text rejection audit failure remains rejected", delegate { Setup(2020, "150"); NORMHelper.FailAudit = true; bool failed = false; try { Inspect(Change(csv, "\"-135000\"", "\"-135001\"")); } catch (InvalidDataException e) { failed = e.Message.Contains("audit evidence could not be saved"); } Assert(failed && Calls("INSERT dbo.tblNORM_Import ").Count == 0, "Audit failure accepted or hidden"); });
            Test("Text profile provenance write failure rolls back acceptance", delegate { Setup(2020, "150"); NORMImportPreview p = Inspect(csv); NORMHelper.FailAuditCode = "IMPORT_SOURCE_PROFILE"; bool failed = false; try { NORMImportService.Confirm(p, "isolated-test"); } catch (Exception) { failed = true; } Assert(failed && Calls("INSERT dbo.tblNORM_TrialBalanceRow").Count == 0 && NORMHelper.Runs == 0, "Partial accepted profile"); });
            Test("Text unchecked replacement adds no audit", delegate { Setup(2020, "150"); NORMHelper.ExistingCompany = true; NORMImportPreview p = Inspect(csv); try { NORMImportService.Confirm(p, "isolated-test"); } catch (InvalidOperationException) { } Assert(NORMHelper.Committed.Count == 0, "Unchecked acknowledgement audited"); });
            Test("Text duplicate-scope inspection batch records D5 evidence", delegate { Setup(2020, "150"); NORMImportPreview p = Inspect(csv), q = Inspect(csv); bool failed = false; try { NORMImportService.ValidateBatch(new[] { p, q }); } catch (InvalidDataException) { failed = true; } Assert(failed && NORMHelper.Committed.Count == 2 && Calls("INSERT dbo.tblNORM_Import ").Count == 0, "Batch conflict evidence missing"); });
            Test("Text profile version tampering rejects confirmation", delegate { Setup(2020, "150"); NORMImportPreview p = Inspect(csv); p.IdentityFingerprint = "changed"; bool failed = false; try { NORMImportService.Confirm(p, "isolated-test"); } catch (InvalidOperationException) { failed = true; } Assert(failed && Calls("INSERT dbo.tblNORM_Import ").Count == 0, "Stale profile accepted"); });
            Test("Text same-scope replacement acknowledgement and predecessor audit", delegate { Setup(2020, "150"); NORMHelper.ExistingCompany = true; NORMImportPreview p = Inspect(csv); Assert(p.HasReplacement, "Replacement absent"); NORMImportService.ConfirmBatch(new[] { p }, "isolated-test", true); Assert(NORMHelper.Committed.Exists(delegate(NORMHelper.Call c) { return c.Values.ContainsKey("@event") && (string)c["@event"] == "IMPORT_SOURCE_REPLACED"; }), "Predecessor not retained"); });
            Test("Text original retrieval uses actual handler and retained bytes", delegate
            {
                Setup(2016, "86"); NORMHelper.ScalarResponse = delegate { return retained; }; NORMHelper.ExecResponse = delegate { };
                DownloadRequest request = new DownloadRequest();
                {
                    HttpContext ctx = new HttpContext(request);
                    ctx.User = new GenericPrincipal(new GenericIdentity("isolated-test", "test"), new string[0]);
                    new NORMSourceFileHandler().ProcessRequest(ctx); ctx.Response.Flush(); Assert(NORMCrypto.Sha256(request.Body.ToArray()) == retainedHash, "Download hash mismatch");
                }
            });
            Console.WriteLine("TOTAL PASS " + passed + " (genuine positives; isolated database/identity substitutes)"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
