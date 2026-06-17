using System;
using System.Web;
using System.Data;
using System.Data.OleDb;
using System.Collections.Generic;
using System.Configuration;

/// <summary>
/// Data-access helper for NORM. Mirrors the LPPIHelper pattern: a P() factory
/// for parameters plus thin Query/Exec/Scalar wrappers over OLE DB.
///
/// NOTE on OLE DB parameters: the OLE DB provider binds parameters positionally
/// by "?" placeholders, not by name. The Name on each OleDbParameter is for
/// readability only — order is what matters. Always list parameters in the same
/// order the "?" appear in the SQL.
/// </summary>
public static class NORMHelper
{
    public static string ConnString
    {
    get
    {
        // 1) explicit override (useful for non-web callers / tests)
        string ovr = ConfigurationManager.AppSettings["NORM.ConnectionString"];
        if (!string.IsNullOrEmpty(ovr)) { return ovr; }

        // 2) the standard CPLATFORM location — same UDL the rest of the platform uses
        if (HttpContext.Current != null)
        {
            return "File Name=" + HttpContext.Current.Server.MapPath("~/Database/CPlatform.udl") + ";";
        }

        throw new InvalidOperationException(
            "NORM: no HttpContext available to resolve ~/Database/CPlatform.udl.");
    }
    }

    /// <summary>Build a parameter. Null is converted to DBNull.</summary>
    public static OleDbParameter P(string name, object value)
    {
        OleDbParameter p = new OleDbParameter();
        p.ParameterName = name;
        if (value == null)
        {
            p.Value = DBNull.Value;
        }
        else
        {
            p.Value = value;
        }
        return p;
    }

    public static DataTable Query(string sql, params OleDbParameter[] ps)
    {
        DataTable dt = new DataTable();
        using (OleDbConnection cn = new OleDbConnection(ConnString))
        {
            using (OleDbCommand cmd = new OleDbCommand(sql, cn))
            {
                if (ps != null)
                {
                    for (int i = 0; i < ps.Length; i++)
                    {
                        cmd.Parameters.Add(ps[i]);
                    }
                }
                cn.Open();
                using (OleDbDataAdapter da = new OleDbDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }
        }
        return dt;
    }

    public static int Exec(string sql, params OleDbParameter[] ps)
    {
        using (OleDbConnection cn = new OleDbConnection(ConnString))
        {
            using (OleDbCommand cmd = new OleDbCommand(sql, cn))
            {
                if (ps != null)
                {
                    for (int i = 0; i < ps.Length; i++)
                    {
                        cmd.Parameters.Add(ps[i]);
                    }
                }
                cn.Open();
                return cmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>Insert and return the new identity (SCOPE_IDENTITY). SQL must NOT include the SELECT.</summary>
    public static int ExecReturnId(string sql, params OleDbParameter[] ps)
    {
        using (OleDbConnection cn = new OleDbConnection(ConnString))
        {
            using (OleDbCommand cmd = new OleDbCommand(sql + "; SELECT CAST(SCOPE_IDENTITY() AS INT);", cn))
            {
                if (ps != null)
                {
                    for (int i = 0; i < ps.Length; i++)
                    {
                        cmd.Parameters.Add(ps[i]);
                    }
                }
                cn.Open();
                object o = cmd.ExecuteScalar();
                if (o == null || o == DBNull.Value)
                {
                    return 0;
                }
                return Convert.ToInt32(o);
            }
        }
    }

    public static object Scalar(string sql, params OleDbParameter[] ps)
    {
        using (OleDbConnection cn = new OleDbConnection(ConnString))
        {
            using (OleDbCommand cmd = new OleDbCommand(sql, cn))
            {
                if (ps != null)
                {
                    for (int i = 0; i < ps.Length; i++)
                    {
                        cmd.Parameters.Add(ps[i]);
                    }
                }
                cn.Open();
                return cmd.ExecuteScalar();
            }
        }
    }

    // ---- small typed readers (avoid null-conditional, which is not in C# 5) ----
    public static string Str(DataRow r, string col)
    {
        if (r.IsNull(col)) { return null; }
        return Convert.ToString(r[col]);
    }

    public static int Int(DataRow r, string col)
    {
        if (r.IsNull(col)) { return 0; }
        return Convert.ToInt32(r[col]);
    }

    public static decimal Dec(DataRow r, string col)
    {
        if (r.IsNull(col)) { return 0m; }
        return Convert.ToDecimal(r[col]);
    }
}
