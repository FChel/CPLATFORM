using System;
using System.Data;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    public partial class LPPI_Admin : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
                Bind();
        }

        private void Bind()
        {
            // Stats
            var s = LPPIHelper.GetDashboardSummary();
            if (s != null)
            {
                litTotal.Text       = Convert.ToString(s["TotalDocs"]);
                litReviewed.Text    = Convert.ToString(s["TotalReviewed"]);
                litOutstanding.Text = Convert.ToString(s["TotalOutstanding"]);
                litOpen.Text        = Convert.ToString(s["OpenPackages"]);
                litNear.Text        = Convert.ToString(s["NearDeadlinePackages"]);
                litOverdue.Text     = Convert.ToString(s["OverduePackages"]);
                litBatches.Text     = Convert.ToString(s["TotalBatches"]);
            }

            // Open packages — covers NotSent / Sent / InReview.
            // (Token column kept in the projection for future use; the
            // Dashboard no longer renders an Open review button — that
            // action lives on Send-outs only.)
            var pkgSql = @"
SELECT p.PackageID, p.Token, p.CreatedDate, p.DueDate, p.Status,
       cm.Program AS CmDisplay,
       (SELECT COUNT(*)
          FROM dbo.tblLPPI_ReviewPackageDocuments d
         WHERE d.PackageID = p.PackageID) AS DocCount,
       (SELECT COUNT(*)
          FROM dbo.tblLPPI_ReviewPackageDocuments d
         INNER JOIN dbo.tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
         WHERE d.PackageID = p.PackageID
           AND r.ReasonCodeID IS NOT NULL) AS ReviewedCount
  FROM dbo.tblLPPI_ReviewPackages p
 INNER JOIN dbo.tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
 WHERE p.Status IN ('NotSent','Sent','InReview')
 ORDER BY
    CASE p.Status WHEN 'NotSent' THEN 0 WHEN 'Sent' THEN 1 ELSE 2 END,
    p.DueDate ASC;";

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

        protected string RenderStatusPill(object dataItem)
        {
            var row      = (DataRowView)dataItem;
            var status   = Convert.ToString(row["Status"]);
            var due      = Convert.ToDateTime(row["DueDate"]);
            var docCount = Convert.ToInt32(row["DocCount"]);
            var rev      = Convert.ToInt32(row["ReviewedCount"]);

            // Active statuses get the overdue / due-soon augmentation.
            bool active = string.Equals(status, "Sent",     StringComparison.OrdinalIgnoreCase)
                       || string.Equals(status, "InReview", StringComparison.OrdinalIgnoreCase);

            string label;
            string cls;
            switch ((status ?? "").ToLowerInvariant())
            {
                case "notsent":   label = "Not sent";  cls = "notsent";   break;
                case "sent":      label = "Sent";      cls = "sent";      break;
                case "inreview":  label = "In review"; cls = "inreview";  break;
                case "complete":  label = "Complete";  cls = "complete";  break;
                case "cancelled": label = "Cancelled"; cls = "cancelled"; break;
                default:          label = status;     cls = "";          break;
            }

            // Override to "Complete"-style if every doc reviewed.
            if (docCount > 0 && rev >= docCount && active)
            {
                label = "Complete (pending close)";
                cls   = "complete";
                active = false;
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
