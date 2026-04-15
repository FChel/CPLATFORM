using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Security.Cryptography;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Central data access + utility helper for the LPPI Review utility.
    /// Parameterised SQL only. No ORM. Connection string read from web.config
    /// appSetting "LPPI.ConnectionString" (falls back to ConnectionStrings["CPlatform"]).
    /// </summary>
    public static class LPPIHelper
    {
        // -------------------------------------------------------------------
        // Config helpers
        // -------------------------------------------------------------------

        // CPLATFORM's existing helpers (EmailHelper.cs, eJET_aspx.cs) read
        // their database connection from a Microsoft Data Link (.udl) file
        // under the site's Database folder, and use OleDbConnection (not
        // SqlConnection — SqlClient does not understand "File Name=...").
        // We follow the same convention so LPPI works on stock CPLATFORM
        // with no web.config changes.
        public static string ConnectionString
        {
            get
            {
                // 1) explicit override (useful for non-web callers / tests)
                var ovr = ConfigurationManager.AppSettings["LPPI.ConnectionString"];
                if (!string.IsNullOrEmpty(ovr)) return ovr;

                // 2) the standard CPLATFORM location
                if (HttpContext.Current != null)
                {
                    return "File Name=" + HttpContext.Current.Server.MapPath("~/Database/CPlatform.udl") + ";";
                }

                throw new InvalidOperationException(
                    "LPPI: no HttpContext available to resolve ~/Database/CPlatform.udl. " +
                    "Set the appSetting LPPI.ConnectionString explicitly for non-web callers.");
            }
        }

        public static string Setting(string key, string fallback = "")
        {
            var v = ConfigurationManager.AppSettings[key];
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        public static int SettingInt(string key, int fallback)
        {
            int n;
            return int.TryParse(Setting(key, ""), out n) ? n : fallback;
        }

        public static string SourcePath
            { get { return Setting("LPPI.SourcePath",
                @"\\d85sitcifs.dpesit.protectedsit.mil.au\d85dws01_cif_sit_01\OUTPUT_MIRROR"); } }

        public static string ExportPath
            { get { return Setting("LPPI.ExportPath", @"C:\Temp\LPPI_Exports"); } }

        public static string Environment
            { get { return Setting("LPPI.Environment", "DEV").ToUpperInvariant(); } }

        public static int ReminderWindowDays
            { get { return SettingInt("LPPI.ReminderWindowDays", 3); } }

        public static int DefaultDueDays
            { get { return SettingInt("LPPI.DefaultDueDays", 14); } }

        // -------------------------------------------------------------------
        // Identity helpers — pull current user from the host site context.
        // The host site already provides Windows / PMKeys identity; we just
        // use whatever HttpContext gives us, with a clean fallback.
        // -------------------------------------------------------------------

        public static string CurrentUserId()
        {
            try
            {
                var ctx = HttpContext.Current;
                if (ctx != null && ctx.User != null && ctx.User.Identity != null
                    && !string.IsNullOrEmpty(ctx.User.Identity.Name))
                    return ctx.User.Identity.Name;
            }
            catch { }
            return System.Environment.UserName ?? "unknown";
        }

        public static string CurrentUserDisplayName()
        {
            // The host site may surface a display name in Session; we honour it
            // if present, otherwise fall back to the identity name.
            try
            {
                var ctx = HttpContext.Current;
                if (ctx != null && ctx.Session != null)
                {
                    var dn = ctx.Session["UserDisplayName"] as string;
                    if (!string.IsNullOrEmpty(dn)) return dn;
                }
            }
            catch { }
            return CurrentUserId();
        }

        // -------------------------------------------------------------------
        // Token generation — cryptographically strong, URL-safe, opaque.
        // ~22 chars of base64url (16 random bytes). Not a sequential ID.
        // -------------------------------------------------------------------

        public static string GenerateToken()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);

            var b64 = Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
            return b64;
        }

        // -------------------------------------------------------------------
        // Low-level execution helpers — OLE DB (matches eJET / EmailHelper).
        // The rest of the LPPI code uses @-prefixed named parameters for
        // readability; we rewrite them to positional ? markers here so
        // callers do not have to care that the underlying provider is OleDb.
        // -------------------------------------------------------------------

        public static DataTable ExecuteTable(string sql, params OleDbParameter[] parameters)
        {
            var dt = new DataTable();
            using (var cn = new OleDbConnection(ConnectionString))
            using (var cmd = BuildCommand(cn, sql, parameters))
            using (var da = new OleDbDataAdapter(cmd))
            {
                da.Fill(dt);
            }
            return dt;
        }

        public static object ExecuteScalar(string sql, params OleDbParameter[] parameters)
        {
            using (var cn = new OleDbConnection(ConnectionString))
            using (var cmd = BuildCommand(cn, sql, parameters))
            {
                cn.Open();
                object o = cmd.ExecuteScalar();
                return (o == null || o == DBNull.Value) ? null : o;
            }
        }

        public static int ExecuteNonQuery(string sql, params OleDbParameter[] parameters)
        {
            using (var cn = new OleDbConnection(ConnectionString))
            using (var cmd = BuildCommand(cn, sql, parameters))
            {
                cn.Open();
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Build an OleDbCommand from a SQL string that uses @name placeholders
        /// and a flat list of named OleDbParameters. We rewrite the SQL to use
        /// positional ? markers (which OLE DB requires) and add parameters to
        /// the command in the order they appear in the SQL — the same @name
        /// can be referenced multiple times and each occurrence gets its own
        /// ? slot bound to the same value.
        /// String literals in the SQL are skipped so '@' inside quoted text
        /// is not mistaken for a parameter.
        /// </summary>
        private static OleDbCommand BuildCommand(OleDbConnection cn, string sql, OleDbParameter[] parameters)
        {
            var byName = new Dictionary<string, OleDbParameter>(StringComparer.OrdinalIgnoreCase);
            if (parameters != null)
            {
                foreach (var p in parameters)
                {
                    if (p == null || string.IsNullOrEmpty(p.ParameterName)) continue;
                    byName[p.ParameterName] = p;
                }
            }

            var rewritten = new System.Text.StringBuilder(sql.Length);
            var ordered = new List<OleDbParameter>();
            int i = 0;
            while (i < sql.Length)
            {
                char c = sql[i];

                // Pass through single-quoted string literals untouched.
                if (c == '\'')
                {
                    int end = i + 1;
                    while (end < sql.Length)
                    {
                        if (sql[end] == '\'')
                        {
                            // doubled '' is an escaped quote inside a literal
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

                if (c == '@' && i + 1 < sql.Length && (char.IsLetter(sql[i + 1]) || sql[i + 1] == '_'))
                {
                    int j = i + 1;
                    while (j < sql.Length && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_')) j++;
                    string name = sql.Substring(i, j - i); // includes '@'

                    OleDbParameter src;
                    if (!byName.TryGetValue(name, out src))
                    {
                        throw new InvalidOperationException(
                            "LPPI: SQL references parameter " + name + " but no value was supplied.");
                    }

                    // OleDbParameters cannot be shared across commands and the
                    // same @name may appear more than once in the SQL, so we
                    // clone for each occurrence. We copy OleDbType, Size,
                    // Precision and Scale as well as Value so explicitly
                    // stamped parameters (e.g. DateTime with scale 3) survive
                    // the rewrite step.
                    var clone = new OleDbParameter();
                    clone.ParameterName = "?";
                    clone.OleDbType     = src.OleDbType;
                    clone.Size          = src.Size;
                    clone.Precision     = src.Precision;
                    clone.Scale         = src.Scale;
                    clone.Value         = src.Value ?? DBNull.Value;
                    ordered.Add(clone);
                    rewritten.Append('?');
                    i = j;
                    continue;
                }

                rewritten.Append(c);
                i++;
            }

            var cmd = new OleDbCommand(rewritten.ToString(), cn);
            cmd.CommandType = CommandType.Text;
            foreach (var p in ordered) { cmd.Parameters.Add(p); }
            return cmd;
        }

        /// <summary>
        /// Convenience constructor for a named OleDbParameter. Call sites use
        /// LPPIHelper.P("@foo", value) and never see the OleDb type directly.
        ///
        /// DateTime values are converted to ISO 8601 strings before binding,
        /// sidestepping the OLE DB DBPARAMBINDINFO bScale issue entirely.
        /// SQL Server implicitly converts ISO 8601 strings (yyyy-MM-dd
        /// HH:mm:ss.fff) to datetime2 on insert/update, so the effect is
        /// identical but the OLE DB driver never has to negotiate a temporal
        /// type on the wire. This is the same strategy widely recommended
        /// for OLE DB + SQL Server interop.
        /// </summary>
        public static OleDbParameter P(string name, object value)
        {
            if (value == null || value == DBNull.Value)
                return new OleDbParameter(name, DBNull.Value);

            // Unwrap Nullable<DateTime>.
            if (value is DateTime?)
            {
                var nd = (DateTime?)value;
                if (!nd.HasValue)
                    return new OleDbParameter(name, DBNull.Value);
                value = nd.Value;
            }

            // Convert DateTime to ISO 8601 string — SQL Server will parse it
            // unambiguously into datetime2 on the server side.
            if (value is DateTime)
            {
                var dt = (DateTime)value;
                return new OleDbParameter(name, dt.ToString("yyyy-MM-dd HH:mm:ss.fff",
                    System.Globalization.CultureInfo.InvariantCulture));
            }

            return new OleDbParameter(name, value);
        }

        // -------------------------------------------------------------------
        // Parsing helpers — for BODS extracts
        // -------------------------------------------------------------------

        public static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            DateTime d;
            // Australian format
            if (DateTime.TryParseExact(s.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out d)) return d;
            if (DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out d)) return d;
            return null;
        }

        public static decimal? ParseDecimal(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            decimal d;
            if (decimal.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return d;
            return null;
        }

        public static int? ParseInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            int n;
            return int.TryParse(s.Trim(), out n) ? n : (int?)null;
        }

        public static string CleanString(string s)
        {
            if (s == null) return null;
            var t = s.Trim();
            return t.Length == 0 ? null : t;
        }

        // -------------------------------------------------------------------
        // Reason codes
        // -------------------------------------------------------------------

        public static DataTable GetReasonCodes(bool activeOnly = true)
        {
            var sql = @"SELECT ReasonCodeID, Code, Description, Outcome, DisplayOrder,
                               RequiresComments, IsActive
                        FROM dbo.tblLPPI_ReasonCodes
                        WHERE (@Active = 0 OR IsActive = 1)
                        ORDER BY DisplayOrder, ReasonCodeID";
            return ExecuteTable(sql, P("@Active", activeOnly ? 1 : 0));
        }

        // -------------------------------------------------------------------
        // Capability managers
        // -------------------------------------------------------------------

        public static DataTable GetCapabilityManagers(bool includeInactive = false)
        {
            var sql = @"SELECT cm.CmID, cm.Program, cm.DisplayName, cm.IsActive,
                               cm.CreatedDate, cm.ModifiedDate,
                               (SELECT COUNT(*) FROM dbo.tblLPPI_CapabilityManagerEmails e
                                  WHERE e.CmID = cm.CmID AND e.IsActive = 1) AS EmailCount
                        FROM dbo.tblLPPI_CapabilityManagers cm
                        WHERE (@IncludeInactive = 1 OR cm.IsActive = 1)
                        ORDER BY cm.Program";
            return ExecuteTable(sql, P("@IncludeInactive", includeInactive ? 1 : 0));
        }

        public static DataTable GetCmEmails(int cmId)
        {
            var sql = @"SELECT CmEmailID, CmID, Email, DisplayName, IsCC, IsActive
                        FROM dbo.tblLPPI_CapabilityManagerEmails
                        WHERE CmID = @CmID
                        ORDER BY IsCC, Email";
            return ExecuteTable(sql, P("@CmID", cmId));
        }

        public static List<string> GetActiveRecipients(int cmId, out List<string> ccList)
        {
            var to = new List<string>();
            var cc = new List<string>();
            var dt = GetCmEmails(cmId);
            foreach (DataRow r in dt.Rows)
            {
                if (Convert.ToBoolean(r["IsActive"]) == false) continue;
                var em = Convert.ToString(r["Email"]);
                if (string.IsNullOrWhiteSpace(em)) continue;
                if (Convert.ToBoolean(r["IsCC"])) cc.Add(em); else to.Add(em);
            }
            ccList = cc;
            return to;
        }

        public static int UpsertCapabilityManager(string program, string displayName, bool isActive)
        {
            var sql = @"
MERGE dbo.tblLPPI_CapabilityManagers AS target
USING (SELECT @Program AS Program) AS src
   ON target.Program = src.Program
WHEN MATCHED THEN
   UPDATE SET DisplayName = @DisplayName, IsActive = @IsActive, ModifiedDate = SYSDATETIME()
WHEN NOT MATCHED THEN
   INSERT (Program, DisplayName, IsActive) VALUES (@Program, @DisplayName, @IsActive)
OUTPUT inserted.CmID;";

            var o = ExecuteScalar(sql,
                P("@Program", program),
                P("@DisplayName", displayName),
                P("@IsActive", isActive ? 1 : 0));
            return Convert.ToInt32(o);
        }

        public static List<string> GetUnconfiguredPrograms()
        {
            var sql = @"
SELECT DISTINCT d.CapabilityManagerProgram
FROM dbo.tblLPPI_Documents d
WHERE d.CapabilityManagerProgram IS NOT NULL
  AND LTRIM(RTRIM(d.CapabilityManagerProgram)) <> ''
  AND NOT EXISTS (
        SELECT 1 FROM dbo.tblLPPI_CapabilityManagers cm
        INNER JOIN dbo.tblLPPI_CapabilityManagerEmails e
            ON e.CmID = cm.CmID AND e.IsActive = 1
        WHERE cm.Program = d.CapabilityManagerProgram
          AND cm.IsActive = 1
  );";
            var dt = ExecuteTable(sql);
            var list = new List<string>();
            foreach (DataRow r in dt.Rows) list.Add(Convert.ToString(r[0]));
            return list;
        }

        // -------------------------------------------------------------------
        // Dashboard summary
        // -------------------------------------------------------------------

        public static DataRow GetDashboardSummary()
        {
            var sql = @"
SELECT
   (SELECT COUNT(*) FROM dbo.tblLPPI_Documents)                                   AS TotalDocs,
   (SELECT COUNT(*) FROM dbo.tblLPPI_Reviews WHERE ReasonCodeID IS NOT NULL)      AS TotalReviewed,
   (SELECT COUNT(*) FROM dbo.tblLPPI_Documents d
       LEFT JOIN dbo.tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
       WHERE r.ReasonCodeID IS NULL)                                              AS TotalOutstanding,
   (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackages WHERE Status='Open')          AS OpenPackages,
   (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackages
       WHERE Status='Open' AND DueDate < SYSDATETIME())                               AS OverduePackages,
   (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackages
       WHERE Status='Open' AND DueDate BETWEEN SYSDATETIME() AND DATEADD(day, @WarnDays, SYSDATETIME()))
                                                                                  AS NearDeadlinePackages,
   (SELECT COUNT(*) FROM dbo.tblLPPI_LoadBatches)                                 AS TotalBatches;";
            var dt = ExecuteTable(sql, P("@WarnDays", ReminderWindowDays));
            return dt.Rows.Count > 0 ? dt.Rows[0] : null;
        }

        // -------------------------------------------------------------------
        // HTML encoding shortcut
        // -------------------------------------------------------------------

        public static string Enc(object o)
        {
            if (o == null || o == DBNull.Value) return "";
            return HttpUtility.HtmlEncode(Convert.ToString(o));
        }

        public static string FormatDate(object o, string fmt = "dd/MM/yyyy")
        {
            if (o == null || o == DBNull.Value) return "";
            DateTime d;
            if (DateTime.TryParse(Convert.ToString(o), out d))
                return d.ToString(fmt, CultureInfo.InvariantCulture);
            return "";
        }

        public static string FormatMoney(object o)
        {
            if (o == null || o == DBNull.Value) return "";
            decimal d;
            if (decimal.TryParse(Convert.ToString(o), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return d.ToString("N2", CultureInfo.GetCultureInfo("en-AU"));
            return "";
        }
    }
}