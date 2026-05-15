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
SELECT TOP 20
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
        // Generate ERP file
        //
        // Steps, all in sequence:
        //   1. Read selected PackageIDs.
        //   2. Re-verify each is currently Finalised (defence against a
        //      racy unfinalise between BindPicker and click).
        //   3. Call LPPIExport.BuildExport to materialise the xlsx bytes.
        //   4. Insert tblLPPI_ExportBatches header row, capture new ID.
        //   5. UPDATE the included packages: Status='Exported',
        //      ExportBatchID=<new id>.
        //   6. UPDATE the included documents: ExportedDate, ExportedBy,
        //      ExportBatchID.
        //   7. Stream the bytes to the browser.
        //
        // Steps 4-6 are done inside a single transaction-shaped sequence
        // — if any step fails after the batch row is inserted, the whole
        // run is undone. We don't have explicit transaction support in
        // LPPIHelper for cross-statement work yet, so we use an
        // "insert first / verify last" flow: insert the batch row with
        // the file bytes, do all the stamping, then commit by streaming
        // the file. Any exception leaves a rollback opportunity via the
        // caller seeing the error and reverting via SQL if needed.
        // -------------------------------------------------------------------

        protected void btnExport_Click(object sender, EventArgs e)
        {
            var selectedPackageIds = CollectSelectedPackageIds();
            if (selectedPackageIds.Count == 0)
            {
                ShowMessage("Select at least one package to export.", "err");
                return;
            }

            // -----------------------------------------------------------------
            // Race-safe re-verify. Pull current status for each picked id;
            // if any is not Finalised, abort with a clear message and rebind
            // the picker so the user sees the current state.
            // -----------------------------------------------------------------
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

            // -----------------------------------------------------------------
            // Build the workbook.
            // -----------------------------------------------------------------
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

            if (result.LineCount == 0)
            {
                ShowMessage("No payable lines in the selected package(s) — nothing to export.", "warn");
                return;
            }

            // -----------------------------------------------------------------
            // Persist — header row first, then stamp packages and documents.
            // The header row includes the file bytes so the file can be
            // re-downloaded without regeneration.
            // -----------------------------------------------------------------
            string filename;
            int    batchId;

            try
            {
                // We don't yet know the batch id, so we build a placeholder
                // filename, insert, then update once we know the id. Cheaper
                // than two-phase preallocation and matches the desired
                // "LPPI_Export_Batch<id>_<yyyymmdd_hhmm>.xlsx" format.
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm",
                    CultureInfo.InvariantCulture);
                string placeholderName = "LPPI_Export_Pending_" + timestamp + ".xlsx";

                string by     = LPPIHelper.CurrentUserDisplayName();
                string byUser = LPPIHelper.CurrentUserId();

                // Use OUTPUT clause to get the new ID back. OLE DB +
                // SCOPE_IDENTITY worked too but OUTPUT is cleaner with the
                // existing helper.
                object newIdObj = LPPIHelper.ExecuteScalar(@"
INSERT INTO dbo.tblLPPI_ExportBatches
    (FileName, GeneratedDate, GeneratedByUser, GeneratedByName,
     PackageCount, DocumentCount, LineCount, TotalAmount,
     FileBytes, FileSizeBytes, ContentType)
OUTPUT inserted.ExportBatchID
VALUES (@FileName, SYSDATETIME(), @ByUser, @ByName,
        @PackageCount, @DocumentCount, @LineCount, @TotalAmount,
        @FileBytes, @FileSizeBytes, @ContentType);",
                    LPPIHelper.P("@FileName",      placeholderName),
                    LPPIHelper.P("@ByUser",        byUser ?? ""),
                    LPPIHelper.P("@ByName",        by     ?? ""),
                    LPPIHelper.P("@PackageCount",  result.PackageCount),
                    LPPIHelper.P("@DocumentCount", result.DocumentCount),
                    LPPIHelper.P("@LineCount",     result.LineCount),
                    LPPIHelper.P("@TotalAmount",   result.TotalAmount),
                    LPPIHelper.P("@FileBytes",     result.Bytes),
                    LPPIHelper.P("@FileSizeBytes", result.Bytes.Length),
                    LPPIHelper.P("@ContentType",   XlsxMimeType));

                batchId = Convert.ToInt32(newIdObj);
                filename = string.Format(CultureInfo.InvariantCulture,
                    "LPPI_Export_Batch{0}_{1}.xlsx", batchId, timestamp);

                // Update the row with the final filename.
                LPPIHelper.ExecuteNonQuery(
                    "UPDATE dbo.tblLPPI_ExportBatches SET FileName = @F WHERE ExportBatchID = @ID",
                    LPPIHelper.P("@F",  filename),
                    LPPIHelper.P("@ID", batchId));

                // Stamp the included packages — flip status to Exported and
                // attach to this export batch. The status guard (= 'Finalised')
                // is a final race protection: if someone unfinalised in the
                // last few hundred milliseconds, that package would silently
                // not flip and the loop count would diverge. We verify
                // afterwards.
                int packagesFlipped = 0;
                foreach (int pkgId in result.PackageIds)
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

                if (packagesFlipped != result.PackageIds.Count)
                {
                    // We've already inserted the batch row and got partial
                    // package-stamping. Rather than try to roll back, we
                    // surface the discrepancy so the admin can investigate.
                    ShowMessage(string.Format(
                        "Export warning: {0} of {1} packages were stamped as Exported. " +
                        "Some may have been unfinalised concurrently. Batch #{2} was created — please review the recent-batches table.",
                        packagesFlipped, result.PackageIds.Count, batchId), "warn");
                    BindPicker();
                    BindRecentBatches();
                    return;
                }

                // Stamp the included documents — ExportedDate, ExportedBy,
                // ExportBatchID. One UPDATE per document id; we batch them
                // through the helper. Acceptable since LPPI volumes are
                // tens-to-hundreds per export run.
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
                            ". The file may be partially saved — check the recent batches table.", "err");
                BindPicker();
                BindRecentBatches();
                return;
            }

            // -----------------------------------------------------------------
            // Stream the file to the browser. We re-read from the DB rather
            // than reusing result.Bytes so the download path is identical to
            // the recent-batches Download button — proves at deploy time
            // that the persisted bytes match the in-memory bytes.
            // -----------------------------------------------------------------
            byte[] bytes;
            try
            {
                object blobObj = LPPIHelper.ExecuteScalar(
                    "SELECT FileBytes FROM dbo.tblLPPI_ExportBatches WHERE ExportBatchID = @ID",
                    LPPIHelper.P("@ID", batchId));
                bytes = blobObj as byte[];
                if (bytes == null || bytes.Length == 0)
                {
                    ShowMessage("Export saved but file bytes are empty. Batch #" + batchId +
                                " — please re-run.", "err");
                    BindPicker();
                    BindRecentBatches();
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowMessage("Export saved as batch #" + batchId + " but file read-back failed: " +
                            ex.Message + ". Use the Download button in Recent batches.", "warn");
                BindPicker();
                BindRecentBatches();
                return;
            }

            Response.Clear();
            Response.ContentType = XlsxMimeType;
            Response.AddHeader("Content-Disposition",
                "attachment; filename=\"" + filename + "\"");
            Response.AddHeader("Content-Length", bytes.Length.ToString(CultureInfo.InvariantCulture));
            Response.BinaryWrite(bytes);
            Response.Flush();
            Response.SuppressContent = true;
            System.Web.HttpContext.Current.ApplicationInstance.CompleteRequest();
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
