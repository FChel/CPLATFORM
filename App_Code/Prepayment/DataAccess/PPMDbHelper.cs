using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Web;

namespace Prepayment.Web.DataAccess
{
    /// <summary>
    /// Centralised OLE DB access for the Prepayment module, per the README's documented
    /// per-module pattern: "All data access goes through the module's helper class — direct
    /// ADO.NET calls are not the pattern." and "OLE DB needs positional `?` placeholders, not
    /// named `@param` markers."
    ///
    /// Every call — stored procedure or ad-hoc SQL — uses the ODBC/OLE DB call-escape syntax
    /// with literal `?` placeholders (e.g. "{call prepayment.Admin_ForceAdvance(?,?)}"), with one
    /// <see cref="OleDbParameter"/> manually constructed and added per placeholder, in the order
    /// the target stored procedure declares its parameters (Implementation_Plan_CPlatform_Port.md
    /// §6: "parameters added in strict positional order matching each stored procedure's
    /// parameter order, instead of by name"). No parameter metadata is derived from the server —
    /// callers supply the values in the declared order and the helper binds them positionally.
    ///
    /// Row-to-object mapping is manual per Implementation_Plan_CPlatform_Port.md §6: "Dapper's
    /// automatic column→property mapping goes away; every entity read needs a manual
    /// reader-to-object mapper method." Repositories supply that mapper as a delegate; this
    /// class never inspects a type's properties.
    /// </summary>
    public static class PPMDbHelper
    {
        private const string UdlPathAppSettingKey = "PPM.UdlPath";

