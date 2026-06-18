using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Central data access + utility helper for the LPPI Review utility.
    /// Parameterised SQL only. No ORM. Connection string read from web.config
    /// appSetting "LPPI.ConnectionString" (falls back to a UDL under
    /// ~/Database/CPlatform.udl, matching CPLATFORM convention).
    ///
    ///  Reviewer page now supports two token types: AS Fin token (the
    ///  existing tblLPPI_ReviewPackages.Token) and POC token (new
    ///  tblLPPI_PackagePocs.Token). ResolveReviewToken inspects both
    ///  tables and returns a typed result so callers can dispatch on it.
    /// </summary>
    public static class LPPIHelper
    {
        // -------------------------------------------------------------------
        // Lifecycle status constants and sets
        //
        // The package lifecycle (driven entirely by app code, not SQL defaults):
        //
        //     NotSent -> Sent -> InReview -> Finalised -> Exported
        //                                       ^
        //                                       └── admin can Unfinalise back
        //                                           to InReview while still
        //                                           Finalised (i.e. not yet
        //                                           Exported).
        //
        //     Cancelled is the side-branch, terminal.
        //
        // ActivePackageStatusList covers everything that is in flight — i.e.
        // created and not yet either committed to ERP (Exported) or withdrawn
        // (Cancelled). Includes Finalised: from a workflow perspective, a
        // Finalised package is still "in the system" — admins can unfinalise
        // it, and it has not yet been billed to ERP. It only leaves the
        // dashboard active list when it goes to Exported or Cancelled.
        // -------------------------------------------------------------------
        public const string StatusNotSent   = "NotSent";
        public const string StatusSent      = "Sent";
        public const string StatusInReview  = "InReview";
        public const string StatusFinalised = "Finalised";
        public const string StatusExported  = "Exported";
        public const string StatusCancelled = "Cancelled";

        // SQL-quoted IN list of statuses considered "active" for the dashboard,
        // package lists, etc. Excludes the two terminal statuses (Exported,
        // Cancelled).
        public const string ActivePackageStatusList = "'NotSent','Sent','InReview','Finalised'";

        // SQL-quoted IN list for the cumulative ("All cycles") Summary scope.
        // Adds Exported to the active list so an all-cycles total includes
        // shipped packages; Cancelled stays excluded.
        public const string CumulativePackageStatusList = "'NotSent','Sent','InReview','Finalised','Exported'";

        // Reviewer-page write gate — saves are rejected when the package is
        // in any of these states. Only a Finalised package can be unfinalised
        // (admin action on Send-outs); Exported and Cancelled are terminal.
        public const string ReadOnlyPackageStatusList = "'Finalised','Exported','Cancelled'";

        // The system reason code applied when AS Fin clicks Finalise on a
        // package that still has undecided documents. IsActive = 0 in
        // tblLPPI_ReasonCodes so it does NOT appear in the reviewer dropdown.
        public const string NoResponseReasonCode = "RC-NR";

        // The reload-eligible reason code. Outcome = NotPayable, but with
        // a side effect: when the package is finalised, every line of any
        // document carrying RC-RL gets IsDeactivated = 1, which excludes
        // it from ERP exports and exempts the same (DocNoAccounting,
        // ItemSequence) from the load-time duplicate skip — the next file
        // load that contains a corrected row supersedes the deactivated
        // one. IsActive = 1 (visible in the reviewer dropdown).
        public const string ReloadReasonCode = "RC-RL";

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

        public static string Environment
            { get { return Setting("CPlatform.Environment", "DEV").ToUpperInvariant(); } }

        /// <summary>
        /// Environment tag prefixed to admin-downloaded export filenames so
        /// admins can tell UAT and PROD downloads apart in their Downloads
        /// folder. Uppercase, suffixed with an underscore so the caller
        /// concatenates without ceremony — e.g. "UAT_LPPI_Summary_...xlsx".
        ///
        /// Sanitised: only A-Z and 0-9 survive, anything else falls back to
        /// "ENV". Length capped at 8 characters to keep filenames sane.
        /// </summary>
        public static string EnvironmentFileTag
        {
            get
            {
                var env = Environment;
                var sb = new System.Text.StringBuilder(env.Length);
                foreach (char c in env)
                {
                    if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                        sb.Append(c);
                    if (sb.Length >= 8) break;
                }
                return (sb.Length == 0 ? "ENV" : sb.ToString()) + "_";
            }
        }

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
        // Access control
        //
        // Access model:
        //   Reviewer page  = token-based; admin gate disabled. IIS Windows
        //                    auth still captures the identity though, and we
        //                    use it for audit (ChangedByName etc.).
        //                    Two token types: AS Fin (full package) and POC
        //                    (POC-scoped); see ResolveReviewToken.
        //   Everything else = gated by tblLPPI_AdminUsers.
        //   Admin           = full access to all LPPI admin pages and actions.
        //   Non-admin       = LPPI_Review.aspx only (via token link).
        //
        // Results are cached in HttpContext.Items for the lifetime of the
        // current request — one DB round-trip per page load, not per call.
        // -------------------------------------------------------------------

        private const string ItemsKeyAdmin = "LPPI_IsAdmin";

        /// <summary>
        /// Returns true if the current Windows identity is an active admin
        /// in tblLPPI_AdminUsers. Cached per request in HttpContext.Items.
        /// </summary>
        public static bool IsAdminUser()
        {
            var ctx = HttpContext.Current;
            if (ctx == null) return false;

            if (ctx.Items.Contains(ItemsKeyAdmin))
                return (bool)ctx.Items[ItemsKeyAdmin];

            bool result = false;
            try
            {
                string userId = CurrentUserId();
                if (!string.IsNullOrEmpty(userId))
                {
                    var o = ExecuteScalar(
                        @"SELECT COUNT(1) FROM dbo.tblLPPI_AdminUsers
                          WHERE LOWER(UserId) = LOWER(@UserId) AND IsActive = 1",
                        P("@UserId", userId));
                    result = (o != null && Convert.ToInt32(o) > 0);
                }
            }
            catch { }

            ctx.Items[ItemsKeyAdmin] = result;
            return result;
        }

        /// <summary>
        /// Convenience alias — true if the current user has any LPPI admin
        /// access (currently equivalent to IsAdminUser).
        /// </summary>
        public static bool HasLppiAccess()
        {
            return IsAdminUser();
        }

        // -------------------------------------------------------------------
        // Token generation — cryptographically strong, URL-safe, opaque.
        // ~22 chars of base64url (16 random bytes). Not a sequential ID.
        // Same generator is used for both AS Fin tokens (on tblLPPI_ReviewPackages)
        // and POC tokens (on tblLPPI_PackagePocs).
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
        // Reviewer-token resolution
        //
        // The reviewer page accepts a single ?t=<token> querystring. The token
        // can be either an AS Fin token (full package, can save and finalise)
        // or a POC token (POC-scoped view, can save but not finalise). We
        // look in tblLPPI_ReviewPackages first because that lookup is by the
        // primary unique-key on Token; on miss, we look in tblLPPI_PackagePocs.
        //
        // Returns a struct describing what was found. Callers dispatch on
        // TokenKind:
        //   AsFin -> proceed as before; full package view, finalise allowed.
        //   Poc   -> filter docs to PocEmail; suppress finalise button.
        //   None  -> show the generic "invalid link" page.
        // -------------------------------------------------------------------

        public enum ReviewTokenKind { None = 0, AsFin = 1, Poc = 2 }

        public class ReviewTokenInfo
        {
            public ReviewTokenKind Kind;
            public int             PackageID;
            public string          PocEmail;     // POC tokens only — null otherwise
            public int             PackagePocID; // POC tokens only — 0 otherwise
        }

        public static ReviewTokenInfo ResolveReviewToken(string token)
        {
            var info = new ReviewTokenInfo { Kind = ReviewTokenKind.None };
            if (string.IsNullOrWhiteSpace(token)) return info;
            string t = token.Trim();

            // 1) AS Fin token
            object pidObj = ExecuteScalar(
                "SELECT PackageID FROM dbo.tblLPPI_ReviewPackages WHERE Token = @t",
                P("@t", t));
            if (pidObj != null && pidObj != DBNull.Value)
            {
                info.Kind      = ReviewTokenKind.AsFin;
                info.PackageID = Convert.ToInt32(pidObj);
                return info;
            }

            // 2) POC token
            DataTable dt = ExecuteTable(
                @"SELECT PackagePocID, PackageID, PocEmail
                    FROM dbo.tblLPPI_PackagePocs
                   WHERE Token = @t",
                P("@t", t));
            if (dt.Rows.Count == 1)
            {
                DataRow r = dt.Rows[0];
                info.Kind         = ReviewTokenKind.Poc;
                info.PackagePocID = Convert.ToInt32(r["PackagePocID"]);
                info.PackageID    = Convert.ToInt32(r["PackageID"]);
                info.PocEmail     = Convert.ToString(r["PocEmail"]);
                return info;
            }

            return info;
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
        // Email validation — Defence-only, app-side gate.
        //
        // The CK_tblLPPI_CapabilityManagers_Email constraint catches anything
        // that bypasses the UI; this helper is the user-facing gate. Returns
        // the cleaned address (lowercased) on success, or null when the
        // address is malformed or not a defence.gov.au address. errorMessage
        // is set to a UI-suitable message on failure.
        //
        // Used by the CM admin page when saving Email, and by the email send
        // pipeline to filter out malformed POC addresses pulled from BODS.
        // -------------------------------------------------------------------

        private static readonly Regex EmailShape =
            new Regex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$",
                      RegexOptions.Compiled);

        public static string ValidateDefenceEmail(string raw, out string errorMessage)
        {
            errorMessage = null;
            string s = (raw ?? "").Trim();
            if (s.Length == 0) { errorMessage = "Email is required."; return null; }

            if (!EmailShape.IsMatch(s))
            {
                errorMessage = "Email is not a valid address.";
                return null;
            }

            // Accept @defence.gov.au / @<sub>.defence.gov.au and
            // @annpsr.gov.au / @<sub>.annpsr.gov.au. Both are legitimate
            // Defence-agency domains for AS Fin / POC contacts. Mirrors the
            // CK_tblLPPI_CapabilityManagers_Email DB constraint.
            string lower = s.ToLowerInvariant();
            int atIdx = lower.LastIndexOf('@');
            string domain = atIdx >= 0 ? lower.Substring(atIdx + 1) : "";

            bool ok = domain == "defence.gov.au"
                   || domain.EndsWith(".defence.gov.au", StringComparison.Ordinal)
                   || domain == "annpsr.gov.au"
                   || domain.EndsWith(".annpsr.gov.au", StringComparison.Ordinal);
            if (!ok)
            {
                errorMessage = "Only defence.gov.au or annpsr.gov.au addresses are accepted (subdomains allowed).";
                return null;
            }

            return lower;
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

        /// <summary>
        /// Looks up a single reason code by its Code string (e.g. "RC-NR")
        /// and returns its ReasonCodeID. Used by the finalise flow to find
        /// the system "no response" code without hard-coding an ID. Returns
        /// null when the code does not exist or is missing.
        /// </summary>
        public static int? GetReasonCodeIdByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var o = ExecuteScalar(
                "SELECT ReasonCodeID FROM dbo.tblLPPI_ReasonCodes WHERE Code = @c",
                P("@c", code.Trim()));
            if (o == null || o == DBNull.Value) return null;
            return Convert.ToInt32(o);
        }

        // -------------------------------------------------------------------
        // Capability managers
        //
        // The recipient model collapsed (May 2026): each CM has a single
        // Email + EmailDisplayName on the row itself. There is no
        // tblLPPI_CapabilityManagerEmails any more.
        //
        //   - GetCapabilityManagers — list view with per-CM email projected
        //     in line. EmailConfigured is a bit so the CM admin page can
        //     render a status pill without reading the address itself.
        //   - GetCmEmail — single-row lookup for a CM, returns null when
        //     not configured.
        //   - UpsertCapabilityManager / SaveCmEmail — write paths.
        // -------------------------------------------------------------------

        public class CmEmail
        {
            public int    CmID;
            public string Program;
            public string Email;
            public string EmailDisplayName;
            /// <summary>True iff Email AND EmailDisplayName are both populated.</summary>
            public bool   IsConfigured
            {
                get
                {
                    return !string.IsNullOrWhiteSpace(Email)
                        && !string.IsNullOrWhiteSpace(EmailDisplayName);
                }
            }
        }

        public static DataTable GetCapabilityManagers(bool includeInactive = false)
        {
            var sql = @"SELECT cm.CmID, cm.Program,
                               cm.Email, cm.EmailDisplayName,
                               cm.IsActive, cm.CreatedDate, cm.ModifiedDate,
                               CASE WHEN cm.Email IS NOT NULL
                                     AND LTRIM(RTRIM(cm.Email)) <> ''
                                     AND cm.EmailDisplayName IS NOT NULL
                                     AND LTRIM(RTRIM(cm.EmailDisplayName)) <> ''
                                    THEN 1 ELSE 0 END AS EmailConfigured
                        FROM dbo.tblLPPI_CapabilityManagers cm
                        WHERE (@IncludeInactive = 1 OR cm.IsActive = 1)
                        ORDER BY cm.Program";
            return ExecuteTable(sql, P("@IncludeInactive", includeInactive ? 1 : 0));
        }

        /// <summary>
        /// Returns the configured CM email + display name for sending.
        /// Returns null if the CM does not exist, or if either field is
        /// missing — the send pipeline checks IsConfigured to gate dispatch.
        /// </summary>
        public static CmEmail GetCmEmail(int cmId)
        {
            const string sql = @"
SELECT CmID, Program, Email, EmailDisplayName
  FROM dbo.tblLPPI_CapabilityManagers
 WHERE CmID = @CmID";
            var dt = ExecuteTable(sql, P("@CmID", cmId));
            if (dt.Rows.Count != 1) return null;
            DataRow r = dt.Rows[0];
            return new CmEmail
            {
                CmID             = Convert.ToInt32(r["CmID"]),
                Program          = AsStr(r["Program"]),
                Email            = AsStr(r["Email"]),
                EmailDisplayName = AsStr(r["EmailDisplayName"])
            };
        }

        /// <summary>
        /// Returns the count of active CM groups missing email configuration.
        /// Used by the CM admin page to render a single banner at the top
        /// of the list.
        /// </summary>
        public static int CountCmsMissingEmail()
        {
            object o = ExecuteScalar(@"
SELECT COUNT(*)
  FROM dbo.tblLPPI_CapabilityManagers cm
 WHERE cm.IsActive = 1
   AND (cm.Email IS NULL
        OR LTRIM(RTRIM(cm.Email)) = ''
        OR cm.EmailDisplayName IS NULL
        OR LTRIM(RTRIM(cm.EmailDisplayName)) = '')");
            return o == null ? 0 : Convert.ToInt32(o);
        }

        public static int UpsertCapabilityManager(string program, bool isActive)
        {
            // Inserts a new CM row keyed by Program, or updates the active
            // flag on an existing one. Deliberately does NOT touch Email /
            // EmailDisplayName — file-load auto-create cannot populate
            // those (BODS does not supply them), and admin edits to the
            // existing email must not be wiped by a re-load.
            var sql = @"
MERGE dbo.tblLPPI_CapabilityManagers AS target
USING (SELECT @Program AS Program) AS src
   ON target.Program = src.Program
WHEN MATCHED THEN
   UPDATE SET IsActive = @IsActive, ModifiedDate = SYSDATETIME()
WHEN NOT MATCHED THEN
   INSERT (Program, IsActive) VALUES (@Program, @IsActive)
OUTPUT inserted.CmID;";

            var o = ExecuteScalar(sql,
                P("@Program", program),
                P("@IsActive", isActive ? 1 : 0));
            return Convert.ToInt32(o);
        }

        /// <summary>
        /// Save the CM email + display name. Validates that the address is a
        /// defence.gov.au address. Allows clearing both fields by passing
        /// null/empty for both. If only one is supplied, returns false with
        /// errorMessage describing the gap.
        /// </summary>
        public static bool SaveCmEmail(int cmId, string emailRaw, string displayName, out string errorMessage)
        {
            errorMessage = null;
            string em = (emailRaw ?? "").Trim();
            string dn = (displayName ?? "").Trim();

            // Both blank — clear the configuration.
            if (em.Length == 0 && dn.Length == 0)
            {
                ExecuteNonQuery(@"
UPDATE dbo.tblLPPI_CapabilityManagers
   SET Email = NULL,
       EmailDisplayName = NULL,
       ModifiedDate = SYSDATETIME()
 WHERE CmID = @CmID",
                    P("@CmID", cmId));
                return true;
            }

            // Either set: both must be set, and the email must validate.
            if (em.Length == 0 || dn.Length == 0)
            {
                errorMessage = "Both email and display name are required (or leave both blank to clear).";
                return false;
            }

            string cleaned = ValidateDefenceEmail(em, out errorMessage);
            if (cleaned == null) return false;

            ExecuteNonQuery(@"
UPDATE dbo.tblLPPI_CapabilityManagers
   SET Email = @Email,
       EmailDisplayName = @Dn,
       ModifiedDate = SYSDATETIME()
 WHERE CmID = @CmID",
                P("@Email", cleaned),
                P("@Dn",    dn),
                P("@CmID",  cmId));
            return true;
        }

        /// <summary>
        /// Returns the list of active CM Programs whose Email or
        /// EmailDisplayName is not configured — i.e. would be refused at
        /// send time. Used by the Send-outs warning banner and the file-load
        /// reconcile follow-up message.
        /// </summary>
        public static List<string> GetUnconfiguredPrograms()
        {
            var sql = @"
SELECT cm.Program
  FROM dbo.tblLPPI_CapabilityManagers cm
 WHERE cm.IsActive = 1
   AND (cm.Email IS NULL
        OR LTRIM(RTRIM(cm.Email)) = ''
        OR cm.EmailDisplayName IS NULL
        OR LTRIM(RTRIM(cm.EmailDisplayName)) = '')
   AND EXISTS (SELECT 1 FROM dbo.tblLPPI_Documents d
                WHERE d.CapabilityManagerProgram = cm.Program)
 ORDER BY cm.Program";
            var dt = ExecuteTable(sql);
            var list = new List<string>();
            foreach (DataRow r in dt.Rows) list.Add(Convert.ToString(r[0]));
            return list;
        }

        // -------------------------------------------------------------------
        // Dashboard package + batch counts.
        //
        // Returns the package-status tallies and total batch count for the
        // Dashboard stat-grid. Document, reviewed and dollar figures are NOT
        // here — the Dashboard sources those from
        // GetSummaryScopeHeader(CurrentCycle) so its headline matches the
        // Summary page and reflects the live cycle rather than drifting
        // cumulative as packages ship.
        //
        // OpenPackages / Overdue / NearDeadline are scoped to
        // ActivePackageStatusList (NotSent / Sent / InReview / Finalised);
        // Exported and Cancelled are terminal and drop off. TotalBatches is a
        // system-wide load count.
        // -------------------------------------------------------------------
        public static DataRow GetDashboardSummary()
        {
            // The IN list is built from the StatusXxx constants so a future
            // status rename only happens in one place.
            var activeIn = ActivePackageStatusList;

            var sql = @"
SELECT
   (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackages
       WHERE Status IN (" + activeIn + @"))                                      AS OpenPackages,
   (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackages
       WHERE Status IN (" + activeIn + @")
         AND DueDate < SYSDATETIME())                                            AS OverduePackages,
   (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackages
       WHERE Status IN (" + activeIn + @")
         AND DueDate BETWEEN SYSDATETIME() AND DATEADD(day, @WarnDays, SYSDATETIME()))
                                                                                 AS NearDeadlinePackages,
   (SELECT COUNT(*) FROM dbo.tblLPPI_LoadBatches)                                AS TotalBatches;";
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

        // -------------------------------------------------------------------
        // SAP Fiori deep-link helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns the configured SAP base URL with trailing slashes trimmed, or ""
        /// if the LPPI.SapBaseUrl app setting is missing / empty.
        /// </summary>
        public static string SapBaseUrl
        {
            get
            {
                var raw = Setting("LPPI.SapBaseUrl", "");
                if (string.IsNullOrWhiteSpace(raw)) return "";
                return raw.TrimEnd('/');
            }
        }

        /// <summary>
        /// Build an SAP Fiori deep link for a Purchase Order. Returns "" if the
        /// base URL or the PO value is missing.
        /// </summary>
        public static string SapPoLink(object poNumber)
        {
            string po = (poNumber == null || poNumber == DBNull.Value) ? "" : Convert.ToString(poNumber).Trim();
            if (po.Length == 0) return "";
            var baseUrl = SapBaseUrl;
            if (baseUrl.Length == 0) return "";

            return baseUrl
                 + "/sap/bc/ui2/flp?sap-language=EN#PurchaseOrder-display"
                 + "?PurchaseOrder=" + System.Uri.EscapeDataString(po)
                 + "&sap-app-origin-hint="
                 + "&uitype=advanced";
        }

        /// <summary>
        /// Build an SAP Fiori deep link for an FI accounting document.
        /// fiscalYear is the document's own fiscal year as carried in the BODS
        /// FISCAL_YEAR column (e.g. "2025", "2026"). Appended to the URL when
        /// it parses as a 4-digit integer in the 1900..2999 range; omitted
        /// otherwise so a malformed value never produces a broken URL.
        /// </summary>
        public static string SapFiLink(object docNoAccounting, object companyCode, object fiscalYear)
        {
            string doc = (docNoAccounting == null || docNoAccounting == DBNull.Value) ? "" : Convert.ToString(docNoAccounting).Trim();
            string cc  = (companyCode     == null || companyCode     == DBNull.Value) ? "" : Convert.ToString(companyCode).Trim();
            string fy  = (fiscalYear      == null || fiscalYear      == DBNull.Value) ? "" : Convert.ToString(fiscalYear).Trim();

            if (doc.Length == 0) return "";
            var baseUrl = SapBaseUrl;
            if (baseUrl.Length == 0) return "";

            var sb = new System.Text.StringBuilder();
            sb.Append(baseUrl)
              .Append("/sap/bc/ui2/flp?sap-language=EN#AccountingDocument-displayDocument")
              .Append("?AccountingDocument=").Append(System.Uri.EscapeDataString(doc));

            if (cc.Length > 0)
            {
                sb.Append("&CompanyCode=").Append(System.Uri.EscapeDataString(cc));
            }

            // Append fiscal year only when it parses cleanly as a 4-digit year.
            // BODS sometimes emits blank or malformed values; in that case omit
            // the parameter rather than send junk to SAP.
            int fyNum;
            if (fy.Length > 0
                && int.TryParse(fy, NumberStyles.Integer, CultureInfo.InvariantCulture, out fyNum)
                && fyNum >= 1900 && fyNum <= 2999)
            {
                sb.Append("&FiscalYear=").Append(fyNum.ToString(CultureInfo.InvariantCulture));
            }

            sb.Append("&sap-app-origin-hint=")
              .Append("&uitype=advanced");
            return sb.ToString();
        }

        /// <summary>
        /// Render a PO number as an anchor to its SAP Fiori PO display page, or as
        /// plain HTML-encoded text when the URL cannot be built.
        /// </summary>
        public static string SapPoNumberHtml(object poNumber)
        {
            string po = (poNumber == null || poNumber == DBNull.Value) ? "" : Convert.ToString(poNumber).Trim();
            if (po.Length == 0) return "";

            var href = SapPoLink(poNumber);
            if (href.Length == 0) return Enc(po);

            return BuildNumberAnchor(href, po, "Open PO " + po + " in SAP");
        }

        /// <summary>
        /// Render an FI document number as an anchor to its SAP Fiori
        /// Accounting-Document display page, or plain HTML-encoded text when the
        /// URL cannot be built. fiscalYear comes from the document's own
        /// FISCAL_YEAR column (BODS-supplied).
        /// </summary>
        public static string SapFiNumberHtml(object docNoAccounting, object companyCode, object fiscalYear)
        {
            string doc = (docNoAccounting == null || docNoAccounting == DBNull.Value) ? "" : Convert.ToString(docNoAccounting).Trim();
            if (doc.Length == 0) return "";

            var href = SapFiLink(docNoAccounting, companyCode, fiscalYear);
            if (href.Length == 0) return Enc(doc);

            return BuildNumberAnchor(href, doc, "Open document " + doc + " in SAP");
        }

        /// <summary>
        /// Build an SAP webgui deep link for a VIM document (ZFIVIMPOC transaction).
        /// Webgui has SSO so this opens directly to the document.
        ///
        /// Returns "" if the base URL or the VIM doc ID is missing.
        /// </summary>
        public static string SapVimLink(object vimDocId)
        {
            string id = (vimDocId == null || vimDocId == DBNull.Value) ? "" : Convert.ToString(vimDocId).Trim();
            if (id.Length == 0) return "";
            var baseUrl = SapBaseUrl;
            if (baseUrl.Length == 0) return "";

            return baseUrl
                 + "/sap/bc/gui/sap/its/webgui?~transaction=*ZFIVIMPOC%20S_DOCID-LOW="
                 + System.Uri.EscapeDataString(id)
                 + ";DYNP_OKCODE=ONLI#";
        }

        /// <summary>
        /// Render a VIM document ID as an anchor to its webgui display page,
        /// or as plain HTML-encoded text when the URL cannot be built.
        /// </summary>
        public static string SapVimNumberHtml(object vimDocId)
        {
            string id = (vimDocId == null || vimDocId == DBNull.Value) ? "" : Convert.ToString(vimDocId).Trim();
            if (id.Length == 0) return "";

            var href = SapVimLink(vimDocId);
            if (href.Length == 0) return Enc(id);

            return BuildNumberAnchor(href, id, "Open VIM document " + id + " in SAP");
        }

        private static string BuildNumberAnchor(string href, string text, string title)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<a href=\"").Append(HttpUtility.HtmlAttributeEncode(href)).Append("\"")
              .Append(" target=\"_blank\" rel=\"noopener\"")
              .Append(" title=\"").Append(HttpUtility.HtmlAttributeEncode(title)).Append("\">")
              .Append(Enc(text))
              .Append("</a>");
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Finalise / Unfinalise — package-level lifecycle transitions.
        //
        // FINALISE
        //   AS Fin clicks Finalise on the reviewer page. Any document still
        //   without a reason code is auto-stamped with reason code 'RC-NR'
        //   (Payable per RMG-417, no response received). A history row is
        //   written for each auto-applied review. The package status flips
        //   to 'Finalised' and FinalisedDate / FinalisedBy are stamped.
        //
        //   The reviewer page becomes read-only after this. Admin can
        //   Unfinalise from Send-outs until the package is Exported.
        //
        //   All work happens in a single transaction so a partial failure
        //   leaves the package in its previous state.
        //
        // UNFINALISE
        //   Admin clicks Unfinalise on Send-outs. The auto-applied 'RC-NR'
        //   reviews are wiped (option (i) — clean intent). For each wiped
        //   review, a history row is written with ReasonCodeID = NULL and
        //   a marker in the comments so the audit trail is unambiguous.
        //
        //   Status flips back to 'InReview', FinalisedDate / FinalisedBy
        //   are cleared, and the reviewer page becomes editable again.
        //   Refused if the package is already Exported (terminal).
        //
        // The reviewer page only ever calls FinalisePackage; admin-side
        // UnfinalisePackage lives behind the admin gate.
        //
        // POC tokens cannot drive either transition — the .ashx handlers
        // refuse POC-token calls before reaching here.
        // -------------------------------------------------------------------

        public class LifecycleResult
        {
            public bool   Success;
            public string ErrorMessage;
            public int    AutoAppliedCount;   // FINALISE only — # docs auto-coded RC-NR
            public int    AutoClearedCount;   // UNFINALISE only — # RC-NR rows wiped
        }

        /// <summary>
        /// Finalise a package. Auto-applies reason code RC-NR to any
        /// undecided document, flips status to Finalised, stamps
        /// FinalisedDate and FinalisedBy. Refuses unless the package is in
        /// NotSent / Sent / InReview.
        /// </summary>
        public static LifecycleResult FinalisePackage(int packageId)
        {
            var result = new LifecycleResult();

            // Look up RC-NR id once. If it's missing the schema is corrupt.
            int? noRespId = GetReasonCodeIdByCode(NoResponseReasonCode);
            if (!noRespId.HasValue)
            {
                result.Success = false;
                result.ErrorMessage = "System reason code '" + NoResponseReasonCode +
                    "' is missing — finalise cannot proceed. Re-run the schema script.";
                return result;
            }

            // Look up the package's current status.
            object stObj = ExecuteScalar(
                "SELECT Status FROM dbo.tblLPPI_ReviewPackages WHERE PackageID = @p",
                P("@p", packageId));
            if (stObj == null)
            {
                result.Success = false;
                result.ErrorMessage = "Package not found.";
                return result;
            }
            string status = Convert.ToString(stObj);

            // Status guard — finalise only valid for in-flight states.
            if (!(string.Equals(status, StatusNotSent,  StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(status, StatusSent,     StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(status, StatusInReview, StringComparison.OrdinalIgnoreCase)))
            {
                result.Success = false;
                result.ErrorMessage = "Cannot finalise a package whose status is '" + status +
                    "'. Finalise is only available for NotSent, Sent or InReview packages.";
                return result;
            }

            string userId   = CurrentUserId();
            string userName = CurrentUserDisplayName();

            // ISO 8601 string used as both ReviewedDate and ChangedDate so
            // the auto-applied reviews and their history rows share a single
            // timestamp.
            string nowIso = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string autoComment = "AS Fin finalised on " +
                DateTime.Now.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-AU")) +
                " — no response received from CM by close-off.";

            using (var cn = new OleDbConnection(ConnectionString))
            {
                cn.Open();
                using (var tx = cn.BeginTransaction())
                {
                    try
                    {
                        // 1) Find every package document that has no review row,
                        //    and INSERT one with RC-NR. Each INSERT is paired
                        //    with a history row.
                        //
                        //    Documents that already have a review (any code, or
                        //    even NULL) are left alone — we do not overwrite
                        //    deliberate decisions. The query targets documents
                        //    where tblLPPI_Reviews has no row at all.
                        //
                        //    Then, separately, deal with any review row that
                        //    exists but has ReasonCodeID = NULL: stamp RC-NR
                        //    via UPDATE. (This case is rare under normal flow
                        //    but possible if a reviewer cleared a code.)

                        // 1a) INSERT path — documents with no review row yet.
                        ExecTx(cn, tx, @"
INSERT INTO dbo.tblLPPI_Reviews
    (DocumentID, ReasonCodeID, Comments, ObjectiveReference,
     ReviewedByUserId, ReviewedByName, ReviewedDate, IsFinal)
SELECT pd.DocumentID, @rc, @cm, NULL,
       @uid, @uname, @nv, 0
  FROM dbo.tblLPPI_ReviewPackageDocuments pd
 WHERE pd.PackageID = @p
   AND NOT EXISTS (SELECT 1 FROM dbo.tblLPPI_Reviews r
                    WHERE r.DocumentID = pd.DocumentID);",
                            P("@p",     packageId),
                            P("@rc",    noRespId.Value),
                            P("@cm",    autoComment),
                            P("@uid",   userId),
                            P("@uname", userName),
                            P("@nv",    nowIso));

                        // 1b) History row for every newly-INSERTed review.
                        //     The matching predicate is "review row exists for
                        //     a doc in this package, with our ReviewedDate".
                        //     Safer than re-scanning by code because someone
                        //     could have legitimately picked RC-NR if it were
                        //     active — we want only the rows we just wrote.
                        ExecTx(cn, tx, @"
INSERT INTO dbo.tblLPPI_ReviewHistory
    (DocumentID, PackageID, ReasonCodeID, Comments,
     ObjectiveReference, ChangedByUserId, ChangedByName, ChangedDate)
SELECT r.DocumentID, @p, r.ReasonCodeID, r.Comments,
       r.ObjectiveReference, r.ReviewedByUserId, r.ReviewedByName, r.ReviewedDate
  FROM dbo.tblLPPI_Reviews r
 INNER JOIN dbo.tblLPPI_ReviewPackageDocuments pd ON pd.DocumentID = r.DocumentID
 WHERE pd.PackageID    = @p
   AND r.ReviewedDate  = @nv
   AND r.ReasonCodeID  = @rc;",
                            P("@p",     packageId),
                            P("@nv",    nowIso),
                            P("@rc",    noRespId.Value));

                        // 1c) UPDATE path — review row exists but has no code.
                        //     Bring it up to RC-NR. We use a slightly
                        //     different ReviewedDate (1 millisecond later) so
                        //     step 1d can find these specifically without
                        //     colliding with the INSERT path's history.
                        string nowIso2 = DateTime.Now.AddMilliseconds(1)
                            .ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

                        ExecTx(cn, tx, @"
UPDATE r
   SET r.ReasonCodeID     = @rc,
       r.Comments         = @cm,
       r.ReviewedByUserId = @uid,
       r.ReviewedByName   = @uname,
       r.ReviewedDate     = @nv2
  FROM dbo.tblLPPI_Reviews r
 INNER JOIN dbo.tblLPPI_ReviewPackageDocuments pd ON pd.DocumentID = r.DocumentID
 WHERE pd.PackageID = @p
   AND r.ReasonCodeID IS NULL;",
                            P("@p",     packageId),
                            P("@rc",    noRespId.Value),
                            P("@cm",    autoComment),
                            P("@uid",   userId),
                            P("@uname", userName),
                            P("@nv2",   nowIso2));

                        // 1d) History row for the UPDATE path.
                        ExecTx(cn, tx, @"
INSERT INTO dbo.tblLPPI_ReviewHistory
    (DocumentID, PackageID, ReasonCodeID, Comments,
     ObjectiveReference, ChangedByUserId, ChangedByName, ChangedDate)
SELECT r.DocumentID, @p, r.ReasonCodeID, r.Comments,
       r.ObjectiveReference, r.ReviewedByUserId, r.ReviewedByName, r.ReviewedDate
  FROM dbo.tblLPPI_Reviews r
 INNER JOIN dbo.tblLPPI_ReviewPackageDocuments pd ON pd.DocumentID = r.DocumentID
 WHERE pd.PackageID    = @p
   AND r.ReviewedDate  = @nv2
   AND r.ReasonCodeID  = @rc;",
                            P("@p",     packageId),
                            P("@nv2",   nowIso2),
                            P("@rc",    noRespId.Value));

                        // 2) Count how many docs were auto-applied (for the
                        //    response message). Sum of the INSERT and UPDATE
                        //    path reviews carrying our two timestamps.
                        int autoCount = ExecScalarTx(cn, tx, @"
SELECT COUNT(*)
  FROM dbo.tblLPPI_Reviews r
 INNER JOIN dbo.tblLPPI_ReviewPackageDocuments pd ON pd.DocumentID = r.DocumentID
 WHERE pd.PackageID = @p
   AND (r.ReviewedDate = @nv OR r.ReviewedDate = @nv2)
   AND r.ReasonCodeID  = @rc;",
                            P("@p",     packageId),
                            P("@nv",    nowIso),
                            P("@nv2",   nowIso2),
                            P("@rc",    noRespId.Value));

                        // RC-RL stamping (must happen INSIDE the transaction,
                        // BEFORE the status flip).
                        //
                        // For every line of every document in this package
                        // whose first-line review carries the reload-eligible
                        // reason code, set IsDeactivated = 1. The whole
                        // DocNoAccounting is stamped (not just the first
                        // line) so multi-line documents are handled
                        // uniformly; the export filter and the deactivated
                        // watch-list both query at line granularity.
                        //
                        // Rows already deactivated (e.g. from a prior
                        // finalise → unfinalise → re-finalise round-trip on
                        // the same data) are left as-is. Rows already
                        // superseded by a later load are not in this
                        // package, so the join would not pick them up.
                        int? rlId = GetReasonCodeIdByCode(ReloadReasonCode);
                        if (rlId.HasValue)
                        {
                            ExecTx(cn, tx, @"
UPDATE d
   SET d.IsDeactivated = 1
  FROM dbo.tblLPPI_Documents d
 INNER JOIN dbo.tblLPPI_Documents fl
         ON fl.DocNoAccounting = d.DocNoAccounting
 INNER JOIN dbo.tblLPPI_ReviewPackageDocuments pd
         ON pd.DocumentID = (SELECT MIN(d2.DocumentID)
                               FROM dbo.tblLPPI_Documents d2
                              WHERE d2.DocNoAccounting = d.DocNoAccounting)
 INNER JOIN dbo.tblLPPI_Reviews r
         ON r.DocumentID = pd.DocumentID
 WHERE pd.PackageID    = @p
   AND r.ReasonCodeID  = @rl
   AND d.IsDeactivated = 0;",
                                P("@p",  packageId),
                                P("@rl", rlId.Value));
                        }

                        // Race-safe — if another finalise call
                        //    won, this UPDATE affects 0 rows and we surface a
                        //    generic message.
                        int statusUpdated = ExecTx(cn, tx, @"
UPDATE dbo.tblLPPI_ReviewPackages
   SET Status        = 'Finalised',
       FinalisedDate = SYSDATETIME(),
       FinalisedBy   = @by
 WHERE PackageID = @p
   AND Status   IN ('NotSent','Sent','InReview');",
                            P("@p",  packageId),
                            P("@by", userName));

                        if (statusUpdated == 0)
                        {
                            tx.Rollback();
                            result.Success = false;
                            result.ErrorMessage = "Package status changed during finalise — please reload.";
                            return result;
                        }

                        tx.Commit();
                        result.Success = true;
                        result.AutoAppliedCount = autoCount;
                        return result;
                    }
                    catch (Exception ex)
                    {
                        try { tx.Rollback(); } catch { /* swallow */ }
                        result.Success = false;
                        result.ErrorMessage = "Finalise failed: " + ex.Message;
                        return result;
                    }
                }
            }
        }

        /// <summary>
        /// Unfinalise a Finalised package — wipes the auto-applied RC-NR
        /// reviews, clears FinalisedDate / FinalisedBy, flips status back
        /// to InReview. Refused if the package is already Exported.
        ///
        /// Wipe semantics: only RC-NR reviews authored by the finalise
        /// flow itself are removed. Any review with a different reason
        /// code — i.e. a deliberate AS Fin decision recorded before
        /// finalising — is left alone.
        /// </summary>
        public static LifecycleResult UnfinalisePackage(int packageId)
        {
            var result = new LifecycleResult();

            int? noRespId = GetReasonCodeIdByCode(NoResponseReasonCode);
            if (!noRespId.HasValue)
            {
                result.Success = false;
                result.ErrorMessage = "System reason code '" + NoResponseReasonCode +
                    "' is missing — unfinalise cannot proceed.";
                return result;
            }

            object stObj = ExecuteScalar(
                "SELECT Status FROM dbo.tblLPPI_ReviewPackages WHERE PackageID = @p",
                P("@p", packageId));
            if (stObj == null)
            {
                result.Success = false;
                result.ErrorMessage = "Package not found.";
                return result;
            }
            string status = Convert.ToString(stObj);

            if (!string.Equals(status, StatusFinalised, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.ErrorMessage = "Only Finalised packages can be unfinalised (current status: " + status + ").";
                return result;
            }

            string userId   = CurrentUserId();
            string userName = CurrentUserDisplayName();
            string nowIso   = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string clearComment = "Unfinalised on " +
                DateTime.Now.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-AU")) +
                " — auto-applied 'no response' code cleared.";

            using (var cn = new OleDbConnection(ConnectionString))
            {
                cn.Open();
                using (var tx = cn.BeginTransaction())
                {
                    try
                    {
                        // 1) History row for every RC-NR review we are about
                        //    to clear. ReasonCodeID = NULL on the history row
                        //    captures the new state ("undecided again").
                        ExecTx(cn, tx, @"
INSERT INTO dbo.tblLPPI_ReviewHistory
    (DocumentID, PackageID, ReasonCodeID, Comments,
     ObjectiveReference, ChangedByUserId, ChangedByName, ChangedDate)
SELECT r.DocumentID, @p, NULL, @cm,
       NULL, @uid, @uname, @nv
  FROM dbo.tblLPPI_Reviews r
 INNER JOIN dbo.tblLPPI_ReviewPackageDocuments pd ON pd.DocumentID = r.DocumentID
 WHERE pd.PackageID    = @p
   AND r.ReasonCodeID  = @rc;",
                            P("@p",     packageId),
                            P("@cm",    clearComment),
                            P("@uid",   userId),
                            P("@uname", userName),
                            P("@nv",    nowIso),
                            P("@rc",    noRespId.Value));

                        // 2) Wipe the RC-NR review rows themselves. Hard
                        //    delete (option (i) — clean intent). The history
                        //    insert above preserved the audit trail.
                        int cleared = ExecTx(cn, tx, @"
DELETE r
  FROM dbo.tblLPPI_Reviews r
 INNER JOIN dbo.tblLPPI_ReviewPackageDocuments pd ON pd.DocumentID = r.DocumentID
 WHERE pd.PackageID    = @p
   AND r.ReasonCodeID  = @rc;",
                            P("@p",  packageId),
                            P("@rc", noRespId.Value));

                        // 2b) Reverse RC-RL deactivation for any line in this
                        //     package that is not yet superseded. If a
                        //     subsequent file load already replaced the line
                        //     (SupersededByDocumentID IS NOT NULL) the
                        //     supersession chain is committed and we leave
                        //     the row alone — the corrected row in the next
                        //     package is the live one.
                        ExecTx(cn, tx, @"
UPDATE d
   SET d.IsDeactivated = 0
  FROM dbo.tblLPPI_Documents d
 INNER JOIN dbo.tblLPPI_ReviewPackageDocuments pd
         ON pd.DocumentID = (SELECT MIN(d2.DocumentID)
                               FROM dbo.tblLPPI_Documents d2
                              WHERE d2.DocNoAccounting = d.DocNoAccounting)
 WHERE pd.PackageID                 = @p
   AND d.IsDeactivated               = 1
   AND d.SupersededByDocumentID IS NULL;",
                            P("@p", packageId));

                        // 3) Flip status back. Race-safe — if it has moved on
                        //    to Exported (impossible legitimately, but defence
                        //    in depth), this affects 0 rows.
                        int statusUpdated = ExecTx(cn, tx, @"
UPDATE dbo.tblLPPI_ReviewPackages
   SET Status        = 'InReview',
       FinalisedDate = NULL,
       FinalisedBy   = NULL
 WHERE PackageID = @p
   AND Status    = 'Finalised';",
                            P("@p", packageId));

                        if (statusUpdated == 0)
                        {
                            tx.Rollback();
                            result.Success = false;
                            result.ErrorMessage = "Package status changed during unfinalise — please reload.";
                            return result;
                        }

                        tx.Commit();
                        result.Success = true;
                        result.AutoClearedCount = cleared;
                        return result;
                    }
                    catch (Exception ex)
                    {
                        try { tx.Rollback(); } catch { /* swallow */ }
                        result.Success = false;
                        result.ErrorMessage = "Unfinalise failed: " + ex.Message;
                        return result;
                    }
                }
            }
        }

        // -------------------------------------------------------------------
        // Internal — execute SQL inside a caller-owned transaction.
        // Same parameter rewrite as BuildCommand. Used by FinalisePackage /
        // UnfinalisePackage so all their statements share a single tx.
        // -------------------------------------------------------------------
        private static int ExecTx(OleDbConnection cn, OleDbTransaction tx, string sql, params OleDbParameter[] parameters)
        {
            using (var cmd = BuildTxCommand(cn, tx, sql, parameters))
            {
                return cmd.ExecuteNonQuery();
            }
        }

        private static int ExecScalarTx(OleDbConnection cn, OleDbTransaction tx, string sql, params OleDbParameter[] parameters)
        {
            using (var cmd = BuildTxCommand(cn, tx, sql, parameters))
            {
                object o = cmd.ExecuteScalar();
                if (o == null || o == DBNull.Value) return 0;
                return Convert.ToInt32(o);
            }
        }

        private static OleDbCommand BuildTxCommand(OleDbConnection cn, OleDbTransaction tx,
                                                    string sql, OleDbParameter[] parameters)
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

                if (c == '@' && i + 1 < sql.Length && (char.IsLetter(sql[i + 1]) || sql[i + 1] == '_'))
                {
                    int j = i + 1;
                    while (j < sql.Length && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_')) j++;
                    string name = sql.Substring(i, j - i);

                    OleDbParameter src;
                    if (!byName.TryGetValue(name, out src))
                        throw new InvalidOperationException(
                            "LPPI: SQL references parameter " + name + " but no value was supplied.");

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

            var cmd = new OleDbCommand(rewritten.ToString(), cn, tx);
            cmd.CommandType = CommandType.Text;
            foreach (var p in ordered) { cmd.Parameters.Add(p); }
            return cmd;
        }

        // -------------------------------------------------------------------
        // Tiny string helper — DataRow column to non-null string.
        // -------------------------------------------------------------------
        private static string AsStr(object o)
        {
            if (o == null || o == DBNull.Value) return "";
            return Convert.ToString(o);
        }

        // -------------------------------------------------------------------
        // Summary scope — three named scopes driving the Summary page.
        //
        //   Active : packages whose Status IN ActivePackageStatusList
        //            (NotSent / Sent / InReview / Finalised). The default
        //            "current cycle" view.
        //   All    : cumulative — every package that has shipped or is in
        //            flight (the active list plus Exported). Cancelled is
        //            excluded. Surfaces an all-cycles total on the Summary
        //            page; the Dashboard always uses Active.
        //   Batch  : packages that contain at least one (live) document
        //            from a specific load batch. BatchID is required.
        //
        // A scope resolves to a SQL fragment of the form "pd.PackageID IN
        // (<subquery>)" plus its bound parameters, via
        // ResolveScopeFilter() below. All six aggregations call that
        // helper so the scope semantics stay consistent.
        // -------------------------------------------------------------------
        public enum SummaryScopeKind
        {
            Active = 0,
            All    = 1,
            Batch  = 2
        }

        public sealed class SummaryScope
        {
            public SummaryScopeKind Kind;
            public int? BatchID;

            // Optional Capability Manager filter. When set, the package set
            // is further narrowed to packages owned by this CmID. Composes
            // with Kind — Scope picks the universe (active / all / batch),
            // CmID narrows within it. Default null = no CM filter.
            public int? CmID;

            public static SummaryScope CurrentCycle() { return new SummaryScope { Kind = SummaryScopeKind.Active }; }
            public static SummaryScope AllActive()    { return new SummaryScope { Kind = SummaryScopeKind.All }; }
            public static SummaryScope ForBatch(int batchId)
            {
                return new SummaryScope { Kind = SummaryScopeKind.Batch, BatchID = batchId };
            }

            /// <summary>
            /// Fluent CM filter. Returns the same instance so factory calls
            /// can chain: SummaryScope.CurrentCycle().WithCm(7).
            /// Passing null clears the filter.
            /// </summary>
            public SummaryScope WithCm(int? cmId)
            {
                this.CmID = cmId;
                return this;
            }
        }

        /// <summary>
        /// Build the "PackageID IN (...)" filter fragment for a scope, plus
        /// its bound parameters. The fragment is intended to be dropped
        /// into a query as "AND pd.PackageID IN (<fragment>)" — caller
        /// supplies the "pd.PackageID IN" part so the alias and column
        /// name can differ per query.
        ///
        /// Active / All resolve to a status-IN subquery. Batch resolves
        /// to a DISTINCT subquery against tblLPPI_ReviewPackageDocuments
        /// joined to tblLPPI_Documents, filtered to live rows from the
        /// given BatchID.
        ///
        /// Returns the fragment without enclosing parentheses; the caller
        /// wraps it. Parameter names are namespaced ('@SS_...') so they
        /// will not collide with the outer query's parameters.
        /// </summary>
        private static string BuildScopePackageSubquery(SummaryScope scope, List<OleDbParameter> outParams)
        {
            if (scope == null) scope = SummaryScope.CurrentCycle();

            // Optional CM filter — composes with whichever Kind is selected.
            // For Batch scope we need an explicit join to tblLPPI_ReviewPackages
            // to reach CmID; for Active/All scope p_s is already in the FROM
            // and we just AND the predicate.
            bool hasCm = scope.CmID.HasValue;
            if (hasCm)
            {
                outParams.Add(P("@SS_CmID", scope.CmID.Value));
            }

            switch (scope.Kind)
            {
                case SummaryScopeKind.Batch:
                    if (!scope.BatchID.HasValue)
                        throw new InvalidOperationException("Summary scope Batch requires BatchID.");
                    outParams.Add(P("@SS_BatchID", scope.BatchID.Value));
                    return @"SELECT DISTINCT pd_s.PackageID
                              FROM dbo.tblLPPI_ReviewPackageDocuments pd_s
                              INNER JOIN dbo.tblLPPI_Documents d_s
                                      ON d_s.DocumentID = pd_s.DocumentID
                              "
                          + (hasCm
                              ? "INNER JOIN dbo.tblLPPI_ReviewPackages p_s ON p_s.PackageID = pd_s.PackageID "
                              : "")
                          + @"
                             WHERE d_s.BatchID      = @SS_BatchID
                               AND d_s.IsDeactivated = 0"
                          + (hasCm ? " AND p_s.CmID = @SS_CmID" : "");

                case SummaryScopeKind.All:
                    // Cumulative — every package that has shipped or is in
                    // flight (active list plus Exported). Cancelled stays
                    // excluded.
                    return "SELECT p_s.PackageID FROM dbo.tblLPPI_ReviewPackages p_s WHERE p_s.Status IN (" + CumulativePackageStatusList + ")"
                         + (hasCm ? " AND p_s.CmID = @SS_CmID" : "");

                case SummaryScopeKind.Active:
                default:
                    // In-flight only — the default current-cycle universe.
                    return "SELECT p_s.PackageID FROM dbo.tblLPPI_ReviewPackages p_s WHERE p_s.Status IN (" + ActivePackageStatusList + ")"
                         + (hasCm ? " AND p_s.CmID = @SS_CmID" : "");
            }
        }

        // -------------------------------------------------------------------
        // Scope header — single-row figures for the Cycle overview cards on
        // the Summary page, and for the current-cycle headline on the
        // Dashboard (both pages call this so their figures match).
        //
        //   PackageCount        : packages in scope
        //   DocCount            : distinct DocNoAccounting across in-scope
        //                         packages (one per document, not per line)
        //   ReviewedCount       : distinct documents with a coded review on
        //                         their first-line DocumentID
        //   TotalInterest       : sum of InterestPayable across every live
        //                         line of every in-scope document
        //   PayableInterest     : TotalInterest restricted to documents whose
        //                         first-line review outcome is Payable
        //   NotPayableInterest  : ... outcome is NotPayable
        //   AwaitingInterest    : ... documents with no reason code yet
        //
        // Payable + NotPayable + Awaiting sum to Total: every in-scope
        // document falls into exactly one bucket (its first-line outcome, or
        // Awaiting when uncoded). Per-document dollars are summed once across
        // all live lines of the DocNoAccounting, then classified by the
        // first-line review — same pattern as the per-package summary in
        // LPPIEmail.cs but scope-wide.
        // -------------------------------------------------------------------
        public static DataRow GetSummaryScopeHeader(SummaryScope scope)
        {
            var parms = new List<OleDbParameter>();
            string scopeSql = BuildScopePackageSubquery(scope, parms);

            string sql = @"
WITH ScopePkgs AS (
    " + scopeSql + @"
),
PkgDocs AS (
    SELECT DISTINCT
           d.DocNoAccounting,
           (SELECT MIN(d2.DocumentID)
              FROM dbo.tblLPPI_Documents d2
             WHERE d2.DocNoAccounting = d.DocNoAccounting
               AND d2.IsDeactivated   = 0) AS FirstLineDocumentID,
           (SELECT SUM(d3.InterestPayable)
              FROM dbo.tblLPPI_Documents d3
             WHERE d3.DocNoAccounting = d.DocNoAccounting
               AND d3.IsDeactivated   = 0) AS DocInterest
      FROM dbo.tblLPPI_ReviewPackageDocuments pd
      INNER JOIN dbo.tblLPPI_Documents d ON d.DocumentID = pd.DocumentID
     WHERE pd.PackageID IN (SELECT PackageID FROM ScopePkgs)
       AND d.IsDeactivated = 0
)
SELECT
    (SELECT COUNT(*) FROM ScopePkgs)                                                       AS PackageCount,
    (SELECT COUNT(*) FROM PkgDocs)                                                         AS DocCount,
    (SELECT COUNT(*)
       FROM PkgDocs pdr
       INNER JOIN dbo.tblLPPI_Reviews r ON r.DocumentID = pdr.FirstLineDocumentID
      WHERE r.ReasonCodeID IS NOT NULL)                                                    AS ReviewedCount,
    ISNULL(SUM(pdx.DocInterest), 0)                                                        AS TotalInterest,
    ISNULL(SUM(CASE WHEN rc.Outcome = 'Payable'    THEN pdx.DocInterest ELSE 0 END), 0)    AS PayableInterest,
    ISNULL(SUM(CASE WHEN rc.Outcome = 'NotPayable' THEN pdx.DocInterest ELSE 0 END), 0)    AS NotPayableInterest,
    ISNULL(SUM(CASE WHEN rc.ReasonCodeID IS NULL   THEN pdx.DocInterest ELSE 0 END), 0)    AS AwaitingInterest
  FROM PkgDocs pdx
  LEFT JOIN dbo.tblLPPI_Reviews r2      ON r2.DocumentID   = pdx.FirstLineDocumentID
  LEFT JOIN dbo.tblLPPI_ReasonCodes rc  ON rc.ReasonCodeID = r2.ReasonCodeID;";

            DataTable dt = ExecuteTable(sql, parms.ToArray());
            return dt.Rows.Count > 0 ? dt.Rows[0] : null;
        }

        // -------------------------------------------------------------------
        // By reason code — every reason code with at least one in-scope
        // document. Returns one row per coded reason code; the Payable /
        // NotPayable split is done in the caller (LPPI_Summary.aspx.cs)
        // since the two summary sections share the same source.
        //
        // Awaiting (uncoded) documents are NOT included. The Summary page
        // already surfaces overall progress via the Cycle overview's
        // reviewed-vs-total bar; a separate "Awaiting" row on the
        // reason-code table is redundant once the Payable/NotPayable split
        // is in place.
        //
        // Columns returned:
        //   Code                NVARCHAR(20)
        //   Description         NVARCHAR(500)
        //   Outcome             NVARCHAR(20)
        //   DisplayOrder        INT
        //   DocCount            INT
        //   Interest            DECIMAL(19,4)
        //   PctOfTotal          INT            0..100, share of total
        //                                       interest in scope.
        //
        // Inactive codes are included as long as they appear on a review
        // (RC-NR will surface here when auto-applied at finalise).
        // -------------------------------------------------------------------
        public static DataTable GetSummaryByReasonCode(SummaryScope scope)
        {
            var parms = new List<OleDbParameter>();
            string scopeSql = BuildScopePackageSubquery(scope, parms);

            string sql = @"
WITH ScopePkgs AS (
    " + scopeSql + @"
),
PkgDocs AS (
    SELECT DISTINCT
           d.DocNoAccounting,
           (SELECT MIN(d2.DocumentID)
              FROM dbo.tblLPPI_Documents d2
             WHERE d2.DocNoAccounting = d.DocNoAccounting
               AND d2.IsDeactivated   = 0) AS FirstLineDocumentID
      FROM dbo.tblLPPI_ReviewPackageDocuments pd
      INNER JOIN dbo.tblLPPI_Documents d ON d.DocumentID = pd.DocumentID
     WHERE pd.PackageID IN (SELECT PackageID FROM ScopePkgs)
       AND d.IsDeactivated = 0
),
DocFigures AS (
    SELECT pd.DocNoAccounting,
           pd.FirstLineDocumentID,
           (SELECT SUM(d2.InterestPayable)
              FROM dbo.tblLPPI_Documents d2
             WHERE d2.DocNoAccounting = pd.DocNoAccounting
               AND d2.IsDeactivated   = 0) AS DocInterest,
           r.ReasonCodeID
      FROM PkgDocs pd
      LEFT JOIN dbo.tblLPPI_Reviews r ON r.DocumentID = pd.FirstLineDocumentID
),
Totals AS (
    SELECT ISNULL(SUM(DocInterest), 0) AS GrandTotal FROM DocFigures
)
SELECT
    rc.Code                                                         AS Code,
    rc.Description                                                  AS Description,
    rc.Outcome                                                      AS Outcome,
    rc.DisplayOrder                                                 AS DisplayOrder,
    COUNT(*)                                                        AS DocCount,
    ISNULL(SUM(df.DocInterest), 0)                                  AS Interest,
    CASE WHEN (SELECT GrandTotal FROM Totals) > 0
         THEN CAST(ROUND(ISNULL(SUM(df.DocInterest), 0) * 100.0
                         / (SELECT GrandTotal FROM Totals), 0) AS INT)
         ELSE 0
    END                                                             AS PctOfTotal
  FROM DocFigures df
  INNER JOIN dbo.tblLPPI_ReasonCodes rc ON rc.ReasonCodeID = df.ReasonCodeID
 GROUP BY rc.Code, rc.Description, rc.Outcome, rc.DisplayOrder
 ORDER BY rc.DisplayOrder, rc.Code;";

            return ExecuteTable(sql, parms.ToArray());
        }

        // -------------------------------------------------------------------
        // By Capability Manager program — one row per program with at
        // least one in-scope package.
        //
        // Columns:
        //   Program             NVARCHAR(200)
        //   PackageCount        INT  — packages for this program in scope
        //   DocCount            INT
        //   ReviewedCount       INT
        //   PocCount            INT  — distinct first-line POC emails
        //   NoPocCount          INT  — first-line docs with no POC email
        //   FlaggedReloadCount  INT  — first-line docs coded RC-RL (live)
        //   Interest            DECIMAL(19,4)
        // -------------------------------------------------------------------
        public static DataTable GetSummaryByProgram(SummaryScope scope)
        {
            var parms = new List<OleDbParameter>();
            string scopeSql = BuildScopePackageSubquery(scope, parms);

            // PkgDocs carries DocInterest + PocEmailClean as plain columns
            // so the outer aggregates are over columns rather than over
            // correlated subqueries (SQL Server rejects an aggregate
            // applied to a correlated subquery).
            //
            // PocEmailClean is the trimmed first-line PocEmail; blank /
            // null become NULL so COUNT(DISTINCT ...) ignores them, and
            // an explicit no-POC count is exposed separately.
            string sql = @"
WITH ScopePkgs AS (
    " + scopeSql + @"
),
PkgDocs AS (
    SELECT DISTINCT
           cm.Program,
           p.PackageID,
           d.DocNoAccounting,
           (SELECT MIN(d2.DocumentID)
              FROM dbo.tblLPPI_Documents d2
             WHERE d2.DocNoAccounting = d.DocNoAccounting
               AND d2.IsDeactivated   = 0) AS FirstLineDocumentID,
           (SELECT SUM(d3.InterestPayable)
              FROM dbo.tblLPPI_Documents d3
             WHERE d3.DocNoAccounting = d.DocNoAccounting
               AND d3.IsDeactivated   = 0) AS DocInterest,
           NULLIF(LTRIM(RTRIM(
               (SELECT TOP 1 d4.PocEmail
                  FROM dbo.tblLPPI_Documents d4
                 WHERE d4.DocumentID = (SELECT MIN(d5.DocumentID)
                                          FROM dbo.tblLPPI_Documents d5
                                         WHERE d5.DocNoAccounting = d.DocNoAccounting
                                           AND d5.IsDeactivated   = 0)))
               ), '') AS PocEmailClean
      FROM dbo.tblLPPI_ReviewPackageDocuments pd
      INNER JOIN dbo.tblLPPI_Documents d ON d.DocumentID = pd.DocumentID
      INNER JOIN dbo.tblLPPI_ReviewPackages p ON p.PackageID = pd.PackageID
      INNER JOIN dbo.tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
     WHERE pd.PackageID IN (SELECT PackageID FROM ScopePkgs)
       AND d.IsDeactivated = 0
),
DeactByProgram AS (
    SELECT cm.Program,
           COUNT(DISTINCT d.DocNoAccounting) AS DeactivatedCount
      FROM dbo.tblLPPI_ReviewPackageDocuments pd
      INNER JOIN dbo.tblLPPI_Documents d ON d.DocumentID = pd.DocumentID
      INNER JOIN dbo.tblLPPI_ReviewPackages p ON p.PackageID = pd.PackageID
      INNER JOIN dbo.tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
     WHERE pd.PackageID IN (SELECT PackageID FROM ScopePkgs)
       AND d.IsDeactivated = 1
       AND d.SupersededByDocumentID IS NULL
     GROUP BY cm.Program
)
SELECT
    pd.Program,
    COUNT(DISTINCT pd.PackageID)                                  AS PackageCount,
    COUNT(*)                                                      AS DocCount,
    SUM(CASE WHEN r.ReasonCodeID IS NOT NULL THEN 1 ELSE 0 END)   AS ReviewedCount,
    COUNT(DISTINCT pd.PocEmailClean)                              AS PocCount,
    SUM(CASE WHEN pd.PocEmailClean IS NULL THEN 1 ELSE 0 END)     AS NoPocCount,
    SUM(CASE WHEN rc.Code = N'RC-RL' THEN 1 ELSE 0 END)           AS FlaggedReloadCount,
    ISNULL(MAX(dp.DeactivatedCount), 0)                           AS DeactivatedCount,
    ISNULL(SUM(pd.DocInterest), 0)                                AS Interest
  FROM PkgDocs pd
  LEFT JOIN dbo.tblLPPI_Reviews     r  ON r.DocumentID    = pd.FirstLineDocumentID
  LEFT JOIN dbo.tblLPPI_ReasonCodes rc ON rc.ReasonCodeID = r.ReasonCodeID
  LEFT JOIN DeactByProgram          dp ON dp.Program      = pd.Program
 GROUP BY pd.Program
 ORDER BY pd.Program;";

            return ExecuteTable(sql, parms.ToArray());
        }

        // -------------------------------------------------------------------
        // Deactivated documents awaiting reload — system-wide.
        //
        // Documents whose lines were deactivated by an RC-RL finalise and
        // have not yet been replaced by a corrected reload
        // (SupersededByDocumentID IS NULL). Counted as distinct
        // DocNoAccounting. NOT cycle-scoped — this is the live size of the
        // reload backlog across all cycles, the same set the Deactivated
        // watch-list shows.
        // -------------------------------------------------------------------
        public static int GetDeactivatedAwaitingReloadCount()
        {
            object o = ExecuteScalar(@"
SELECT COUNT(DISTINCT d.DocNoAccounting)
  FROM dbo.tblLPPI_Documents d
 WHERE d.IsDeactivated = 1
   AND d.SupersededByDocumentID IS NULL;");
            return (o == null || o == DBNull.Value) ? 0 : Convert.ToInt32(o);
        }

        // -------------------------------------------------------------------
        // By Capability Manager number — the LPPI Charge Cost Centre /
        // tblLPPI_Documents.CapabilityManager value.
        //
        // Some CM numbers cross programs, which is why this is a distinct
        // cut from GetSummaryByProgram. Rows ordered by Interest DESC
        // since the operational use is "where is the biggest concentration
        // of exposure".
        //
        // Columns:
        //   CapabilityManager      NVARCHAR(50)
        //   CapabilityManagerName  NVARCHAR(200)  (best-effort, picked from
        //                                          any first-line row)
        //   DocCount               INT
        //   Interest               DECIMAL(19,4)
        // -------------------------------------------------------------------
        public static DataTable GetSummaryByCm(SummaryScope scope)
        {
            var parms = new List<OleDbParameter>();
            string scopeSql = BuildScopePackageSubquery(scope, parms);

            // PkgDocs carries DocInterest as a plain column so the outer
            // SUM does not nest an aggregate inside a subquery. The inner
            // GROUP BY (d.DocNoAccounting + CM) collapses multi-line docs
            // to one row per (document, CM bucket) before the outer
            // GROUP BY rolls up to one row per CM.
            string sql = @"
WITH ScopePkgs AS (
    " + scopeSql + @"
),
PkgDocs AS (
    SELECT
           d.DocNoAccounting,
           ISNULL(NULLIF(LTRIM(RTRIM(d.CapabilityManager)), ''), N'(blank)') AS CapabilityManager,
           MAX(d.CapabilityManagerName)                                       AS CapabilityManagerName,
           (SELECT SUM(d3.InterestPayable)
              FROM dbo.tblLPPI_Documents d3
             WHERE d3.DocNoAccounting = d.DocNoAccounting
               AND d3.IsDeactivated   = 0)                                    AS DocInterest
      FROM dbo.tblLPPI_ReviewPackageDocuments pd
      INNER JOIN dbo.tblLPPI_Documents d ON d.DocumentID = pd.DocumentID
     WHERE pd.PackageID IN (SELECT PackageID FROM ScopePkgs)
       AND d.IsDeactivated = 0
     GROUP BY d.DocNoAccounting,
              ISNULL(NULLIF(LTRIM(RTRIM(d.CapabilityManager)), ''), N'(blank)')
)
SELECT
    pd.CapabilityManager,
    MAX(pd.CapabilityManagerName) AS CapabilityManagerName,
    COUNT(*)                      AS DocCount,
    ISNULL(SUM(pd.DocInterest), 0) AS Interest
  FROM PkgDocs pd
 GROUP BY pd.CapabilityManager
 ORDER BY Interest DESC, pd.CapabilityManager;";

            return ExecuteTable(sql, parms.ToArray());
        }

        // -------------------------------------------------------------------
        // By POC — TOP 10 outstanding.
        //
        // "Outstanding" = documents in scope whose first-line review has
        // no reason code yet. POC email comes from the document's
        // first-line PocEmail (same source the reviewer page uses for POC
        // scoping).
        //
        // Blank / null POC emails are folded into a single '(no POC)' row
        // so they do not silently disappear; that row is often the
        // largest one and signals upstream data issues.
        //
        // Columns:
        //   PocEmail   NVARCHAR(200)
        //   DocCount   INT
        //   Interest   DECIMAL(19,4)
        // -------------------------------------------------------------------
        public static DataTable GetSummaryByPocOutstanding(SummaryScope scope)
        {
            var parms = new List<OleDbParameter>();
            string scopeSql = BuildScopePackageSubquery(scope, parms);

            string sql = @"
WITH ScopePkgs AS (
    " + scopeSql + @"
),
PkgDocs AS (
    SELECT DISTINCT
           d.DocNoAccounting,
           (SELECT MIN(d2.DocumentID)
              FROM dbo.tblLPPI_Documents d2
             WHERE d2.DocNoAccounting = d.DocNoAccounting
               AND d2.IsDeactivated   = 0) AS FirstLineDocumentID
      FROM dbo.tblLPPI_ReviewPackageDocuments pd
      INNER JOIN dbo.tblLPPI_Documents d ON d.DocumentID = pd.DocumentID
     WHERE pd.PackageID IN (SELECT PackageID FROM ScopePkgs)
       AND d.IsDeactivated = 0
),
Outstanding AS (
    SELECT pd.DocNoAccounting,
           pd.FirstLineDocumentID,
           ISNULL(NULLIF(LTRIM(RTRIM(
               (SELECT TOP 1 d4.PocEmail
                  FROM dbo.tblLPPI_Documents d4
                 WHERE d4.DocumentID = pd.FirstLineDocumentID))), ''), N'(no POC)') AS PocEmail,
           (SELECT SUM(d3.InterestPayable)
              FROM dbo.tblLPPI_Documents d3
             WHERE d3.DocNoAccounting = pd.DocNoAccounting
               AND d3.IsDeactivated   = 0) AS DocInterest
      FROM PkgDocs pd
      LEFT JOIN dbo.tblLPPI_Reviews r ON r.DocumentID = pd.FirstLineDocumentID
     WHERE r.ReasonCodeID IS NULL
)
SELECT TOP (10)
    PocEmail,
    COUNT(*)                  AS DocCount,
    ISNULL(SUM(DocInterest),0) AS Interest
  FROM Outstanding
 GROUP BY PocEmail
 ORDER BY DocCount DESC, Interest DESC, PocEmail;";

            return ExecuteTable(sql, parms.ToArray());
        }

        // -------------------------------------------------------------------
        // By POC — TOP 10 outstanding by VALUE.
        //
        // Same data shape as GetSummaryByPocOutstanding, but ordered by
        // outstanding interest descending instead of doc count. Lets admins
        // identify the highest-dollar pending exposure before defaulting
        // the package (so the big-ticket POCs get chased first).
        //
        // Counts and dollar totals are identical to the by-count helper
        // for a given POC — only the TOP 10 selection and the ordering
        // differ.
        // -------------------------------------------------------------------
        public static DataTable GetSummaryByPocOutstandingByValue(SummaryScope scope)
        {
            var parms = new List<OleDbParameter>();
            string scopeSql = BuildScopePackageSubquery(scope, parms);

            string sql = @"
WITH ScopePkgs AS (
    " + scopeSql + @"
),
PkgDocs AS (
    SELECT DISTINCT
           d.DocNoAccounting,
           (SELECT MIN(d2.DocumentID)
              FROM dbo.tblLPPI_Documents d2
             WHERE d2.DocNoAccounting = d.DocNoAccounting
               AND d2.IsDeactivated   = 0) AS FirstLineDocumentID
      FROM dbo.tblLPPI_ReviewPackageDocuments pd
      INNER JOIN dbo.tblLPPI_Documents d ON d.DocumentID = pd.DocumentID
     WHERE pd.PackageID IN (SELECT PackageID FROM ScopePkgs)
       AND d.IsDeactivated = 0
),
Outstanding AS (
    SELECT pd.DocNoAccounting,
           pd.FirstLineDocumentID,
           ISNULL(NULLIF(LTRIM(RTRIM(
               (SELECT TOP 1 d4.PocEmail
                  FROM dbo.tblLPPI_Documents d4
                 WHERE d4.DocumentID = pd.FirstLineDocumentID))), ''), N'(no POC)') AS PocEmail,
           (SELECT SUM(d3.InterestPayable)
              FROM dbo.tblLPPI_Documents d3
             WHERE d3.DocNoAccounting = pd.DocNoAccounting
               AND d3.IsDeactivated   = 0) AS DocInterest
      FROM PkgDocs pd
      LEFT JOIN dbo.tblLPPI_Reviews r ON r.DocumentID = pd.FirstLineDocumentID
     WHERE r.ReasonCodeID IS NULL
)
SELECT TOP (10)
    PocEmail,
    COUNT(*)                  AS DocCount,
    ISNULL(SUM(DocInterest),0) AS Interest
  FROM Outstanding
 GROUP BY PocEmail
 ORDER BY Interest DESC, DocCount DESC, PocEmail;";

            return ExecuteTable(sql, parms.ToArray());
        }

        // -------------------------------------------------------------------
        // Batch list for the scope dropdown.
        //
        // Only batches that actually map to at least one (live) document
        // in a current package — older batches whose docs have all
        // exported and dropped off are filtered out so the dropdown
        // doesn't grow unbounded. RowsInserted is included so the dropdown
        // label can show file size at a glance.
        //
        // Columns:
        //   BatchID       INT
        //   FileName      NVARCHAR(260)
        //   LoadedDate    DATETIME2(3)
        //   DocCount      INT  — live, in-scope-of-any-package docs from
        //                        this batch
        // -------------------------------------------------------------------
        public static DataTable GetSummaryBatchList()
        {
            const string sql = @"
SELECT lb.BatchID,
       lb.FileName,
       lb.LoadedDate,
       (SELECT COUNT(*)
          FROM dbo.tblLPPI_Documents d
          INNER JOIN dbo.tblLPPI_ReviewPackageDocuments pd ON pd.DocumentID = d.DocumentID
         WHERE d.BatchID       = lb.BatchID
           AND d.IsDeactivated = 0) AS DocCount
  FROM dbo.tblLPPI_LoadBatches lb
 ORDER BY lb.LoadedDate DESC;";

            return ExecuteTable(sql);
        }

        // -------------------------------------------------------------------
        // CM picker list — distinct Capability Manager programs that have
        // at least one in-scope package given the supplied scope.
        //
        // The picker on LPPI_Summary.aspx re-binds on Scope change so the
        // list is always in step with what is selectable. Pass a scope
        // WITHOUT a CmID filter — this helper builds the list of CMs
        // available within that scope (it would be circular to constrain
        // the picker by its own value).
        //
        // Columns:
        //   CmID      INT
        //   Program   NVARCHAR(200)
        // -------------------------------------------------------------------
        public static DataTable GetSummaryCmList(SummaryScope scope)
        {
            // Strip any incoming CmID so the picker shows the unfiltered
            // population of programs in this scope. Callers are expected
            // to pass an un-filtered scope but this is defence in depth.
            var pickerScope = new SummaryScope { Kind = scope.Kind, BatchID = scope.BatchID, CmID = null };

            var parms = new List<OleDbParameter>();
            string scopeSql = BuildScopePackageSubquery(pickerScope, parms);

            string sql = @"
WITH ScopePkgs AS (
    " + scopeSql + @"
)
SELECT DISTINCT cm.CmID, cm.Program
  FROM dbo.tblLPPI_ReviewPackages p
  INNER JOIN dbo.tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
 WHERE p.PackageID IN (SELECT PackageID FROM ScopePkgs)
 ORDER BY cm.Program;";

            return ExecuteTable(sql, parms.ToArray());
        }

        // -------------------------------------------------------------------
        // Resolve a scope to its concrete PackageID list. Used by
        // LPPI_Summary_Export.ashx to seed the export's variable-length
        // IN-clause.
        // -------------------------------------------------------------------
        public static List<int> GetSummaryScopePackageIds(SummaryScope scope)
        {
            var parms = new List<OleDbParameter>();
            string scopeSql = BuildScopePackageSubquery(scope, parms);

            DataTable dt = ExecuteTable(scopeSql, parms.ToArray());
            var ids = new List<int>(dt.Rows.Count);
            foreach (DataRow r in dt.Rows)
            {
                ids.Add(Convert.ToInt32(r[0]));
            }
            return ids;
        }

    }
}
