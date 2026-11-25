using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Web.Compilation;
using System.Web.Hosting;

[Serializable]
public sealed class NarrativeResponse
{
    public int Status;
    public string Html;
    public string SessionCookie;
}

public sealed class NarrativeRequest : SimpleWorkerRequest
{
    private readonly string method;
    private readonly string cookie;
    private readonly string fault;
    private readonly bool administrator;
    private readonly byte[] form;
    private readonly MemoryStream body = new MemoryStream();
    public readonly ManualResetEvent Done = new ManualResetEvent(false);
    public readonly NarrativeResponse Result = new NarrativeResponse();

    public NarrativeRequest(string method, string form, string cookie, bool administrator, string fault)
        : base(administrator ? "NORM_AdminUsers.aspx" : "NORM_Reporting.aspx", administrator ? "" : "narratives=1&release=19", TextWriter.Null)
    {
        this.method = method;
        this.cookie = cookie;
        this.form = Encoding.UTF8.GetBytes(form ?? "");
        this.administrator = administrator;
        this.fault = fault;
    }

    public NarrativeRequest(string method, string form, string cookie, string page)
        : base(page, "", TextWriter.Null)
    {
        this.method = method;
        this.cookie = cookie;
        this.form = Encoding.UTF8.GetBytes(form ?? "");
    }

    public override string GetHttpVerbName() { return method; }
    public override string GetKnownRequestHeader(int index)
    {
        if (index == HeaderCookie) return cookie;
        if (index == HeaderContentType && method == "POST") return "application/x-www-form-urlencoded";
        if (index == HeaderContentLength && method == "POST") return form.Length.ToString();
        return base.GetKnownRequestHeader(index);
    }
    public override byte[] GetPreloadedEntityBody() { return form; }
    public override string[][] GetUnknownRequestHeaders()
    { return new[] { new[] { "X-NORM-Fixture-Admin", administrator ? "1" : "0" }, new[] { "X-NORM-Fixture-Fault", fault ?? "" } }; }
    public override int GetTotalEntityBodyLength() { return form.Length; }
    public override bool IsEntireEntityBodyIsPreloaded() { return true; }
    public override void SendStatus(int status, string description) { Result.Status = status; }
    public override void SendKnownResponseHeader(int index, string value)
    {
        if (index == HeaderSetCookie) SendUnknownResponseHeader("Set-Cookie", value);
    }
    public override void SendUnknownResponseHeader(string name, string value)
    {
        if (!name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)) return;
        Match match = Regex.Match(value, @"ASP\.NET_SessionId=[^;]+");
        if (match.Success) Result.SessionCookie = match.Value;
    }
    public override void SendResponseFromMemory(byte[] data, int length) { body.Write(data, 0, length); }
    public override void FlushResponse(bool finalFlush) { }
    public override void EndOfRequest() { Result.Html = Encoding.UTF8.GetString(body.ToArray()); Done.Set(); }
}

public sealed class NarrativePostbackHost : MarshalByRefObject
{
    public override object InitializeLifetimeService() { return null; }
    public NarrativeResponse Request(string method, string form, string cookie, bool administrator = false, string fault = null)
    {
        return Process(new NarrativeRequest(method, form, cookie, administrator, fault));
    }

    public NarrativeResponse PreparersAdminRequest(string form, string cookie)
    { return Process(new NarrativeRequest(form == null ? "GET" : "POST", form, cookie, "NORM_AdminUsers.aspx")); }

    private static NarrativeResponse Process(NarrativeRequest request)
    {
        HttpRuntime.ProcessRequest(request);
        if (!request.Done.WaitOne(TimeSpan.FromSeconds(60))) throw new InvalidOperationException("Hosted NORM Reporting request timed out.");
        return request.Result;
    }

