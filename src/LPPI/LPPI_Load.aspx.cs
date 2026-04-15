using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI;

namespace CPlatform.LPPI
{
    public partial class LPPI_Load : LPPIBasePage
    {
        // Session keys for the uploaded file between Preview click and Commit click.
        // We park the parsed ParseResult plus a few bits of file metadata so Commit
        // does not have to re-upload or re-parse. Keyed by a per-upload token so two
        // operators uploading concurrently do not collide.
        private const string SK_Token     = "LPPI_Load_Token";
        private const string SK_Parsed    = "LPPI_Load_Parsed";
        private const string SK_FileName  = "LPPI_Load_FileName";
        private const string SK_FileBytes = "LPPI_Load_FileBytes";

        protected void Page_Load(object sender, EventArgs e)
        {
            // Nothing to pre-bind — the page starts empty and reacts to button clicks.
        }

        // -------------------------------------------------------------------
        // Step 1: Upload & preview
        // -------------------------------------------------------------------
        protected void btnPreview_Click(object sender, EventArgs e)
        {
            if (!fuBods.HasFile)
            {
                ShowMessage("Please pick a file to upload.", "err");
                return;
            }

            var posted = fuBods.PostedFile;
            string fileName = Path.GetFileName(posted.FileName ?? "");
            if (string.IsNullOrEmpty(fileName))
            {
                ShowMessage("Could not determine the file name.", "err");
                return;
            }

            // Light-touch name check — warn, do not block. BODS produces files named
            // LATEPMT_INTEREST_REVIEW_*.xls but we do not want to reject a legitimate
            // file that has been renamed. Header validation is the real gate.
            if (!fileName.StartsWith("LATEPMT_INTEREST_REVIEW_", StringComparison.OrdinalIgnoreCase)
                || !fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            {
                ShowMessage("Warning: expected a file named <code>LATEPMT_INTEREST_REVIEW_*.xls</code>. Continuing anyway — header validation below is the authoritative check.", "warn");
            }

            // Slurp the posted stream into a byte[] so we can park it in Session
            // and re-parse on Commit without relying on the (now-closed) upload stream.
            byte[] bytes;
            try
            {
                using (var ms = new MemoryStream())
                {
                    posted.InputStream.CopyTo(ms);
                    bytes = ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                ShowMessage("Could not read the uploaded file: " + HttpUtility.HtmlEncode(ex.Message), "err");
                return;
            }

            LPPIFileParser.ParseResult parsed;
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    parsed = LPPIFileParser.Parse(ms);
                }
            }
            catch (Exception ex)
            {
                ShowMessage("Could not parse file: " + HttpUtility.HtmlEncode(ex.Message), "err");
                return;
            }

            // Park everything in Session keyed by a token so concurrent operators
            // do not step on each other.
            string token = Guid.NewGuid().ToString("N");
            Session[SK_Token]     = token;
            Session[SK_Parsed]    = parsed;
            Session[SK_FileName]  = fileName;
            Session[SK_FileBytes] = (long)bytes.Length;

            litPreviewName.Text = HttpUtility.HtmlEncode(fileName);

            if (!parsed.HeaderValid)
            {
                litHeaderStatus.Text = "<span class=\"pill pill-overdue\">FAILED</span>";
                var errs = string.Join("<br/>", parsed.HeaderErrors.Select(HttpUtility.HtmlEncode));
                litPreviewTable.Text = "<div class=\"alert alert-err\" style=\"margin-top:12px;\">" + errs + "</div>";
                btnCommit.Enabled = false;
            }
            else
            {
                litHeaderStatus.Text = "<span class=\"pill pill-reviewed\">OK</span>";
                litPreviewTable.Text = BuildPreviewTable(parsed);
                btnCommit.Enabled = true;
            }
            pnlPreview.Visible = true;
            pnlResult.Visible = false;
        }

        private string BuildPreviewTable(LPPIFileParser.ParseResult p)
        {
            var sb = new StringBuilder();
            sb.Append("<p class=\"muted\" style=\"font-size:13px;\">")
              .Append(p.Rows.Count).Append(" data rows in file.</p>");
            sb.Append("<table class=\"tbl\"><thead><tr>");
            var cols = new[] { "DOC_NO_ACCOUNTING", "VENDOR_NAME", "PO_NUMBER", "INVOICE_DATE",
                               "PAYMENT_RUN_DATE", "DAYS_VARIANCE", "INTEREST_PAYABLE",
                               "CAPABILITY_MANAGER_PROGRAM" };
            foreach (var c in cols) sb.Append("<th>").Append(c).Append("</th>");
            sb.Append("</tr></thead><tbody>");
            int n = 0;
            foreach (var row in p.Rows)
            {
                if (n++ >= 20) break;
                sb.Append("<tr>");
                foreach (var c in cols)
                {
                    string v;
                    row.Fields.TryGetValue(c, out v);
                    sb.Append("<td>").Append(HttpUtility.HtmlEncode(LPPIHelper.CleanString(v) ?? "")).Append("</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Step 2: Commit
        // -------------------------------------------------------------------
        protected void btnCommit_Click(object sender, EventArgs e)
        {
            var parsed   = Session[SK_Parsed] as LPPIFileParser.ParseResult;
            var fileName = Session[SK_FileName] as string;
            var sizeObj  = Session[SK_FileBytes];

            if (parsed == null || string.IsNullOrEmpty(fileName))
            {
                ShowMessage("Upload expired. Please upload the file again.", "err");
                pnlPreview.Visible = false;
                return;
            }
            if (!parsed.HeaderValid)
            {
                ShowMessage("Header validation failed; cannot commit.", "err");
                return;
            }

            long fileSize = sizeObj == null ? 0L : Convert.ToInt64(sizeObj);
            // SourcePath field in tblLPPI_LoadBatches now records the uploader
            // rather than a share path, since the file came from their machine.
            string provenance = "upload by " + LPPIHelper.CurrentUserDisplayName();

            var res = LPPIFileParser.Commit(parsed, fileName, provenance, fileSize, DateTime.Now);

            litResIns.Text   = res.RowsInserted.ToString();
            litResSkip.Text  = res.RowsSkipped.ToString();
            litResFail.Text  = res.RowsFailed.ToString();
            litResTotal.Text = res.RowsInFile.ToString();
            litResSkipList.Text = res.SkippedDocNumbers.Count == 0
                ? "(none)"
                : HttpUtility.HtmlEncode(string.Join("\r\n", res.SkippedDocNumbers));
            litResFailList.Text = res.FailedRows.Count == 0
                ? "(none)"
                : HttpUtility.HtmlEncode(string.Join("\r\n", res.FailedRows));

            // Clear the parked upload — the commit is one-shot.
            ClearSession();

            pnlPreview.Visible = false;
            pnlResult.Visible = true;
        }

        protected void btnCancel_Click(object sender, EventArgs e)
        {
            ClearSession();
            pnlPreview.Visible = false;
        }

        private void ClearSession()
        {
            Session.Remove(SK_Token);
            Session.Remove(SK_Parsed);
            Session.Remove(SK_FileName);
            Session.Remove(SK_FileBytes);
        }

        private void ShowMessage(string html, string kind)
        {
            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-").Append(kind).Append("\">")
              .Append(html).Append("</div>");
            phMessages.Controls.Add(new LiteralControl(sb.ToString()));
        }
    }
}