        private static string ConnectionString
        {
            get
            {
                string udlPath = ConfigurationManager.AppSettings[UdlPathAppSettingKey];
                if (string.IsNullOrWhiteSpace(udlPath))
                {
                    throw new ConfigurationErrorsException(
                        "Missing '" + UdlPathAppSettingKey + "' appSetting in Web.config. " +
                        "It must point at the CPlatform.udl file for the current environment.");
                }
                string physicalPath = ResolvePhysicalPath(udlPath);
                if (!File.Exists(physicalPath))
                    throw new ConfigurationErrorsException(
                        "The configured prepayment UDL file was not found: " + physicalPath);

                // CPlatform environments contain both standard Unicode UDLs and legacy
                // text-encoded UDLs. OleDb's `File Name=` loader rejects the latter even
                // though their provider connection line is valid, so read that line in
                // process and pass it directly to OleDbConnection. The value is never logged.
                string providerLine = File.ReadLines(physicalPath)
                    .Select(line => line.Trim())
                    .FirstOrDefault(line => line.StartsWith("Provider=", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(providerLine))
                    throw new ConfigurationErrorsException(
                        "The configured prepayment UDL file does not contain a provider connection string.");

                return providerLine;
            }
        }

        /// <summary>
        /// A relative appSetting value must resolve against the site's physical root, not the
        /// worker process's current directory (IIS Express runs from its own install folder, so
        /// a bare relative path silently resolves to the wrong place otherwise).
        /// </summary>
        private static string ResolvePhysicalPath(string configuredPath)
        {
            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(HttpRuntime.AppDomainAppPath, configuredPath);
        }

        private static OleDbConnection OpenConnection()
        {
            var connection = new OleDbConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        /// <summary>Builds "{call schema.Proc(?,?,?)}" for a stored procedure with N parameters.</summary>
        private static string BuildCallText(string procedureName, int parameterCount)
        {
            if (parameterCount == 0)
            {
                return "{call " + procedureName + "}";
            }
            return "{call " + procedureName + "(" + string.Join(",", Enumerable.Repeat("?", parameterCount)) + ")}";
        }

        private static OleDbCommand CreateCommand(OleDbConnection connection, OleDbTransaction transaction, string commandText)
        {
            var command = connection.CreateCommand();
            command.CommandText = commandText;
            command.CommandType = CommandType.Text;
            command.Transaction = transaction;
            return command;
        }

        /// <summary>Adds one input parameter per value, in order. Nulls bind as DBNull.Value.</summary>
        private static void AddInputParameters(OleDbCommand command, object[] values)
        {
            if (values == null) return;
            foreach (var value in values)
            {
                command.Parameters.Add(new OleDbParameter { Value = value ?? DBNull.Value });
            }
        }

        // ── Stored-procedure calls ──────────────────────────────────────────────────────────

        public static List<T> Query<T>(string procedureName, Func<IDataRecord, T> map, params object[] parameters)
        {
            using (var connection = OpenConnection())
            using (var command = CreateCommand(connection, null, BuildCallText(procedureName, parameters.Length)))
            {
                AddInputParameters(command, parameters);
                using (var reader = command.ExecuteReader())
                {
                    return ReadAll(reader, map);
                }
            }
        }

        public static T QuerySingleOrDefault<T>(string procedureName, Func<IDataRecord, T> map, params object[] parameters)
        {
            var rows = Query(procedureName, map, parameters);
            return rows.Count > 0 ? rows[0] : default(T);
        }

        public static T QuerySingle<T>(string procedureName, Func<IDataRecord, T> map, params object[] parameters)
        {
            return Query(procedureName, map, parameters)[0];
        }

        public static TScalar ExecuteScalar<TScalar>(string procedureName, params object[] parameters)
        {
            using (var connection = OpenConnection())
            using (var command = CreateCommand(connection, null, BuildCallText(procedureName, parameters.Length)))
            {
                AddInputParameters(command, parameters);
                return ConvertScalar<TScalar>(command.ExecuteScalar());
            }
        }

        /// <summary>Runs a stored procedure that returns several result sets in one round trip.</summary>
        public static void QueryMultiple(string procedureName, Action<PPMMultiResult> handleResults, params object[] parameters)
        {
            using (var connection = OpenConnection())
            using (var command = CreateCommand(connection, null, BuildCallText(procedureName, parameters.Length)))
            {
                AddInputParameters(command, parameters);
                using (var reader = command.ExecuteReader())
                {
                    handleResults(new PPMMultiResult(reader));
                }
            }
        }

        /// <summary>
        /// Runs a stored procedure with a single OUTPUT parameter (Recon_SaveExtract's
        /// @NewFileId BIGINT OUTPUT — Implementation_Plan_CPlatform_Port.md §6: "OleDbParameter
        /// with Direction = ParameterDirection.Output"). <paramref name="inputValues"/> holds the
        /// proc's input parameters, in declared order; the output parameter is always the last
        /// placeholder, matching Recon_SaveExtract's declared parameter list.
        /// </summary>
        public static long ExecuteWithBigIntOutput(string procedureName, params object[] inputValues)
        {
            using (var connection = OpenConnection())
            using (var command = CreateCommand(connection, null, BuildCallText(procedureName, inputValues.Length + 1)))
            {
                AddInputParameters(command, inputValues);
                var output = new OleDbParameter
                {
                    OleDbType = OleDbType.BigInt,
                    Direction = ParameterDirection.Output
                };
                command.Parameters.Add(output);

                command.ExecuteNonQuery();

                return output.Value == null || output.Value == DBNull.Value ? 0L : Convert.ToInt64(output.Value);
            }
        }

        /// <summary>Opens a connection + transaction for a multi-statement unit of work (e.g. the bulk import) — existing pre-Phase-2 behaviour, preserved.</summary>
        public static PPMTransaction BeginTransaction()
        {
            var connection = OpenConnection();
            return new PPMTransaction(connection);
        }

        // ── Ad-hoc SQL text (literal positional `?` placeholders) ──────────────────────────

        public static List<T> QueryText<T>(string sqlText, Func<IDataRecord, T> map, params object[] parameters)
        {
            using (var connection = OpenConnection())
            using (var command = CreateCommand(connection, null, sqlText))
            {
                AddInputParameters(command, parameters);
                using (var reader = command.ExecuteReader())
                {
                    return ReadAll(reader, map);
                }
            }
        }

        public static T QuerySingleOrDefaultText<T>(string sqlText, Func<IDataRecord, T> map, params object[] parameters)
        {
            var rows = QueryText(sqlText, map, parameters);
            return rows.Count > 0 ? rows[0] : default(T);
        }

        public static int ExecuteText(string sqlText, params object[] parameters)
        {
            using (var connection = OpenConnection())
            using (var command = CreateCommand(connection, null, sqlText))
            {
                AddInputParameters(command, parameters);
                return command.ExecuteNonQuery();
            }
        }

        // ── Shared plumbing ─────────────────────────────────────────────────────────────────

        internal static List<T> ReadAll<T>(IDataReader reader, Func<IDataRecord, T> map)
        {
            var results = new List<T>();
            while (reader.Read())
            {
                results.Add(map(reader));
            }
            return results;
        }

        internal static TScalar ConvertScalar<TScalar>(object raw)
        {
            Type target = typeof(TScalar);
            Type underlying = Nullable.GetUnderlyingType(target);
            Type effective = underlying ?? target;

            if (raw == null || raw == DBNull.Value)
            {
                return (TScalar)(effective.IsValueType && underlying == null ? Activator.CreateInstance(effective) : null);
            }

            return (TScalar)Convert.ChangeType(raw, effective);
        }

        internal static OleDbCommand CreateCommandInternal(OleDbConnection connection, OleDbTransaction transaction, string commandText)
        {
            return CreateCommand(connection, transaction, commandText);
        }

        internal static void AddInputParametersInternal(OleDbCommand command, object[] values)
        {
            AddInputParameters(command, values);
        }

        internal static string BuildCallTextInternal(string procedureName, int parameterCount)
        {
            return BuildCallText(procedureName, parameterCount);
        }
    }

    /// <summary>
    /// Represents an in-flight multi-statement unit of work driven through <see cref="PPMDbHelper"/>.
    /// Not documented in the README/Plan — added only because PPMImportService's bulk import already
    /// required one atomic transaction before Phase 2, and that existing behaviour is preserved.
    /// </summary>
    public sealed class PPMTransaction : IDisposable
    {
        private readonly OleDbConnection _connection;
        private readonly OleDbTransaction _transaction;
        private bool _completed;

        internal PPMTransaction(OleDbConnection connection)
        {
            _connection = connection;
            _transaction = connection.BeginTransaction();
        }

        public List<T> Query<T>(string procedureName, Func<IDataRecord, T> map, params object[] parameters)
        {
            using (var command = PPMDbHelper.CreateCommandInternal(_connection, _transaction, PPMDbHelper.BuildCallTextInternal(procedureName, parameters.Length)))
            {
                PPMDbHelper.AddInputParametersInternal(command, parameters);
                using (var reader = command.ExecuteReader())
                {
                    return PPMDbHelper.ReadAll(reader, map);
                }
            }
        }

        public int Execute(string procedureName, params object[] parameters)
        {
            using (var command = PPMDbHelper.CreateCommandInternal(_connection, _transaction, PPMDbHelper.BuildCallTextInternal(procedureName, parameters.Length)))
            {
                PPMDbHelper.AddInputParametersInternal(command, parameters);
                return command.ExecuteNonQuery();
            }
        }

        public List<T> QueryText<T>(string sqlText, Func<IDataRecord, T> map, params object[] parameters)
        {
            using (var command = PPMDbHelper.CreateCommandInternal(_connection, _transaction, sqlText))
            {
                PPMDbHelper.AddInputParametersInternal(command, parameters);
                using (var reader = command.ExecuteReader())
                {
                    return PPMDbHelper.ReadAll(reader, map);
                }
            }
        }

        public T QuerySingleOrDefaultText<T>(string sqlText, Func<IDataRecord, T> map, params object[] parameters)
        {
            var rows = QueryText(sqlText, map, parameters);
            return rows.Count > 0 ? rows[0] : default(T);
        }

        public T QuerySingleText<T>(string sqlText, Func<IDataRecord, T> map, params object[] parameters)
        {
            return QueryText(sqlText, map, parameters)[0];
        }

        public TScalar ExecuteScalarText<TScalar>(string sqlText, params object[] parameters)
        {
            using (var command = PPMDbHelper.CreateCommandInternal(_connection, _transaction, sqlText))
            {
                PPMDbHelper.AddInputParametersInternal(command, parameters);
                return PPMDbHelper.ConvertScalar<TScalar>(command.ExecuteScalar());
            }
        }

        public int ExecuteText(string sqlText, params object[] parameters)
        {
            using (var command = PPMDbHelper.CreateCommandInternal(_connection, _transaction, sqlText))
            {
                PPMDbHelper.AddInputParametersInternal(command, parameters);
                return command.ExecuteNonQuery();
            }
        }

        /// <summary>Reads a two-column result set into a lookup dictionary (code/id style joins) — plain scalar reads, not entity mapping.</summary>
        public Dictionary<string, long> QueryStringLongDictionaryText(string sqlText, params object[] parameters)
        {
            var map = new Dictionary<string, long>();
            using (var command = PPMDbHelper.CreateCommandInternal(_connection, _transaction, sqlText))
            {
                PPMDbHelper.AddInputParametersInternal(command, parameters);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        map[reader.GetString(0)] = Convert.ToInt64(reader.GetValue(1));
                    }
                }
            }
            return map;
        }