    public bool HasTestBreakEntryPoint()
    {
        var page = BuildManager.GetCompiledType("~/NORM_Statements.aspx");
        var importService = BuildManager.GetType("NORMImportService", true, false);
        return page.GetMethod("CreateBreak_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null ||
            importService.GetMethod("CreateTestBreak") != null;
    }
}

internal static class HostedPostbackTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static Dictionary<string, string> HiddenFields(string page)
    {
        var fields = new Dictionary<string, string>();
        foreach (Match tag in Regex.Matches(page, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (!Regex.IsMatch(tag.Value, "\\btype=\"hidden\"", RegexOptions.IgnoreCase)) continue;
            Match name = Regex.Match(tag.Value, "\\bname=\"([^\"]+)\"");
            Match value = Regex.Match(tag.Value, "\\bvalue=\"([^\"]*)\"");
            if (name.Success && value.Success) fields[name.Groups[1].Value] = HttpUtility.HtmlDecode(value.Groups[1].Value);
        }
        return fields;
    }

    private static string EncodeForm(Dictionary<string, string> fields)
    { return String.Join("&", fields.Select(field => HttpUtility.UrlEncode(field.Key) + "=" + HttpUtility.UrlEncode(field.Value))); }

    private static string Form(string page, string destination, string token, int sequence = 9)
    {
        var fields = HiddenFields(page);
        Check(fields.ContainsKey("__VIEWSTATE") && fields.ContainsKey("__EVENTVALIDATION") && fields.ContainsKey("narrativeToken"),
            "Hosted form is missing the actual state or narrative token.");
        if (token != null) fields["narrativeToken"] = token;
        fields["SaveNarrativeBlock"] = "Save block";
        fields["blockId"] = "0";
        fields["blockDestination"] = destination;
        fields["blockHtml"] = "<p>Hosted <strong>policy</strong><span style='color:red'> wording</span>.</p>";
        fields["blockType"] = "AccountingPolicy";
        fields["blockPlacement"] = "After";
        fields["blockSequence"] = sequence.ToString();
        fields["blockStatus"] = "Draft";
        return EncodeForm(fields);
    }

    private static string LookupForm(NarrativeResponse page, string reference)
    {
        var fields = HiddenFields(page.Html);
        fields["DiagnosticReference"] = reference;
        fields["FindDiagnosticButton"] = "Find diagnostic";
        return EncodeForm(fields);
    }

    private static string CheckFailure(NarrativeResponse response, string expected)
    {
        Match reference = Regex.Match(response.Html, @"Reference: ([a-f0-9]{32})\.");
        Check(response.Status == 500 && response.Html.Contains(expected) && reference.Success &&
            response.Html.Contains("Return to narrative editor") && !response.Html.Contains("Fixture confidential") &&
            !response.Html.Contains("Hosted ") && !response.Html.Contains("InvalidOperationException"),
            "Narrative failure did not show a safe " + expected + " outcome. HTTP " + response.Status);
        return reference.Groups[1].Value;
    }

