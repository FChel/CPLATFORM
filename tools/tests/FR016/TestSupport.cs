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
        public OleDbTransaction BeginTransaction() { NORMHelper.Transactions++; return new OleDbTransaction(); }
        public OleDbTransaction BeginTransaction(IsolationLevel level) { return BeginTransaction(); }
        public void Dispose() { }
    }
    public sealed class OleDbTransaction : IDisposable
    {
        private readonly int id = NORMHelper.Transactions;
        public OleDbTransaction() { NORMHelper.TransactionEvents.Add("Begin:" + id); }
        internal List<NORMHelper.Call> Pending = new List<NORMHelper.Call>();
        public void Commit() { if (NORMHelper.FailCommit) throw new Exception("private connection detail"); if (NORMHelper.OnCommit != null) { NORMHelper.OnCommit(Pending.AsReadOnly()); } NORMHelper.Committed.AddRange(Pending); NORMHelper.Commits++; Pending.Clear(); NORMHelper.TransactionEvents.Add("Commit:" + id); }
        public void Rollback() { NORMHelper.Rollbacks++; Pending.Clear(); NORMHelper.TransactionEvents.Add("Rollback:" + id); }
        public void Dispose() { Pending.Clear(); NORMHelper.TransactionEvents.Add("Dispose:" + id); }
    }
}

