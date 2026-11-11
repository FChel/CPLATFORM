using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using TestDb;

namespace TestDb
{
    public enum OleDbType { LongVarBinary }
    public sealed class OleDbParameter { public string Name; public object Value; public OleDbType OleDbType; }
    public sealed class OleDbConnection : IDisposable
    {
        public OleDbTransaction BeginTransaction(IsolationLevel level) { return new OleDbTransaction(); }
        public void Dispose() { }
    }
    public sealed class OleDbTransaction : IDisposable
    {
        public List<Action> Pending = new List<Action>();
        public void Commit() { if (NORMHelper.FailCommit) throw new Exception("private connection detail"); foreach (Action a in Pending) a(); Pending.Clear(); }
        public void Rollback() { Pending.Clear(); }
        public void Dispose() { }
    }
}
// No connection strings or SQL Server access. Command/transaction boundary substitute.
public static class NORMHelper
{
    public sealed class Document { public long Id; public int Setup; public string Type, Name, Hash, Status; public byte[] Content; public bool Superseded; }
    public sealed class SetupRow { public int Id, Year; public string Entity; public DateTime Updated; }
    public static List<SetupRow> Setups;
    public static List<Document> Documents;
    public static Dictionary<string,string> Names;
    public static List<string> Audit;
    public static List<string> Commands;
    public static int Current, ApprovedYear, Release, LastReadYear;
    public static bool Assigned, Admin, RevokedAtWrite, Authenticated, FailAudit, FailCommit, NoConfiguredEntity, NoDocument;
    public static DateTime Stamp = new DateTime(2026,11,11,12,0,0,DateTimeKind.Utc);
    public static void Reset()
    {
        Current=2; ApprovedYear=2026; Release=3; LastReadYear=0;
        Assigned=Authenticated=true; Admin=RevokedAtWrite=FailAudit=FailCommit=NoConfiguredEntity=NoDocument=false;
        Setups=new List<SetupRow> { new SetupRow {Id=1,Year=2025,Entity="DEPT",Updated=Stamp},new SetupRow {Id=2,Year=2026,Entity="DEPT",Updated=Stamp},new SetupRow {Id=3,Year=2026,Entity="OTHER",Updated=Stamp} };
        Documents=new List<Document>(); Names=new Dictionary<string,string> { {"DEPT2026","Defence 2026"},{"DEPT2025","Defence 2025"},{"OTHER2026","Other entity"} };
        Audit=new List<string>(); Commands=new List<string>();
    }
    public static string CurrentUserId() { return "isolated\\preparer"; }
    public static bool HasPrepareAccess() { return Authenticated; }
    public static bool CanPrepareEntity(string e, OleDbConnection c=null, OleDbTransaction t=null)
    { return Authenticated && !NoConfiguredEntity && (Admin || Assigned && e=="DEPT") && !(t!=null && RevokedAtWrite); }
    public static void RequireEntityAccess(string e, OleDbConnection c=null, OleDbTransaction t=null)
    { if (!CanPrepareEntity(e,c,t)) throw new UnauthorizedAccessException(); }
    public static int SettingInt(string key,int fallback) { return fallback; }
    public static OleDbConnection OpenConnection() { return new OleDbConnection(); }
    public static OleDbParameter P(string n,object v) { return new OleDbParameter {Name=n,Value=v??DBNull.Value}; }
    static object V(OleDbParameter[] p,string name) { return p.First(x=>x.Name==name).Value; }
    public static string Str(DataRow r,string c) { return r.IsNull(c)?null:Convert.ToString(r[c]); }
    public static int Int(DataRow r,string c) { return Convert.ToInt32(r[c]); }
    public static long Long(DataRow r,string c) { return Convert.ToInt64(r[c]); }
    public static decimal Dec(DataRow r,string c) { return Convert.ToDecimal(r[c]); }
    static DataTable Table(string names) { var t=new DataTable(); foreach(string n in names.Split(',')) t.Columns.Add(n,typeof(object)); return t; }
    public static DataTable Query(string sql,params OleDbParameter[] p) { return Query(null,null,sql,p); }
    public static DataTable Query(OleDbConnection c,OleDbTransaction tx,string sql,params OleDbParameter[] p)
    {
        Commands.Add(sql);
        if (sql.Contains("SELECT DISTINCT EntityCode")) { var t=Table("EntityCode"); t.Rows.Add("DEPT"); t.Rows.Add("OTHER"); return t; }
        if (sql.Contains("SELECT y.EntityCode,d.SourceFileName")) {
            var t=Table("EntityCode,SourceFileName,SourceFileHash");
            var d=Documents.FirstOrDefault(x=>x.Id==Convert.ToInt64(V(p,"@id")));
            if(d!=null&&!NoDocument) t.Rows.Add(Setups.First(x=>x.Id==d.Setup).Entity,d.Name,d.Hash); return t;
        }
        if(sql.Contains("FROM dbo.tblNORM_YearSetupDocument")) {
            var t=Table("YearSetupDocumentId,DocumentTypeCode,SourceFileName,SourceFileHash,SourceFileBytes,ExtractionStatus,DetectedStart,ExtractedFigureCount,ExtractionDetail,UploadedBy,UploadedUtc,IsDeactivated");
            foreach(var d in Documents.Where(x=>x.Setup==Convert.ToInt32(V(p,"@setup"))).OrderByDescending(x=>x.Id)) {
                if(sql.Contains("DocumentTypeCode=@type") && (d.Type!=Convert.ToString(V(p,"@type"))||d.Superseded)) continue;
                t.Rows.Add(d.Id,d.Type,d.Name,d.Hash,d.Content.LongLength,d.Status,"",0,"Retained for review",CurrentUserId(),Stamp,d.Superseded);
            } return t;
        }
        if(sql.Contains("FROM dbo.tblNORM_YearSetupFigure f")) return Table("DocumentTypeCode,SourceFileName,FigureType,StatementCode,LineCode,LineLabel,Amount,SourceLocator,MatchConfidence,ReviewStatus");
        if(sql.Contains("FROM dbo.tblNORM_YearSetup y")) {
            LastReadYear=Convert.ToInt32(V(p,"@year"));
            if(sql.Contains("SELECT n.StatementLine")) return Table("StatementLine,NoteSubLine,Amount");
            if(sql.Contains("SELECT f.RowCode")) return Table("RowCode,AssetClassCode,Amount");
            var t=Table("StatementCode,LineCode,Amount"); t.Rows.Add("SOCI","Example",LastReadYear==2025?25m:26m); return t;
        }
        if(sql.Contains("FROM dbo.tblNORM_YearSetup")) {
            var t=Table("YearSetupId,EntityCode,CurrentFinancialYear,UpdatedUtc,IsCurrent");
            foreach(var r in Setups) {
                if(p.Any(x=>x.Name=="@id") && r.Id!=Convert.ToInt32(V(p,"@id"))) continue;
                if(p.Any(x=>x.Name=="@entity") && r.Entity!=Convert.ToString(V(p,"@entity"))) continue;
                if(sql.Contains("IsCurrent=1") && r.Id!=Current) continue;
                t.Rows.Add(r.Id,r.Entity,r.Year,r.Updated,r.Id==Current);
            } return t;
        }
        if(sql.Contains("FROM dbo.tblNORM_StatementLine")) return Table("StatementCode,LineCode,LineLabel");
        throw new Exception("Unexpected query: "+sql);
    }
    public static object Scalar(string sql,params OleDbParameter[] p) { return Scalar(null,null,sql,p); }
    public static object Scalar(OleDbConnection c,OleDbTransaction t,string sql,params OleDbParameter[] p)
    {
        Commands.Add(sql);
        if(sql.Contains("OBJECT_ID")) return 1;
        if(sql.Contains("ConfigurationReleaseId FROM")) {
            int y=Convert.ToInt32(V(p,"@year")); return y==ApprovedYear && !NoConfiguredEntity ? (object)Release : null;
        }
        if(sql.Contains("SELECT EntityCode FROM dbo.tblNORM_ReportingEntity")) return NoConfiguredEntity?null:(object)"DEPT";
        if(sql.Contains("SELECT EntityName FROM")) {
            string key=Convert.ToString(V(p,"@entity"))+Convert.ToString(V(p,"@year")); return Names.ContainsKey(key)?Names[key]:null;
        }
        if(sql.Contains("SELECT YearSetupId FROM")) {
            var r=Setups.FirstOrDefault(x=>x.Entity==Convert.ToString(V(p,"@entity"))&&x.Year==Convert.ToInt32(V(p,"@year"))); return r==null?null:(object)r.Id;
        }
        if(sql.Contains("SELECT FileContent")) { var d=Documents.FirstOrDefault(x=>x.Id==Convert.ToInt64(V(p,"@id"))); return d==null?null:d.Content; }
        if(sql.Contains("INSERT dbo.tblNORM_YearSetupDocument")) {
            var d=new Document { Id=Documents.Count+10,Setup=Convert.ToInt32(V(p,"@setup")),Type=Convert.ToString(V(p,"@type")),Name=Convert.ToString(V(p,"@file")),Hash=Convert.ToString(V(p,"@hash")),Content=(byte[])V(p,"@content"),Status=Convert.ToString(V(p,"@status")) };
            t.Pending.Add(delegate { Documents.Add(d); }); return d.Id;
        }
        if(sql.Contains("SELECT i.FinancialYear")) return 2025;
        throw new Exception("Unexpected scalar: "+sql);
    }
    public static int InsertId(OleDbConnection c,OleDbTransaction t,string sql,params OleDbParameter[] p) {
        int id=Setups.Count+1; int y=Convert.ToInt32(V(p,"@year")); string e=Convert.ToString(V(p,"@entity"));
        t.Pending.Add(delegate { Setups.Add(new SetupRow {Id=id,Year=y,Entity=e,Updated=Stamp.AddSeconds(1)}); Current=id; }); return id;
    }
    public static int Exec(OleDbConnection c,OleDbTransaction t,string sql,params OleDbParameter[] p)
    {
        Commands.Add(sql);
        if(sql.Contains("INSERT dbo.tblNORM_AuditEvent")) {
            if(FailAudit) throw new Exception("private connection detail");
            string a=Convert.ToString(V(p,"@event"))+"|"+Convert.ToString(V(p,"@detail"))+"|"+Convert.ToString(V(p,"@user"));
            t.Pending.Add(delegate { Audit.Add(a); });
        }
        else if(sql.Contains("SET IsCurrent=1")) {
            int id=Convert.ToInt32(V(p,"@id"));t.Pending.Add(delegate { Current=id; Setups.First(x=>x.Id==id).Updated=Stamp.AddSeconds(1); });
        }
        else if(sql.Contains("SET IsCurrent=0")) { /* Atomic final pointer applied above on commit. */ }
        else if(sql.Contains("SET EntityName=@name")) {
            string key=Convert.ToString(V(p,"@entity"))+Convert.ToString(V(p,"@year"));string name=Convert.ToString(V(p,"@name"));t.Pending.Add(delegate { Names[key]=name; });
        }
        else if(sql.Contains("SET IsDeactivated=1 WHERE YearSetupId")) {
            int id=Convert.ToInt32(V(p,"@setup"));string type=Convert.ToString(V(p,"@type"));
            var originals=Documents.Where(x=>x.Setup==id&&x.Type==type&&!x.Superseded).ToArray();
            t.Pending.Add(delegate {foreach(var d in originals) d.Superseded=true; });
        }
        else if(!sql.Contains("INSERT dbo.tblNORM_YearSetupFigure")) throw new Exception("Unexpected write: "+sql);
        return 1;
    }
}

// Extraction line catalogue is outside these setup/control tests.
public static class NORMAdministeredStatements
{
    public sealed class Definition { public string Label {get;set;} public string StatementCode {get;set;} public string LineCode {get;set;} }
    public static List<Definition> ExtractionDefinitions() { return new List<Definition>(); }
}
