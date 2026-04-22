using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace CPlatform.LPPI
{
    public partial class LPPI_Review : LPPIBasePage
    {
        protected string TokenForClient = "";
        protected string ProgramName = "";
        protected DateTime DueDate;
        protected int ReviewedCount;
        protected int TotalCount;
        protected int ProgressPercent
        {
            get { return TotalCount == 0 ? 0 : (int)Math.Round(100.0 * ReviewedCount / TotalCount); }
        }
        protected string DueCountdownText;
        protected string DueCssClass;

        private DataTable _reasonCodes;
        private DataTable _mainTable;   // one row per DocNoAccounting — kept for BuildFacetOptions

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

            if (pkg.Rows.Count != 1) { ShowError(); return; }

            DataRow pr = pkg.Rows[0];
            string status = Convert.ToString(pr["Status"]);
            if (!string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase))
            {
                ShowError();
                return;
            }

            int packageId = Convert.ToInt32(pr["PackageID"]);
            TokenForClient = token;
            string displayName = pr["DisplayName"] == DBNull.Value ? ""
                                                                   : Convert.ToString(pr["DisplayName"]);
            ProgramName = Convert.ToString(pr["Program"]);
            if (!string.IsNullOrEmpty(displayName)) ProgramName = ProgramName + " — " + displayName;
            DueDate = Convert.ToDateTime(pr["DueDate"]);
            SetDueCountdown();

            phReview.Visible = true;
            phError.Visible  = false;

            _reasonCodes = LPPIHelper.GetReasonCodes(activeOnly: true);

            LoadDocuments(packageId);
        }

        // -------------------------------------------------------------------
        // Main query: one row per DocNoAccounting, aggregated from all lines.
        //
        // pd.DocumentID is guaranteed to be the first-line DocumentID because
        // CreatePackage now writes MIN(DocumentID) per DocNoAccounting.
        // The review join is therefore a direct equality — no correlated
        // sub-query needed here.
        //
        // Fields that MAY differ across lines (WBS, GL, PC, TaxCode, DM,
        // POC) use CASE WHEN MIN = MAX THEN MIN END — NULL means "(mixed)".
        // Fields that are guaranteed uniform across lines use MIN().
        //
        // Eval() bindings in LPPI_Review.aspx MUST match these aliases exactly.
        // -------------------------------------------------------------------
        private void LoadDocuments(int packageId)
        {
            // ------------------------------------------------------------------
            // MAIN view query — one row per document
            // Columns bound by rptMain Eval():
            //   FirstLineDocumentID, DocNoAccounting, LineCount, TotalInterest,
            //   CompanyCode, VendorNum, VendorName, PoNumber, VendorInvoiceNo,
            //   ClearingMonth, FiscalYear, InvoiceDate, PaymentRunDate, DaysVariance,
            //   WbsElement, WbsDesc, GlAccount, ProfitCentre, TaxCode,
            //   DeliveryManager, DeliveryManagerName, DeliveryManagerProgram,
            //   PocEmail, SelectedReasonCodeID, Comments, ObjectiveReference,
            //   ReasonCode, SearchBlob
            // ------------------------------------------------------------------
            DataTable main = LPPIHelper.ExecuteTable(@"
                SELECT
                    pd.DocumentID                                                        AS FirstLineDocumentID,
                    d.DocNoAccounting,
                    COUNT(*)                                                             AS LineCount,
                    SUM(d.InterestPayable)                                               AS TotalInterest,

                    -- Uniform fields (same on every line by design)
                    MIN(d.CompanyCode)                                                   AS CompanyCode,
                    MIN(d.VendorNum)                                                     AS VendorNum,
                    MIN(d.VendorName)                                                    AS VendorName,
                    MIN(d.PoNumber)                                                      AS PoNumber,
                    MIN(d.VendorInvoiceNo)                                               AS VendorInvoiceNo,
                    MIN(d.ClearingMonth)                                                 AS ClearingMonth,
                    MIN(d.FiscalYear)                                                    AS FiscalYear,
                    MIN(d.InvoiceDate)                                                   AS InvoiceDate,
                    MIN(d.PaymentRunDate)                                                AS PaymentRunDate,
                    MIN(d.DaysVariance)                                                  AS DaysVariance,

                    -- Mixed-capable fields: NULL when lines disagree
                    CASE WHEN MIN(d.WbsElement)             = MAX(d.WbsElement)             THEN MIN(d.WbsElement)             END AS WbsElement,
                    CASE WHEN MIN(d.WbsDesc)                = MAX(d.WbsDesc)                THEN MIN(d.WbsDesc)                END AS WbsDesc,
                    CASE WHEN MIN(d.GlAccount)              = MAX(d.GlAccount)              THEN MIN(d.GlAccount)              END AS GlAccount,
                    CASE WHEN MIN(d.ProfitCentre)           = MAX(d.ProfitCentre)           THEN MIN(d.ProfitCentre)           END AS ProfitCentre,
                    CASE WHEN MIN(d.TaxCode)                = MAX(d.TaxCode)                THEN MIN(d.TaxCode)                END AS TaxCode,
                    CASE WHEN MIN(d.DeliveryManager)        = MAX(d.DeliveryManager)        THEN MIN(d.DeliveryManager)        END AS DeliveryManager,
                    CASE WHEN MIN(d.DeliveryManagerName)    = MAX(d.DeliveryManagerName)    THEN MIN(d.DeliveryManagerName)    END AS DeliveryManagerName,
                    CASE WHEN MIN(d.DeliveryManagerProgram) = MAX(d.DeliveryManagerProgram) THEN MIN(d.DeliveryManagerProgram) END AS DeliveryManagerProgram,
                    CASE WHEN MIN(d.PocEmail)               = MAX(d.PocEmail)               THEN MIN(d.PocEmail)               END AS PocEmail,

                    -- Review from the first-line row (pd.DocumentID IS the first-line id)
                    r.ReasonCodeID  AS SelectedReasonCodeID,
                    r.Comments,
                    r.ObjectiveReference,
                    rc.Code         AS ReasonCode,
                    rc.Outcome      AS ReasonOutcome,
                    rc.RequiresComments

                FROM tblLPPI_ReviewPackageDocuments pd
                INNER JOIN tblLPPI_Documents d
                        ON d.DocNoAccounting = (SELECT d2.DocNoAccounting
                                                  FROM tblLPPI_Documents d2
                                                 WHERE d2.DocumentID = pd.DocumentID)
                LEFT  JOIN tblLPPI_Reviews r    ON r.DocumentID  = pd.DocumentID
                LEFT  JOIN tblLPPI_ReasonCodes rc ON rc.ReasonCodeID = r.ReasonCodeID
                WHERE pd.PackageID = @p
                GROUP BY pd.DocumentID, d.DocNoAccounting,
                         r.ReasonCodeID, r.Comments, r.ObjectiveReference,
                         rc.Code, rc.Outcome, rc.RequiresComments
                ORDER BY SUM(d.InterestPayable) DESC",
                LPPIHelper.P("@p", packageId));

            // Add SearchBlob computed column
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
                    NullOrString(r["ProfitCentre"]),
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
            // DETAIL view query — one row per line, read-only review columns.
            // Sorted to match Main view order (document interest DESC, then
            // ItemSequence ASC within each document).
            //
            // Columns bound by rptDetail Eval():
            //   DocumentID, DocNoAccounting, ItemSequence, FirstLineDocumentID,
            //   CompanyCode, VendorName, VendorNum, PoNumber, WbsElement, WbsDesc,
            //   GlAccount, ProfitCentre, TaxCode, DeliveryManagerProgram,
            //   DeliveryManagerName, PocEmail, PaymentRunDate, DaysVariance,
            //   InterestPayable, ClearingMonth, ReasonCode, Comments,
            //   ObjectiveReference, SearchBlob
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
                    d.WbsElement,
                    d.WbsDesc,
                    d.GlAccount,
                    d.ProfitCentre,
                    d.TaxCode,
                    d.DeliveryManager,
                    d.DeliveryManagerName,
                    d.DeliveryManagerProgram,
                    d.PocEmail,
                    d.InvoiceDate,
                    d.PaymentRunDate,
                    d.DaysVariance,
                    d.InterestPayable,
                    d.ClearingMonth,
                    r.ReasonCodeID,
                    rc.Code         AS ReasonCode,
                    r.Comments,
                    r.ObjectiveReference
                FROM tblLPPI_ReviewPackageDocuments pd
                INNER JOIN tblLPPI_Documents d
                        ON d.DocNoAccounting = (SELECT d2.DocNoAccounting
                                                  FROM tblLPPI_Documents d2
                                                 WHERE d2.DocumentID = pd.DocumentID)
                LEFT  JOIN tblLPPI_Reviews r    ON r.DocumentID  = pd.DocumentID
                LEFT  JOIN tblLPPI_ReasonCodes rc ON rc.ReasonCodeID = r.ReasonCodeID
                WHERE pd.PackageID = @p
                ORDER BY
                    (SELECT SUM(d3.InterestPayable)
                       FROM tblLPPI_Documents d3
                      WHERE d3.DocNoAccounting = d.DocNoAccounting) DESC,
                    d.DocNoAccounting,
                    d.ItemSequence",
                LPPIHelper.P("@p", packageId));

            // Add SearchBlob for Detail rows (used by filter when Detail view is active)
            if (!detail.Columns.Contains("SearchBlob"))
                detail.Columns.Add("SearchBlob", typeof(string));

            foreach (DataRow r in detail.Rows)
            {
                r["SearchBlob"] = string.Join(" ", new[]
                {
                    Convert.ToString(r["VendorName"]),
                    Convert.ToString(r["DocNoAccounting"]),
                    Convert.ToString(r["PoNumber"]),
                    Convert.ToString(r["WbsElement"]),
                    Convert.ToString(r["WbsDesc"]),
                    Convert.ToString(r["ProfitCentre"]),
                    Convert.ToString(r["DeliveryManagerProgram"]),
                    Convert.ToString(r["DeliveryManagerName"]),
                    Convert.ToString(r["PocEmail"])
                });
            }

            rptDetail.DataSource = detail;
            rptDetail.DataBind();
        }

        // -------------------------------------------------------------------
        // Helpers for .aspx rendering
        // -------------------------------------------------------------------

        /// <summary>
        /// Renders the field value HTML-encoded, or a small "(mixed)" chip if
        /// the value is DBNull (meaning lines within this document disagree).
        /// </summary>
        protected string MixedOrEnc(object val)
        {
            if (val == null || val == DBNull.Value)
                return "<span class=\"chip-mixed\">(mixed)</span>";
            var s = Convert.ToString(val);
            return string.IsNullOrWhiteSpace(s) ? "" : LPPIHelper.Enc(s);
        }

        private static string NullOrString(object val)
        {
            if (val == null || val == DBNull.Value) return "";
            return Convert.ToString(val);
        }

        /// <summary>
        /// Reason-code &lt;option&gt; list with data-outcome and data-requires so
        /// the client-side rules can read them without another round-trip.
        /// </summary>
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
                int    id       = Convert.ToInt32(r["ReasonCodeID"]);
                string code     = Convert.ToString(r["Code"]);
                string desc     = Convert.ToString(r["Description"]);
                string outcome  = Convert.ToString(r["Outcome"]);
                bool   requires = Convert.ToBoolean(r["RequiresComments"]);
                sb.Append("<option value=\"").Append(id).Append("\"")
                  .Append(" data-outcome=\"").Append(LPPIHelper.Enc(outcome)).Append("\"")
                  .Append(" data-requires=\"").Append(requires ? "1" : "0").Append("\"");
                if (sel.HasValue && sel.Value == id) sb.Append(" selected");
                string label = string.IsNullOrEmpty(desc) ? code : desc;
                sb.Append(">").Append(LPPIHelper.Enc(label)).Append("</option>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Build the &lt;option&gt; list for ONE facet filter dropdown.
        /// "dm" now reads DeliveryManagerProgram (not DeliveryManagerName).
        /// "(mixed)" is treated as a distinct bucket so reviewers can find
        /// split-manager documents quickly.
        /// </summary>
        protected string BuildFacetOptions(string kind)
        {
            if (_mainTable == null || _mainTable.Rows.Count == 0) return "";

            string column;
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "dm":  column = "DeliveryManagerProgram"; break;
                case "poc": column = "PocEmail";               break;
                case "wbs": column = "WbsElement";             break;
                case "pc":  column = "ProfitCentre";           break;
                default:    return "";
            }

            var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasMixed = false;
            foreach (DataRow r in _mainTable.Rows)
            {
                var v = r[column];
                if (v == null || v == DBNull.Value) { hasMixed = true; continue; }
                var s = Convert.ToString(v);
                if (!string.IsNullOrWhiteSpace(s)) values.Add(s.Trim());
            }

            var sb = new StringBuilder();
            if (hasMixed)
            {
                sb.Append("<option value=\"(mixed)\">(mixed)</option>");
            }
            foreach (var v in values)
            {
                sb.Append("<option value=\"").Append(LPPIHelper.Enc(v)).Append("\">")
                  .Append(LPPIHelper.Enc(v)).Append("</option>");
            }
            return sb.ToString();
        }

        private void SetDueCountdown()
        {
            TimeSpan diff = DueDate.Date - DateTime.Today;
            int days = (int)diff.TotalDays;
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
