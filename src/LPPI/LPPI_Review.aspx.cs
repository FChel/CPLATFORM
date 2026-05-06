using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;

namespace CPlatform.LPPI
{
    public partial class LPPI_Review : LPPIBasePage
    {
        // This page authenticates via an unguessable token, not Windows identity.
        // Opt out of the admin access gate in LPPIBasePage.OnLoad.
        protected override bool RequiresAdminAccess { get { return false; } }

        protected string TokenForClient = "";
        protected string ProgramName    = "";
        protected DateTime DueDate;
        protected int ReviewedCount;
        protected int TotalCount;
        protected int ProgressPercent
        {
            get { return TotalCount == 0 ? 0 : (int)Math.Round(100.0 * ReviewedCount / TotalCount); }
        }
        protected string DueCountdownText;
        protected string DueCssClass;

        // Read-only mode. Only Complete and Cancelled are read-only.
        // NotSent / Sent / InReview are all fully editable so admins can QA
        // during pre-launch and reviewers can edit normally once the package
        // is sent. Editing a NotSent package does NOT flip its status —
        // status only moves to Sent when the operator hits Send on the
        // Send-outs page.
        protected bool IsReadOnly;
        protected string StatusBannerHtml = "";

        // Exposure (dollar) figures — scoped to this package only. Driven into
        // the head-row "Exposure" cell. The three component values sum to the
        // total so the stacked bar reconciles.
        protected string ExposureTotalText            = "0.00";
        protected string ExposurePayableTextShort     = "0";
        protected string ExposureNotPayableTextShort  = "0";
        protected string ExposureAwaitingTextShort    = "0";
        protected int    ExposurePayablePct;
        protected int    ExposureNotPayablePct;
        protected int    ExposureAwaitingPct;

        private DataTable _reasonCodes;
        private DataTable _mainTable;

        protected void Page_Load(object sender, EventArgs e)
        {
            string token = (Request.QueryString["t"] ?? "").Trim();
            if (token.Length == 0) { ShowError(); return; }

            DataTable pkg = LPPIHelper.ExecuteTable(@"
                SELECT p.PackageID, p.CmID, p.DueDate, p.Status, cm.Program, cm.DisplayName
                FROM tblLPPI_ReviewPackages p
                INNER JOIN tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
                WHERE p.Token = @t",
                LPPIHelper.P("@t", token));

            // The ONLY hard reject is a missing/invalid token. Every other
            // status renders the page; the save handler enforces write rules.
            if (pkg.Rows.Count != 1) { ShowError(); return; }

            DataRow pr     = pkg.Rows[0];
            string  status = Convert.ToString(pr["Status"]);

            // Read-only when Complete or Cancelled. Everything else is editable.
            bool readOnly = string.Equals(status, "Complete",  StringComparison.OrdinalIgnoreCase)
                         || string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);
            IsReadOnly = readOnly;
            StatusBannerHtml = BuildStatusBanner(status);

            int packageId  = Convert.ToInt32(pr["PackageID"]);
            TokenForClient = token;
            string dispName = pr["DisplayName"] == DBNull.Value ? "" : Convert.ToString(pr["DisplayName"]);
            ProgramName    = Convert.ToString(pr["Program"]);
            if (!string.IsNullOrEmpty(dispName)) ProgramName = ProgramName + " \u2014 " + dispName;
            DueDate        = Convert.ToDateTime(pr["DueDate"]);
            SetDueCountdown();

            phReview.Visible = true;
            phError.Visible  = false;

            _reasonCodes = LPPIHelper.GetReasonCodes(activeOnly: true);
            LoadDocuments(packageId);
            LoadExposure(packageId);
        }

