using System;
using System.Data;
using System.Linq;
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

            // Open packages
            var pkgSql = @"
SELECT p.PackageID, p.CreatedDate, p.DueDate, p.Status,
       ISNULL(NULLIF(cm.DisplayName,''), cm.Program) AS CmDisplay,
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
 WHERE p.Status = 'Open'
 ORDER BY p.DueDate ASC;";

            var pkgs = LPPIHelper.ExecuteTable(pkgSql);
            pkgs.Columns.Add("CanRemind", typeof(bool));
            int warn = LPPIHelper.ReminderWindowDays;
            foreach (DataRow r in pkgs.Rows)
            {
                var due      = Convert.ToDateTime(r["DueDate"]);
                var docCount = Convert.ToInt32(r["DocCount"]);
                var rev      = Convert.ToInt32(r["ReviewedCount"]);
                var pct      = docCount == 0 ? 100 : (rev * 100 / docCount);
                r["CanRemind"] = (due <= DateTime.Today.AddDays(warn)) && pct < 100;
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
            var due      = Convert.ToDateTime(row["DueDate"]);
            var docCount = Convert.ToInt32(row["DocCount"]);
            var rev      = Convert.ToInt32(row["ReviewedCount"]);
            if (docCount > 0 && rev >= docCount) return "<span class=\"pill reviewed\">Complete</span>";
            if (due < DateTime.Today)             return "<span class=\"pill overdue\">Overdue</span>";
            if (due <= DateTime.Today.AddDays(LPPIHelper.ReminderWindowDays))
                                                  return "<span class=\"pill pending\">Due soon</span>";
            return "<span class=\"pill open\">Open</span>";
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
                        "<div class=\"alert err\">Reminder failed: " +
                        System.Web.HttpUtility.HtmlEncode(res.ErrorMessage) + "</div>"));
                }
                else
                {
                    phWarnings.Controls.Add(new LiteralControl(
                        "<div class=\"alert ok\">Reminder sent.</div>"));
                }
                Bind();
            }
        }
    }
}
