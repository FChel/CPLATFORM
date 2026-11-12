using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Web;
using System.Web.Hosting;
using CPlatform.NORM;

public static class Tests
{
    sealed class Request : SimpleWorkerRequest
    {
        public MemoryStream Body=new MemoryStream();
        public Request(string query) : base("/",Path.GetTempPath(),"source.ashx",query,new StringWriter()) { }
        public override void SendResponseFromMemory(byte[] bytes,int length) { Body.Write(bytes,0,length); }
    }
    static int passed;
    static void Assert(bool value,string reason) { if(!value) throw new Exception(reason); }
    static string Hash(byte[] b) { using(SHA256 h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant(); }
    static HttpContext Invoke(string query,bool authenticated,out Request request)
    {
        request=new Request(query); HttpContext context=new HttpContext(request);
        context.User=new GenericPrincipal(new GenericIdentity(authenticated ? "authorised-test-user" : "",authenticated ? "test" : ""),new string[0]);
        new NORMSourceFileHandler().ProcessRequest(context); context.Response.Flush(); return context;
    }
    static void Good()
    {
        Request request; string run=NORMHelper.RunStatus,import=NORMHelper.ImportStatus;
        HttpContext context=Invoke("run=9&file=10",true,out request);
        Assert(context.Response.StatusCode==200,"Accepted source download denied");
        Assert(Hash(request.Body.ToArray())==NORMHelper.Hash && request.Body.Length==27669,"Original bytes changed");
        Assert(NORMHelper.Audits.Count==2 && NORMHelper.Audits[0]=="existing audit preserved" && NORMHelper.Audits[1].Contains(NORMHelper.Hash),"Download audit not appended correctly");
        Assert(NORMHelper.RunStatus==run && NORMHelper.ImportStatus==import,"Handler changed calculation/acceptance status");
    }
    static void Denied(string query,int status,bool authenticated)
    {
        Request request; HttpContext context=Invoke(query,authenticated,out request);
        Assert(context.Response.StatusCode==status,"Wrong denial status");
        Assert(Hash(request.Body.ToArray())!=NORMHelper.Hash && NORMHelper.Audits.Count==1,"Denied request exposed original or appended success audit");
    }
    static void Test(string name,Action a) { NORMHelper.Reset();a();passed++;Console.WriteLine("PASS "+name); }
    public static int Main(string[] args)
    {
        try
        {
            NORMHelper.Original=File.ReadAllBytes(Path.Combine(args[0],"docs/Trial Balance Examples/AAG_TrialBalanceFinStats150.csv"));
            NORMHelper.Hash=Hash(NORMHelper.Original);
            Assert(NORMHelper.Hash=="9d3a113b3c0ef344c7d3c419f5c8d6e241c9543b2731f4f9d53098cfae5ad631","Genuine fixture changed");
            if(args[1]=="True")
            {
                Test("deployed handler reproduces blocked original for accepted Import 9 / failed Run 9",delegate { Denied("run=9&file=10",404,true); });
                Console.WriteLine("TOTAL PASS "+passed+" baseline reproduction"); return 0;
            }
            Test("accepted Imported source and failed run serve exact genuine bytes; statuses/history unchanged",Good);
            Test("existing Calculated source and Complete run still download",delegate { NORMHelper.ImportStatus="Calculated";NORMHelper.RunStatus="Complete";Good(); });
            Test("accepted source with Running calculation remains retrievable",delegate { NORMHelper.RunStatus="Running";Good(); });
            Test("accepted Imported source with Complete run remains retrievable",delegate { NORMHelper.RunStatus="Complete";Good(); });
            Test("unaccepted Importing source denied even with Complete run",delegate { NORMHelper.ImportStatus="Importing";NORMHelper.RunStatus="Complete";Denied("run=9&file=10",404,true); });
            Test("failed import denied even with Complete run",delegate { NORMHelper.ImportStatus="Failed";NORMHelper.RunStatus="Complete";Denied("run=9&file=10",404,true); });
            Test("foreign file belonging to another import denied",delegate { Denied("run=9&file=11",404,true); });
            Test("mismatched run/file denied",delegate { Denied("run=99&file=10",404,true); });
            Test("inactive run denied",delegate { NORMHelper.RunDeactivated=true;Denied("run=9&file=10",404,true); });
            Test("inactive import denied",delegate { NORMHelper.ImportDeactivated=true;Denied("run=9&file=10",404,true); });
            Test("unauthenticated request denied without querying storage",delegate { Denied("run=9&file=10",403,false);Assert(NORMHelper.MetadataReads==0,"Unauthenticated query"); });
            Test("missing prepare access denied without querying storage",delegate { NORMHelper.Prepare=false;Denied("run=9&file=10",403,true);Assert(NORMHelper.MetadataReads==0,"Unauthorised query"); });
            Test("stored entity access enforced; forged query entity ignored",delegate { NORMHelper.EntityAllowed=false;Denied("run=9&file=10&entity=DEPT",403,true);Assert(NORMHelper.BlobReads==0,"Unauthorised bytes read"); });
            Test("malformed identifiers denied",delegate { Denied("run=invalid&file=10",400,true);Assert(NORMHelper.MetadataReads==0,"Malformed query reached storage"); });
            Test("nonpositive identifiers denied",delegate { Denied("run=9&file=0",400,true); });
            Test("missing retained bytes denied",delegate { NORMHelper.MissingBytes=true;Denied("run=9&file=10",404,true); });
            Test("acceptance rechecked at byte fetch",delegate { NORMHelper.AfterMetadata=delegate {NORMHelper.ImportStatus="Failed";};Denied("run=9&file=10",404,true); });
            Test("run deactivation rechecked at byte fetch",delegate { NORMHelper.AfterMetadata=delegate {NORMHelper.RunDeactivated=true;};Denied("run=9&file=10",404,true); });
            Test("entity binding rechecked at byte fetch",delegate { NORMHelper.AfterMetadata=delegate {NORMHelper.StoredEntity="OTHER";};Denied("run=9&file=10",404,true); });
            Test("audit failure returns no original bytes and preserves history",delegate {
                NORMHelper.AuditFails=true;Request request=new Request("run=9&file=10");HttpContext c=new HttpContext(request);c.User=new GenericPrincipal(new GenericIdentity("authorised-test-user","test"),new string[0]);bool failed=false;
                try {new NORMSourceFileHandler().ProcessRequest(c);}catch(InvalidOperationException){failed=true;}
                Assert(failed && request.Body.Length==0 && NORMHelper.Audits.Count==1,"Audit failure leaked bytes or modified history");
            });
            Console.WriteLine("TOTAL PASS "+passed+" (production handler/HTTP; isolated storage and identity fixtures)");return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e);return 1; }
    }
}