        /// <summary>
        /// Builds the status banner shown above the review header. NotSent
        /// gets an informational note (editable but not yet sent). Sent and
        /// InReview render no banner — the page looks normal. Complete and
        /// Cancelled render a read-only banner.
        /// </summary>
        private static string BuildStatusBanner(string status)
        {
            string title;
            string body;
            string kind;
            switch ((status ?? "").ToLowerInvariant())
            {
                case "notsent":
                    kind  = "info";
                    title = "Not yet sent";
                    body  = "This package has not been emailed to recipients yet. You can edit reason codes here for testing or QA — your changes will be visible to the reviewer once the package is issued from Send-outs.";
                    break;
                case "complete":
                    kind  = "ok";
                    title = "Complete";
                    body  = "Every document in this package has been reviewed. The package is closed and read-only.";
                    break;
                case "cancelled":
                    kind  = "warn";
                    title = "Cancelled";
                    body  = "This package has been cancelled. It is read-only and the documents are eligible for repackaging on the next file load.";
                    break;
                case "sent":
                case "inreview":
                    return "";
                default:
                    kind  = "warn";
                    title = "Unknown status";
                    body  = "This package is in an unrecognised state.";
                    break;
            }
            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-").Append(kind).Append("\" style=\"margin:0 0 16px 0;\">")
              .Append("<div><strong>").Append(LPPIHelper.Enc(title)).Append("</strong> &mdash; ")
              .Append(LPPIHelper.Enc(body))
              .Append("</div></div>");
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Exposure totals — scoped to this package
        //
        // Mirrors LPPIHelper.GetExposureSummary's logic but constrained to the
        // package via tblLPPI_ReviewPackageDocuments. Per-document totals are
        // computed from every line of each DocNoAccounting (BODS multi-line),
        // then classified by the first-line review's outcome.
        // -------------------------------------------------------------------
        private void LoadExposure(int packageId)
        {
            const string sql = @"
WITH PkgDocs AS (
    SELECT pd.DocumentID AS FirstLineDocumentID,
           (SELECT MIN(d2.DocumentID)
              FROM tblLPPI_Documents d2
             WHERE d2.DocNoAccounting = (SELECT d3.DocNoAccounting
                                           FROM tblLPPI_Documents d3
                                          WHERE d3.DocumentID = pd.DocumentID)) AS NormalisedFirstLine,
           (SELECT d4.DocNoAccounting
              FROM tblLPPI_Documents d4
             WHERE d4.DocumentID = pd.DocumentID) AS DocNoAccounting
      FROM tblLPPI_ReviewPackageDocuments pd
     WHERE pd.PackageID = @p
),
DocTotals AS (
    SELECT pkd.FirstLineDocumentID,
           SUM(d.InterestPayable) AS DocInterest
      FROM PkgDocs pkd
      INNER JOIN tblLPPI_Documents d
              ON d.DocNoAccounting = pkd.DocNoAccounting
     GROUP BY pkd.FirstLineDocumentID
)
SELECT
    ISNULL(SUM(dt.DocInterest), 0)                                                        AS TotalExposure,
    ISNULL(SUM(CASE WHEN rc.Outcome = 'Payable'    THEN dt.DocInterest ELSE 0 END), 0) AS PayableExposure,
    ISNULL(SUM(CASE WHEN rc.Outcome = 'NotPayable' THEN dt.DocInterest ELSE 0 END), 0) AS NotPayableExposure,
    ISNULL(SUM(CASE WHEN rc.ReasonCodeID IS NULL   THEN dt.DocInterest ELSE 0 END), 0) AS AwaitingExposure
FROM DocTotals dt
LEFT JOIN tblLPPI_Reviews r       ON r.DocumentID    = dt.FirstLineDocumentID
LEFT JOIN tblLPPI_ReasonCodes rc  ON rc.ReasonCodeID = r.ReasonCodeID;";

            DataTable dt = LPPIHelper.ExecuteTable(sql, LPPIHelper.P("@p", packageId));
            if (dt.Rows.Count == 0) return;

            DataRow r = dt.Rows[0];
            decimal total      = AsDecimal(r, "TotalExposure");
            decimal payable    = AsDecimal(r, "PayableExposure");
            decimal notPayable = AsDecimal(r, "NotPayableExposure");
            decimal awaiting   = AsDecimal(r, "AwaitingExposure");

            ExposureTotalText           = total.ToString("N2", CultureInfo.GetCultureInfo("en-AU"));
            ExposurePayableTextShort    = FormatShortMoney(payable);
            ExposureNotPayableTextShort = FormatShortMoney(notPayable);
            ExposureAwaitingTextShort   = FormatShortMoney(awaiting);

            ExposurePayablePct    = SharePct(payable,    total);
            ExposureNotPayablePct = SharePct(notPayable, total);
            ExposureAwaitingPct   = SharePct(awaiting,   total);
        }

