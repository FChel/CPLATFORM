using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    public partial class LPPI_SendOuts : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            BindUnconfigured();

            if (!IsPostBack)
            {
                txtDueDate.Text = DateTime.Today.AddDays(LPPIHelper.DefaultDueDays).ToString("yyyy-MM-dd");
                BindGroups();
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
                "Use the <em>Preview email</em> button to review the formatted email for any group. " +
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
        // Data binding
        // -------------------------------------------------------------------

        private void BindGroups()
        {
            // Columns required by rptGroups Eval():
            //   CmID, Program, ToCount, ToList, UnreviewedDocs, OpenPackageID
            const string sql = @"
                SELECT cm.CmID,
                       cm.Program,
                       (SELECT COUNT(*)
                          FROM tblLPPI_CapabilityManagerEmails e
                         WHERE e.CmID = cm.CmID AND e.IsCC = 0) AS ToCount,
                       ISNULL(STUFF((SELECT ', ' + e.Email
                                       FROM tblLPPI_CapabilityManagerEmails e
                                      WHERE e.CmID = cm.CmID AND e.IsCC = 0
                                      FOR XML PATH('')), 1, 2, ''), '') AS ToList,
                       (SELECT COUNT(DISTINCT d.DocNoAccounting)
                          FROM tblLPPI_Documents d
                          LEFT JOIN tblLPPI_Reviews r
                                 ON r.DocumentID = (SELECT MIN(d2.DocumentID)
                                                      FROM tblLPPI_Documents d2
                                                     WHERE d2.DocNoAccounting = d.DocNoAccounting)
                         WHERE d.CapabilityManagerProgram = cm.Program
                           AND r.ReasonCodeID IS NULL) AS UnreviewedDocs,
                       (SELECT TOP 1 p.PackageID
                          FROM tblLPPI_ReviewPackages p
                         WHERE p.CmID = cm.CmID AND p.Status = 'Open'
                         ORDER BY p.CreatedDate DESC) AS OpenPackageID
                  FROM tblLPPI_CapabilityManagers cm
                 WHERE cm.IsActive = 1
                 ORDER BY cm.Program";
            rptGroups.DataSource = LPPIHelper.ExecuteTable(sql);
            rptGroups.DataBind();
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
                 ORDER BY p.CreatedDate DESC";
            rptRecent.DataSource = LPPIHelper.ExecuteTable(sql);
            rptRecent.DataBind();
        }

        // -------------------------------------------------------------------
        // Render helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Actions column for rptRecent rows. Open packages get review link
        /// buttons plus a Preview email button. All packages get Preview email.
        /// </summary>
        protected string RenderRecentActions(object packageIdObj, object tokenObj, object statusObj)
        {
            if (packageIdObj == null || packageIdObj == DBNull.Value) return "";

            int    packageId = Convert.ToInt32(packageIdObj);
            string status    = statusObj != null && statusObj != DBNull.Value
                               ? Convert.ToString(statusObj) : "";

            var sb = new StringBuilder();

            // Review link buttons — open packages only
            if (string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)
                && tokenObj != null && tokenObj != DBNull.Value)
            {
                string token   = LPPIHelper.Enc(tokenObj);
                string baseUrl = LPPIHelper.Enc(LPPIHelper.Setting("LPPI.BaseUrl", ""));
                sb.AppendFormat(
                    "<button type=\"button\" class=\"btn btn-sm btn-secondary\" " +
                    "onclick=\"openReviewLink('{0}','{1}');\">Open review &rarr;</button> ",
                    token, baseUrl);
            }

            // Preview email — all packages
            // Use "Reminder" type label for closed packages so the preview
            // reflects a more complete view; Initial for Open.
            string emailType = string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)
                               ? "Initial" : "Reminder";
            sb.AppendFormat(
                "<button type=\"button\" class=\"btn btn-sm btn-ghost\" " +
                "onclick=\"openPreview({0},'{1}')\">Preview email</button>",
                packageId, emailType);

            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Send selected groups
        // -------------------------------------------------------------------

        protected void btnSend_Click(object sender, EventArgs e)
        {
            // Belt-and-braces — the button should already be disabled in the
            // markup when ProductionMode is false, but guard here too.
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

            var selectedCmIds = new List<int>();
            foreach (RepeaterItem item in rptGroups.Items)
            {
                var chk = item.FindControl("chkPick") as CheckBox;
                var hf  = item.FindControl("hfCmId") as HiddenField;
                if (chk != null && hf != null && chk.Checked)
                {
                    int id;
                    if (int.TryParse(hf.Value, out id)) selectedCmIds.Add(id);
                }
            }

            if (selectedCmIds.Count == 0)
            {
                ShowMessage("Select at least one group to send.", "err");
                return;
            }

            var failNotes    = new StringBuilder();
            var sentPackages = new List<Tuple<string, string>>();
            int created = 0, emailed = 0, failed = 0;

            foreach (int cmId in selectedCmIds)
            {
                string packageToken;
                int packageId = CreatePackage(cmId, due, out packageToken);
                if (packageId == 0)
                {
                    failNotes.Append("<li>CmID ").Append(cmId)
                             .Append(": no outstanding documents — skipped.</li>");
                    continue;
                }
                created++;

                object progObj = LPPIHelper.ExecuteScalar(
                    "SELECT Program FROM tblLPPI_CapabilityManagers WHERE CmID = @id",
                    LPPIHelper.P("@id", cmId));
                string progName = progObj != null && progObj != DBNull.Value
                    ? Convert.ToString(progObj) : "CmID " + cmId;
                sentPackages.Add(Tuple.Create(progName, packageToken));

                var result = LPPIEmail.SendInitial(packageId);
                if (result.Success)
                {
                    emailed++;
                }
                else
                {
                    failed++;
                    failNotes.Append("<li>Package #").Append(packageId).Append(": ")
                             .Append(LPPIHelper.Enc(result.ErrorMessage)).Append("</li>");
                }
            }

            string kind = failed == 0 ? "ok" : "warn";
            var msg = new StringBuilder();
            msg.Append(created).Append(" package").Append(created == 1 ? "" : "s").Append(" created, ")
               .Append(emailed).Append(" email").Append(emailed == 1 ? "" : "s").Append(" sent.");
            if (failed > 0)
                msg.Append(" ").Append(failed).Append(" failure").Append(failed == 1 ? "" : "s").Append(".");
            if (failNotes.Length > 0)
                msg.Append("<ul>").Append(failNotes).Append("</ul>");

            ShowMessageRaw(msg.ToString(), kind);
            BindGroups();
            BindRecent();
        }

        // -------------------------------------------------------------------
        // Package creation
        // -------------------------------------------------------------------

        private int CreatePackage(int cmId, DateTime due, out string token)
        {
            token = "";

            object count = LPPIHelper.ExecuteScalar(@"
                SELECT COUNT(*)
                  FROM (
                      SELECT MIN(d.DocumentID) AS FirstLineDocumentID
                        FROM tblLPPI_Documents d
                        LEFT JOIN tblLPPI_Reviews r
                               ON r.DocumentID = (SELECT MIN(d2.DocumentID)
                                                    FROM tblLPPI_Documents d2
                                                   WHERE d2.DocNoAccounting = d.DocNoAccounting)
                       WHERE d.CapabilityManagerProgram =
                             (SELECT Program FROM tblLPPI_CapabilityManagers WHERE CmID = @cm)
                         AND r.ReasonCodeID IS NULL
                       GROUP BY d.DocNoAccounting
                  ) x",
                LPPIHelper.P("@cm", cmId));

            if (count == null || Convert.ToInt32(count) == 0) return 0;

            token = LPPIHelper.GenerateToken();
            object idObj = LPPIHelper.ExecuteScalar(@"
                INSERT INTO tblLPPI_ReviewPackages
                    (CmID, Token, CreatedDate, CreatedBy, DueDate, Status)
                OUTPUT inserted.PackageID
                VALUES (@cm, @tok, SYSDATETIME(), @by, @due, 'Open')",
                LPPIHelper.P("@cm",  cmId),
                LPPIHelper.P("@tok", token),
                LPPIHelper.P("@by",  LPPIHelper.CurrentUserDisplayName()),
                LPPIHelper.P("@due", due.ToString("yyyy-MM-dd HH:mm:ss.000")));
            int packageId = Convert.ToInt32(idObj);

            LPPIHelper.ExecuteNonQuery(@"
                INSERT INTO tblLPPI_ReviewPackageDocuments (PackageID, DocumentID)
                SELECT @pkg, MIN(d.DocumentID)
                  FROM tblLPPI_Documents d
                  LEFT JOIN tblLPPI_Reviews r
                         ON r.DocumentID = (SELECT MIN(d2.DocumentID)
                                              FROM tblLPPI_Documents d2
                                             WHERE d2.DocNoAccounting = d.DocNoAccounting)
                 WHERE d.CapabilityManagerProgram =
                       (SELECT Program FROM tblLPPI_CapabilityManagers WHERE CmID = @cm)
                   AND r.ReasonCodeID IS NULL
                 GROUP BY d.DocNoAccounting",
                LPPIHelper.P("@pkg", packageId),
                LPPIHelper.P("@cm",  cmId));

            return packageId;
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
