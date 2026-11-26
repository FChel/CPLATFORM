using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Hosting;
using System.Web.Script.Serialization;

[Serializable]
public sealed class FixtureRequest
{
    public string Page, Query, Method, Cookie, Form, Identity;
}

[Serializable]
public sealed class FixtureResponse
{
    public int Status = 200;
    public string Body;
    public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class FixtureWorker : SimpleWorkerRequest
{
    private readonly FixtureRequest request;
    private readonly byte[] form;
    private readonly MemoryStream body = new MemoryStream();
    public readonly ManualResetEvent Done = new ManualResetEvent(false);
    public readonly FixtureResponse Result = new FixtureResponse();

    public FixtureWorker(FixtureRequest request) : base(request.Page, request.Query, TextWriter.Null)
    {
        this.request = request;
        form = Encoding.UTF8.GetBytes(request.Form ?? "");
    }

    public override string GetHttpVerbName() { return request.Method; }
    public override string GetKnownRequestHeader(int index)
    {
        if (index == HeaderCookie) return request.Cookie;
        if (index == HeaderContentType && request.Method == "POST") return "application/x-www-form-urlencoded";
        if (index == HeaderContentLength && request.Method == "POST") return form.Length.ToString();
        return base.GetKnownRequestHeader(index);
    }
    public override string[][] GetUnknownRequestHeaders()
    { return new[] { new[] { "X-NORM-Fixture-Identity", request.Identity ?? "Manual tester" } }; }
    public override byte[] GetPreloadedEntityBody() { return form; }
    public override int GetTotalEntityBodyLength() { return form.Length; }
    public override bool IsEntireEntityBodyIsPreloaded() { return true; }
    public override void SendStatus(int code, string text) { Result.Status = code; }
    public override void SendKnownResponseHeader(int index, string value)
    { SendUnknownResponseHeader(GetKnownResponseHeaderName(index), value); }
    public override void SendUnknownResponseHeader(string name, string value) { Result.Headers[name] = value; }
    public override void SendResponseFromMemory(byte[] bytes, int length) { body.Write(bytes, 0, length); }
    public override void FlushResponse(bool finalFlush) { }
    public override void EndOfRequest() { Result.Body = Encoding.UTF8.GetString(body.ToArray()); Done.Set(); }
}

public sealed class FixtureApplication : MarshalByRefObject
{
    public override object InitializeLifetimeService() { return null; }
    public FixtureResponse Process(FixtureRequest request)
    {
        var worker = new FixtureWorker(request);
        HttpRuntime.ProcessRequest(worker);
        if (!worker.Done.WaitOne(TimeSpan.FromSeconds(45))) throw new TimeoutException("Hosted Web Forms request timed out.");
        return worker.Result;
    }
}

internal static class Program
{
    public static void Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        var app = (FixtureApplication)ApplicationHost.CreateApplicationHost(typeof(FixtureApplication), "/", args[0]);
        var json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            try { Console.WriteLine(json.Serialize(app.Process(json.Deserialize<FixtureRequest>(line)))); }
            catch (Exception error) { Console.WriteLine(json.Serialize(new { Error = error.ToString() })); }
        }
    }
}
