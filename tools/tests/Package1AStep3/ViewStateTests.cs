using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Web.Hosting;

[Serializable]
public sealed class CapturedResponse
{
    public int Status;
    public string Html;
    public List<string> CookieHeaders = new List<string>();
}
public sealed class LocalRequest : SimpleWorkerRequest
{
    readonly string method, cookie, user;
    readonly byte[] entity;
    readonly MemoryStream body = new MemoryStream();
    public readonly CapturedResponse Result = new CapturedResponse();
    public readonly ManualResetEvent Done = new ManualResetEvent(false);
    public LocalRequest(string verb, string form, string cookies, string identity)
        : base("NORM_YearSetup.aspx", "", new StringWriter())
    { method=verb; entity=Encoding.UTF8.GetBytes(form??""); cookie=cookies; user=identity; }
    public override string GetHttpVerbName() { return method; }
    public override string GetKnownRequestHeader(int index)
    {
        if(index==HeaderCookie) return cookie;
        if(index==HeaderContentType && method=="POST") return "application/x-www-form-urlencoded";
        if(index==HeaderContentLength && method=="POST") return entity.Length.ToString();
        if(index==HeaderUserAgent) return "Isolated ASP.NET session regression";
        return base.GetKnownRequestHeader(index);
    }
    public override string[][] GetUnknownRequestHeaders() { return new[]{new[]{"X-Isolated-User",user}}; }
    public override byte[] GetPreloadedEntityBody() { return entity; }
    public override bool IsEntireEntityBodyIsPreloaded() { return true; }
    public override int GetTotalEntityBodyLength() { return entity.Length; }
    public override void SendStatus(int code,string description) { Result.Status=code; }
    public override void SendKnownResponseHeader(int index,string value)
    { if(index==HeaderSetCookie) Result.CookieHeaders.Add(value); }
    public override void SendUnknownResponseHeader(string name,string value)
    { if(name.Equals("Set-Cookie",StringComparison.OrdinalIgnoreCase)) Result.CookieHeaders.Add(value); }
    public override void SendResponseFromMemory(byte[] data,int length) { body.Write(data,0,length); }
    public override void SendResponseFromFile(string filename,long offset,long length)
    { using(var f=File.OpenRead(filename)){f.Position=offset;byte[] b=new byte[8192];while(length>0){int n=f.Read(b,0,(int)Math.Min(b.Length,length));if(n==0)break;body.Write(b,0,n);length-=n;}} }
    public override void FlushResponse(bool finalFlush) { }
    public override void EndOfRequest() { Result.Html=Encoding.UTF8.GetString(body.ToArray());Done.Set(); }
}
public sealed class AspNetDriver : MarshalByRefObject
{
    public override object InitializeLifetimeService() { return null; }
    public CapturedResponse Request(string verb,string form,string cookie,string user)
    {
        var request=new LocalRequest(verb,form,cookie,user);
        HttpRuntime.ProcessRequest(request);
        if(!request.Done.WaitOne(TimeSpan.FromSeconds(45))) throw new Exception("Isolated ASP.NET request timed out");
        return request.Result;
    }
}
public static class ViewStateTests
{
    static int passed;
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    static string Form(CapturedResponse response,string selected)
    {
        var fields=new Dictionary<string,string>();
        foreach(Match tag in Regex.Matches(response.Html,"<input\\b[^>]*>",RegexOptions.IgnoreCase)) {
            Match name=Regex.Match(tag.Value,"name=\"([^\"]+)\"",RegexOptions.IgnoreCase);
            Match value=Regex.Match(tag.Value,"value=\"([^\"]*)\"",RegexOptions.IgnoreCase);
            if(name.Success&&value.Success) fields[HttpUtility.HtmlDecode(name.Groups[1].Value)]=HttpUtility.HtmlDecode(value.Groups[1].Value);
        }
        Check(fields.ContainsKey("__VIEWSTATE")&&fields.ContainsKey("__EVENTVALIDATION"),"Real WebForms hidden state missing");
        fields["__EVENTTARGET"]="SetupSelection";fields["__EVENTARGUMENT"]="";
        fields["EntitySelection"]="DEPT";fields["SetupSelection"]=selected;
        fields["CurrentFinancialYear"]="2026";fields["ReportingName"]="Defence 2026";
        return String.Join("&",fields.Select(x=>HttpUtility.UrlEncode(x.Key)+"="+HttpUtility.UrlEncode(x.Value)).ToArray());
    }
    static string Cookie(CapturedResponse response)
    {
        foreach(string header in response.CookieHeaders) {Match m=Regex.Match(header,"ASP.NET_SessionId=([^;]+)");if(m.Success)return m.Value;}
        return null;
    }
    static bool Selected(string html,string value)
    {
        Match select=Regex.Match(html,"<select[^>]*name=\"SetupSelection\"[^>]*>(.*?)</select>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
        return select.Success&&Regex.Matches(select.Groups[1].Value,"<option\\b[^>]*>",RegexOptions.IgnoreCase).Cast<Match>().Any(m=>m.Value.Contains("selected=\"selected\"")&&m.Value.Contains("value=\""+value+"\""));
    }
    static bool StateRejected(CapturedResponse r)
    {return r.Status==500&&(r.Html.IndexOf("viewstate",StringComparison.OrdinalIgnoreCase)>=0||r.Html.IndexOf("MAC failed",StringComparison.OrdinalIgnoreCase)>=0);}
    static void Pass(string text) {passed++;Console.WriteLine("PASS "+text);}
    public static int Main(string[] args)
    {
        try {
            var old=(AspNetDriver)ApplicationHost.CreateApplicationHost(typeof(AspNetDriver),"/",Path.GetFullPath(args[0])+Path.DirectorySeparatorChar);
            var first=old.Request("GET",null,null,"isolated-preparer");
            Check(first.Status==200,"Original GET failed: "+first.Status+" "+Regex.Replace(first.Html,"<[^>]+>"," ").Substring(0,Math.Min(900,Regex.Replace(first.Html,"<[^>]+>"," ").Length)));
            Check(Cookie(first)==null,"Original unexpectedly persisted a session cookie");
            Check(StateRejected(old.Request("POST",Form(first,"1"),null,"isolated-preparer")),"Original fresh GET/POST did not reproduce ViewState failure");
            Pass("original production OnInit reproduces missing-cookie fresh-page MAC failure in real ASP.NET");
            if(args.Length==1){Console.WriteLine("TOTAL PASS "+passed+" (reproduction only)");return 0;}
            var fixedApp=(AspNetDriver)ApplicationHost.CreateApplicationHost(typeof(AspNetDriver),"/",Path.GetFullPath(args[1])+Path.DirectorySeparatorChar);
            var get=fixedApp.Request("GET",null,null,"isolated-preparer");
            string cookie=Cookie(get);Check(get.Status==200&&!String.IsNullOrEmpty(cookie),"Corrected initial GET did not establish session cookie");
            Pass("corrected Year Setup GET establishes the existing session before signing ViewState");
            var post=fixedApp.Request("POST",Form(get,"1"),cookie,"isolated-preparer");
            Check(post.Status==200&&Selected(post.Html,"1")&&post.Html.Contains("Defence 2025"),"Corrected FY2025 postback failed: "+post.Status);
            Pass("FY2025 document-year selection succeeds with real signed ViewState and event validation");
            var again=fixedApp.Request("POST",Form(post,"2"),cookie,"isolated-preparer");Check(again.Status==200&&Selected(again.Html,"2"),"Subsequent FY2026 selection failed");
            Pass("subsequent Year Setup postback succeeds with the same session");
            Check(StateRejected(fixedApp.Request("POST",Form(get,"1"),null,"isolated-preparer")),"Missing/expired session accepted old signed state");
            Pass("lost session still rejects stale signed state");
            Check(StateRejected(fixedApp.Request("POST",Form(get,"1"),cookie,"different-isolated-user")),"Another identity accepted signed state");
            Pass("different authenticated identity cannot replay the signed state");
            string tampered=Form(get,"1");Match state=Regex.Match(tampered,"__VIEWSTATE=([^&]+)");Check(state.Success,"No state to tamper");
            tampered=tampered.Remove(state.Groups[1].Index,1).Insert(state.Groups[1].Index,state.Groups[1].Value[0]=='A'?"B":"A");
            Check(StateRejected(fixedApp.Request("POST",tampered,cookie,"isolated-preparer")),"Tampered state accepted");
            Pass("ViewState MAC rejects tampered state");
            Console.WriteLine("TOTAL PASS "+passed+" (real ASP.NET pipeline/session/MAC; isolated identities and database substitutes)");return 0;
        }catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}
    }
}