    public static int Main(string[] args)
    {
        try
        {
            var app = (NarrativePostbackHost)ApplicationHost.CreateApplicationHost(typeof(NarrativePostbackHost), "/", Path.GetFullPath(args[0]) + Path.DirectorySeparatorChar);
            var initial = app.Request("GET", null, null);
            Check(initial.Status == 200 && !String.IsNullOrEmpty(initial.SessionCookie), "Hosted narrative GET did not establish its session. HTTP " + initial.Status);
            const string note = "Note|NI_IS_EXP_SUP";
            var success = app.Request("POST", Form(initial.Html, note, null), initial.SessionCookie);
            Check(success.Status == 200 && success.Html.Contains("Narrative saved."), "Hosted rich-text save failed. HTTP " + success.Status);
            var reopened = app.Request("GET", null, initial.SessionCookie);
            Check(reopened.Status == 200 && reopened.Html.Replace("\\u003c", "<").Replace("\\u003e", ">").Contains("Hosted <strong>policy</strong> wording."),
                "Stored formatted content did not reload in the hosted editor.");
            var malformed = app.Request("POST", Form(reopened.Html, "<p>Invalid destination</p>", null), initial.SessionCookie);
            Check(malformed.Status == 500 && malformed.Html.Contains("Reference:"), "HTML outside the narrative content no longer received normal request validation.");
            var wrongToken = app.Request("POST", Form(reopened.Html, note, "wrong-token"), initial.SessionCookie);
            Check(wrongToken.Status >= 400 && !wrongToken.Html.Contains("Narrative saved."), "Hosted save accepted an invalid narrative token.");
            Console.WriteLine("PASS real Web Forms narrative GET and rich HTML save, sanitised reload, ordinary field validation and session token");
            var before = app.Request("POST", Form(reopened.Html, note, null, 6), initial.SessionCookie, fault: "before");
            var after = app.Request("POST", Form(reopened.Html, note, null, 7), initial.SessionCookie, fault: "after");
            var uncertain = app.Request("POST", Form(reopened.Html, note, null, 8), initial.SessionCookie, fault: "uncertain");
            string[] references = { CheckFailure(before, "Not saved."), CheckFailure(after, "Saved. Reopen"),
                CheckFailure(uncertain, "Could not confirm.") };
            Check(!uncertain.Html.Contains("Not saved."), "Commit uncertainty was presented as a confirmed failure.");
            Console.WriteLine("PASS hosted failures before commit, after commit and without commit acknowledgement show distinct safe outcomes");

            var admin = app.Request("GET", null, null, administrator: true);
            Check(admin.Status == 200 && admin.Html.Contains("Find a diagnostic"), "Administrator diagnostic form is unavailable.");
            string[] outcomes = { "Not saved.", "Saved.", "Could not confirm." };
            for (int index = 0; index < references.Length; index++)
            {
                var found = app.Request("POST", LookupForm(admin, references[index]), admin.SessionCookie, administrator: true);
                Check(found.Status == 200 && found.Html.Contains("UTC") && found.Html.Contains("Save narrative block.") &&
                    found.Html.Contains(outcomes[index]) && !found.Html.Contains("Fixture confidential") && !found.Html.Contains("Hosted ") &&
                    !found.Html.Contains("InvalidOperationException") && !found.Html.Contains("HResult") && !found.Html.Contains("Method="),
                    "Administrator lookup did not return the safe exact-reference summary.");
            }
            var legacy = app.Request("POST", LookupForm(admin, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), admin.SessionCookie, administrator: true);
            Check(legacy.Status == 200 && legacy.Html.Contains("Save outcome was not recorded.") && !legacy.Html.Contains("Fixture confidential"), "Legacy diagnostic summary is misleading or discloses details.");
            var missing = app.Request("POST", LookupForm(admin, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"), admin.SessionCookie, administrator: true);
            Check(missing.Status == 200 && missing.Html.Contains("No diagnostic found for this reference."), "Unknown exact reference was not handled.");
            var partial = app.Request("POST", LookupForm(admin, references[0].Substring(0, 16)), admin.SessionCookie, administrator: true);
            Check(partial.Status == 200 && partial.Html.Contains("Enter the complete 32-character diagnostic reference."), "Partial diagnostic reference was accepted.");
            Check(app.PreparersAdminRequest(null, initial.SessionCookie).Status == 403 &&
                app.PreparersAdminRequest(LookupForm(admin, references[0]), initial.SessionCookie).Status >= 400,
                "An ordinary preparer could reach the administrator diagnostic lookup.");
            Console.WriteLine("PASS administrator exact-reference lookup, safe legacy and missing results, and denied preparer requests");
            Check(!app.HasTestBreakEntryPoint(), "Production Statements or its import service still exposes a test-break entry point.");
            Console.WriteLine("PASS the compiled production site has no Statements test-break handler or import operation");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error.Message); return 1; }
    }
}
