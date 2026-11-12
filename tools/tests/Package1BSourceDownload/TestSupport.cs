using System;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;

public sealed class TestParameter { public string Name; public object Value; }

// Storage/identity boundary only. Production handler and HTTP response path are real.
public static class NORMHelper
{
    public static byte[] Original;
    public static string Hash, ImportStatus, RunStatus, StoredEntity;
    public static bool Prepare, EntityAllowed, RunDeactivated, ImportDeactivated, MissingBytes, AuditFails;
    public static Action AfterMetadata;
    public static int MetadataReads, BlobReads;
    public static List<string> Audits = new List<string>();
    public static void Reset()
    {
        ImportStatus="Imported"; RunStatus="Failed"; StoredEntity="UAT_AAG";
        Prepare=EntityAllowed=true; RunDeactivated=ImportDeactivated=MissingBytes=AuditFails=false;
        AfterMetadata=null; MetadataReads=BlobReads=0; Audits.Clear(); Audits.Add("existing audit preserved");
    }
    public static bool HasPrepareAccess() { return Prepare; }
    public static bool CanPrepareEntity(string entity) { return EntityAllowed && entity=="UAT_AAG"; }
    public static string CurrentUserId() { return "authorised-test-user"; }
    public static TestParameter P(string name,object value) { return new TestParameter { Name=name,Value=value }; }
    public static string Str(DataRow row,string col) { return Convert.ToString(row[col]); }
    static object Value(TestParameter[] p,string name)
    { foreach(TestParameter x in p) { if(x.Name==name) return x.Value; } throw new Exception("Missing query parameter "+name); }
    static bool Matches(string sql,TestParameter[] p,bool blob)
    {
        string q=Regex.Replace(sql,@"\s+","").ToLowerInvariant();
        if(!q.Contains("r.calculationrunid=@run") || !q.Contains("f.importfileid=@file"))
            throw new Exception("Handler lost the parameterised run/file filters");
        // Each join/predicate is required by the fixture evaluator. Missing
        // clauses would expose the wrong fixture and fail negative HTTP cases.
        bool run=(int)Value(p,"@run")==9;
        bool file=(int)Value(p,"@file")==10;
        if(q.Contains("r.calculationrunid=@run") && !run) return false;
        if(q.Contains("f.importfileid=@file") && !file) return false;
        // Fixture file 11 belongs to another import/entity, never to run 9.
        if(q.Contains("r.importid=f.importid") && q.Contains("i.importid=r.importid") && (!run || !file)) return false;
        if(!q.Contains("r.importid=f.importid") || !q.Contains("i.importid=r.importid"))
            throw new Exception("Handler lost an import/file/run join");
        if(q.Contains("r.isdeactivated=0") && RunDeactivated) return false;
        if(q.Contains("i.isdeactivated=0") && ImportDeactivated) return false;
        if(q.Contains("r.statuscode='complete'") && RunStatus!="Complete") return false;
        if(q.Contains("i.statuscodein('imported','calculated')") && ImportStatus!="Imported" && ImportStatus!="Calculated") return false;
        if(blob)
        {
            if(!q.Contains("i.entitycode=@entity")) throw new Exception("Blob fetch lost stored-entity binding");
            if((string)Value(p,"@entity")!=StoredEntity) return false;
        }
        return true;
    }
    public static DataTable Query(string sql,params TestParameter[] p)
    {
        MetadataReads++;
        DataTable t=new DataTable();
        foreach(string name in new[] {"ImportFileId","SourceType","SourceFileName","SourceFileHash","EntityCode"}) t.Columns.Add(name,typeof(object));
        if(Matches(sql,p,false)) t.Rows.Add(10,"ERP","AAG_TrialBalanceFinStats150.csv",Hash,StoredEntity);
        if(AfterMetadata!=null) AfterMetadata();
        return t;
    }
    public static object Scalar(string sql,params TestParameter[] p)
    { BlobReads++; return Matches(sql,p,true) && !MissingBytes ? Original : null; }
    public static int Exec(string sql,params TestParameter[] p)
    {
        if(!sql.Contains("'SOURCE_FILE_DOWNLOADED'")) throw new Exception("Unexpected write outside existing download audit");
        if(AuditFails) throw new InvalidOperationException("Injected audit failure");
        if((string)Value(p,"@id")!="10" || (string)Value(p,"@user")!="authorised-test-user") throw new Exception("Wrong audit identity/file");
        Audits.Add((string)Value(p,"@detail")); return 1;
    }
}
