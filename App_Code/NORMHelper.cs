using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text;
using System.Web;

/// <summary>
/// Shared NORM database, configuration and identity helpers. SQL call sites use
/// readable named parameters; commands are rewritten to positional markers for
/// OLE DB in the exact order the placeholders occur.
/// </summary>
public static class NORMHelper
{
    public static string ConnectionString
    {
        get
        {
            string configured = ConfigurationManager.AppSettings["NORM.ConnectionString"];
            if (!String.IsNullOrWhiteSpace(configured)) { return configured; }
            if (HttpContext.Current == null)
            {
                throw new InvalidOperationException("NORM cannot resolve the database connection outside a web request.");
            }
            string appDataPath = HttpContext.Current.Server.MapPath("~/App_Data/Connections/CPlatform.udl");
            if (System.IO.File.Exists(appDataPath)) { return ReadUdlConnectionString(appDataPath); }
            return ReadUdlConnectionString(HttpContext.Current.Server.MapPath("~/Database/CPlatform.udl"));
        }
    }

    private static string ReadUdlConnectionString(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            throw new System.IO.FileNotFoundException("The CPlatform database connection file was not found.", path);
        }

        StringBuilder builder = new StringBuilder();
        foreach (string rawLine in System.IO.File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("[") || line.StartsWith(";")) { continue; }
            if (builder.Length > 0 && builder[builder.Length - 1] != ';') { builder.Append(';'); }
            builder.Append(line);
        }

        if (builder.Length == 0)
        {
            throw new System.IO.InvalidDataException("The CPlatform database connection file contains no connection string.");
        }
        return builder.ToString();
    }

    public static string Environment
    {
        get { return Setting("CPlatform.Environment", "DEV").ToUpperInvariant(); }
    }

    public static string Setting(string key, string fallback)
    {
        string value = ConfigurationManager.AppSettings[key];
        return String.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    public static int SettingInt(string key, int fallback)
    {
        int value;
        return Int32.TryParse(Setting(key, ""), out value) ? value : fallback;
    }

    public static string SapBaseUrl
    {
        get
        {
            string value = Setting("NORM.SapBaseUrl", Setting("LPPI.SapBaseUrl", ""));
            value = (value ?? "").Trim().TrimEnd('/');
            if (value.IndexOf("YOUR-SAP-SERVER", StringComparison.OrdinalIgnoreCase) >= 0) { return ""; }
            return value;
        }
    }

    /// <summary>
    /// Builds the standard SAP Fiori intent for Display Line Items in General
    /// Ledger (F2217). A NORM figure is an aggregate, so this is a live
    /// investigation route rather than a substitute for the retained source
    /// file or frozen NORM lineage.
    /// </summary>
    public static string SapGlLineItemsLink(string glAccount, string companyCode, int financialYear)
    {
        string baseUrl = SapBaseUrl;
        string gl = (glAccount ?? "").Trim();
        string company = (companyCode ?? "").Trim();
        if (baseUrl.Length == 0 || gl.Length == 0 || company.Length == 0 ||
            String.Equals(company, "ROMAN", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        string intent = Setting("NORM.SapGlLineItemsIntent", "GLAccount-displayGLLineItemReportingView");
        return baseUrl + "/sap/bc/ui2/flp?sap-language=EN#" + intent +
            "?GLAccount=" + HttpUtility.UrlEncode(gl) +
            "&CompanyCode=" + HttpUtility.UrlEncode(company) +
            "&FiscalYear=" + financialYear.ToString(CultureInfo.InvariantCulture) +
            "&sap-app-origin-hint=&uitype=advanced";
    }

    public static string CurrentUserId()
    {
        if (HttpContext.Current != null && HttpContext.Current.User != null &&
            HttpContext.Current.User.Identity != null && HttpContext.Current.User.Identity.IsAuthenticated)
        {
            return HttpContext.Current.User.Identity.Name;
        }
        return System.Environment.UserName ?? "unknown";
    }

    public static string CurrentUserDisplayName()
    {
        string user = CurrentUserId();
        int slash = user.LastIndexOf('\\');
        return slash >= 0 && slash < user.Length - 1 ? user.Substring(slash + 1) : user;
    }

    public static bool HasPrepareAccess()
    {
        string mode = Setting("NORM.PreparerAccessMode", "Database");
        if (String.Equals(mode, "AllAuthenticated", StringComparison.OrdinalIgnoreCase))
        {
            return HttpContext.Current != null && HttpContext.Current.User != null &&
                HttpContext.Current.User.Identity != null && HttpContext.Current.User.Identity.IsAuthenticated;
        }

        if (!HasAuthenticatedIdentity()) { return false; }
        object result = Scalar(
            "SELECT COUNT(1) FROM dbo.tblNORM_AdminUser WHERE UserId = @user AND IsDeactivated = 0",
            P("@user", CurrentUserId()));
        return result != null && Convert.ToInt32(result) > 0;
    }

    public static bool HasAdminAccess()
    {
        if (!HasAuthenticatedIdentity()) { return false; }
        object result = Scalar(
            "SELECT COUNT(1) FROM dbo.tblNORM_AdminUser " +
            "WHERE LOWER(UserId) = LOWER(@user) AND RoleCode = 'Administrator' AND IsDeactivated = 0",
            P("@user", CurrentUserId()));
        return result != null && Convert.ToInt32(result) > 0;
    }

    private static bool HasAuthenticatedIdentity()
    {
        return HttpContext.Current != null && HttpContext.Current.User != null &&
            HttpContext.Current.User.Identity != null &&
            HttpContext.Current.User.Identity.IsAuthenticated &&
            !String.IsNullOrWhiteSpace(HttpContext.Current.User.Identity.Name);
    }

    public static OleDbConnection OpenConnection()
    {
        OleDbConnection connection = new OleDbConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public static DataTable Query(string sql, params OleDbParameter[] parameters)
    {
        using (OleDbConnection connection = OpenConnection())
        using (OleDbCommand command = BuildCommand(connection, null, sql, parameters))
        using (OleDbDataAdapter adapter = new OleDbDataAdapter(command))
        {
            DataTable table = new DataTable();
            adapter.Fill(table);
            return table;
        }
    }

    public static object Scalar(string sql, params OleDbParameter[] parameters)
    {
        using (OleDbConnection connection = OpenConnection())
        using (OleDbCommand command = BuildCommand(connection, null, sql, parameters))
        {
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? null : value;
        }
    }

    public static int Exec(string sql, params OleDbParameter[] parameters)
    {
        using (OleDbConnection connection = OpenConnection())
        using (OleDbCommand command = BuildCommand(connection, null, sql, parameters))
        {
            return command.ExecuteNonQuery();
        }
    }

    public static OleDbCommand BuildCommand(OleDbConnection connection, OleDbTransaction transaction,
        string sql, params OleDbParameter[] parameters)
    {
        Dictionary<string, OleDbParameter> byName =
            new Dictionary<string, OleDbParameter>(StringComparer.OrdinalIgnoreCase);
        if (parameters != null)
        {
            for (int p = 0; p < parameters.Length; p++)
            {
                if (parameters[p] != null && !String.IsNullOrEmpty(parameters[p].ParameterName))
                {
                    byName[parameters[p].ParameterName] = parameters[p];
                }
            }
        }

        StringBuilder rewritten = new StringBuilder(sql.Length);
        List<OleDbParameter> ordered = new List<OleDbParameter>();
        int i = 0;
        while (i < sql.Length)
        {
            char c = sql[i];
            if (c == '\'')
            {
                int end = i + 1;
                while (end < sql.Length)
                {
                    if (sql[end] == '\'')
                    {
                        if (end + 1 < sql.Length && sql[end + 1] == '\'') { end += 2; continue; }
                        end++;
                        break;
                    }
                    end++;
                }
                rewritten.Append(sql, i, end - i);
                i = end;
                continue;
            }

            if (c == '@' && i + 1 < sql.Length && (Char.IsLetter(sql[i + 1]) || sql[i + 1] == '_'))
            {
                int j = i + 1;
                while (j < sql.Length && (Char.IsLetterOrDigit(sql[j]) || sql[j] == '_')) { j++; }
                string name = sql.Substring(i, j - i);
                OleDbParameter source;
                if (!byName.TryGetValue(name, out source))
                {
                    throw new InvalidOperationException("NORM SQL references " + name + " but no value was supplied.");
                }
                OleDbParameter clone = new OleDbParameter();
                clone.ParameterName = "?";
                clone.OleDbType = source.OleDbType;
                clone.Size = source.Size;
                clone.Precision = source.Precision;
                clone.Scale = source.Scale;
                clone.Value = source.Value ?? DBNull.Value;
                ordered.Add(clone);
                rewritten.Append('?');
                i = j;
                continue;
            }

            rewritten.Append(c);
            i++;
        }

        OleDbCommand command = new OleDbCommand(rewritten.ToString(), connection);
        command.Transaction = transaction;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 300;
        for (int p = 0; p < ordered.Count; p++) { command.Parameters.Add(ordered[p]); }
        return command;
    }

    public static int Exec(OleDbConnection connection, OleDbTransaction transaction,
        string sql, params OleDbParameter[] parameters)
    {
        using (OleDbCommand command = BuildCommand(connection, transaction, sql, parameters))
        {
            return command.ExecuteNonQuery();
        }
    }

    public static object Scalar(OleDbConnection connection, OleDbTransaction transaction,
        string sql, params OleDbParameter[] parameters)
    {
        using (OleDbCommand command = BuildCommand(connection, transaction, sql, parameters))
        {
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? null : value;
        }
    }

    public static DataTable Query(OleDbConnection connection, OleDbTransaction transaction,
        string sql, params OleDbParameter[] parameters)
    {
        using (OleDbCommand command = BuildCommand(connection, transaction, sql, parameters))
        using (OleDbDataAdapter adapter = new OleDbDataAdapter(command))
        {
            DataTable table = new DataTable();
            adapter.Fill(table);
            return table;
        }
    }

    public static int InsertId(OleDbConnection connection, OleDbTransaction transaction,
        string sql, params OleDbParameter[] parameters)
    {
        object value = Scalar(connection, transaction, sql + "; SELECT CAST(SCOPE_IDENTITY() AS INT);", parameters);
        if (value == null) { throw new InvalidOperationException("NORM insert did not return an identity value."); }
        return Convert.ToInt32(value);
    }

    public static OleDbParameter P(string name, object value)
    {
        if (value == null || value == DBNull.Value) { return new OleDbParameter(name, DBNull.Value); }
        if (value is DateTime)
        {
            DateTime date = (DateTime)value;
            return new OleDbParameter(name, date.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        }
        return new OleDbParameter(name, value);
    }

    public static string Str(DataRow row, string column)
    {
        return row.IsNull(column) ? null : Convert.ToString(row[column]);
    }

    public static int Int(DataRow row, string column)
    {
        return row.IsNull(column) ? 0 : Convert.ToInt32(row[column]);
    }

    public static long Long(DataRow row, string column)
    {
        return row.IsNull(column) ? 0L : Convert.ToInt64(row[column]);
    }

    public static decimal Dec(DataRow row, string column)
    {
        return row.IsNull(column) ? 0m : Convert.ToDecimal(row[column]);
    }
}