        /// <summary>
        /// Compact money string for the legend (e.g. $98k, $1.2M, $345). Total
        /// stays full-precision; only the per-segment legend uses this.
        /// Negative values shouldn't occur for LPPI but handled defensively.
        /// </summary>
        private static string FormatShortMoney(decimal value)
        {
            if (value == 0m) return "0";

            decimal abs = Math.Abs(value);
            string sign = value < 0m ? "-" : "";

            if (abs >= 1000000m)
                return sign + (abs / 1000000m).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (abs >= 1000m)
                return sign + (abs / 1000m).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return sign + abs.ToString("0", CultureInfo.InvariantCulture);
        }

        private static decimal AsDecimal(DataRow row, string column)
        {
            if (row == null || row[column] == DBNull.Value) return 0m;
            return Convert.ToDecimal(row[column]);
        }

        private static int SharePct(decimal part, decimal whole)
        {
            if (whole <= 0m) return 0;
            decimal pct = (part / whole) * 100m;
            int rounded = (int)Math.Round(pct, MidpointRounding.AwayFromZero);
            if (rounded < 0)   rounded = 0;
            if (rounded > 100) rounded = 100;
            return rounded;
        }

        // -------------------------------------------------------------------
        // MAIN VIEW QUERY — one row per DocNoAccounting.
        //
        // Account-assignment fields are sourced exclusively from the
        // ItemSequence = 1 line (d1 lateral join), not MIN/MAX across all
        // lines. This matches the BODS extract convention where line 1 carries
        // the primary account assignment for the document.
        //
        // Capability Manager fields (CapabilityManager number + Name) are
        // projected so the reviewer page can show the LPPI Charge Cost Centre
        // column. Within a single CM_program package (e.g. ARMY) different
        // documents can have different individual CMs (e.g. 50001580 "Dir Gen
        // Land Operations"), so this column varies row by row even though the
        // package is grouped by CM_program.
        //
        // The review MERGE target is pd.DocumentID, which the reconcile
        // step writes as the package-time first-line DocumentID. The save
        // handler resolves this via the package table directly.
        //
        // r.ReviewedDate is projected as ReviewedVersion. The aspx writes it
        // onto each row as a data-version attribute. The save handler's
        // optimistic-lock check compares this against the current value at
        // save time — if they differ, the save is rejected as stale.
        //
        // Eval() bindings in LPPI_Review.aspx must match these aliases exactly.
        // -------------------------------------------------------------------
        private void LoadDocuments(int packageId)
        {
            DataTable main = LPPIHelper.ExecuteTable(@"
                SELECT
                    pd.DocumentID                           AS FirstLineDocumentID,
                    d.DocNoAccounting,
                    COUNT(*)                                AS LineCount,
                    SUM(d.InterestPayable)                  AS TotalInterest,

                    -- Uniform fields (same on every line by design)
                    MIN(d.CompanyCode)                      AS CompanyCode,
                    MIN(d.VendorNum)                        AS VendorNum,
                    MIN(d.VendorName)                       AS VendorName,
                    MIN(d.PoNumber)                         AS PoNumber,
                    MIN(d.VendorInvoiceNo)                  AS VendorInvoiceNo,
                    MIN(d.ClearingMonth)                    AS ClearingMonth,
                    MIN(d.FiscalYear)                       AS FiscalYear,
                    MIN(d.PaymentRunDate)                   AS PaymentRunDate,
                    MIN(d.DaysVariance)                     AS DaysVariance,

                    -- Account assignment from ItemSequence = 1 line only
                    d1.WbsElement,
                    d1.WbsDesc,
                    d1.GlAccount,
                    d1.ProfitCentre,
                    d1.TaxCode,
                    d1.CapabilityManager,
                    d1.CapabilityManagerName,
                    d1.CapabilityManagerProgram,
                    d1.DeliveryManager,
                    d1.DeliveryManagerName,
                    d1.DeliveryManagerProgram,
                    d1.PocEmail,

                    -- Review fields (joined to pd.DocumentID = package first-line id)
                    r.ReasonCodeID                          AS SelectedReasonCodeID,
                    r.Comments,
                    r.ObjectiveReference,
                    r.ReviewedDate                          AS ReviewedVersion,
                    rc.Code                                 AS ReasonCode,
                    rc.Outcome                              AS ReasonOutcome,
                    ISNULL(rc.RequiresComments, 0)          AS RequiresComments

                FROM tblLPPI_ReviewPackageDocuments pd

                INNER JOIN tblLPPI_Documents d
                        ON d.DocNoAccounting = (SELECT d2.DocNoAccounting
                                                  FROM tblLPPI_Documents d2
                                                 WHERE d2.DocumentID = pd.DocumentID)

                INNER JOIN tblLPPI_Documents d1
                        ON d1.DocNoAccounting = d.DocNoAccounting
                       AND d1.ItemSequence    = 1

                LEFT  JOIN tblLPPI_Reviews r
                        ON r.DocumentID = pd.DocumentID
                LEFT  JOIN tblLPPI_ReasonCodes rc
                        ON rc.ReasonCodeID = r.ReasonCodeID

                WHERE pd.PackageID = @p

                GROUP BY
                    pd.DocumentID, d.DocNoAccounting,
                    d1.WbsElement, d1.WbsDesc, d1.GlAccount, d1.ProfitCentre, d1.TaxCode,
                    d1.CapabilityManager, d1.CapabilityManagerName, d1.CapabilityManagerProgram,
                    d1.DeliveryManager, d1.DeliveryManagerName, d1.DeliveryManagerProgram,
                    d1.PocEmail,
                    r.ReasonCodeID, r.Comments, r.ObjectiveReference, r.ReviewedDate,
                    rc.Code, rc.Outcome, rc.RequiresComments

                ORDER BY SUM(d.InterestPayable) DESC",
                LPPIHelper.P("@p", packageId));

            if (!main.Columns.Contains("SearchBlob"))
                main.Columns.Add("SearchBlob", typeof(string));

            foreach (DataRow r in main.Rows)
            {
                r["SearchBlob"] = string.Join(" ", new[]
                {
                    Convert.ToString(r["VendorName"]),
                    Convert.ToString(r["DocNoAccounting"]),
                    Convert.ToString(r["PoNumber"]),
                    NullOrString(r["WbsElement"]),
                    NullOrString(r["WbsDesc"]),
                    NullOrString(r["CapabilityManager"]),
                    NullOrString(r["CapabilityManagerName"]),
                    NullOrString(r["DeliveryManager"]),
                    NullOrString(r["DeliveryManagerProgram"]),
                    NullOrString(r["DeliveryManagerName"]),
                    NullOrString(r["PocEmail"])
                });
            }

            TotalCount    = main.Rows.Count;
            ReviewedCount = 0;
            foreach (DataRow r in main.Rows)
                if (r["SelectedReasonCodeID"] != DBNull.Value) ReviewedCount++;

            _mainTable = main;

            rptMain.DataSource = main;
            rptMain.DataBind();
            phEmpty.Visible = TotalCount == 0;

            // ------------------------------------------------------------------
            // DETAIL VIEW QUERY — one row per line, read-only.
            // FiscalYear projected for the SapFiNumberHtml deep link.
            // CapabilityManager / Name / Program projected so the All Lines tab
            // and the per-row expand panel can show the LPPI Charge Cost Centre.
            // ------------------------------------------------------------------
            DataTable detail = LPPIHelper.ExecuteTable(@"
                SELECT
                    d.DocumentID,
                    d.DocNoAccounting,
                    d.ItemSequence,
                    pd.DocumentID                       AS FirstLineDocumentID,
                    d.CompanyCode,
                    d.VendorName,
                    d.VendorNum,
                    d.PoNumber,
                    d.ClearingMonth,
                    d.FiscalYear,
                    d.WbsElement,
                    d.WbsDesc,
                    d.GlAccount,
                    d.ProfitCentre,
                    d.TaxCode,
                    d.CapabilityManager,
                    d.CapabilityManagerName,
                    d.CapabilityManagerProgram,
                    d.DeliveryManager,
                    d.DeliveryManagerName,
                    d.DeliveryManagerProgram,
                    d.PocEmail,
                    d.PaymentRunDate,
                    d.DaysVariance,
                    d.InterestPayable,
                    r.Comments,
                    r.ObjectiveReference,
                    rc.Code                             AS ReasonCode
                FROM tblLPPI_ReviewPackageDocuments pd
                INNER JOIN tblLPPI_Documents d
                        ON d.DocNoAccounting = (SELECT d2.DocNoAccounting
                                                  FROM tblLPPI_Documents d2
                                                 WHERE d2.DocumentID = pd.DocumentID)
                LEFT  JOIN tblLPPI_Reviews r
                        ON r.DocumentID = pd.DocumentID
                LEFT  JOIN tblLPPI_ReasonCodes rc
                        ON rc.ReasonCodeID = r.ReasonCodeID
                WHERE pd.PackageID = @p
                ORDER BY
                    (SELECT SUM(d3.InterestPayable)
                       FROM tblLPPI_Documents d3
                      WHERE d3.DocNoAccounting = d.DocNoAccounting) DESC,
                    d.DocNoAccounting,
                    d.ItemSequence",
                LPPIHelper.P("@p", packageId));

            if (!detail.Columns.Contains("SearchBlob"))
                detail.Columns.Add("SearchBlob", typeof(string));

            foreach (DataRow r in detail.Rows)
            {
                r["SearchBlob"] = string.Join(" ", new[]
                {
                    Convert.ToString(r["VendorName"]),
                    Convert.ToString(r["DocNoAccounting"]),
                    Convert.ToString(r["PoNumber"]),
                    NullOrString(r["WbsElement"]),
                    NullOrString(r["WbsDesc"]),
                    NullOrString(r["CapabilityManager"]),
                    NullOrString(r["CapabilityManagerName"]),
                    NullOrString(r["DeliveryManager"]),
                    NullOrString(r["DeliveryManagerProgram"]),
                    NullOrString(r["DeliveryManagerName"]),
                    NullOrString(r["PocEmail"])
                });
            }

            rptDetail.DataSource = detail;
            rptDetail.DataBind();
        }

