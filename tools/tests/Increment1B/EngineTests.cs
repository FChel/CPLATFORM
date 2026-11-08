using System;
using System.Data;
using System.IO;
using System.Reflection;

public static class EngineTests
{
    static DataTable Table(string columns)
    { DataTable t = new DataTable(); foreach (string column in columns.Split(',')) { t.Columns.Add(column, typeof(object)); } return t; }
    static void Assert(bool ok, string message) { if (!ok) { throw new Exception(message); } }
    public static int Main(string[] args)
    {
        try
        {
            NORMHelper.Reset();
            NORMParsedImport parsed = ErpTrialBalanceImporter.ParseForInspection(File.ReadAllBytes(args[0]), new string[] { "1000" }, 2026);
            NORMHelper.QueryResponse = delegate(string sql) {
                DataTable t;
                if (sql.Contains("FROM dbo.tblNORM_AccountMap"))
                {
                    t = Table("AccountMapId,GlCode,AccountType,StatementLine,NoteSubLine,CashFlowClass");
                    t.Rows.Add(1, "100000", "Income", "TEST_INCOME", "", ""); return t;
                }
                if (sql.Contains("FROM dbo.tblNORM_TrialBalanceRow"))
                {
                    t = Table("TbRowId,SourceLedger,GlAccount,GlText,DebitMovement,CreditMovement,AccumBalance"); int id = 0;
                    foreach (NORMTrialBalanceRow row in parsed.Rows) { t.Rows.Add(++id, row.SourceLedger, ErpTrialBalanceImporter.ExtractGl(row.GlAccount), row.GlText, row.DebitMovement, row.CreditMovement, row.AccumBalance); }
                    return t;
                }
                if (sql.Contains("CalculationKind = 'Formula'")) { return Table("StatementLineId,StatementCode,LineCode,SeqNo,FormulaSpec"); }
                if (sql.Contains("FROM dbo.tblNORM_StatementLine"))
                { t = Table("StatementLineId,StatementCode,LineCode,SeqNo,CalculationKind,FormulaSpec"); t.Rows.Add(1, "SOCI", "TEST_INCOME", 1, "Mapped", ""); return t; }
                if (sql.Contains("FROM dbo.tblNORM_PublishedFigure")) { return Table("StatementCode,LineCode,AmountCurrent"); }
                if (sql.Contains("FROM dbo.tblNORM_ImportFile")) { t = Table("SourceType,PeriodStart,PeriodEnd,IsStatementInput"); t.Rows.Add("ERP", 1, 12, true); return t; }
                throw new Exception("Unexpected engine query: " + sql);
            };
            Type contextType = typeof(NORMStatementEngine).GetNestedType("ImportContext", BindingFlags.NonPublic);
            object context = Activator.CreateInstance(contextType, true);
            contextType.GetField("ImportId").SetValue(context, 42);
            contextType.GetField("ReleaseId").SetValue(context, 1);
            contextType.GetField("SourceType").SetValue(context, "ERP");
            contextType.GetField("ReleaseVersion").SetValue(context, "synthetic-partial-map");
            typeof(NORMStatementEngine).GetMethod("Calculate", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 24, context, "isolated-test" });
            var edges = NORMHelper.Committed.FindAll(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_Lineage"); });
            Assert(edges.Count == 984, "Engine discarded source rows");
            Assert(edges.FindAll(delegate(NORMHelper.Call c) { return (string)c["@derivation"] == "UNMAPPED"; }).Count == 983, "Unmapped rows incorrectly mapped/discarded");
            var check = NORMHelper.Committed.Find(delegate(NORMHelper.Call c) { return c.Values.ContainsKey("@code") && (string)c["@code"] == "UNMAPPED_DISPOSITION"; });
            Assert(check != null && (string)check["@result"] == "Warning" && Convert.ToInt32(check["@actual"]) == 983, "Existing unmapped count lost");
            Assert(NORMHelper.Committed.Exists(delegate(NORMHelper.Call c) { return c.Sql.Contains("SET StatusCode = 'Complete'"); }), "Incomplete mapping stopped calculation");
            Console.WriteLine("PASS real Calculate: 984 lineage rows, 983 unmapped, existing warning/count retained (database substitutes)"); return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
    }
}
