using System;
using System.Collections.Generic;
using System.Data;
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

            if (pkg.Rows.Count != 1) { ShowError(); return; }

            DataRow pr     = pkg.Rows[0];
            string  status = Convert.ToString(pr["Status"]);
            if (!string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase))
            {
                ShowError();
                return;
            }

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
        }

        // -------------------------------------------------------------------
        // MAIN VIEW QUERY — one row per DocNoAccounting.
        //
        // Account-assignment fields are sourced exclusively from the
        // ItemSequence = 1 line (d1 lateral join), not MIN/MAX across all
        // lines. This matches the BODS extract convention where line 1 carries
        // the primary account assignment for the document.
        //
        // The review MERGE target is pd.DocumentID, which CreatePackage
        // writes as the package-time first-line DocumentID. The save handler
        // resolves this via the package table directly (no cross-batch risk).
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
                    d1.DeliveryManager,
                    d1.DeliveryManagerName,
                    d1.DeliveryManagerProgram,
                    d1.PocEmail,

                    -- Review fields (joined to pd.DocumentID = package first-line id)
                    r.ReasonCodeID                          AS SelectedReasonCodeID,
                    r.Comments,
                    r.ObjectiveReference,
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
                    d1.DeliveryManager, d1.DeliveryManagerName, d1.DeliveryManagerProgram,
                    d1.PocEmail,
                    r.ReasonCodeID, r.Comments, r.ObjectiveReference,
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
            // DETAIL VIEW QUERY — one row per line, read-only.
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
                    d.WbsElement,
                    d.WbsDesc,
                    d.GlAccount,
                    d.ProfitCentre,
                    d.TaxCode,
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
                    NullOrString(r["ProfitCentre"]),
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
            foreach (DataRow r in _mainTable.Rows)
            {
                var v = r[column];
                if (v == null || v == DBNull.Value) continue;
                var s = Convert.ToString(v);
                if (!string.IsNullOrWhiteSpace(s)) values.Add(s.Trim());
            }

            var sb = new StringBuilder();
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
