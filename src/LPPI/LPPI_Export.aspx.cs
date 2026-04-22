using System;
using System.Data;
using System.Text;
using System.Web.UI;

namespace CPlatform.LPPI
{
    public partial class LPPI_Export : LPPIBasePage
    {
        private const string XlsxMimeType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                txtFrom.Text = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1).ToString("yyyy-MM-dd");
                txtTo.Text   = DateTime.Today.ToString("yyyy-MM-dd");
                BindBatches();
            }
        }

        private void BindBatches()
        {
            ddlBatch.Items.Clear();
            ddlBatch.Items.Add(new System.Web.UI.WebControls.ListItem("(all batches)", ""));
            DataTable dt = LPPIHelper.ExecuteTable(@"
                SELECT TOP (200) BatchID, FileName, LoadedDate
                FROM tblLPPI_LoadBatches
                ORDER BY LoadedDate DESC");
            foreach (DataRow r in dt.Rows)
            {
                string label = string.Format("#{0} — {1} ({2:dd/MM/yyyy})",
                    r["BatchID"], r["FileName"], Convert.ToDateTime(r["LoadedDate"]));
                ddlBatch.Items.Add(new System.Web.UI.WebControls.ListItem(label, Convert.ToString(r["BatchID"])));
            }
        }

        private bool ReadInputs(out DateTime from, out DateTime to, out int? batchId)
        {
            from = DateTime.MinValue; to = DateTime.MinValue; batchId = null;
            if (!DateTime.TryParse(txtFrom.Text, out from)) { ShowMessage("Invalid From date.", "err"); return false; }
            if (!DateTime.TryParse(txtTo.Text,   out to))   { ShowMessage("Invalid To date.", "err");   return false; }
            if (to < from) { ShowMessage("To must be on or after From.", "err"); return false; }
            int b;
            if (!string.IsNullOrEmpty(ddlBatch.SelectedValue) && int.TryParse(ddlBatch.SelectedValue, out b)) { batchId = b; }
            return true;
        }

        protected void btnPreview_Click(object sender, EventArgs e)
        {
            DateTime from, to; int? batchId;
            if (!ReadInputs(out from, out to, out batchId)) { return; }

            // Preview must match the export filter. Under the per-line export
            // model we report BOTH:
            //   - distinct payable documents  (what the operator thinks in)
            //   - total payable lines         (what the file will actually contain)
            // The reason code lives at document level, so the review is joined
            // to the first-line DocumentID and every line of the same document
            // inherits its outcome — same join shape as LPPIExport.BuildExport.
            var sb = new StringBuilder();
            sb.Append(@"
                SELECT COUNT(DISTINCT d.DocNoAccounting) AS DocCount,
                       COUNT(*)                          AS LineCount
                  FROM tblLPPI_Documents d
                  INNER JOIN tblLPPI_Reviews r
                     ON r.DocumentID = (SELECT MIN(d2.DocumentID)
                                          FROM tblLPPI_Documents d2
                                         WHERE d2.DocNoAccounting = d.DocNoAccounting)
                  INNER JOIN tblLPPI_ReasonCodes rc ON rc.ReasonCodeID = r.ReasonCodeID
                 WHERE r.ReasonCodeID IS NOT NULL
                   AND rc.Outcome = 'Payable'
                   AND d.FirstSeenDate >= @From
                   AND d.FirstSeenDate <  DATEADD(day, 1, @To)");
            if (!chkIncludeExported.Checked) { sb.Append(" AND d.ExportedDate IS NULL"); }
            if (batchId.HasValue)            { sb.Append(" AND d.BatchID = @B"); }

            var parms = new System.Collections.Generic.List<System.Data.OleDb.OleDbParameter>
            {
                LPPIHelper.P("@From", from),
                LPPIHelper.P("@To",   to)
            };
            if (batchId.HasValue) { parms.Add(LPPIHelper.P("@B", batchId.Value)); }

            DataTable dt = LPPIHelper.ExecuteTable(sb.ToString(), parms.ToArray());
            int docCount  = 0;
            int lineCount = 0;
            if (dt.Rows.Count > 0)
            {
                if (dt.Rows[0]["DocCount"]  != DBNull.Value) docCount  = Convert.ToInt32(dt.Rows[0]["DocCount"]);
                if (dt.Rows[0]["LineCount"] != DBNull.Value) lineCount = Convert.ToInt32(dt.Rows[0]["LineCount"]);
            }

            string kind = docCount > 0 ? "ok" : "warn";
            string msg;
            if (docCount == 0)
            {
                msg = "No payable documents match those parameters.";
            }
            else if (lineCount == docCount)
            {
                msg = string.Format("{0} payable document(s) would be exported ({1} row{2} in the file).",
                    docCount, lineCount, lineCount == 1 ? "" : "s");
            }
            else
            {
                msg = string.Format("{0} payable document(s) would be exported across {1} line(s) — the file will contain {1} row(s).",
                    docCount, lineCount);
            }
            ShowMessage(msg, kind);
        }

        protected void btnExport_Click(object sender, EventArgs e)
        {
            DateTime from, to; int? batchId;
            if (!ReadInputs(out from, out to, out batchId)) { return; }

            try
            {
                var result = LPPIExport.BuildExport(from, to, chkIncludeExported.Checked, batchId, chkMark.Checked);
                if (result.RowCount == 0)
                {
                    ShowMessage("No payable documents match those parameters — nothing was exported.", "warn");
                    return;
                }

                Response.Clear();
                Response.ContentType = XlsxMimeType;
                Response.AddHeader("Content-Disposition", "attachment; filename=\"" + result.FileName + "\"");
                Response.AddHeader("Content-Length", result.Bytes.Length.ToString());
                Response.BinaryWrite(result.Bytes);
                Response.Flush();
                Response.SuppressContent = true;
                System.Web.HttpContext.Current.ApplicationInstance.CompleteRequest();
            }
            catch (Exception ex)
            {
                ShowMessage("Export failed: " + ex.Message, "err");
            }
        }

        private void ShowMessage(string msg, string kind)
        {
            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-").Append(kind).Append("\">")
              .Append(LPPIHelper.Enc(msg)).Append("</div>");
            phMessage.Controls.Add(new LiteralControl(sb.ToString()));
        }
    }
}
