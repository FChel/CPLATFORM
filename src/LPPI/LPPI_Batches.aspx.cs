using System;
using System.Data;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    public partial class LPPI_Batches : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                BindBatches();
                string bArg = Request.QueryString["b"];
                int batchId;
                if (!string.IsNullOrEmpty(bArg) && int.TryParse(bArg, out batchId))
                {
                    ShowBatch(batchId);
                }
            }
        }

        private void BindBatches()
        {
            const string sql = @"
                SELECT TOP (200) BatchID, FileName, LoadedDate, LoadedByName,
                       RowsInFile, RowsInserted, RowsSkipped, RowsFailed
                FROM tblLPPI_LoadBatches
                ORDER BY LoadedDate DESC";
            rptBatches.DataSource = LPPIHelper.ExecuteTable(sql);
            rptBatches.DataBind();
        }

        protected void rptBatches_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName == "View")
            {
                ShowBatch(Convert.ToInt32(e.CommandArgument));
            }
        }

        private void ShowBatch(int batchId)
        {
            pnlDocs.Visible = true;
            litBatchId.Text = batchId.ToString();

            // Columns required by rptDocs in LPPI_Batches.aspx:
            //   DocNoAccounting, VendorName, PoNumber, CapabilityManagerProgram,
            //   InvoiceDate, PaymentRunDate, DaysVariance, InterestPayable,
            //   ExportedDate (counted server-side only), ReasonCode,
            //   CompanyCode, ClearingMonth      <-- new, feed SapFiLinkIcon
            const string sql = @"
                SELECT d.DocNoAccounting, d.VendorName, d.PoNumber, d.CapabilityManagerProgram,
                       d.CompanyCode, d.ClearingMonth,
                       d.InvoiceDate, d.PaymentRunDate, d.DaysVariance, d.InterestPayable,
                       d.ExportedDate,
                       rc.Code AS ReasonCode
                FROM tblLPPI_Documents d
                LEFT JOIN tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
                LEFT JOIN tblLPPI_ReasonCodes rc ON rc.ReasonCodeID = r.ReasonCodeID
                WHERE d.BatchID = @b
                ORDER BY d.CapabilityManagerProgram, d.DocNoAccounting";
            DataTable dt = LPPIHelper.ExecuteTable(sql, LPPIHelper.P("@b", batchId));
            rptDocs.DataSource = dt;
            rptDocs.DataBind();

            int total = dt.Rows.Count;
            int reviewed = 0, exported = 0;
            foreach (DataRow r in dt.Rows)
            {
                if (r["ReasonCode"] != DBNull.Value) { reviewed++; }
                if (r["ExportedDate"] != DBNull.Value) { exported++; }
            }
            litTotal.Text = total.ToString();
            litReviewed.Text = reviewed.ToString();
            litOutstanding.Text = (total - reviewed).ToString();
            litExported.Text = exported.ToString();
        }
    }
}