        public void Commit()
        {
            _transaction.Commit();
            _completed = true;
        }

        public void Rollback()
        {
            _transaction.Rollback();
            _completed = true;
        }

        public void Dispose()
        {
            if (!_completed)
            {
                try { _transaction.Rollback(); }
                catch { /* connection may already be broken/closed — nothing more to do */ }
            }
            _transaction.Dispose();
            _connection.Dispose();
        }
    }

    /// <summary>
    /// One stored procedure call's worth of result sets, read in declaration order. Not
    /// documented in the README/Plan — needed because several pre-existing stored procedures
    /// (e.g. Admin_GetActionTargets, Recon_GetVarianceDetail) already return multiple result
    /// sets; preserving that existing SQL/behaviour required an equivalent read mechanism.
    /// </summary>
    public sealed class PPMMultiResult
    {
        private readonly IDataReader _reader;
        private bool _currentConsumed;

        internal PPMMultiResult(IDataReader reader)
        {
            _reader = reader;
        }

        public List<T> Read<T>(Func<IDataRecord, T> map)
        {
            AdvanceIfNeeded();
            var rows = PPMDbHelper.ReadAll(_reader, map);
            _currentConsumed = true;
            return rows;
        }

        public T ReadSingleOrDefault<T>(Func<IDataRecord, T> map)
        {
            var rows = Read(map);
            return rows.Count > 0 ? rows[0] : default(T);
        }

