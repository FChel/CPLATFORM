using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Export page — admin checkpoint for shipping payable cases to ERP.
    ///
    /// Workflow (May 2026 rebuild):
    ///   1. AS Fin teams finalise their packages on the reviewer page.
    ///   2. This page lists Finalised packages awaiting export.
    ///   3. Admin ticks one or more, clicks Generate ERP file.
    ///   4. The xlsx is built, stored in tblLPPI_ExportBatches.FileBytes,
    ///      and downloaded to the browser. The included packages flip to
    ///      Exported (terminal) with ExportBatchID stamped on each
    ///      package and on each document row included in the file.
    ///   5. Past export batches appear in the Recent batches table with
    ///      Download buttons that re-stream the stored bytes.
    ///
    /// Admin action only — the page inherits LPPIBasePage's admin gate.
    ///
    /// Stamping is done in the same SQL transaction as the batch row
    /// insert so partial state cannot leak (no half-exported packages,
    /// no orphaned batch rows).
    /// </summary>
    public partial class LPPI_Export : LPPIBasePage
    {
        private const string XlsxMimeType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                BindPicker();
                BindRecentBatches();
            }
        }

        // -------------------------------------------------------------------
        // Picker — Finalised packages awaiting export
        // -------------------------------------------------------------------

        private void BindPicker()
        {
            // One row per Finalised package. PayableDocCount and
            // PayableInterest are the live numbers from joined Reviews/
            // ReasonCodes — Outcome='Payable' only — so the totals strip
            // shows what would actually appear in the file. DocCount is the
            // total package size for context.
            //
            // The reason code lives at document level (joined via the
            // first-line DocumentID), so each document's payable status is
            // determined once and inherited by all its lines. The counts
            // here are at DOCUMENT granularity, matching what the operator
            // thinks in. The actual file row count may be higher if
            // multi-line documents are included — that's surfaced later in
            // the audit row's LineCount.
            //
            // May 2026 — supersession model: the PayableInterest sum joins
            // tblLPPI_Documents d on DocNoAccounting to aggregate every line
            // for each document. Without an IsDeactivated = 0 filter on that
            // join, deactivated history rows from prior RC-RL cycles would
            // be added to the total, inflating the figure shown next to each
            // finalised package. The filter ensures only live lines count.
            const string sql = @"
SELECT p.PackageID,
       p.Token,
       cm.Program,
       p.FinalisedDate,
       p.FinalisedBy,
       (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackageDocuments pd
         WHERE pd.PackageID = p.PackageID) AS DocCount,
       (SELECT COUNT(*)
          FROM dbo.tblLPPI_ReviewPackageDocuments pd
         INNER JOIN dbo.tblLPPI_Reviews r       ON r.DocumentID = pd.DocumentID
         INNER JOIN dbo.tblLPPI_ReasonCodes rc  ON rc.ReasonCodeID = r.ReasonCodeID
         WHERE pd.PackageID = p.PackageID
           AND rc.Outcome   = 'Payable') AS PayableDocCount,
       ISNULL((SELECT SUM(d.InterestPayable)
                 FROM dbo.tblLPPI_ReviewPackageDocuments pd
                INNER JOIN dbo.tblLPPI_Reviews r       ON r.DocumentID = pd.DocumentID
                INNER JOIN dbo.tblLPPI_ReasonCodes rc  ON rc.ReasonCodeID = r.ReasonCodeID
                INNER JOIN dbo.tblLPPI_Documents d
                        ON d.DocNoAccounting = (SELECT d2.DocNoAccounting
                                                  FROM dbo.tblLPPI_Documents d2
                                                 WHERE d2.DocumentID = pd.DocumentID)
                       AND d.IsDeactivated   = 0
                WHERE pd.PackageID = p.PackageID
                  AND rc.Outcome  = 'Payable'), 0) AS PayableInterest
  FROM dbo.tblLPPI_ReviewPackages p
 INNER JOIN dbo.tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
 WHERE p.Status = 'Finalised'
 ORDER BY p.FinalisedDate ASC, cm.Program ASC, p.PackageID ASC;";

            DataTable dt = LPPIHelper.ExecuteTable(sql);
            rptFinalised.DataSource = dt;
            rptFinalised.DataBind();

            phPicker.Visible = dt.Rows.Count > 0;
            phEmpty.Visible  = dt.Rows.Count == 0;

            // Disable the export button when there's nothing to pick. JS
            // re-enables it on the first checkbox click; this is just the
            // initial state.
            btnExport.Enabled = dt.Rows.Count > 0;
        }

        // -------------------------------------------------------------------
        // Recent batches table — last 20, with Download links
        // -------------------------------------------------------------------

        private void BindRecentBatches()
        {
            const string sql = @"
SELECT TOP 50
       ExportBatchID, FileName, GeneratedDate, GeneratedByName,
       PackageCount, DocumentCount, LineCount, TotalAmount
  FROM dbo.tblLPPI_ExportBatches
 ORDER BY GeneratedDate DESC;";
            DataTable dt = LPPIHelper.ExecuteTable(sql);
            rptBatches.DataSource = dt;
            rptBatches.DataBind();
            phNoBatches.Visible = dt.Rows.Count == 0;
        }

        // -------------------------------------------------------------------
        // Per-batch row bind — attach the company-code download links. New
        // batches have child files in tblLPPI_ExportBatchFiles (?f= links).
        // Legacy batches predate the per-company split and serve their single
        // combined file from the header (?b= link). Zero-payable batches have
        // neither and show "(no file)".
        // -------------------------------------------------------------------
        protected void rptBatches_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            if (e.Item.ItemType != ListItemType.Item && e.Item.ItemType != ListItemType.AlternatingItem)
                return;

            DataRowView drv = e.Item.DataItem as DataRowView;
            if (drv == null) return;

            int batchId  = Convert.ToInt32(drv["ExportBatchID"]);
            var rptFiles = e.Item.FindControl("rptBatchFiles") as Repeater;
            var litLegacy = e.Item.FindControl("litLegacyDownload") as Literal;

            DataTable files = LPPIHelper.ExecuteTable(@"
SELECT ExportBatchFileID, CompanyCode, FileName, FileSizeBytes
  FROM dbo.tblLPPI_ExportBatchFiles
 WHERE ExportBatchID = @B
 ORDER BY CompanyCode;",
                LPPIHelper.P("@B", batchId));

            if (files.Rows.Count > 0)
            {
                if (rptFiles != null) { rptFiles.DataSource = files; rptFiles.DataBind(); }
                if (litLegacy != null) litLegacy.Text = "";
                return;
            }

            if (rptFiles != null) { rptFiles.DataSource = null; rptFiles.DataBind(); }

            int docCount = drv["DocumentCount"] == DBNull.Value ? 0 : Convert.ToInt32(drv["DocumentCount"]);
            if (litLegacy != null)
            {
                litLegacy.Text = docCount == 0
                    ? "<span class=\"muted\">(no file)</span>"
                    : "<a class=\"btn btn-sm btn-secondary\" href=\"LPPI_Export_Download.ashx?b=" +
                      batchId.ToString(CultureInfo.InvariantCulture) + "\">Download</a>";
            }
        }

        // -------------------------------------------------------------------
        // Selection helper
        // -------------------------------------------------------------------

        private List<int> CollectSelectedPackageIds()
        {
            var ids = new List<int>();
            foreach (RepeaterItem item in rptFinalised.Items)
            {
                // chkPick is a plain HTML checkbox with runat=server (not an
                // asp:CheckBox), so it materialises as HtmlInputCheckBox here
                // — see the comment in the .aspx for the JS-class-on-input
                // reason behind this choice.
                var chk = item.FindControl("chkPick") as System.Web.UI.HtmlControls.HtmlInputCheckBox;
                var hf  = item.FindControl("hfPackageId") as HiddenField;
                if (chk != null && hf != null && chk.Checked)
                {
                    int id;
                    if (int.TryParse(hf.Value, out id)) ids.Add(id);
                }
            }
            return ids;
        }

        // -------------------------------------------------------------------
        // Generate ERP files
        //
        // Steps, all in sequence:
        //   1. Read selected PackageIDs.
        //   2. Re-verify each is currently Finalised (defence against a racy
        //      unfinalise between BindPicker and click).
        //   3. Call LPPIExport.BuildExport to materialise one xlsx per
        //      company code.
        //   4. Insert the tblLPPI_ExportBatches header row, capture new ID.
        //   5. Insert one tblLPPI_ExportBatchFiles row per company-code file.
        //   6. UPDATE the included packages: Status='Exported',
        //      ExportBatchID=<new id>.
        //   7. UPDATE the included documents: ExportedDate, ExportedBy,
        //      ExportBatchID.
        //   8. Show the per-company download links in Recent batches.
        //
        // The files are not auto-streamed — a batch can produce several
        // files, so the operator downloads each from the Recent batches
        // table. Steps 4-7 run as a single transaction-shaped sequence; an
        // exception after the header insert surfaces a clear message so the
        // admin can reconcile via the recent-batches table.
        // -------------------------------------------------------------------

        protected void btnExport_Click(object sender, EventArgs e)
        {
            var selectedPackageIds = CollectSelectedPackageIds();
            if (selectedPackageIds.Count == 0)
            {
                ShowMessage("Select at least one package to export.", "err");
                return;
            }

            // Race-safe re-verify. Pull current status for each picked id; if
            // any is not Finalised, abort and rebind the picker.
            var notFinalised = new List<int>();
            foreach (int pid in selectedPackageIds)
            {
                object statusObj = LPPIHelper.ExecuteScalar(
                    "SELECT Status FROM dbo.tblLPPI_ReviewPackages WHERE PackageID = @P",
                    LPPIHelper.P("@P", pid));
                string status = statusObj == null || statusObj == DBNull.Value
                    ? "" : Convert.ToString(statusObj);
                if (!string.Equals(status, "Finalised", StringComparison.OrdinalIgnoreCase))
                    notFinalised.Add(pid);
            }
            if (notFinalised.Count > 0)
            {
                var ids = string.Join(", ", notFinalised.ConvertAll(i => "#" + i.ToString(CultureInfo.InvariantCulture)));
                ShowMessage("One or more selected packages are no longer Finalised: " + ids +
                            ". The list has been refreshed.", "err");
                BindPicker();
                BindRecentBatches();
                return;
            }

            // Build one workbook per company code.
            LPPIExport.ExportResult result;
            try
            {
                result = LPPIExport.BuildExport(selectedPackageIds);
            }
            catch (Exception ex)
            {
                ShowMessage("Export build failed: " + ex.Message, "err");
                return;
            }

            // A zero-payable selection (every document NotPayable / RC-RL) is
            // intentionally NOT bailed here. The packages still need to reach
            // Exported so they clear the queue, so we record a header-only
            // batch (no files), flip the selected packages, then show a
            // message — see the Files.Count == 0 branch after persistence.

            int batchId;
            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
                string envTag    = LPPIHelper.EnvironmentFileTag;
                string by        = LPPIHelper.CurrentUserDisplayName();
                string byUser    = LPPIHelper.CurrentUserId();

                // Header row first — counts roll up across the per-company
                // files; the file bytes live on the child rows. LineCount
                // mirrors DocumentCount now that the file is one row per
                // document.
                object newIdObj = LPPIHelper.ExecuteScalar(@"
INSERT INTO dbo.tblLPPI_ExportBatches
    (FileName, GeneratedDate, GeneratedByUser, GeneratedByName,
     PackageCount, DocumentCount, LineCount, TotalAmount)
OUTPUT inserted.ExportBatchID
VALUES (@FileName, SYSDATETIME(), @ByUser, @ByName,
        @PackageCount, @DocumentCount, @LineCount, @TotalAmount);",
                    LPPIHelper.P("@FileName",      envTag + "LPPI_Export_Pending_" + timestamp),
                    LPPIHelper.P("@ByUser",        byUser ?? ""),
                    LPPIHelper.P("@ByName",        by     ?? ""),
                    LPPIHelper.P("@PackageCount",  selectedPackageIds.Count),
                    LPPIHelper.P("@DocumentCount", result.DocumentCount),
                    LPPIHelper.P("@LineCount",     result.DocumentCount),
                    LPPIHelper.P("@TotalAmount",   result.TotalAmount));

                batchId = Convert.ToInt32(newIdObj);

                LPPIHelper.ExecuteNonQuery(
                    "UPDATE dbo.tblLPPI_ExportBatches SET FileName = @F WHERE ExportBatchID = @ID",
                    LPPIHelper.P("@F",  string.Format(CultureInfo.InvariantCulture,
                                        "{0}LPPI_Export_Batch{1}_{2}", envTag, batchId, timestamp)),
                    LPPIHelper.P("@ID", batchId));

                // One child row per company-code file. FileName carries the
                // company code so the audit folder and download are
                // self-describing.
                foreach (LPPIExport.ExportFile f in result.Files)
                {
                    string fileName = string.Format(CultureInfo.InvariantCulture,
                        "{0}LPPI_Export_Batch{1}_{2}_{3}.xlsx",
                        envTag, batchId, SafeCompanyToken(f.CompanyCode), timestamp);

                    LPPIHelper.ExecuteNonQuery(@"
INSERT INTO dbo.tblLPPI_ExportBatchFiles
    (ExportBatchID, CompanyCode, FileName, DocumentCount, TotalAmount,
     FileBytes, FileSizeBytes, ContentType)
VALUES (@B, @CC, @FN, @DC, @TA, @Bytes, @Size, @CT);",
                        LPPIHelper.P("@B",     batchId),
                        LPPIHelper.P("@CC",    f.CompanyCode ?? ""),
                        LPPIHelper.P("@FN",    fileName),
                        LPPIHelper.P("@DC",    f.DocumentCount),
                        LPPIHelper.P("@TA",    f.TotalAmount),
                        LPPIHelper.P("@Bytes", f.Bytes),
                        LPPIHelper.P("@Size",  f.Bytes.Length),
                        LPPIHelper.P("@CT",    XlsxMimeType));
                }

                // Flip the included packages to Exported. The status guard
                // (= 'Finalised') is a final race protection.
                int packagesFlipped = 0;
                foreach (int pkgId in selectedPackageIds)
                {
                    int rows = LPPIHelper.ExecuteNonQuery(@"
UPDATE dbo.tblLPPI_ReviewPackages
   SET Status        = 'Exported',
       ExportBatchID = @B
 WHERE PackageID = @P
   AND Status    = 'Finalised';",
                        LPPIHelper.P("@B", batchId),
                        LPPIHelper.P("@P", pkgId));
                    packagesFlipped += rows;
                }

                if (packagesFlipped != selectedPackageIds.Count)
                {
                    ShowMessage(string.Format(
                        "Export warning: {0} of {1} packages were stamped as Exported. " +
                        "Some may have been unfinalised concurrently. Batch #{2} was created — please review the recent-batches table.",
                        packagesFlipped, selectedPackageIds.Count, batchId), "warn");
                    BindPicker();
                    BindRecentBatches();
                    return;
                }

                // Stamp the included documents — ExportedDate, ExportedBy,
                // ExportBatchID. One UPDATE per line; LPPI volumes are
                // tens-to-hundreds per run.
                foreach (int docId in result.DocumentIds)
                {
                    LPPIHelper.ExecuteNonQuery(@"
UPDATE dbo.tblLPPI_Documents
   SET ExportedDate  = SYSDATETIME(),
       ExportedBy    = @By,
       ExportBatchID = @B
 WHERE DocumentID = @ID;",
                        LPPIHelper.P("@By", by ?? ""),
                        LPPIHelper.P("@B",  batchId),
                        LPPIHelper.P("@ID", docId));
                }
            }
            catch (Exception ex)
            {
                ShowMessage("Export persistence failed: " + ex.Message +
                            ". The batch may be partially saved — check the recent batches table.", "err");
                BindPicker();
                BindRecentBatches();
                return;
            }

            // Zero-payable selection — packages are Exported and a header-only
            // batch recorded, but there are no files to ship.
            if (result.Files.Count == 0)
            {
                ShowMessage(string.Format(CultureInfo.InvariantCulture,
                    "Selected package(s) had no payable documents. They have been marked Exported (batch #{0}); no ERP file was produced.",
                    batchId), "ok");
                BindPicker();
                BindRecentBatches();
                return;
            }

            // Success — list the company-code files. The operator downloads
            // each from the Recent batches table below (one file per company
            // code; a batch can produce several).
            var codes = new List<string>();
            foreach (LPPIExport.ExportFile f in result.Files) codes.Add(f.CompanyCode);
            ShowMessage(string.Format(CultureInfo.InvariantCulture,
                "Generated {0} ERP file{1} for batch #{2} — company code{1}: {3}. Download each from Recent export batches below.",
                result.Files.Count,
                result.Files.Count == 1 ? "" : "s",
                batchId,
                string.Join(", ", codes)), "ok");
            BindPicker();
            BindRecentBatches();
        }

        // Sanitise a company code for use in a file name — keep alphanumerics
        // and dash/underscore only.
        private static string SafeCompanyToken(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "NA";
            var sb = new StringBuilder();
            foreach (char c in raw)
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            string t = sb.ToString();
            return t.Length == 0 ? "NA" : t;
        }

        // -------------------------------------------------------------------
        // Message helper
        // -------------------------------------------------------------------
        private void ShowMessage(string msg, string kind)
        {
            phMessage.Controls.Clear();
            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-").Append(kind).Append("\">")
              .Append(LPPIHelper.Enc(msg)).Append("</div>");
            phMessage.Controls.Add(new LiteralControl(sb.ToString()));
        }
    }
}