public static class NORMHelper
{
    public sealed class Call
    {
        public string Sql; public Dictionary<string, object> Values = new Dictionary<string, object>();
        public List<string> ParameterNames = new List<string>();
        public bool InTransaction;
        public int InsertedId;
        public object this[string name] { get { return Values[name]; } }
    }
    public static List<Call> Committed = new List<Call>();
    public static List<string> TransactionEvents = new List<string>();
    public static bool FailAudit, FailCommit, Duplicate;
    public static bool ExistingCompany;
    public static bool FailCalculation;
    public static bool PrepareAccess = true;
    public static int? CurrentYear = 2026;
    public static Func<int?> ReadYear;
    public static Func<Call, int?> ReadYearCall;
    public static Func<string, bool> TransactionEntityAccess;
    public static Action<Call> OnExecute;
    public static Action<IList<Call>> OnCommit;
    public static Action<int> OnCalculate;
    public static Func<Call, object> ScalarCallResponse;
    public static Action<Call> NonTransactionalExec;
    public static int Transactions, Commits, Rollbacks;
    public static int? MaxUploadBytes;
    public static Func<string, bool> EntityAccess = delegate { return true; };
    public static bool CanPrepareEntity(string entity, OleDbConnection c = null, OleDbTransaction t = null)
    { return PrepareAccess && (t != null && TransactionEntityAccess != null ? TransactionEntityAccess(entity) : EntityAccess(entity)); }
    public static void RequireEntityAccess(string entity, OleDbConnection c = null, OleDbTransaction t = null)
    { if (!CanPrepareEntity(entity, c, t)) { throw new UnauthorizedAccessException("Entity access denied."); } }
    public static List<string[]> PermittedPairs = new List<string[]>();
    public static List<string> Queries = new List<string>();
    public static Func<string, DataTable> QueryResponse;
    public static Func<Call, DataTable> QueryCallResponse;
    public static Func<string, object> ScalarResponse;
    public static Action<string> ExecResponse;
    public static Func<Call, int> CurrentSource;
    public static string FailImportFileName;
    private static int nextImportId;
    public static string FailAuditCode;
    public static int Runs, Opens;
    public static void Reset() { Committed.Clear(); FailAudit = FailCommit = Duplicate = ExistingCompany = FailCalculation = false; PrepareAccess = true; CurrentYear = 2026; ReadYear = null; EntityAccess = delegate { return true; }; FailAuditCode = null; Runs = Opens = Transactions = Commits = Rollbacks = 0; ReadYearCall = null; TransactionEntityAccess = null; OnExecute = null; OnCommit = null; OnCalculate = null; ScalarCallResponse = null; NonTransactionalExec = null; MaxUploadBytes = null; PermittedPairs.Clear(); PermittedPairs.Add(new string[] { "1000", "0L" }); Queries.Clear(); QueryResponse = null; QueryCallResponse = null; ScalarResponse = null; ExecResponse = null; CurrentSource = null; FailImportFileName = null; nextImportId = 42; }
    public static string CurrentUserId() { return "isolated-test"; }
    public static bool HasPrepareAccess() { return PrepareAccess; }
    public static OleDbParameter P(string name, object value) { return new OleDbParameter { Name = name, Value = value ?? DBNull.Value }; }
    public static OleDbConnection OpenConnection() { Opens++; return new OleDbConnection(); }
    public static int SettingInt(string name, int fallback) { return name == "NORM.MaxUploadBytes" && MaxUploadBytes.HasValue ? MaxUploadBytes.Value : fallback; }
    public static string Str(DataRow r, string c) { return r.IsNull(c) ? null : Convert.ToString(r[c], CultureInfo.InvariantCulture); }
    public static int Int(DataRow r, string c) { return Convert.ToInt32(r[c]); }
    public static long Long(DataRow r, string c) { return Convert.ToInt64(r[c]); }
    public static decimal Dec(DataRow r, string c) { return Convert.ToDecimal(r[c]); }
    public static object Scalar(string sql, params OleDbParameter[] p) { return ScalarCallResponse != null ? ScalarCallResponse(QueryCall(sql, p)) : ScalarResponse != null ? ScalarResponse(sql) : Duplicate ? (object)7 : null; }
    public static object Scalar(OleDbConnection c, OleDbTransaction t, string sql, params OleDbParameter[] p)
    {
        if (sql.Contains("SELECT TOP 1 i.ImportId")) { return ExistingCompany ? (object)7 : null; }
        return 0;
    }
    public static int Exec(string sql, params OleDbParameter[] p) { if (NonTransactionalExec != null) { NonTransactionalExec(QueryCall(sql, p)); return 1; } if (ExecResponse != null) { ExecResponse(sql); return 1; } throw new Exception("Unexpected nontransactional write"); }
    public static int Exec(OleDbConnection c, OleDbTransaction t, string sql, params OleDbParameter[] p)
    {
        if (FailAudit && sql.Contains("INSERT dbo.tblNORM_AuditEvent")) throw new Exception("private connection detail");
        Call call = QueryCall(sql, p, true);
        if (OnExecute != null) { OnExecute(call); }
        if (call.Values.ContainsKey("@event") && (string)call["@event"] == FailAuditCode) throw new Exception("private connection detail");
        if (FailImportFileName != null && sql.Contains("INSERT dbo.tblNORM_ImportFile") &&
            (string)call["@file"] == FailImportFileName) { throw new InvalidOperationException("Injected source write failure"); }
        t.Pending.Add(call); return 1;
    }
    public static int InsertId(OleDbConnection c, OleDbTransaction t, string sql, params OleDbParameter[] p) { Exec(c, t, sql, p); int id = sql.Contains("INSERT dbo.tblNORM_Import ") ? nextImportId++ : 42; t.Pending[t.Pending.Count - 1].InsertedId = id; return id; }
    public static DataTable Query(string sql, params OleDbParameter[] p)
    {
        if (sql.Contains("FROM dbo.tblNORM_YearSetup")) { return YearRows(QueryCall(sql,p)); }
        if (sql.Contains("AS ScopeRows")) { return ScopeRows(sql, p, false); }
        if (QueryCallResponse != null) { return QueryCallResponse(QueryCall(sql, p)); }
        if (QueryResponse != null) { return QueryResponse(sql); }
        DataTable table = new DataTable();
        Queries.Add(sql);
        if (sql.Contains("SELECT SourceCompanyCode,SourceLedger"))
        {
            table.Columns.Add("SourceCompanyCode"); table.Columns.Add("SourceLedger"); table.Columns.Add("ReportingStream");
            foreach (string[] pair in PermittedPairs) { table.Rows.Add(pair[0], pair[1], "Departmental"); }
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
        if (sql.Contains("FROM dbo.tblNORM_YearSetup")) { return YearRows(QueryCall(sql,p,true)); }
        if (sql.Contains("AS ScopeRows")) { return ScopeRows(sql, p, true); }
        if (QueryCallResponse != null) { return QueryCallResponse(QueryCall(sql, p, true)); }
        if (QueryResponse != null) { return QueryResponse(sql); }
        throw new Exception("Unexpected query");
    }

    private static Call QueryCall(string sql, OleDbParameter[] parameters, bool inTransaction = false)
    {
        Call call = new Call { Sql = sql, InTransaction = inTransaction };
        foreach (OleDbParameter parameter in parameters) { call.Values.Add(parameter.Name, parameter.Value); call.ParameterNames.Add(parameter.Name); }
        return call;
    }

    private static DataTable YearRows(Call call)
    {
        int? year = ReadYearCall != null ? ReadYearCall(call) : ReadYear == null ? CurrentYear : ReadYear();
        DataTable table = new DataTable();
        table.Columns.Add("CurrentFinancialYear", typeof(int));
        if (year.HasValue) { table.Rows.Add(year.Value); }
        return table;
    }

    private static DataTable ScopeRows(string sql, OleDbParameter[] p, bool inTransaction)
    {
        Call call = QueryCall(sql, p, inTransaction);
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
    public static int Run(int importId, string user) { NORMHelper.Runs++; if (NORMHelper.OnCalculate != null) { NORMHelper.OnCalculate(importId); } if (NORMHelper.FailCalculation) { throw new Exception("synthetic private calculation failure"); } return 24; }
}
#endif