        private void AdvanceIfNeeded()
        {
            if (_currentConsumed)
            {
                _reader.NextResult();
                _currentConsumed = false;
            }
        }
    }

    /// <summary>
    /// Manual, explicit-by-name column readers shared by every repository's mapper methods.
    /// This is not reflection and does not discover a type's shape — each call names one exact
    /// column, matching Implementation_Plan_CPlatform_Port.md §6's instruction that "every entity
    /// read needs a manual reader-to-object mapper method" using the IDataReader/IDataRecord API.
    /// </summary>
    public static class PPMRow
    {
        public static string GetString(IDataRecord r, string column)
        {
            int i = r.GetOrdinal(column);
            return r.IsDBNull(i) ? null : r.GetValue(i).ToString();
        }

        public static int GetInt(IDataRecord r, string column)
        {
            int i = r.GetOrdinal(column);
            return r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
        }

        public static int? GetIntN(IDataRecord r, string column)
        {
            int i = r.GetOrdinal(column);
            return r.IsDBNull(i) ? (int?)null : Convert.ToInt32(r.GetValue(i));
        }

        public static long GetLong(IDataRecord r, string column)
        {
            int i = r.GetOrdinal(column);
            return r.IsDBNull(i) ? 0L : Convert.ToInt64(r.GetValue(i));
        }

        public static long? GetLongN(IDataRecord r, string column)
        {
            int i = r.GetOrdinal(column);
            return r.IsDBNull(i) ? (long?)null : Convert.ToInt64(r.GetValue(i));
        }

        public static decimal GetDecimal(IDataRecord r, string column)
        {
            int i = r.GetOrdinal(column);
            return r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i));
        }

        public static decimal? GetDecimalN(IDataRecord r, string column)
        {
            int i = r.GetOrdinal(column);
            return r.IsDBNull(i) ? (decimal?)null : Convert.ToDecimal(r.GetValue(i));
        }

        public static DateTime? GetDateTimeN(IDataRecord r, string column)
        {
            int i = r.GetOrdinal(column);
            return r.IsDBNull(i) ? (DateTime?)null : Convert.ToDateTime(r.GetValue(i));
        }

        public static bool GetBool(IDataRecord r, string column)
        {
            int i = r.GetOrdinal(column);
            return !r.IsDBNull(i) && Convert.ToBoolean(r.GetValue(i));
        }
    }
}
