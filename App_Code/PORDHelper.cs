using System;
using System.Configuration;
using System.Globalization;
using System.Text;
using System.Web;
using CPlatform.LPPI;

namespace CPlatform.PORD
{
    /// <summary>
    /// Shared helpers for the PO Review module.
    ///
    /// Deliberately thin: identity, SAP deep links and admin access delegate
    /// to LPPIHelper so both modules behave identically (same admin list,
    /// same Fiori host, same user display name). When PORD gets its own
    /// schema the data access moves behind IPordStore (see PORDStore.cs);
    /// pages never talk to the store implementation directly.
    ///
    /// Settings (all optional — web.config is server-owned on WARATAH, so
    /// every key has a safe default):
    ///   PORD.DemoMode           true  — in-memory demo data, no SQL needed
    ///   PORD.DefaultDueDays     14
    ///   PORD.ReminderWindowDays 3
    ///   PORD.ExclusionMonths    12
    ///   PORD.EscalateAtReview   2     — Review Nbr at which a PO escalates
    ///   PORD.TermKeyDays        "Z005:5;Z020:20;..."
    ///   PORD.SupportMailboxTo   falls back to LPPI.SupportMailboxTo
    /// </summary>
    public static class PORDHelper
    {
        public const string ModuleName  = "PO Review";
        public const string CheckName   = "Non-Standard Payment Terms";
        public const string PolicyUrl   =
            "https://www.finance.gov.au/publications/resource-management-guides/supplier-pay-time-or-pay-interest-policy-rmg-417";

        private static readonly CultureInfo AU = CultureInfo.GetCultureInfo("en-AU");

        public static string Setting(string key, string fallback)
        {
            var v = ConfigurationManager.AppSettings[key];
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        public static int SettingInt(string key, int fallback)
        {
            int n;
            return int.TryParse(Setting(key, ""), out n) ? n : fallback;
        }

        public static bool DemoMode
        {
            get { return !string.Equals(Setting("PORD.DemoMode", "true"), "false", StringComparison.OrdinalIgnoreCase); }
        }

        public static int DefaultDueDays     { get { return SettingInt("PORD.DefaultDueDays", 14); } }
        public static int ReminderWindowDays { get { return SettingInt("PORD.ReminderWindowDays", 3); } }
        public static int ExclusionMonths    { get { return SettingInt("PORD.ExclusionMonths", 12); } }
        public static int EscalateAtReview   { get { return SettingInt("PORD.EscalateAtReview", 2); } }

        public static string Environment     { get { return LPPIHelper.Environment; } }
        public static string CurrentUser     { get { return LPPIHelper.CurrentUserDisplayName(); } }

        public static string SupportMailbox
        {
            get { return Setting("PORD.SupportMailboxTo", LPPIHelper.Setting("LPPI.SupportMailboxTo", "")); }
        }

        /// <summary>
        /// Admin gate. PORD shares the LPPI admin allow-list for now — the
        /// same FSPI team administers both. Split into tblPORD_AdminUsers
        /// when the module gets its own schema.
        /// </summary>
        public static bool HasAccess()
        {
            return LPPIHelper.HasLppiAccess();
        }

        public static IPordStore Store
        {
            get { return PordDemoStore.Instance; }
        }

        // ------------------------------------------------------------------
        // Formatting
        // ------------------------------------------------------------------

        public static string Enc(object o)
        {
            if (o == null || o == DBNull.Value) return "";
            return HttpUtility.HtmlEncode(Convert.ToString(o));
        }

        public static string Attr(object o)
        {
            if (o == null || o == DBNull.Value) return "";
            return HttpUtility.HtmlAttributeEncode(Convert.ToString(o));
        }

        public static string Date(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "";
        }

        public static string DateTimeShort(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "";
        }

        public static string IsoDate(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        }

        public static string Money(decimal v)
        {
            return v.ToString("N2", AU);
        }

        /// <summary>Compact money for tiles: $1.2m, $845k, $900.</summary>
        public static string MoneyShort(decimal v)
        {
            decimal a = Math.Abs(v);
            if (a >= 1000000m) return (v / 1000000m).ToString("0.0", AU) + "m";
            if (a >= 1000m)    return (v / 1000m).ToString("0", AU) + "k";
            return v.ToString("0", AU);
        }

        public static string Num(int v)
        {
            return v.ToString("N0", AU);
        }

        public static int Pct(int part, int whole)
        {
            if (whole <= 0) return 0;
            int p = (int)Math.Round(part * 100m / whole, MidpointRounding.AwayFromZero);
            return Math.Max(0, Math.Min(100, p));
        }

        public static int Pct(decimal part, decimal whole)
        {
            if (whole <= 0m) return 0;
            int p = (int)Math.Round(part * 100m / whole, MidpointRounding.AwayFromZero);
            return Math.Max(0, Math.Min(100, p));
        }

        public static string PoLinkHtml(string poNumber)
        {
            return LPPIHelper.SapPoNumberHtml(poNumber);
        }

        public static string ReviewUrl(string token)
        {
            return "PORD_Review.aspx?t=" + Uri.EscapeDataString(token ?? "");
        }

        public static string StatusPill(string status, DateTime due)
        {
            string label, cls;
            switch (status ?? "")
            {
                case PordStatus.NotSent:   label = "Not sent";  cls = "notsent";   break;
                case PordStatus.Sent:      label = "Sent";      cls = "sent";      break;
                case PordStatus.InReview:  label = "In review"; cls = "inreview";  break;
                case PordStatus.Finalised: label = "Finalised"; cls = "finalised"; break;
                case PordStatus.Closed:    label = "Closed";    cls = "exported";  break;
                case PordStatus.Cancelled: label = "Cancelled"; cls = "cancelled"; break;
                default:                   label = status;      cls = "";          break;
            }
            var sb = new StringBuilder();
            sb.Append("<span class=\"pill ").Append(cls).Append("\">").Append(Enc(label)).Append("</span>");

            bool active = status == PordStatus.Sent || status == PordStatus.InReview;
            if (active && due.Date < DateTime.Today)
                sb.Append(" <span class=\"pill overdue\">Overdue</span>");
            else if (active && due.Date <= DateTime.Today.AddDays(ReminderWindowDays))
                sb.Append(" <span class=\"pill duesoon\">Due soon</span>");
            return sb.ToString();
        }

        public static string CategoryPill(PordCategory c)
        {
            return "<span class=\"pord-cat " + PORDRules.CategoryCss(c) + "\" title=\""
                 + Attr(PORDRules.CategoryDescription(c)) + "\">"
                 + Enc(PORDRules.CategoryLabel(c)) + "</span>";
        }

        public static string ReviewNbrChip(int n)
        {
            if (n <= 1) return "<span class=\"pord-rn\" title=\"First time this PO has been flagged\">1</span>";
            string cls = n >= EscalateAtReview ? "pord-rn hot" : "pord-rn";
            return "<span class=\"" + cls + "\" title=\"Flagged in " + n
                 + " review rounds — still not corrected\">" + n + "</span>";
        }

        // ------------------------------------------------------------------
        // Minimal JSON writing (the handlers return small, flat payloads)
        // ------------------------------------------------------------------

        public static string Js(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    case '<':  sb.Append("\\u003c"); break;
                    case '>':  sb.Append("\\u003e"); break;
                    case '&':  sb.Append("\\u0026"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string VersionOf(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) : "";
        }
    }
}