        // -------------------------------------------------------------------
        // Rendering helpers
        // -------------------------------------------------------------------

        private static string NullOrString(object val)
        {
            if (val == null || val == DBNull.Value) return "";
            return Convert.ToString(val);
        }

        protected string BuildReasonOptions(object selectedId)
        {
            int? sel = null;
            if (selectedId != null && selectedId != DBNull.Value)
            {
                int n;
                if (int.TryParse(Convert.ToString(selectedId), out n)) sel = n;
            }
            var sb = new StringBuilder();
            if (_reasonCodes == null) return "";
            foreach (DataRow r in _reasonCodes.Rows)
            {
                int    id      = Convert.ToInt32(r["ReasonCodeID"]);
                string code    = Convert.ToString(r["Code"]);
                string desc    = Convert.ToString(r["Description"]);
                string outcome = Convert.ToString(r["Outcome"]);
                bool   req     = Convert.ToBoolean(r["RequiresComments"]);
                sb.Append("<option value=\"").Append(id).Append("\"")
                  .Append(" data-outcome=\"").Append(LPPIHelper.Enc(outcome)).Append("\"")
                  .Append(" data-requires=\"").Append(req ? "1" : "0").Append("\"");
                if (sel.HasValue && sel.Value == id) sb.Append(" selected");
                string label = string.IsNullOrEmpty(desc) ? code : desc;
                sb.Append(">").Append(LPPIHelper.Enc(label)).Append("</option>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Builds the &lt;option&gt; list for the toolbar facet selects. Source
        /// column varies by facet kind. For the Capability Manager facet, the
        /// option label combines the CM number and name (e.g. "50001289 — DG
        /// Air Cbt Cap-AF") so reviewers can recognise either; the option value
        /// is the bare CM number to match the data-cm attribute on rows.
        /// </summary>
        protected string BuildFacetOptions(string kind)
        {
            if (_mainTable == null || _mainTable.Rows.Count == 0) return "";

            string column;
            string nameColumn = null;
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "dm":  column = "DeliveryManagerProgram"; break;
                case "poc": column = "PocEmail";               break;
                case "wbs": column = "WbsElement";             break;
                case "cm":  column = "CapabilityManager"; nameColumn = "CapabilityManagerName"; break;
                default:    return "";
            }

            // Use a dictionary so we can pair the value (number) with a
            // human-readable name where one exists. SortedDictionary keeps
            // the dropdown alphabetised by the value string.
            var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow r in _mainTable.Rows)
            {
                var v = r[column];
                if (v == null || v == DBNull.Value) continue;
                var s = Convert.ToString(v);
                if (string.IsNullOrWhiteSpace(s)) continue;
                s = s.Trim();

                string display = s;
                if (nameColumn != null)
                {
                    var nv = r[nameColumn];
                    if (nv != null && nv != DBNull.Value)
                    {
                        var ns = Convert.ToString(nv).Trim();
                        if (!string.IsNullOrEmpty(ns)) display = s + " \u2014 " + ns;
                    }
                }

                if (!values.ContainsKey(s)) values[s] = display;
            }

            var sb = new StringBuilder();
            foreach (var kv in values)
            {
                sb.Append("<option value=\"").Append(LPPIHelper.Enc(kv.Key)).Append("\">")
                  .Append(LPPIHelper.Enc(kv.Value)).Append("</option>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Formats a ReviewedDate value as the ISO 8601 string the save
        /// handler expects in the version field. Empty string when the
        /// review row does not yet exist.
        /// </summary>
        protected static string FormatVersion(object reviewedDate)
        {
            if (reviewedDate == null || reviewedDate == DBNull.Value) return "";
            DateTime dt;
            if (reviewedDate is DateTime) dt = (DateTime)reviewedDate;
            else if (!DateTime.TryParse(Convert.ToString(reviewedDate),
                CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return "";
            return dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private void SetDueCountdown()
        {
            TimeSpan diff = DueDate.Date - DateTime.Today;
            int days      = (int)diff.TotalDays;
            if (days < 0)       { DueCountdownText = (-days) + " day(s) overdue"; DueCssClass = "err"; }
            else if (days == 0) { DueCountdownText = "Due today";                 DueCssClass = "warn"; }
            else if (days <= LPPIHelper.ReminderWindowDays)
                                { DueCountdownText = days + " day(s) remaining";  DueCssClass = "warn"; }
            else                { DueCountdownText = days + " day(s) remaining";  DueCssClass = ""; }
        }

        private void ShowError()
        {
            phError.Visible  = true;
            phReview.Visible = false;
        }
    }
}
