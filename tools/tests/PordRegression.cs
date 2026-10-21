using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CPlatform.PORD;

// Identity is replaced only in this isolated test executable. The application
// continues to use LPPI's Windows identity and database-backed admin check.
namespace CPlatform.LPPI
{
    public static class LPPIHelper
    {
        public static string Environment { get { return "UAT"; } }
        public static string CurrentUserDisplayName() { return "Regression reviewer"; }
        public static bool HasLppiAccess() { return false; }
        public static string Setting(string key, string fallback) { return fallback; }
        public static string SapPoNumberHtml(string po) { return po; }
    }
}

public static class PordRegression
{
    private static int passed;
    private static readonly PordDemoStore Store = PordDemoStore.Instance;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        passed++;
    }
    private static PordSaveResult Save(PordPo po, string response, string reason, string evidence, string comments, string poc, string version)
    {
        string status;
        return Store.Save(po.PackageID, poc, new List<PordSaveRow> {
            new PordSaveRow { PoID = po.PoID, Response = response, ReasonCode = reason, EvidenceRef = evidence, Comments = comments,
                TargetDate = DateTime.Today.AddDays(14).ToString("yyyy-MM-dd"), Version = version }
        }, "Test reviewer", out status)[0];
    }
    private static PordPo Army()
    {
        return Store.GetCurrentCyclePos().First(p => p.DmProgram == "ARMY" && p.CheckType == PORDChecks.Nspt);
    }
    public static int Main()
    {
        try
        {
            Check(PORDRules.Categorise("AUD", "Z020", "Z020") == PordCategory.None, "Standard AUD terms");
            Check(PORDRules.Categorise("AUD", "Z005", "Z020") == PordCategory.Override, "5-day override on a 20-day master");
            Check(PORDRules.Categorise("AUD", "Z007", "Z007") == PordCategory.NonStandard, "Non-standard BP master");
            Check(PORDRules.Categorise("AUD", "Z007", "Z020") == PordCategory.NonStandardAndOverride, "Non-standard override");
            Check(PORDRules.Categorise("USD", "Z010", "Z020") == PordCategory.ForeignCurrency, "FX under 14 days");
            Check(PORDRules.Categorise("USD", "Z014", "Z020") == PordCategory.None, "FX at 14 days");
            Check(!PORDRules.TermDays("UNCONFIRMED020").HasValue, "Never infer days from an unknown SAP key");
            Store.Reset();
            var csv = PORDCsvParser.SampleCsv(new List<PordPo> { Army() }).Replace(Army().PoTermKey, "UNMAPPED");
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv)))
            {
                var result = PORDCsvParser.Parse(ms, "test.csv");
                Check(result.HeaderOk && result.Failed == 1 && result.Compliant == 0, "Unmapped keys rejected rather than classed as compliant");
            }
            var po = Army();
            string original = PORDHelper.VersionOf(po.ReviewedDate);
            Check(!Save(po, "Reason", "VR01", "", "Benefit", null, original).Ok, "Objective evidence required");
            Check(!Save(po, "Reason", "VR01", "BN1", "", null, original).Ok, "VR01 comments required");
            Check(!Save(po, "Reason", "VR02", "BN1", "", null, original).Ok, "VR02 comments required");
            Check(!Save(po, "Reason", "VR99", "BN1", "", null, original).Ok, "VR99 comments required");
            Check(!Save(po, "Reason", "BR01", "BN1", "Bank", null, original).Ok, "Reason restricted to its check");
            Check(!Save(po, "Reassign", null, null, "", null, original).Ok, "Reassignment needs an owner");
            Check(!Save(po, "Amend", null, null, "", "someone.else@defence.gov.au", original).Ok, "POC scope enforced");
            var saved = Save(po, "Reason", "VR01", "BN1", "Approved benefit", po.PocEmail, original);
            Check(saved.Ok, "POC can save their own exception");
            Check(Save(po, "Amend", null, null, "", null, original).ErrorCode == "stale", "Stale response rejected");
            for (int i = 0; i < 25; i++)
            {
                string before = PORDHelper.VersionOf(po.ReviewedDate);
                var r = Save(po, "Reason", "VR01", "BN1", "Benefit " + i, null, before);
                Check(r.Ok && r.Version != before, "Wire version must advance even for rapid saves");
            }
            string error;
            Check(!Store.SaveAsFinGroup("DSTG", "bad.defence.gov.au", "AS Fin", out error), "Mailbox must be a valid email address");
            Check(!Store.SaveAsFinGroup("DSTG", "user@defence.gov.au.attacker.example", "AS Fin", out error), "Mailbox domain must match Defence");
            Check(Store.SaveAsFinGroup("DSTG", "asfin.dstg@defence.gov.au", "AS Fin DSTG", out error), "Valid Defence mailbox accepted");

            Store.Reset(); po = Army();
            var historical = Store.GetExclusions()[0];
            historical.PoNumber = po.PoNumber; historical.CheckType = PORDChecks.CcyBank;
            historical.PoTermKey = po.Currency; historical.GrantedDate = DateTime.Today;
            saved = Save(po, "Reason", "VR01", "BN2", "Approved benefit", null, PORDHelper.VersionOf(po.ReviewedDate));
            int auto;
            Check(saved.Ok && Store.Finalise(po.PackageID, "AS Fin", out auto, out error), "AS Fin can finalise");
            Check(Store.GetPos(po.PackageID).All(p => p.IsReviewed), "No response filled automatically at finalise");
            Check(!Save(po, "Amend", null, null, "", null, saved.Version).Ok, "Finalised responses locked");
            var exclusion = Store.GetExclusions().First(x => x.PoNumber == po.PoNumber && x.CheckType == PORDChecks.Nspt);
            Check(exclusion.SourcePackageID == po.PackageID && exclusion.ExpiryDate == DateTime.Today.AddMonths(12), "Exclusion belongs to finalising package and expires in 12 months");
            Store.RevokeExclusion(po.PoNumber, PORDChecks.Nspt, po.PoTermKey, "AS Fin");
            Check(exclusion.IsRevoked && !historical.IsRevoked, "Revocation limited to PO, check and accepted value");
            exclusion.GrantedDate = DateTime.Today.AddDays(-1);
            Check(Store.Unfinalise(po.PackageID, "AS Fin", out error), "Finalised package can reopen");
            Check(!Store.GetExclusions().Contains(exclusion) && Store.GetExclusions().Contains(historical), "Reopen removes only exclusions from that package, across dates");
            Check(Store.GetPos(po.PackageID).Any(p => !p.IsReviewed), "Reopen clears automatic no-response markers");

            Store.Reset(); po = Army();
            Save(po, "Reason", "VR99", "BN99", "Other reason", null, PORDHelper.VersionOf(po.ReviewedDate));
            Store.Finalise(po.PackageID, "AS Fin", out auto, out error);
            Check(!Store.GetExclusions().Any(x => x.PoNumber == po.PoNumber && x.CheckType == po.CheckType), "VR99 never creates an exclusion");
            Store.CloseFinalised("Admin");
            Check(!Store.Unfinalise(po.PackageID, "AS Fin", out error), "Closed packages cannot reopen");
            Check(Store.GetBpIssues().Count > 0, "Seed data includes the DFIM follow-up journey");

            var values = PORDHelper.OpenValues(new List<PordPo> {
                new PordPo { CompanyCode="1000", PoNumber="1", Currency="AUD", StillToDeliver=100 },
                new PordPo { CompanyCode="1000", PoNumber="1", Currency="AUD", StillToDeliver=100 },
                new PordPo { CompanyCode="1000", PoNumber="2", Currency="USD", StillToDeliver=50 }
            }, false);
            Check(values == "AUD 100.00 · USD 50.00", "No double counting across checks or adding unlike currencies");
            Console.WriteLine("PASS: " + passed + " POD regression assertions; C# 5 compatible.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
    }
}
