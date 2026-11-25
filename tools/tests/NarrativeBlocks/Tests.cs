using System;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Script.Serialization;

internal static class NarrativeTests
{
    private const string Policy = "<h3>Accounting policy</h3><p>Suppliers include <strong>goods and services</strong> " +
        "<em>received</em> and <u>recognised</u> in the reporting period.</p>" +
        "<ul><li>Medical supplies</li><li>Contracted services</li></ul>" +
        "<ol><li>Review the supporting schedule.</li></ol><p>m<sup>2</sup> and CO<sub>2</sub><br/>Example units.</p>";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (Exception ex)
        {
            Check(ex.Message.IndexOf(reason, StringComparison.OrdinalIgnoreCase) >= 0, "Unexpected rejection: " + ex);
            return;
        }
        throw new InvalidOperationException("Expected rejection: " + reason);
    }

    private static void Identity(string user)
    {
        HttpContext.Current.User = new GenericPrincipal(new GenericIdentity(user, "Fixture"), new string[0]);
    }

    public static void Main(string[] args)
    {
        Check(Convert.ToInt32(NORMHelper.Scalar("SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB'))")) == 1 &&
            Convert.ToString(NORMHelper.Scalar("SELECT DB_NAME()")).StartsWith("NORM_NarrativeTest_", StringComparison.Ordinal),
            "Narrative tests only run against the disposable LocalDB fixture.");
        HttpContext.Current = new HttpContext(new HttpRequest("", "http://localhost/NORM/", ""), new HttpResponse(TextWriter.Null));
        Identity("Narrative tester");
        Check(NORMNarrativeBlocks.Installed(), "Additive table is unavailable.");
        const string note = "Note|NI_IS_EXP_SUP";
        const string schedule = "Schedule|NI_BS_LIA_IBL_LEA";
        NORMNarrativeBlocks.Save(19, 0, null, "Overview|OVERVIEW", "EntityOverview", "Before", 1,
            "<h3>Basis of preparation</h3><p>NIHRACS provides health and residential aged care services.</p>", "Draft");
        NORMNarrativeBlocks.Save(19, 0, null, "Section|1", "SectionIntroduction", "Before", 1,
            "<p>This section analyses the financial performance of the service.</p>", "Draft");
        NORMNarrativeBlocks.Save(19, 0, null, note, "Disclosure", "Before", 1,
            "<p>Review the supplier expense totals and the policy below.</p>", "Draft");
        NORMNarrativeBlocks.Save(19, 0, null, note, "AccountingPolicy", "After", 2, Policy, "Prepared");
        NORMNarrativeBlocks.Save(19, 0, null, note, "AccountingPolicy", "After", 3,
            "<p>Apply the same classification to the comparative working schedule.</p>", "Draft");
        NORMNarrativeBlocks.Save(19, 0, null, schedule, "Disclosure", "Before", 1,
            "<p>The following schedule summarises the maturity profile of lease liabilities.</p>", "Draft");
        NORMNarrativeBlocks.Save(19, 0, null, "Statement|SOCI", "Disclosure", "After", 1,
            "<p>Statement commentary for the local fixture.</p>", "Draft");
        NORMNarrativeBlocks.Save(19, 0, null, "Schedule|LOCAL_ONLY", "Disclosure", "Before", 1,
            "<p>Available only in the prior year.</p>", "Draft");
        var source = NORMNarrativeBlocks.Load(19);
        var policy = source.Single(item => item.Type == "AccountingPolicy" && item.Sequence == 2);
        Check(policy.Html == Policy && policy.Status == "Prepared" && policy.Placement == "After" &&
            source.Where(item => item.Key == "NI_IS_EXP_SUP").Select(item => item.Sequence).SequenceEqual(new[] { 1, 2, 3 }),
            "Formatting, multiple blocks or sequence did not round trip.");
        Reject(() => NORMNarrativeBlocks.Save(19, 0, null, note, "Disclosure", "Before", 1, "<p>Duplicate</p>", "Draft"), "already used");
        Reject(() => NORMNarrativeBlocks.Save(19, 0, null, "Section|made-up", "Disclosure", "Before", 1, Policy, "Draft"), "not configured");
        Reject(() => NORMNarrativeBlocks.Save(19, 0, null, note, "Disclosure", "Before", 4, Policy, "Approved"), "preparation status");
        Reject(() => NORMNarrativeBlocks.Save(1, 0, null, note, "Disclosure", "Before", 4, Policy, "Draft"), "eligible configuration draft");
        Identity("Unassigned tester");
        Reject(() => NORMNarrativeBlocks.Save(19, 0, null, note, "Disclosure", "Before", 4, Policy, "Draft"), "do not have access");
        Identity("Narrative tester");

        var disclosure = source.Single(item => item.Key == "NI_IS_EXP_SUP" && item.Type == "Disclosure");
        long disclosureId = Int64.Parse(disclosure.Id);
        NORMNarrativeBlocks.Save(19, disclosureId, disclosure.EditToken, note, "Disclosure", "Before", 1,
            "<div>Updated <b onclick='example'>supplier</b> commentary.</div><script>discard</script>", "Draft");
        Check(NORMNarrativeBlocks.Load(19).Single(item => item.Id == disclosure.Id).Html ==
            "<p>Updated <strong>supplier</strong> commentary.</p>", "Sanitised edits did not round trip.");
        Reject(() => NORMNarrativeBlocks.Save(19, disclosureId, disclosure.EditToken, note, "Disclosure", "Before", 1,
            "<p>A stale update.</p>", "Draft"), "changed since");
        Reject(() => NORMNarrativeBlocks.Sanitize("<!DOCTYPE x [<!ENTITY external SYSTEM 'file:///invalid'>]><p>&external;</p>"), "could not be read");
        Reject(() => NORMNarrativeBlocks.Sanitize("<svg><text>Only unsupported content</text></svg>"), "supported narrative text");
        Check(NORMNarrativeBlocks.Sanitize("<p>A<br>B&nbsp;<span style='color:red'>C</span></p>") ==
            "<p>A<br/>B C</p>", "Browser paragraphs or line breaks were lost.");
        string longText = "<p>" + new string('a', 4500) + "</p>";
        NORMNarrativeBlocks.Save(19, 0, null, "Schedule|LOCAL_ONLY", "Disclosure", "After", 2, longText, "Draft");
        Check(NORMNarrativeBlocks.Load(19).Any(item => item.Html == longText), "Long content was truncated.");

        string first = NORMNarrativeBlocks.RollForward(21, 19);
        string again = NORMNarrativeBlocks.RollForward(21, 19);
        Check(first.Contains("7 carried forward") && first.Contains("LOCAL_ONLY") && again.Contains("7 already copied."),
            "Annual rollover did not preserve valid blocks, report missing destinations or skip existing copies.");
        var next = NORMNarrativeBlocks.Load(21);
        var carried = next.Single(item => item.Type == "AccountingPolicy" && item.Sequence == 2);
        Check(next.Count == 7 && next.All(item => item.RolledForward && item.Status == "Draft") &&
            carried.Html == Policy && carried.Placement == "After" && carried.Sequence == 2,
            "Rollover lost formatting, placement, sequence or the annual review marker.");
        Reject(() => NORMNarrativeBlocks.RollForward(23, 19), "same entity");
        Reject(() => NORMNarrativeBlocks.RollForward(22, 19), "preceding year");
        NORMNarrativeBlocks.Save(21, Int64.Parse(carried.Id), carried.EditToken, note, "AccountingPolicy", "After", 2,
            "<p>Updated FY2027 working policy.</p>", "Prepared");
        Check(NORMNarrativeBlocks.Load(19).Single(item => item.Id == policy.Id).Html == Policy &&
            NORMNarrativeBlocks.Load(21).Single(item => item.Id == carried.Id).RolledForward, "Editing target content changed the prior year or annual marker.");

        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction())
        {
            NORMNarrativeBlocks.CopySuccessor(connection, transaction, 19, 22, "Narrative tester");
            transaction.Commit();
        }
        var successor = NORMNarrativeBlocks.Load(22);
        Check(successor.Count == 9 && successor.All(item => item.Status == "Draft") &&
            successor.Single(item => item.Type == "AccountingPolicy" && item.Sequence == 2).Html == Policy &&
            successor.All(item => !source.Any(previous => previous.Id == item.Id)), "Same-year successor did not preserve independent block copies.");
        Check(Convert.ToInt32(NORMHelper.Scalar("SELECT COUNT(*) FROM dbo.tblNORM_AuditEvent WHERE EventCode='NARRATIVE_BLOCKS_ROLLED_FORWARD'")) == 1,
            "Repeat annual rollover wrote an extra audit event.");
        var fixture = new {
            editable = true,
            destinations = NORMNarrativeBlocks.Destinations(21).Select(item => new { value = item.Value, label = item.Label }),
            blocks = next,
            previous = new[] { new { id = 19, label = "FY2026 · v1" } }
        };
        File.WriteAllText(Path.Combine(args[0], "narratives.json"), new JavaScriptSerializer().Serialize(fixture));
        Console.WriteLine("PASS LocalDB authorised create/edit, formatting, long input, markup rejection, stable destinations, stale and cross-release writes");
        Console.WriteLine("PASS LocalDB annual rollover, missing destinations, repeated rollover, independent edits and same-year successor copy");
    }
}
