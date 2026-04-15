using System;
using System.Data;
using System.Text;
using System.Web.UI;

namespace CPlatform.LPPI
{
    public partial class LPPI_Export : LPPIBasePage
    {
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

            var sb = new StringBuilder();
            sb.Append(@"SELECT COUNT(*) FROM tblLPPI_Documents d
                        INNER JOIN tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
                        WHERE r.ReasonCodeID IS NOT NULL
                          AND d.FirstSeenDate >= @From
                          AND d.FirstSeenDate <  DATEADD(day, 1, @To)");
            if (!chkIncludeExported.Checked) { sb.Append(" AND d.ExportedDate IS NULL"); }
            if (batchId.HasValue) { sb.Append(" AND d.BatchID = @B"); }

            var parms = new System.Collections.Generic.List<System.Data.OleDb.OleDbParameter>
            {
                LPPIHelper.P("@From", from),
                LPPIHelper.P("@To",   to)
            };
            if (batchId.HasValue) { parms.Add(LPPIHelper.P("@B", batchId.Value)); }

            int n = Convert.ToInt32(LPPIHelper.ExecuteScalar(sb.ToString(), parms.ToArray()));
            ShowMessage(n + " document(s) would be exported.", n > 0 ? "ok" : "warn");
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
                    ShowMessage("No documents match those parameters — nothing was exported.", "warn");
                    return;
                }

                Response.Clear();
                Response.ContentType = "text/tab-separated-values";
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
