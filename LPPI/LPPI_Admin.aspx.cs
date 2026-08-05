using System;
using System.Data;
using System.Globalization;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Dashboard. Read-only overview of LPPI activity.
    ///
    /// The exposure headline and document / reviewed / outstanding counts are
    /// current-cycle — in-flight packages (NotSent / Sent / InReview /
    /// Finalised) via GetSummaryScopeHeader(CurrentCycle), so they match the
    /// Summary page and do not drift cumulative as packages ship to ERP. For
    /// an all-cycles total, see the Summary page's cumulative scope.
    ///
    /// Open packages list covers NotSent / Sent / InReview / Finalised
    /// Finalised packages stay visible here so admins can see
    /// what's queued for export at a glance. Exported and Cancelled
    /// packages drop off the dashboard entirely — they're terminal and
    /// surface on the Batches page if needed.
    ///
    /// </summary>
    public partial class LPPI_Admin : LPPIBasePage
    {
        // Exposed to the markup via <%= ExpPayablePct %> etc. Set during Bind()
        // from the dollar-share figures so the progress bars render the right
        // widths inline.
        protected int ExpPayablePct;
        protected int ExpNotPayablePct;
        protected int ExpAwaitingPct;

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
                Bind();
        }

        /// <summary>
        /// Reload-eligible pill for the Open packages list. Shows when a
        /// package still carries documents flagged RC-RL and deactivated at
        /// finalise that have not yet been superseded by a corrected reload.
        /// Reconciles with the Deactivated watch-list for the same package.
        /// Renders nothing when the count is zero.
        /// </summary>
        protected string RenderReloadPill(object countObj)
        {
            int n = (countObj == null || countObj == DBNull.Value) ? 0 : Convert.ToInt32(countObj);
            if (n <= 0) return "";
            string title = n + " document" + (n == 1 ? "" : "s") +
                " flagged reload-eligible (RC-RL), deactivated at finalise and awaiting a corrected reload." +
                " Open the Deactivated watch-list for detail.";
            return "<a href=\"LPPI_Deactivated.aspx\" class=\"pill\" title=\"" +
                System.Web.HttpUtility.HtmlAttributeEncode(title) +
                "\" style=\"margin-left:6px;background:#fff1d6;color:#7a4f00;border:1px solid #e6c478;text-decoration:none;\">RC-RL &times;" +
                n + "</a>";
        }

        private void Bind()
        {
            // -----------------------------------------------------------------
            // Current-cycle headline — in-flight packages (NotSent / Sent /
            // InReview / Finalised), the same universe as the Summary page's
            // "Current cycle" scope. Sourced from GetSummaryScopeHeader so the
            // Dashboard and Summary report identical figures, and so the
            // numbers reflect the live cycle rather than drifting cumulative
            // as packages ship to ERP (Exported) or are Cancelled.
            // -----------------------------------------------------------------
            var s = LPPIHelper.GetSummaryScopeHeader(LPPIHelper.SummaryScope.CurrentCycle());

            decimal expTotal      = AsDecimal(s, "TotalInterest");
            decimal expPayable    = AsDecimal(s, "PayableInterest");
            decimal expNotPayable = AsDecimal(s, "NotPayableInterest");
            decimal expAwaiting   = AsDecimal(s, "AwaitingInterest");
            int cycleDocs        = s == null || s["DocCount"]      == DBNull.Value ? 0 : Convert.ToInt32(s["DocCount"]);
            int cycleReviewed    = s == null || s["ReviewedCount"] == DBNull.Value ? 0 : Convert.ToInt32(s["ReviewedCount"]);
            int cycleOutstanding = cycleDocs - cycleReviewed;

            CultureInfo au = CultureInfo.GetCultureInfo("en-AU");

            // Exposure cards (dollar headline). The three component figures
            // sum to the total so the progress bars are eye-checkable.
            litExpTotal.Text      = FormatMoney(expTotal);
            litExpPayable.Text    = FormatMoney(expPayable);
            litExpNotPayable.Text = FormatMoney(expNotPayable);
            litExpAwaiting.Text   = FormatMoney(expAwaiting);
            litExpDocs.Text       = cycleDocs.ToString("N0", au);

            // Percentage shares — clamped 0..100. When total is zero, all
            // three bars render empty (0%), the right thing for an empty cycle.
            ExpPayablePct    = SharePct(expPayable,    expTotal);
            ExpNotPayablePct = SharePct(expNotPayable, expTotal);
            ExpAwaitingPct   = SharePct(expAwaiting,   expTotal);

            // Count stat-grid — document figures are current-cycle (from the
            // scope header above); package and batch figures come from
            // GetDashboardSummary.
            litTotal.Text       = cycleDocs.ToString("N0", au);
            litReviewed.Text    = cycleReviewed.ToString("N0", au);
            litOutstanding.Text = cycleOutstanding.ToString("N0", au);

            var dash = LPPIHelper.GetDashboardSummary();
            if (dash != null)
            {
                litOpen.Text    = Convert.ToString(dash["OpenPackages"]);
                litNear.Text    = Convert.ToString(dash["NearDeadlinePackages"]);
                litOverdue.Text = Convert.ToString(dash["OverduePackages"]);
                litBatches.Text = Convert.ToString(dash["CycleBatches"]);
            }

            // -----------------------------------------------------------------
            // Open packages — NotSent / Sent / InReview / Finalised.
            //
            // Finalised packages stay visible so admins can see what's
            // queued for export. Exported and Cancelled drop off (terminal).
            //
            // Sort order: alphabetical by Capability Manager, with PackageID
            // as a stable tie-break for the rare case where one CM has more
            // than one open package. The status pill itself communicates
            // urgency, so the row order does not need to.
            // -----------------------------------------------------------------
            var pkgSql = @"
SELECT p.PackageID, p.Token, p.CreatedDate, p.DueDate, p.Status,
       cm.Program AS CmDisplay,
       (SELECT COUNT(*)
          FROM dbo.tblLPPI_ReviewPackageDocuments d
         INNER JOIN dbo.tblLPPI_Documents doc
                 ON doc.DocumentID = d.DocumentID
                AND doc.IsDeactivated = 0
         WHERE d.PackageID = p.PackageID) AS DocCount,
       (SELECT COUNT(*)
          FROM dbo.tblLPPI_ReviewPackageDocuments d
         INNER JOIN dbo.tblLPPI_Documents doc
                 ON doc.DocumentID = d.DocumentID
                AND doc.IsDeactivated = 0
         INNER JOIN dbo.tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
         WHERE d.PackageID = p.PackageID
           AND r.ReasonCodeID IS NOT NULL) AS ReviewedCount,
       (SELECT COUNT(DISTINCT doc.DocNoAccounting)
          FROM dbo.tblLPPI_ReviewPackageDocuments d
         INNER JOIN dbo.tblLPPI_Documents doc
                 ON doc.DocumentID = d.DocumentID
         WHERE d.PackageID = p.PackageID
           AND doc.IsDeactivated = 1
           AND doc.SupersededByDocumentID IS NULL) AS RcRlAwaitingCount
  FROM dbo.tblLPPI_ReviewPackages p
 INNER JOIN dbo.tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
 WHERE p.Status IN ('NotSent','Sent','InReview','Finalised')
 ORDER BY cm.Program, p.PackageID;";

            var pkgs = LPPIHelper.ExecuteTable(pkgSql);
            pkgs.Columns.Add("CanRemind", typeof(bool));
            int warn = LPPIHelper.ReminderWindowDays;
            foreach (DataRow r in pkgs.Rows)
            {
                var due      = Convert.ToDateTime(r["DueDate"]);
                var docCount = Convert.ToInt32(r["DocCount"]);
                var rev      = Convert.ToInt32(r["ReviewedCount"]);
                var status   = Convert.ToString(r["Status"]);
                var pct      = docCount == 0 ? 100 : (rev * 100 / docCount);

                // CanRemind: only on Sent / InReview, near due, and not yet
                // fully reviewed. Finalised packages are NOT remindable —
                // the email cycle is over for them.
                bool isRemindable =
                    string.Equals(status, "Sent",     StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "InReview", StringComparison.OrdinalIgnoreCase);
                r["CanRemind"] = isRemindable && (due <= DateTime.Today.AddDays(warn)) && pct < 100;
            }
            rptPackages.DataSource = pkgs;
            rptPackages.DataBind();
            phNoPackages.Visible = pkgs.Rows.Count == 0;

            // Recent batches
            var batchSql = @"
SELECT TOP 10 BatchID, FileName, LoadedDate, LoadedByName,
              RowsInFile, RowsInserted, RowsSkipped, RowsFailed
FROM dbo.tblLPPI_LoadBatches
ORDER BY LoadedDate DESC;";
            rptBatches.DataSource = LPPIHelper.ExecuteTable(batchSql);
            rptBatches.DataBind();
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private static decimal AsDecimal(DataRow row, string column)
        {
            if (row == null || row[column] == DBNull.Value) return 0m;
            return Convert.ToDecimal(row[column]);
        }

        /// <summary>
        /// Whole-number percentage of part / whole, clamped to 0..100.
        /// Returns 0 when whole is zero to avoid divide-by-zero.
        /// </summary>
        private static int SharePct(decimal part, decimal whole)
        {
            if (whole <= 0m) return 0;
            decimal pct = (part / whole) * 100m;
            int rounded = (int)Math.Round(pct, MidpointRounding.AwayFromZero);
            if (rounded < 0)   rounded = 0;
            if (rounded > 100) rounded = 100;
            return rounded;
        }

        /// <summary>
        /// Money formatter for the exposure block — en-AU thousands separators,
        /// two decimals. The "$" symbol is added by the markup so the value
        /// itself is just the number.
        /// </summary>
        private static string FormatMoney(decimal value)
        {
            return value.ToString("N2", CultureInfo.GetCultureInfo("en-AU"));
        }

        /// <summary>
        /// Status pill for the Open packages table. Authoritative status
        /// drives the colour; overdue / due-soon adornment only applies
        /// to Sent / InReview (NotSent is yet to be sent so the due date
        /// isn't relevant; Finalised is closed off).
        ///
        /// The old "Complete (pending close)" override is gone — Finalised
        /// is its own real status with its own pill.
        /// </summary>
        protected string RenderStatusPill(object dataItem)
        {
            var row    = (DataRowView)dataItem;
            var status = Convert.ToString(row["Status"]);
            var due    = Convert.ToDateTime(row["DueDate"]);

            bool active = string.Equals(status, "Sent",     StringComparison.OrdinalIgnoreCase)
                       || string.Equals(status, "InReview", StringComparison.OrdinalIgnoreCase);

            string label;
            string cls;
            switch ((status ?? "").ToLowerInvariant())
            {
                case "notsent":   label = "Not sent";  cls = "notsent";   break;
                case "sent":      label = "Sent";      cls = "sent";      break;
                case "inreview":  label = "In review"; cls = "inreview";  break;
                case "finalised": label = "Finalised"; cls = "finalised"; break;
                case "exported":  label = "Exported";  cls = "exported";  break;
                case "cancelled": label = "Cancelled"; cls = "cancelled"; break;
                default:          label = status;     cls = "";          break;
            }

            var sb = new StringBuilder();
            sb.AppendFormat("<span class=\"pill {0}\">{1}</span>", cls, LPPIHelper.Enc(label));

            if (active && due < DateTime.Today)
                sb.Append(" <span class=\"pill overdue\">Overdue</span>");
            else if (active && due <= DateTime.Today.AddDays(LPPIHelper.ReminderWindowDays))
                sb.Append(" <span class=\"pill duesoon\">Due soon</span>");

            return sb.ToString();
        }

        /// <summary>
        /// Renders the actions cell for the open packages table:
        ///   - "Send reminder" button (when CanRemind)
        ///
        /// Open review is intentionally NOT rendered here. The Dashboard is
        /// a read-only overview; all package actions (open review, send,
        /// remind) belong on the Send-outs page so there is one obvious
        /// place to act on a package.
        /// </summary>
        protected string RenderPackageActions(object packageIdObj, object tokenObj,
                                              object statusObj, bool canRemind)
        {
            if (packageIdObj == null || packageIdObj == DBNull.Value) return "";

            int packageId = Convert.ToInt32(packageIdObj);

            var sb = new StringBuilder();

            // Send reminder — only when CanRemind (Sent/InReview, near due, not complete).
            if (canRemind)
            {
                sb.AppendFormat(
                    "<button type=\"button\" class=\"btn btn-sm btn-ghost\" " +
                    "onclick=\"document.getElementById('hfRemindPackageId').value='{0}';" +
                    "document.getElementById('btnRemindTrigger').click();\">Send reminder</button>",
                    packageId);
            }

            return sb.ToString();
        }

        protected void OnPackageCommand(object sender, CommandEventArgs e)
        {
            if (e.CommandName == "Remind")
            {
                int pid = Convert.ToInt32(e.CommandArgument);
                var res = LPPIEmail.SendReminder(pid);
                if (!res.Success)
                {
                    phWarnings.Controls.Add(new LiteralControl(
                        "<div class=\"alert alert-err\">Reminder failed: " +
                        System.Web.HttpUtility.HtmlEncode(res.ErrorMessage) + "</div>"));
                }
                else
                {
                    phWarnings.Controls.Add(new LiteralControl(
                        "<div class=\"alert alert-ok\">Reminder sent.</div>"));
                }
                Bind();
            }
        }

        // Hidden postback trigger for the remind button rendered via RenderPackageActions.
        protected void btnRemindTrigger_Click(object sender, EventArgs e)
        {
            string raw = (hfRemindPackageId.Value ?? "").Trim();
            int pid;
            if (!int.TryParse(raw, out pid)) return;

            var res = LPPIEmail.SendReminder(pid);
            if (!res.Success)
            {
                phWarnings.Controls.Add(new LiteralControl(
                    "<div class=\"alert alert-err\">Reminder failed: " +
                    System.Web.HttpUtility.HtmlEncode(res.ErrorMessage) + "</div>"));
            }
            else
            {
                phWarnings.Controls.Add(new LiteralControl(
                    "<div class=\"alert alert-ok\">Reminder sent.</div>"));
            }
            Bind();
        }
    }
}
