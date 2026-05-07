using System;
using System.Data;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Load batches admin page. Lists every file load and lets the operator
    /// drill into one to see the lines it brought in.
    ///
    /// May 2026 update: line detail now surfaces ExportBatchID alongside
    /// ExportedDate so admins can trace which payment file shipped each
    /// payable line. NULL means the line has not been exported (either it
    /// has not been finalised, it was reviewed as Not Payable, or it is
    /// still queued in a Finalised package awaiting export).
    /// </summary>
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
            //   DocNoAccounting, ItemSequence, VendorName, PoNumber,
            //   CapabilityManagerProgram, InvoiceDate, PaymentRunDate,
            //   DaysVariance, InterestPayable, ExportedDate, ExportBatchID,
            //   ReasonCode, CompanyCode, FiscalYear
            //
            // ItemSequence model: tblLPPI_Documents now holds one row per
            // LINE. The reviewer codes each DOCUMENT once, with the review
            // stored against the smallest DocumentID for that DocNoAccounting
            // (option-1 first-line-review). The LEFT JOIN below uses a
            // correlated subquery to find that first-line DocumentID for each
            // row, so every line of a reviewed document inherits the document's
            // reason code — otherwise lines 2..N would show blank and be
            // miscounted as outstanding.
            //
            // The batch detail intentionally keeps one row per LINE (admin
            // visibility — the whole file is shown as it arrived), with
            // ORDER BY ItemSequence so multi-line documents group together.
            //
            // FiscalYear is projected so the doc-number deep link in the
            // markup can pass it to SapFiNumberHtml.
            //
            // ExportBatchID is added (May 2026) so admins can trace lines
            // back to the payment file they shipped in. NULL indicates the
            // line has not (yet) been exported.
            const string sql = @"
                SELECT d.DocNoAccounting, d.ItemSequence,
                       d.VendorName, d.PoNumber, d.CapabilityManagerProgram,
                       d.CompanyCode, d.FiscalYear,
                       d.InvoiceDate, d.PaymentRunDate, d.DaysVariance, d.InterestPayable,
                       d.ExportedDate, d.ExportBatchID,
                       rc.Code AS ReasonCode
                FROM tblLPPI_Documents d
                LEFT JOIN tblLPPI_Reviews r
                       ON r.DocumentID = (SELECT MIN(d2.DocumentID)
                                            FROM tblLPPI_Documents d2
                                           WHERE d2.DocNoAccounting = d.DocNoAccounting)
                LEFT JOIN tblLPPI_ReasonCodes rc ON rc.ReasonCodeID = r.ReasonCodeID
                WHERE d.BatchID = @b
                ORDER BY d.CapabilityManagerProgram, d.DocNoAccounting, d.ItemSequence";
            DataTable dt = LPPIHelper.ExecuteTable(sql, LPPIHelper.P("@b", batchId));
            rptDocs.DataSource = dt;
            rptDocs.DataBind();

            // Headline stats shown above the lines grid. Line counts are what
            // matches the table below. Reviewed/Outstanding are derived per-row
            // from the inherited ReasonCode (via first-line review), so a
            // three-line document with a code counts as 3 reviewed lines.
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
