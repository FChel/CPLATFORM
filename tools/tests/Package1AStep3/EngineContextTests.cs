using System;
using System.Data;
using System.Linq;

public static class EngineContextTests
{
    static string name;
    static DataTable Table(string columns) {var t=new DataTable();foreach(string n in columns.Split(','))t.Columns.Add(n,typeof(object));return t;}
    static DataTable Query(string sql)
    {
        if(sql.Contains("SELECT i.ImportId")) {var t=Table("ImportId,ConfigurationReleaseId,FinancialYear,EntityCode,SourceType,DataFingerprint,VersionCode,EntityName");t.Rows.Add(5,2,2025,"DEPT","ERP","source-hash","approved-v2",name);return t;}
        if(sql.Contains("SELECT 'M' AS Kind")) return Table("Kind,A,B,C,D,E");
        if(sql.Contains("FROM dbo.tblNORM_AccountMap")) {var t=Table("AccountMapId,GlCode,AccountType,StatementLine,NoteSubLine,CashFlowClass");t.Rows.Add(1,"100","Expense","TEST_LINE","","");return t;}
        if(sql.Contains("FROM dbo.tblNORM_StatementLine")) {var t=Table("StatementLineId,StatementCode,LineCode,SeqNo,CalculationKind,FormulaSpec");if(!sql.Contains("CalculationKind = 'Formula'"))t.Rows.Add(1,"SOCI","TEST_LINE",1,"Mapped","");return t;}
        if(sql.Contains("FROM dbo.tblNORM_PublishedFigure")) return Table("StatementCode,LineCode,AmountCurrent");
        if(sql.Contains("FROM dbo.tblNORM_TrialBalanceRow")) return Table("TbRowId,SourceLedger,GlAccount,GlText,DebitMovement,CreditMovement,AccumBalance");
        if(sql.Contains("FROM dbo.tblNORM_ImportFile")) return Table("SourceType,PeriodStart,PeriodEnd,IsStatementInput");
        throw new Exception("Unexpected engine context query: "+sql);
    }
    static void Check(bool b,string why) {if(!b)throw new Exception(why);}
    public static int Main()
    {
        try
        {
            NORMHelper.Reset();NORMHelper.QueryResponse=Query;name="Historical Defence";
            NORMStatementEngine.Run(5,"isolated-admin");
            var first=NORMHelper.Committed.First(x=>x.Sql.Contains("INSERT dbo.tblNORM_CalculationRun"));
            Check((string)first["@name"]=="Historical Defence","Snapshot not persisted");
            Console.WriteLine("PASS real engine persists reporting name with run creation");
            string fingerprint=(string)first["@fingerprint"];
            name="Renamed Defence";NORMHelper.CurrentYear=2027;
            NORMStatementEngine.Run(5,"isolated-admin");
            var second=NORMHelper.Committed.Last(x=>x.Sql.Contains("INSERT dbo.tblNORM_CalculationRun"));
            Check((string)first["@name"]=="Historical Defence"&&(string)second["@name"]=="Renamed Defence","Old run name changed");
            Check(fingerprint!=(string)second["@fingerprint"],"Name edit reused old context");
            Check(NORMHelper.Committed.All(x=>!x.Sql.Contains("SET FinancialYear")&&!x.Sql.Contains("SET ReportingEntityName")),"Historical context rewritten");
            Console.WriteLine("PASS reporting-name edit creates distinct context and retains old snapshot");
            name="Historical Defence";NORMHelper.CurrentYear=2028;NORMStatementEngine.Run(5,"isolated-admin");
            var third=NORMHelper.Committed.Last(x=>x.Sql.Contains("INSERT dbo.tblNORM_CalculationRun"));
            Check(fingerprint==(string)third["@fingerprint"],"Current year changed historical run identity");
            Console.WriteLine("PASS saved current-year changes do not change recorded run context");
            Console.WriteLine("TOTAL PASS 3 (real engine, substituted database; empty amount fixture)");return 0;
        }
        catch(Exception e) {Console.WriteLine("FAIL "+e);return 1;}
    }
}
