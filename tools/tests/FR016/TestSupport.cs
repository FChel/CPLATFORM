using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using TestDb;

// Deliberately no real connection implementation or connection string.
namespace TestDb
{
    public enum OleDbType { LongVarBinary }
    public sealed class OleDbParameter { public string Name; public object Value; public OleDbType OleDbType; }
    public sealed class OleDbConnection : IDisposable
    {
        public OleDbTransaction BeginTransaction() { return new OleDbTransaction(); }
        public OleDbTransaction BeginTransaction(IsolationLevel level) { return new OleDbTransaction(); }
        public void Dispose() { }
    }
    public sealed class OleDbTransaction : IDisposable
    {
        internal List<NORMHelper.Call> Pending = new List<NORMHelper.Call>();
        public void Commit() { if (NORMHelper.FailCommit) throw new Exception("private connection detail"); NORMHelper.Committed.AddRange(Pending); Pending.Clear(); }
        public void Rollback() { Pending.Clear(); }
        public void Dispose() { }
    }
}

public static class NORMHelper
{
    public sealed class Call
    {
        public string Sql; public Dictionary<string, object> Values = new Dictionary<string, object>();
        public object this[string name] { get { return Values[name]; } }
    }
    public static List<Call> Committed = new List<Call>();
    public static bool FailAudit, FailCommit, Duplicate;
    public static bool ExistingCompany;
    public static bool FailCalculation;
    public static bool PrepareAccess = true;
    public static List<string[]> PermittedPairs = new List<string[]>();
    public static List<string> Queries = new List<string>();
    public static Func<string, DataTable> QueryResponse;
    public static Func<Call, int> CurrentSource;
    public static string FailImportFileName;
    private static int nextImportId;
    public static string FailAuditCode;
    public static int Runs, Opens;
    public static void Reset() { Committed.Clear(); FailAudit = FailCommit = Duplicate = ExistingCompany = FailCalculation = false; PrepareAccess = true; FailAuditCode = null; Runs = Opens = 0; PermittedPairs.Clear(); PermittedPairs.Add(new string[] { "1000", "0L" }); Queries.Clear(); QueryResponse = null; CurrentSource = null; FailImportFileName = null; nextImportId = 42; }
    public static string CurrentUserId() { return "isolated-test"; }
    public static bool HasPrepareAccess() { return PrepareAccess; }
    public static OleDbParameter P(string name, object value) { return new OleDbParameter { Name = name, Value = value }; }
    public static OleDbConnection OpenConnection() { Opens++; return new OleDbConnection(); }
    public static int SettingInt(string name, int fallback) { return fallback; }
    public static string Str(DataRow r, string c) { return Convert.ToString(r[c], CultureInfo.InvariantCulture); }
    public static int Int(DataRow r, string c) { return Convert.ToInt32(r[c]); }
    public static long Long(DataRow r, string c) { return Convert.ToInt64(r[c]); }
    public static decimal Dec(DataRow r, string c) { return Convert.ToDecimal(r[c]); }
    public static object Scalar(string sql, params OleDbParameter[] p) { return Duplicate ? (object)7 : null; }
    public static object Scalar(OleDbConnection c, OleDbTransaction t, string sql, params OleDbParameter[] p)
    {
        if (sql.Contains("SELECT TOP 1 i.ImportId")) { return ExistingCompany ? (object)7 : null; }
        return 0;
    }
    public static int Exec(string sql, params OleDbParameter[] p) { throw new Exception("Unexpected nontransactional write"); }
    public static int Exec(OleDbConnection c, OleDbTransaction t, string sql, params OleDbParameter[] p)
    {
        if (FailAudit && sql.Contains("INSERT dbo.tblNORM_AuditEvent")) throw new Exception("private connection detail");
        Call call = new Call { Sql = sql };
        foreach (OleDbParameter item in p) call.Values[item.Name] = item.Value;
        if (call.Values.ContainsKey("@event") && (string)call["@event"] == FailAuditCode) throw new Exception("private connection detail");
        if (FailImportFileName != null && sql.Contains("INSERT dbo.tblNORM_ImportFile") &&
            (string)call["@file"] == FailImportFileName) { throw new InvalidOperationException("Injected source write failure"); }
        t.Pending.Add(call); return 1;
    }
    public static int InsertId(OleDbConnection c, OleDbTransaction t, string sql, params OleDbParameter[] p) { Exec(c, t, sql, p); return sql.Contains("INSERT dbo.tblNORM_Import ") ? nextImportId++ : 42; }
    public static DataTable Query(string sql, params OleDbParameter[] p)
    {
        if (sql.Contains("AS ScopeRows")) { return ScopeRows(sql, p); }
        if (QueryResponse != null) { return QueryResponse(sql); }
        DataTable table = new DataTable();
        Queries.Add(sql);
        if (sql.Contains("SELECT SourceCompanyCode,SourceLedger"))
        {
            table.Columns.Add("SourceCompanyCode"); table.Columns.Add("SourceLedger");
            foreach (string[] pair in PermittedPairs) { table.Rows.Add(pair[0], pair[1]); }
            return table;
        }
        if (sql.Contains("SELECT SourceLedger")) { table.Columns.Add("SourceLedger"); table.Rows.Add("1000"); return table; }
        if (sql.Contains("SELECT ConfigurationReleaseId,FinancialYear"))
        {
            table.Columns.Add("ConfigurationReleaseId", typeof(int)); table.Columns.Add("FinancialYear", typeof(int));
            table.Columns.Add("EntityCode"); table.Columns.Add("VersionCode"); table.Rows.Add(1, 2026, "TEST", "v1"); return table;
        }
        throw new Exception("Unexpected query: " + sql);
    }
    public static DataTable Query(OleDbConnection c, OleDbTransaction t, string sql, params OleDbParameter[] p)
    {
        if (sql.Contains("AS ScopeRows")) { return ScopeRows(sql, p); }
        if (QueryResponse != null) { return QueryResponse(sql); }
        throw new Exception("Unexpected query");
    }

    private static DataTable ScopeRows(string sql, OleDbParameter[] p)
    {
        Call call = new Call { Sql = sql };
        foreach (OleDbParameter item in p) { call.Values[item.Name] = item.Value; }
        int id = CurrentSource != null ? CurrentSource(call) : ExistingCompany ? 7 : 0;
        DataTable table = new DataTable();
        foreach (string column in new[] { "ImportId", "SourceFileName", "SourceFileHash", "ImportedUtc", "ScopeRows" })
        { table.Columns.Add(column, typeof(object)); }
        if (id != 0) { table.Rows.Add(id, "prior & original.xlsx", new string('a', 64), new DateTime(2026, 11, 9), 984); }
        return table;
    }
}

#if SERVICE
public static class NORMStatementEngine
{
    public static int Run(int importId, string user) { NORMHelper.Runs++; if (NORMHelper.FailCalculation) { throw new Exception("synthetic private calculation failure"); } return 24; }
}
#endif
