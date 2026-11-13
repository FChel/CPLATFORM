using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;

public static partial class Tests
{
    sealed class VersionFlags
    {
        public bool Inactive=false,TestBreak=false;
        public int? Parent=null;
        public string Status="Imported";
    }
    static readonly Dictionary<int,VersionFlags> versionFlags=new Dictionary<int,VersionFlags>();
    static readonly Dictionary<int,DataTable> accountEvidence=new Dictionary<int,DataTable>();
    static readonly Dictionary<int,string> profiles=new Dictionary<int,string>();
    static int historyReads;
    static void HistorySetup()
    {
        StoreSetup(); versionFlags.Clear(); accountEvidence.Clear(); profiles.Clear(); historyReads=0;
        NORMHelper.QueryCallResponse=HistoryQuery; NORMHelper.ScalarCallResponse=HistoryBlob;
    }
    static void HTest(string name,Action action) { Test("Step4 "+name,delegate { HistorySetup(); action(); }); }
    static bool AcceptedNode(StoredPopulation p)
    {
        VersionFlags flags; if(!versionFlags.TryGetValue(p.Id,out flags)) { return true; }
        return !flags.Inactive && !flags.TestBreak && !flags.Parent.HasValue && (flags.Status=="Imported" || flags.Status=="Calculated");
    }
    static DataTable SourceAccounts(int id)
    {
        DataTable table; if(accountEvidence.TryGetValue(id,out table)) { return table; }
        StoredPopulation p=store[id]; table=Table("TbRowId,SourceRowNo,GlAccount,GlText,AccumBalance,SourceCompanyCode,SourceLedger,ReportingStream");
        int number=0;
        foreach(string record in p.Rows)
        {
            number++; string[] parts=record.Split('|'); int sourceRow; decimal balance;
            bool parsed=parts.Length>=3 && Int32.TryParse(parts[0],out sourceRow) && Decimal.TryParse(parts[2],NumberStyles.Number,CultureInfo.InvariantCulture,out balance);
            table.Rows.Add((long)id*10000+number,parsed ? Int32.Parse(parts[0]) : number,parsed ? parts[1] : "SEED",parsed ? parts[1] : "Controlled seed",parsed ? Decimal.Parse(parts[2],CultureInfo.InvariantCulture) : 0m,p.Company,(object)p.Ledger ?? DBNull.Value,p.Stream);
        }
        return table;
    }
    static bool MatchesAccount(DataRow row,NORMHelper.Call call)
    {
        return (!call.Values.ContainsKey("@company") || (string)row["SourceCompanyCode"]==(string)call["@company"]) &&
            (!call.Values.ContainsKey("@stream") || (string)row["ReportingStream"]==(string)call["@stream"]) &&
            (!call.Values.ContainsKey("@company") || (call.Values.ContainsKey("@ledger") ? !row.IsNull("SourceLedger") && (string)row["SourceLedger"]==(string)call["@ledger"] : row.IsNull("SourceLedger")));
    }
    static DataTable HistoryQuery(NORMHelper.Call call)
    {
        string sql=call.Sql;
        if(sql.Contains("FROM dbo.tblNORM_ConfigurationRelease") || sql.Contains("FROM dbo.tblNORM_EntityLedger") || sql.Contains("FROM dbo.tblNORM_AccountMap") || sql.Contains("FROM dbo.tblNORM_StatementLine")) { return StoreQuery(call); }
        if(sql.Contains("SELECT TOP 1 f.ImportFileId"))
        {
            if(!call.Values.ContainsKey("@import")) { return StoreQuery(call); }
            Assert(sql.Contains("i.ImportId=f.ImportId") && !sql.Contains("CalculationRun"),"No-run handler needs an exact import/file join");
            DataTable table=Table("ImportFileId,SourceType,SourceFileName,SourceFileHash,EntityCode"); int id=(int)call["@import"];
            if(store.ContainsKey(id) && (int)call["@file"]==id && AcceptedNode(store[id])) { var p=store[id]; table.Rows.Add(id,"ERP",p.FileName,p.Hash,p.Entity); }
            return table;
        }
        historyReads++;
        Assert(sql.Contains("i.IsDeactivated=0") && sql.Contains("i.IsTestBreak=0") && sql.Contains("i.ParentImportId IS NULL") && sql.Contains("i.StatusCode IN ('Imported','Calculated')"),"History includes inactive/test/derived/unaccepted versions");
        Assert(!sql.Contains("CalculationRun") && !sql.Contains("GROUP BY GlAccount") && !sql.Contains("SUM(AccumBalance)"),"History/comparison gained run dependency or GL aggregation");
        bool versions=sql.Contains("AS ProfileEvidence"), accounts=sql.StartsWith("SELECT tb.TbRowId",StringComparison.Ordinal);
        if(call.Values.ContainsKey("@entity"))
        {
            Assert(call.ParameterNames[0]=="@entity" && call.ParameterNames[1]=="@fy" && sql.Contains("i.EntityCode=@entity AND i.FinancialYear=@fy"),"History entity/year filter/order lost");
        }
        if(versions || accounts)
        {
            Assert(sql.Contains("s.SourceCompanyCode=@company") && sql.Contains("s.ReportingStream=@stream") && (call.Values.ContainsKey("@ledger") ? sql.Contains("s.SourceLedger=@ledger") : sql.Contains("s.SourceLedger IS NULL")),"History predecessor key incomplete");
            Assert(!sql.Contains("i.ConfigurationReleaseId=@release"),"History incorrectly restricted to one release");
        }
        if(accounts) { Assert(sql.Contains("tb.SourceCompanyCode=@company") && sql.Contains("tb.ReportingStream=@stream") && (call.Values.ContainsKey("@ledger") ? sql.Contains("tb.SourceLedger=@ledger") : sql.Contains("tb.SourceLedger IS NULL")),"Account query merges population rows"); }
        DataTable result=versions ? Table("ImportId,EntityCode,FinancialYear,SourceFileName,ImportedUtc,ImportedBy,SourceType,ConfigurationReleaseId,VersionCode,ImportFileId,EvidenceFileName,SourceFileHash,FileSourceType,PeriodStart,PeriodEnd,CoverageStart,CoverageEnd,IsFullYear,ProfileEvidence,ReplacementEvidence") : accounts ? Table("TbRowId,SourceRowNo,GlAccount,GlText,AccumBalance,SourceCompanyCode,SourceLedger,ReportingStream") : Table("ImportId,EntityCode,FinancialYear,SourceFileName,ImportedUtc,SourceCompanyCode,SourceLedger,ReportingStream");
        List<int> ids=new List<int>(store.Keys); ids.Sort(); ids.Reverse();
        foreach(int id in ids)
        {
            StoredPopulation p=store[id]; if(!AcceptedNode(p)) { continue; }
            if(call.Values.ContainsKey("@import") && id!=(int)call["@import"]) { continue; }
            if(call.Values.ContainsKey("@entity") && (p.Entity!=(string)call["@entity"] || p.Year!=(int)call["@fy"])) { continue; }
            DataTable evidence=SourceAccounts(id); List<DataRow> matching=new List<DataRow>();
            foreach(DataRow row in evidence.Rows) { if(!row.IsNull("SourceCompanyCode") && !row.IsNull("ReportingStream") && MatchesAccount(row,call)) { matching.Add(row); } }
            if(matching.Count==0) { continue; }
            if(accounts) { foreach(DataRow row in matching) { result.ImportRow(row); } continue; }
            if(versions)
            {
                string profile; profiles.TryGetValue(id,out profile); string replacement=null;
                foreach(NORMHelper.Call audit in acceptedAudits) { if(Value(audit["@id"])!=id.ToString()) { continue; } if((string)audit["@event"]=="IMPORT_SOURCE_PROFILE") { profile=(string)audit["@detail"]; } if((string)audit["@event"]=="IMPORT_SOURCE_REPLACED") { replacement=(string)audit["@detail"]; } }
                result.Rows.Add(id,p.Entity,p.Year,p.FileName,new DateTime(2020,7,1).AddMinutes(id),"CONTROLLED operator","ERP",p.Release,"retained-v"+p.Release,id,p.FileName,p.Hash,"ERP",1,p.Full==false ? 9 : 12,(object)p.Start ?? DBNull.Value,(object)p.End ?? DBNull.Value,(object)p.Full ?? DBNull.Value,(object)profile ?? DBNull.Value,(object)replacement ?? DBNull.Value);
            }
            else
            {
                HashSet<string> seen=new HashSet<string>();
                foreach(DataRow row in matching) { string key=Value(row["SourceCompanyCode"])+"|"+Value(row["SourceLedger"])+"|"+Value(row["ReportingStream"]); if(seen.Add(key)) { result.Rows.Add(id,p.Entity,p.Year,p.FileName,new DateTime(2020,7,1).AddMinutes(id),row["SourceCompanyCode"],row["SourceLedger"],row["ReportingStream"]); } }
            }
        }
        return result;
    }
    static object HistoryBlob(NORMHelper.Call call)
    {
        if(!call.Values.ContainsKey("@import")) { return ReadStoredOriginal(call); }
        Assert(call.Sql.Contains("i.ImportId=f.ImportId") && call.Sql.Contains("i.EntityCode=@entity") && !call.Sql.Contains("CalculationRun"),"Import original lost owner/entity binding");
        int id=(int)call["@import"]; return store.ContainsKey(id) && (int)call["@file"]==id && store[id].Entity==(string)call["@entity"] && AcceptedNode(store[id]) ? store[id].Bytes : null;
    }
    static void SetAccounts(int id,params object[][] rows)
    {
        DataTable t=Table("TbRowId,SourceRowNo,GlAccount,GlText,AccumBalance,SourceCompanyCode,SourceLedger,ReportingStream"); int number=0;
        foreach(object[] row in rows) { number++; t.Rows.Add((long)id*10000+number,number,row[0],row[1],row[2],store[id].Company,(object)store[id].Ledger ?? DBNull.Value,store[id].Stream); }
        accountEvidence[id]=t;
    }
    static void Chain()
    {
        Seed(20,2020,"1000",null,"Departmental"); Seed(30,2020,"1000",null,"Departmental");
        store.Remove(9); store[20].Release=3; store[30].Release=4; Add(3,2020,"1000",null,"Departmental"); Add(4,2020,"1000",null,"Departmental");
        Seed(25,2020,"2000",null,"Departmental"); Seed(26,2019,"1000",null,"Departmental"); Seed(27,2020,"1000","0L","Departmental"); Seed(28,2020,"1000",null,"Administered");
        SetAccounts(10,new object[] { "100","Old description",90m },new object[] { "200","Credit",-90m });
        SetAccounts(20,new object[] { "100","Before <description>",100m },new object[] { "200","Credit",-100m },new object[] { "REMOVED","Zero old",0m },new object[] { "DESC","Before",0m });
        SetAccounts(30,new object[] { "100","After & description",125m },new object[] { "200","Credit",-125m },new object[] { "ADDED","Zero new",0m },new object[] { "DESC","After",0m });
        profiles[20]="Profile=LEGACY-ERP/1;SHA256="+store[20].Hash; profiles[30]="Profile=DEFENCE-ERP/1;SHA256="+store[30].Hash;
    }
    static NORMSourceScope Selected(int id)
    { return NORMSourceHistory.ScopeForImport(id,null,null,null,false); }
}
