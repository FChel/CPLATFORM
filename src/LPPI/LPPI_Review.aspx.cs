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
        private DataTable _docTable;    // kept around so BuildFacetOptions can run after DataBind

        protected void Page_Load(object sender, EventArgs e)
        {
            string token = (Request.QueryString["t"] ?? "").Trim();
            if (token.Length == 0) { ShowError(); return; }

            // Look up package strictly by token
            DataTable pkg = LPPIHelper.ExecuteTable(@"
                SELECT p.PackageID, p.CmID, p.DueDate, p.Status, cm.Program, cm.DisplayName
                FROM tblLPPI_ReviewPackages p
                INNER JOIN tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
                WHERE p.Token = @t",
                LPPIHelper.P("@t", token));

            if (pkg.Rows.Count != 1)
            {
                ShowError();
                return;
            }

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
            if (!string.IsNullOrEmpty(displayName)) { ProgramName = ProgramName + " — " + displayName; }
            DueDate = Convert.ToDateTime(pr["DueDate"]);
            SetDueCountdown();

            phReview.Visible = true;
            phError.Visible = false;

            _reasonCodes = LPPIHelper.GetReasonCodes(activeOnly: true);

            LoadDocuments(packageId);
        }

        private void LoadDocuments(int packageId)
        {
            // Every column named below is bound by Eval() in LPPI_Review.aspx —
            // do NOT remove any of these aliases without also updating the markup.
            //   Card view:
            //     DocumentID, VendorName, DocNoAccounting, PoNumber,
            //     InterestPayable, InvoiceDate, PaymentRunDate, DaysVariance,
            //     WbsElement, ProfitCentre, DeliveryManagerName, PocEmail,
            //     SelectedReasonCodeID, Comments, ObjectiveReference,
            //     CompanyCode, ClearingMonth         (for SAP FI deep link)
            //     SearchBlob                          (added below)
            //   Table view adds:
            //     WbsDesc                             (tooltip on WBS column)
            DataTable dt = LPPIHelper.ExecuteTable(@"
                SELECT d.DocumentID, d.DocNoAccounting, d.VendorName, d.PoNumber,
                       d.WbsElement, d.WbsDesc, d.ProfitCentre,
                       d.DeliveryManagerName, d.PocEmail,
                       d.CompanyCode, d.ClearingMonth,
                       d.InvoiceDate, d.PaymentRunDate,
                       d.DaysVariance, d.InterestPayable,
                       r.ReasonCodeID AS SelectedReasonCodeID,
                       r.Comments, r.ObjectiveReference
                FROM tblLPPI_ReviewPackageDocuments pd
                INNER JOIN tblLPPI_Documents d ON d.DocumentID = pd.DocumentID
                LEFT JOIN tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
                WHERE pd.PackageID = @p
                ORDER BY d.VendorName, d.DocNoAccounting",
                LPPIHelper.P("@p", packageId));

            // Build the SearchBlob — includes every field the user might type
            // to find a row (including DM, POC, WBS, Profit Centre).
            if (!dt.Columns.Contains("SearchBlob"))
            {
                dt.Columns.Add("SearchBlob", typeof(string));
            }
            foreach (DataRow r in dt.Rows)
            {
                r["SearchBlob"] = string.Join(" ", new[]
                {
                    Convert.ToString(r["VendorName"]),
                    Convert.ToString(r["DocNoAccounting"]),
                    Convert.ToString(r["PoNumber"]),
                    Convert.ToString(r["WbsElement"]),
                    Convert.ToString(r["WbsDesc"]),
                    Convert.ToString(r["ProfitCentre"]),
                    Convert.ToString(r["DeliveryManagerName"]),
                    Convert.ToString(r["PocEmail"])
                });
            }

            TotalCount = dt.Rows.Count;
            ReviewedCount = 0;
            foreach (DataRow r in dt.Rows)
            {
                if (r["SelectedReasonCodeID"] != DBNull.Value) { ReviewedCount++; }
            }

            _docTable = dt;

            rptCards.DataSource = dt;
            rptCards.DataBind();
            rptTable.DataSource = dt;
            rptTable.DataBind();
            phEmpty.Visible = TotalCount == 0;
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
                if (int.TryParse(Convert.ToString(selectedId), out n)) { sel = n; }
            }
            var sb = new StringBuilder();
            if (_reasonCodes == null) return "";
            foreach (DataRow r in _reasonCodes.Rows)
            {
                int id = Convert.ToInt32(r["ReasonCodeID"]);
                string code = Convert.ToString(r["Code"]);
                string desc = Convert.ToString(r["Description"]);
                string outcome = Convert.ToString(r["Outcome"]);
                bool requires = Convert.ToBoolean(r["RequiresComments"]);
                sb.Append("<option value=\"").Append(id).Append("\"")
                  .Append(" data-outcome=\"").Append(LPPIHelper.Enc(outcome)).Append("\"")
                  .Append(" data-requires=\"").Append(requires ? "1" : "0").Append("\"");
                if (sel.HasValue && sel.Value == id) { sb.Append(" selected"); }
                // Display just the description (reviewers do not need to see
                // the internal RC01/RC02 codes — those live on tblLPPI_ReasonCodes
                // for admin/export purposes). Fall back to the code only when a
                // description is missing.
                string label = string.IsNullOrEmpty(desc) ? code : desc;
                sb.Append(">").Append(LPPIHelper.Enc(label)).Append("</option>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Build the &lt;option&gt; list for ONE facet filter dropdown (DM, POC,
        /// WBS or Profit Centre). Values are the raw string from the column
        /// (no prefix) — each dropdown is dedicated so the JS knows which
        /// data-* attribute to match against via the dropdown's element id.
        ///
        /// Usage in .aspx:
        ///   &lt;%= BuildFacetOptions("dm")  %&gt;
        ///   &lt;%= BuildFacetOptions("poc") %&gt;
        ///   &lt;%= BuildFacetOptions("wbs") %&gt;
        ///   &lt;%= BuildFacetOptions("pc")  %&gt;
        /// </summary>
        protected string BuildFacetOptions(string kind)
        {
            if (_docTable == null || _docTable.Rows.Count == 0) return "";

            string column;
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "dm":  column = "DeliveryManagerName"; break;
                case "poc": column = "PocEmail";            break;
                case "wbs": column = "WbsElement";          break;
                case "pc":  column = "ProfitCentre";        break;
                default:    return "";
            }

            var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow r in _docTable.Rows)
            {
                var v = r[column];
                if (v == null || v == DBNull.Value) continue;
                var s = Convert.ToString(v);
                if (!string.IsNullOrWhiteSpace(s)) values.Add(s.Trim());
            }

            var sb = new StringBuilder();
            foreach (var v in values)
            {
                sb.Append("<option value=\"")
                  .Append(LPPIHelper.Enc(v))
                  .Append("\">")
                  .Append(LPPIHelper.Enc(v))
                  .Append("</option>");
            }
            return sb.ToString();
        }

        private void SetDueCountdown()
        {
            TimeSpan diff = DueDate.Date - DateTime.Today;
            int days = (int)diff.TotalDays;
            if (days < 0)      { DueCountdownText = (-days) + " day(s) overdue"; DueCssClass = "err"; }
            else if (days == 0){ DueCountdownText = "Due today";                 DueCssClass = "warn"; }
            else if (days <= LPPIHelper.ReminderWindowDays)
                               { DueCountdownText = days + " day(s) remaining";  DueCssClass = "warn"; }
            else               { DueCountdownText = days + " day(s) remaining";  DueCssClass = ""; }
        }

        private void ShowError()
        {
            phError.Visible = true;
            phReview.Visible = false;
        }
    }
}
