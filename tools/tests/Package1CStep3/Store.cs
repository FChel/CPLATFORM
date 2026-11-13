using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Web;
using System.Web.Hosting;
using CPlatform.NORM;
using OfficeOpenXml;

// Bounded storage/HTTP substitutes only. Production parser, validation,
// confirmation, persistence command construction and download handler run unchanged.
public static partial class Tests
{
    sealed class StoredPopulation
    {
        public int Id, Release, Year, Run;
        public string Entity, Company, Ledger, Stream, FileName, Hash, RunStatus;
        public DateTime? Start, End;
        public bool? Full;
        public byte[] Bytes;
        public List<string> Rows = new List<string>();
        public string Identity { get { return Key(Entity, Year, Company, Ledger, Stream); } }
    }
    static readonly Dictionary<int, StoredPopulation> store = new Dictionary<int, StoredPopulation>();
    static readonly List<NORMHelper.Call> acceptedAudits = new List<NORMHelper.Call>();
    static readonly List<int> releaseLocks = new List<int>();
    static readonly List<string> scopeLocks = new List<string>();
    static int acceptanceCommits, reinspectionReads, downloads;
    static Func<NORMHelper.Call, DataTable> transactionQueryOverride;
    static string Key(string entity, int year, string company, string ledger, string stream)
    { return entity + "|" + year + "|" + company + "|" + (ledger == null ? "[NULL]" : ledger) + "|" + stream; }
    static string Value(object value) { return value == null || value == DBNull.Value ? "[NULL]" : Convert.ToString(value, CultureInfo.InvariantCulture); }
    static void Seed(int id, int year, string company, string ledger, string stream)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("CONTROLLED retained seed " + id);
        StoredPopulation p = new StoredPopulation { Id=id, Release=year==2020 ? 1 : 2, Year=year, Entity="CONTROLLED", Company=company, Ledger=ledger,
            Stream=stream, Bytes=bytes, Hash=NORMCrypto.Sha256(bytes), FileName="CONTROLLED-previous-"+id+".xlsx", Start=new DateTime(year-1,7,1), End=new DateTime(year,6,30), Full=true, Run=100+id, RunStatus="Complete" };
        p.Rows.Add("CONTROLLED seed row " + id); store.Add(id,p);
    }
    static string StoreFingerprint(bool originalsOnly)
    {
        List<int> ids = new List<int>(store.Keys); ids.Sort(); StringBuilder text = new StringBuilder();
        foreach (int id in ids)
        {
            if (originalsOnly && id >= 42) { continue; }
            StoredPopulation p=store[id]; text.Append(id).Append('|').Append(p.Identity).Append('|').Append(p.Release).Append('|').Append(p.FileName).Append('|').Append(p.Hash)
                .Append('|').Append(NORMCrypto.Sha256(p.Bytes)).Append('|').Append(p.Start).Append('|').Append(p.End).Append('|').Append(p.Full).Append('|').Append(p.Run).Append('|').Append(p.RunStatus);
            foreach(string row in p.Rows) { text.Append('|').Append(row); }
            text.Append('\n');
        }
        return NORMCrypto.Sha256(text.ToString());
    }
    static int Current(string identity)
    {
        int current=0; foreach(StoredPopulation p in store.Values) { if(p.Identity==identity && p.Id>current) { current=p.Id; } } return current;
    }
    static void StoreSetup()
    {
        NORMHelper.TransactionEvents.Clear();
        PartitionSetup(); Find(1).Populations[1][2]="Administered"; Find(2).Populations[0][0]="3000";
        store.Clear(); acceptedAudits.Clear(); releaseLocks.Clear(); scopeLocks.Clear(); acceptanceCommits=reinspectionReads=downloads=0; transactionQueryOverride=null;
        Seed(9,2020,"1000",null,"Departmental"); Seed(10,2020,"1000",null,"Departmental");
        Seed(11,2020,"9900","0L","Administered"); Seed(12,2019,"3000",null,"Departmental");
        NORMHelper.QueryCallResponse=StoreQuery; NORMHelper.CurrentSource=StoreCurrent;
        NORMHelper.OnCommit=ApplyCommit;
        NORMHelper.OnCalculate=delegate(int id) {
            Assert(acceptanceCommits>0 && store.ContainsKey(id),"Calculation ran before acceptance commit"); store[id].Run=1000+id; store[id].RunStatus=NORMHelper.FailCalculation ? "Failed" : "Complete";
        };
        NORMHelper.ScalarCallResponse=ReadStoredOriginal;
        NORMHelper.NonTransactionalExec=delegate(NORMHelper.Call call) {
            Assert(call.Sql.Contains("SOURCE_FILE_DOWNLOADED"),"Unexpected nontransactional mutation"); downloads++;
        };
    }
    static DataTable StoreQuery(NORMHelper.Call call)
    {
        if(call.Sql.Contains("FROM dbo.tblNORM_ConfigurationRelease") && call.Sql.Contains("HOLDLOCK"))
        {
            if(call.InTransaction) { releaseLocks.Add((int)call["@release"]); } else { reinspectionReads++; }
        }
        if(call.InTransaction && transactionQueryOverride!=null)
        { DataTable result=transactionQueryOverride(call); if(result!=null) { return result; } }
        if(call.Sql.Contains("SELECT TOP 1 f.ImportFileId"))
        {
            Assert(!call.Sql.Contains("r.StatusCode"),"Calculation success incorrectly gates accepted originals");
            Assert(call.ParameterNames[0]=="@run" && call.ParameterNames[1]=="@file","Download parameter order changed");
            DataTable t=Table("ImportFileId,SourceType,SourceFileName,SourceFileHash,EntityCode");
            int id=(int)call["@file"];
            if(store.ContainsKey(id) && store[id].Run==(int)call["@run"])
            { StoredPopulation p=store[id]; t.Rows.Add(id,"ERP",p.FileName,p.Hash,p.Entity); }
            return t;
        }
        return Query(call);
    }
    static object ReadStoredOriginal(NORMHelper.Call call)
    {
        Assert(call.Sql.Contains("SELECT f.FileContent") && call.ParameterNames[0]=="@run" && call.ParameterNames[1]=="@file" && call.ParameterNames[2]=="@entity","Incorrect original lookup/order");
        int id=(int)call["@file"];
        return store.ContainsKey(id) && store[id].Run==(int)call["@run"] && store[id].Entity==(string)call["@entity"] ? store[id].Bytes : null;
    }
    static int StoreCurrent(NORMHelper.Call call)
    {
        Assert(call.Sql.Contains("tb.ReportingStream=@stream") && call.Sql.Contains("i.EntityCode=@entity AND i.FinancialYear=@fy") && !call.Sql.Contains("i.ConfigurationReleaseId"),"Current source has incorrect scope");
        Assert(call.Sql.Contains("i.ParentImportId IS NULL") && call.Sql.Contains("i.IsTestBreak=0"),"Derived/test import entered current source lookup");
        string ledger=call.Values.ContainsKey("@ledger") ? (string)call["@ledger"] : null;
        Assert(ledger!=null || call.Sql.Contains("tb.SourceLedger IS NULL"),"NULL ledger collapsed to actual ledger");
        string identity=Key((string)call["@entity"],(int)call["@fy"],(string)call["@company"],ledger,(string)call["@stream"]);
        if(call.InTransaction) { Assert(call.Sql.Contains("UPDLOCK,HOLDLOCK"),"Predecessor lock lost"); scopeLocks.Add(identity); }
        return Current(identity);
    }
    static void ApplyCommit(IList<NORMHelper.Call> calls)
    {
        // Publish accepted rows/files only when the existing transaction commits.
        // This deliberately does not emulate SQL execution, optimiser or locking.
        Dictionary<int,StoredPopulation> additions=new Dictionary<int,StoredPopulation>();
        foreach(NORMHelper.Call call in calls)
        {
            Assert(!call.Sql.StartsWith("DELETE",StringComparison.OrdinalIgnoreCase),"Source history deleted");
            if(call.Sql.Contains("INSERT dbo.tblNORM_Import "))
            {
                Assert(call.ParameterNames[0]=="@release" && call.ParameterNames[1]=="@fy" && call.ParameterNames[2]=="@entity","Import context parameter order changed");
                additions.Add(call.InsertedId,new StoredPopulation { Id=call.InsertedId,Release=(int)call["@release"],Year=(int)call["@fy"],Entity=(string)call["@entity"] });
            }
        }
        if(additions.Count==0) { return; } // Standalone rejected-attempt audit.
        foreach(NORMHelper.Call call in calls)
        {
            if(call.Sql.Contains("INSERT dbo.tblNORM_ImportFile"))
            {
                StoredPopulation p=additions[(int)call["@import"]]; p.Bytes=(byte[])((byte[])call["@content"]).Clone(); p.FileName=(string)call["@file"]; p.Hash=(string)call["@hash"];
                p.Start=call["@coverageStart"]==DBNull.Value ? (DateTime?)null : (DateTime)call["@coverageStart"]; p.End=call["@coverageEnd"]==DBNull.Value ? (DateTime?)null : (DateTime)call["@coverageEnd"]; p.Full=call["@isFullYear"]==DBNull.Value ? (bool?)null : (bool)call["@isFullYear"];
                Assert(p.Hash==NORMCrypto.Sha256(p.Bytes),"Persisted original hash mismatch");
            }
            if(call.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow"))
            {
                StoredPopulation p=additions[(int)call["@import"]]; string company=(string)call["@company"], ledger=call["@ledger"]==DBNull.Value ? null : (string)call["@ledger"], stream=(string)call["@stream"];
                if(p.Rows.Count>0) { Assert(p.Company==company && p.Ledger==ledger && p.Stream==stream,"Several populations collapsed into one import"); }
                p.Company=company; p.Ledger=ledger; p.Stream=stream;
                p.Rows.Add(Value(call["@row"])+"|"+Value(call["@gl"])+"|"+Value(call["@balance"])+"|"+Value(call["@hash"]));
            }
            if(call.Sql.StartsWith("UPDATE",StringComparison.OrdinalIgnoreCase))
            { Assert(call.Sql.Contains("SET StatusCode = 'Imported'") && additions.ContainsKey((int)call["@import"]),"Previous accepted import mutated"); }
        }
        foreach(StoredPopulation p in additions.Values) { Assert(p.Bytes!=null && p.Rows.Count>0,"Partial accepted population"); }
        foreach(StoredPopulation p in additions.Values) { store.Add(p.Id,p); }
        foreach(NORMHelper.Call call in calls) { if(call.Values.ContainsKey("@event")) { acceptedAudits.Add(call); } }
        acceptanceCommits++;
    }
    static List<NORMImportPreview> MixedBatch()
    {
        List<NORMImportPreview> p=FilePopulations(ControlledLegacy(true,true));
        byte[] prior=Modify(ControlledLegacy(false,true),delegate(ExcelWorksheet s) { s.Cells[1,2].Value=2019; s.Cells[2,2].Value="01.07.2018"; s.Cells[3,2].Value="30.06.2019"; s.Cells[5,1].Value="3000"; s.Cells[6,1].Value="3000"; });
        p.AddRange(FilePopulations(prior)); return p;
    }
    static void RejectUnchanged(Action action,string expected)
    {
        string before=StoreFingerprint(false); int commits=acceptanceCommits, runs=NORMHelper.Runs;
        try { action(); }
        catch(Exception error)
        {
            Assert(error.Message.IndexOf(expected,StringComparison.OrdinalIgnoreCase)>=0,"Wrong failure: "+error);
            Assert(StoreFingerprint(false)==before && acceptanceCommits==commits && NORMHelper.Runs==runs,"Failure changed accepted sources/originals/rows/calculations"); return;
        }
        throw new Exception("Expected rejection: "+expected);
    }
    sealed class StoredDownloadRequest : SimpleWorkerRequest
    {
        public MemoryStream Body=new MemoryStream();
        public StoredDownloadRequest(string query) : base("/",Path.GetTempPath(),"source.ashx",query,new StringWriter()) { }
        public override void SendResponseFromMemory(byte[] bytes,int length) { Body.Write(bytes,0,length); }
    }
    static byte[] Download(int importId)
    {
        StoredDownloadRequest request=new StoredDownloadRequest("run="+store[importId].Run+"&file="+importId);
        HttpContext context=new HttpContext(request); context.User=new GenericPrincipal(new GenericIdentity("isolated-test","controlled"),new string[0]);
        new NORMSourceFileHandler().ProcessRequest(context); context.Response.Flush(); Assert(context.Response.StatusCode==200,"Accepted original download failed"); return request.Body.ToArray();
    }
}
