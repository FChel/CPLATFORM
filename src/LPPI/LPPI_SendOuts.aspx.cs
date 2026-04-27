using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Send-outs page. Lists packages (NotSent / Sent / InReview) and lets
    /// the operator issue them or send reminders. Packages are NOT created
    /// here — they are created by the file-load reconcile step.
    /// </summary>
    public partial class LPPI_SendOuts : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            BindUnconfigured();

            if (!IsPostBack)
            {
                txtDueDate.Text = DateTime.Today.AddDays(LPPIHelper.DefaultDueDays).ToString("yyyy-MM-dd");
                BindPackages();
                BindRecent();
            }

            // Gate the Send button and show a UAT banner when not in production mode.
            btnSend.Enabled = LPPIEmail.ProductionMode;
            RenderUatBanner();
        }

        // -------------------------------------------------------------------
        // UAT banner
        // -------------------------------------------------------------------

        private void RenderUatBanner()
        {
            phUatBanner.Controls.Clear();
            if (LPPIEmail.ProductionMode) return;

            phUatBanner.Controls.Add(new LiteralControl(
                "<div class=\"alert alert-warn\">" +
                "<div><strong>UAT mode</strong> — email sending is disabled. " +
                "Use the <em>Preview email</em> button to review the formatted email for any package. " +
                "Set <code>LPPI.ProductionMode = true</code> in web.config to enable sending.</div>" +
                "</div>"));
        }

        // -------------------------------------------------------------------
        // Unconfigured-CM warning
        // -------------------------------------------------------------------

        private void BindUnconfigured()
        {
            phUnconfigured.Controls.Clear();

            var unconfigured = LPPIHelper.GetUnconfiguredPrograms();
            if (unconfigured.Count == 0) return;

            var msg = "<div class=\"alert alert-warn\"><div><strong>" + unconfigured.Count +
                      " Capability Manager program" + (unconfigured.Count == 1 ? "" : "s") +
                      "</strong> in your loaded data have no recipient email configured. " +
                      "You will not be able to send these out for review until they are added.<br/>" +
                      "Missing: " + string.Join(", ", unconfigured.Select(p => "<code>" + System.Web.HttpUtility.HtmlEncode(p) + "</code>")) +
                      " &nbsp; <a href=\"LPPI_CapabilityManagers.aspx\">Configure now &rarr;</a></div></div>";
            phUnconfigured.Controls.Add(new LiteralControl(msg));
        }

        // -------------------------------------------------------------------
        // Data binding — open packages table
        // -------------------------------------------------------------------

        private void BindPackages()
        {
            // Columns required by rptPackages Eval():
            //   PackageID, Token, Program, ToCount, ToList,
            //   DocCount, ReviewedCount, Status, DueDate, LastEmailDate
            const string sql = @"
                SELECT p.PackageID,
                       p.Token,
                       p.Status,
                       p.DueDate,
                       p.SentDate,
                       cm.CmID,
                       cm.Program,
                       (SELECT COUNT(*)
                          FROM tblLPPI_CapabilityManagerEmails e
                         WHERE e.CmID = cm.CmID AND e.IsCC = 0) AS ToCount,
                       ISNULL(STUFF((SELECT ', ' + e.Email
                                       FROM tblLPPI_CapabilityManagerEmails e
                                      WHERE e.CmID = cm.CmID AND e.IsCC = 0
                                      FOR XML PATH('')), 1, 2, ''), '') AS ToList,
                       (SELECT COUNT(*)
                          FROM tblLPPI_ReviewPackageDocuments pd
                         WHERE pd.PackageID = p.PackageID) AS DocCount,
                       (SELECT COUNT(*)
                          FROM tblLPPI_ReviewPackageDocuments pd
                         INNER JOIN tblLPPI_Reviews r ON r.DocumentID = pd.DocumentID
                         WHERE pd.PackageID = p.PackageID
                           AND r.ReasonCodeID IS NOT NULL) AS ReviewedCount,
                       (SELECT MAX(el.SentDate)
                          FROM tblLPPI_EmailLog el
                         WHERE el.PackageID = p.PackageID) AS LastEmailDate
                  FROM tblLPPI_ReviewPackages p
                 INNER JOIN tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
                 WHERE p.Status IN ('NotSent','Sent','InReview')
                 ORDER BY
                    CASE p.Status WHEN 'NotSent' THEN 0 WHEN 'Sent' THEN 1 ELSE 2 END,
                    cm.Program,
                    p.CreatedDate";

            DataTable dt = LPPIHelper.ExecuteTable(sql);
            rptPackages.DataSource = dt;
            rptPackages.DataBind();
            phNoPackages.Visible = dt.Rows.Count == 0;
        }

        private void BindRecent()
        {
            // Columns required by rptRecent Eval():
            //   PackageID, Token, Program, CreatedDate, DueDate,
            //   TotalDocs, ReviewedDocs, Status, LastEmailDate
            const string sql = @"
                SELECT TOP 50
                       p.PackageID,
                       p.Token,
                       cm.Program,
                       p.CreatedDate,
                       p.DueDate,
                       p.Status,
                       (SELECT COUNT(*)
                          FROM tblLPPI_ReviewPackageDocuments pd
                         WHERE pd.PackageID = p.PackageID) AS TotalDocs,
                       (SELECT COUNT(*)
                          FROM tblLPPI_ReviewPackageDocuments pd
                         INNER JOIN tblLPPI_Reviews r ON r.DocumentID = pd.DocumentID
                         WHERE pd.PackageID = p.PackageID
                           AND r.ReasonCodeID IS NOT NULL) AS ReviewedDocs,
                       (SELECT MAX(el.SentDate)
                          FROM tblLPPI_EmailLog el
                         WHERE el.PackageID = p.PackageID) AS LastEmailDate
                  FROM tblLPPI_ReviewPackages p
                 INNER JOIN tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
                 WHERE p.SentDate IS NOT NULL
                 ORDER BY p.CreatedDate DESC";
            rptRecent.DataSource = LPPIHelper.ExecuteTable(sql);
            rptRecent.DataBind();
        }

        // -------------------------------------------------------------------
        // Render helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Status pill for the Open packages table. Uses package status as
        /// authoritative, but augments with overdue/due-soon when applicable.
        /// </summary>
        protected string RenderStatusPill(object dataItem)
        {
            var row    = (DataRowView)dataItem;
            var status = Convert.ToString(row["Status"]);
            var due    = row["DueDate"] == DBNull.Value ? DateTime.MaxValue : Convert.ToDateTime(row["DueDate"]);

            // For Sent / InReview, show overdue / due-soon as a secondary signal.
            bool active = string.Equals(status, "Sent", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(status, "InReview", StringComparison.OrdinalIgnoreCase);

            string statusLabel;
            string statusClass;
            switch ((status ?? "").ToLowerInvariant())
            {
                case "notsent":   statusLabel = "Not sent";   statusClass = "notsent";   break;
                case "sent":      statusLabel = "Sent";       statusClass = "sent";      break;
                case "inreview":  statusLabel = "In review";  statusClass = "inreview";  break;
                case "complete":  statusLabel = "Complete";   statusClass = "complete";  break;
                case "cancelled": statusLabel = "Cancelled";  statusClass = "cancelled"; break;
                default:          statusLabel = status;       statusClass = "";          break;
            }

            var sb = new StringBuilder();
            sb.AppendFormat("<span class=\"pill {0}\">{1}</span>", statusClass, LPPIHelper.Enc(statusLabel));

            if (active && due < DateTime.Today)
            {
                sb.Append(" <span class=\"pill overdue\">Overdue</span>");
            }
            else if (active && due <= DateTime.Today.AddDays(LPPIHelper.ReminderWindowDays))
            {
                sb.Append(" <span class=\"pill duesoon\">Due soon</span>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Status pill that takes a raw status value (used by Recent send-outs
        /// table where overdue augmentation is not needed).
        /// </summary>
        protected string RenderStatusPillFromStatus(object statusObj)
        {
            string status = statusObj == null || statusObj == DBNull.Value
                          ? "" : Convert.ToString(statusObj);
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
            return string.Format("<span class=\"pill {0}\">{1}</span>", cls, LPPIHelper.Enc(label));
        }

        /// <summary>
        /// Actions cell on the Open packages table. Every package gets an
        /// "Open review" link (admin QA) and a "Preview email" button.
        /// </summary>
        protected string RenderPackageActions(object packageIdObj, object tokenObj, object statusObj)
        {
            if (packageIdObj == null || packageIdObj == DBNull.Value) return "";

            int    packageId = Convert.ToInt32(packageIdObj);
            string status    = statusObj != null && statusObj != DBNull.Value
                               ? Convert.ToString(statusObj) : "";

            var sb = new StringBuilder();

            // Open review link — admin QA. Available for any package with a token.
            if (tokenObj != null && tokenObj != DBNull.Value)
            {
                string token   = LPPIHelper.Enc(tokenObj);
                string baseUrl = LPPIHelper.Enc(LPPIHelper.Setting("LPPI.BaseUrl", ""));
                sb.AppendFormat(
                    "<button type=\"button\" class=\"btn btn-sm btn-secondary\" " +
                    "onclick=\"openReviewLink('{0}','{1}');\">Open review &rarr;</button> ",
                    token, baseUrl);
            }

            // Preview email — always available.
            string emailType = string.Equals(status, "NotSent", StringComparison.OrdinalIgnoreCase)
                               ? "Initial" : "Reminder";
            sb.AppendFormat(
                "<button type=\"button\" class=\"btn btn-sm btn-ghost\" " +
                "onclick=\"openPreview({0},'{1}')\">Preview email</button>",
                packageId, emailType);

            return sb.ToString();
        }

        /// <summary>
        /// Actions column for rptRecent rows — same model: review link
        /// available for any package, preview always available.
        /// </summary>
        protected string RenderRecentActions(object packageIdObj, object tokenObj, object statusObj)
        {
            if (packageIdObj == null || packageIdObj == DBNull.Value) return "";

            int    packageId = Convert.ToInt32(packageIdObj);
            string status    = statusObj != null && statusObj != DBNull.Value
                               ? Convert.ToString(statusObj) : "";

            var sb = new StringBuilder();

            if (tokenObj != null && tokenObj != DBNull.Value)
            {
                string token   = LPPIHelper.Enc(tokenObj);
                string baseUrl = LPPIHelper.Enc(LPPIHelper.Setting("LPPI.BaseUrl", ""));
                sb.AppendFormat(
                    "<button type=\"button\" class=\"btn btn-sm btn-secondary\" " +
                    "onclick=\"openReviewLink('{0}','{1}');\">Open review &rarr;</button> ",
                    token, baseUrl);
            }

            string emailType = string.Equals(status, "NotSent", StringComparison.OrdinalIgnoreCase)
                               ? "Initial" : "Reminder";
            sb.AppendFormat(
                "<button type=\"button\" class=\"btn btn-sm btn-ghost\" " +
                "onclick=\"openPreview({0},'{1}')\">Preview email</button>",
                packageId, emailType);

            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Send / remind selected packages
        // -------------------------------------------------------------------

        protected void btnSend_Click(object sender, EventArgs e)
        {
            if (!LPPIEmail.ProductionMode)
            {
                ShowMessage("Email sending is disabled in UAT. Set LPPI.ProductionMode = true in web.config to enable.", "err");
                return;
            }

            DateTime due;
            if (!DateTime.TryParse(txtDueDate.Text, out due))
            {
                ShowMessage("A valid due date is required.", "err");
                return;
            }

            var selectedPackageIds = new List<int>();
            foreach (RepeaterItem item in rptPackages.Items)
            {
                var chk = item.FindControl("chkPick") as CheckBox;
                var hf  = item.FindControl("hfPackageId") as HiddenField;
                if (chk != null && hf != null && chk.Checked)
                {
                    int id;
                    if (int.TryParse(hf.Value, out id)) selectedPackageIds.Add(id);
                }
            }

            if (selectedPackageIds.Count == 0)
            {
                ShowMessage("Select at least one package to send.", "err");
                return;
            }

            int initialOk = 0, reminderOk = 0, failed = 0;
            var failNotes = new StringBuilder();

            foreach (int pid in selectedPackageIds)
            {
                // Look up current status to decide initial vs reminder, and to
                // apply the user-specified due date for first sends only.
                object statusObj = LPPIHelper.ExecuteScalar(
                    "SELECT Status FROM tblLPPI_ReviewPackages WHERE PackageID = @P",
                    LPPIHelper.P("@P", pid));
                string status = statusObj == null || statusObj == DBNull.Value
                              ? "" : Convert.ToString(statusObj);

                bool isNotSent = string.Equals(status, "NotSent", StringComparison.OrdinalIgnoreCase);

                if (isNotSent)
                {
                    // Apply the chosen due date before sending — first-send
                    // is the operator's last chance to set the due date.
                    LPPIHelper.ExecuteNonQuery(
                        "UPDATE tblLPPI_ReviewPackages SET DueDate = @D WHERE PackageID = @P AND Status = 'NotSent'",
                        LPPIHelper.P("@D", due),
                        LPPIHelper.P("@P", pid));

                    var res = LPPIEmail.SendInitial(pid);
                    if (res.Success) initialOk++;
                    else
                    {
                        failed++;
                        failNotes.Append("<li>Package #").Append(pid).Append(": ")
                                 .Append(LPPIHelper.Enc(res.ErrorMessage)).Append("</li>");
                    }
                }
                else if (string.Equals(status, "Sent", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(status, "InReview", StringComparison.OrdinalIgnoreCase))
                {
                    var res = LPPIEmail.SendReminder(pid);
                    if (res.Success) reminderOk++;
                    else
                    {
                        failed++;
                        failNotes.Append("<li>Package #").Append(pid).Append(": ")
                                 .Append(LPPIHelper.Enc(res.ErrorMessage)).Append("</li>");
                    }
                }
                else
                {
                    failed++;
                    failNotes.Append("<li>Package #").Append(pid)
                             .Append(": cannot send — status is ").Append(LPPIHelper.Enc(status)).Append(".</li>");
                }
            }

            string kind = failed == 0 ? "ok" : "warn";
            var msg = new StringBuilder();
            msg.Append(initialOk).Append(" initial email").Append(initialOk == 1 ? "" : "s").Append(" sent, ")
               .Append(reminderOk).Append(" reminder").Append(reminderOk == 1 ? "" : "s").Append(" sent.");
            if (failed > 0)
                msg.Append(" ").Append(failed).Append(" failure").Append(failed == 1 ? "" : "s").Append(".");
            if (failNotes.Length > 0)
                msg.Append("<ul>").Append(failNotes).Append("</ul>");

            ShowMessageRaw(msg.ToString(), kind);
            BindPackages();
            BindRecent();
        }

        // -------------------------------------------------------------------
        // Message helpers
        // -------------------------------------------------------------------

        private void ShowMessage(string text, string kind)
        {
            ShowMessageRaw(System.Web.HttpUtility.HtmlEncode(text), kind);
        }

        private void ShowMessageRaw(string html, string kind)
        {
            phMessage.Controls.Clear();
            phMessage.Controls.Add(new LiteralControl(
                "<div class=\"alert alert-" + kind + "\">" + html + "</div>"));
        }
    }
}
