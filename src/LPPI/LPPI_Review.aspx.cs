using System;
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
            string displayName = pr["DisplayName"] == DBNull.Value ? "" : Convert.ToString(pr["DisplayName"]);
            ProgramName = Convert.ToString(pr["Program"]);
            if (!string.IsNullOrEmpty(displayName)) { ProgramName = ProgramName + " — " + displayName; }
            DueDate = Convert.ToDateTime(pr["DueDate"]);
            SetDueCountdown();

            phReview.Visible = true;
            phError.Visible = false;

            _reasonCodes = LPPIHelper.GetReasonCodes(activeOnly: true);

            if (!IsPostBack) { LoadDocuments(packageId); }
        }

        private void LoadDocuments(int packageId)
        {
            DataTable dt = LPPIHelper.ExecuteTable(@"
                SELECT d.DocumentID, d.DocNoAccounting, d.VendorName, d.PoNumber, d.WbsElement,
                       d.ProfitCentre, d.DeliveryManagerName, d.InvoiceDate, d.PaymentRunDate,
                       d.DaysVariance, d.InterestPayable,
                       r.ReasonCodeID AS SelectedReasonCodeID,
                       r.Comments, r.ObjectiveReference
                FROM tblLPPI_ReviewPackageDocuments pd
                INNER JOIN tblLPPI_Documents d ON d.DocumentID = pd.DocumentID
                LEFT JOIN tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
                WHERE pd.PackageID = @p
                ORDER BY d.VendorName, d.DocNoAccounting",
                LPPIHelper.P("@p", packageId));

            // Add a SearchBlob column for the JS filter
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
                    Convert.ToString(r["ProfitCentre"])
                });
            }

            TotalCount = dt.Rows.Count;
            ReviewedCount = 0;
            foreach (DataRow r in dt.Rows)
            {
                if (r["SelectedReasonCodeID"] != DBNull.Value) { ReviewedCount++; }
            }

            rptCards.DataSource = dt;
            rptCards.DataBind();
            rptTable.DataSource = dt;
            rptTable.DataBind();
            phEmpty.Visible = TotalCount == 0;
        }

        protected string BuildReasonOptions(object selectedId)
        {
            int? sel = null;
            if (selectedId != null && selectedId != DBNull.Value)
            {
                int v;
                if (int.TryParse(Convert.ToString(selectedId), out v)) { sel = v; }
            }

            if (_reasonCodes == null)
            {
                _reasonCodes = LPPIHelper.GetReasonCodes(activeOnly: true);
            }

            var sb = new StringBuilder();
            sb.Append("<optgroup label=\"Interest Payable\">");
            bool inPayable = true;
            foreach (DataRow r in _reasonCodes.Rows)
            {
                string outcome = Convert.ToString(r["Outcome"]);
                bool isPayable = string.Equals(outcome, "Payable", StringComparison.OrdinalIgnoreCase);
                if (!isPayable && inPayable)
                {
                    sb.Append("</optgroup><optgroup label=\"Interest Not Payable\">");
                    inPayable = false;
                }
                int id = Convert.ToInt32(r["ReasonCodeID"]);
                bool requires = Convert.ToBoolean(r["RequiresComments"]);
                string label = Convert.ToString(r["Description"]);

                sb.Append("<option value=\"").Append(id).Append("\"")
                  .Append(" data-outcome=\"").Append(isPayable ? "Payable" : "NotPayable").Append("\"")
                  .Append(" data-requires=\"").Append(requires ? "1" : "0").Append("\"");
                if (sel.HasValue && sel.Value == id) { sb.Append(" selected"); }
                sb.Append(">").Append(LPPIHelper.Enc(label)).Append("</option>");
            }
            sb.Append("</optgroup>");
            return sb.ToString();
        }

        private void SetDueCountdown()
        {
            int days = (int)(DueDate.Date - DateTime.Today).TotalDays;
            if (days < 0)
            {
                DueCountdownText = (-days) + " day(s) overdue";
                DueCssClass = "meta-err";
            }
            else if (days == 0)
            {
                DueCountdownText = "Due today";
                DueCssClass = "meta-warn";
            }
            else if (days <= LPPIHelper.ReminderWindowDays)
            {
                DueCountdownText = days + " day(s) remaining";
                DueCssClass = "meta-warn";
            }
            else
            {
                DueCountdownText = days + " day(s) remaining";
                DueCssClass = "meta-ok";
            }
        }

        private void ShowError()
        {
            phError.Visible = true;
            phReview.Visible = false;
        }
    }
}
