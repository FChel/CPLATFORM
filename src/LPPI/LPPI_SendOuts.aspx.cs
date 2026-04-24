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
            //
            // Token is included so Open packages can render direct review links.
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
        // Render helper — Open-only link buttons for rptRecent rows
        // -------------------------------------------------------------------

        /// <summary>
        /// Renders two buttons for an open package: "Open review" (new tab)
        /// and "Copy link" (clipboard). Only called when Status = 'Open'.
        /// </summary>
        protected string RenderLinkButtons(object tokenObj)
        {
            if (tokenObj == null || tokenObj == DBNull.Value) return "";
            string token   = LPPIHelper.Enc(tokenObj);
            string baseUrl = LPPIHelper.Enc(LPPIHelper.Setting("LPPI.BaseUrl", ""));
            return string.Format(
                "<button type=\"button\" class=\"btn btn-sm btn-secondary\" " +
                "onclick=\"openReviewLink('{0}','{1}');\">Open review &rarr;</button> " +
                "<button type=\"button\" class=\"btn btn-sm btn-ghost\" " +
                "onclick=\"copyReviewLink('{0}','{1}');\">Copy link</button>",
                token, baseUrl);
        }

        // -------------------------------------------------------------------
        // Send selected groups
        // -------------------------------------------------------------------

        protected void btnSend_Click(object sender, EventArgs e)
        {
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
            var mailtoLinks  = new List<string>();
            // Capture (program, token) pairs for the success message links
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

                // Capture program name for the success message
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
                    if (result.UseClientEmail && !string.IsNullOrEmpty(result.MailtoLink))
                        mailtoLinks.Add(result.MailtoLink);
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
            msg.Append(created).Append(" package(s) created, ")
               .Append(emailed).Append(" email(s) queued.");
            if (failed > 0)
                msg.Append(" ").Append(failed).Append(" failure(s).");
            if (failNotes.Length > 0)
                msg.Append("<ul class=\"bare\">").Append(failNotes).Append("</ul>");

            // Reviewer links in the success banner
            if (sentPackages.Count > 0)
            {
                string baseUrl = LPPIHelper.Setting("LPPI.BaseUrl", "").TrimEnd('/');
                msg.Append("<div style=\"margin-top:10px;\">");
                msg.Append("<strong>Reviewer links</strong> — open or copy to share directly:</div>");
                msg.Append("<ul style=\"margin:6px 0 0;padding:0;list-style:none;\">");
                foreach (var pkg in sentPackages)
                {
                    string reviewUrl = baseUrl + "/LPPI/LPPI_Review.aspx?t="
                        + Uri.EscapeDataString(pkg.Item2);
                    string encToken   = LPPIHelper.Enc(pkg.Item2);
                    string encBaseUrl = LPPIHelper.Enc(LPPIHelper.Setting("LPPI.BaseUrl", ""));
                    msg.AppendFormat(
                        "<li style=\"margin:4px 0;display:flex;align-items:center;gap:8px;\">" +
                        "<strong>{0}</strong>" +
                        "<a href=\"{1}\" target=\"_blank\" class=\"btn btn-sm btn-secondary\" rel=\"noopener\">Open review &rarr;</a>" +
                        "<button type=\"button\" class=\"btn btn-sm btn-ghost\" " +
                        "onclick=\"copyReviewLink('{2}','{3}');\">Copy link</button>" +
                        "</li>",
                        LPPIHelper.Enc(pkg.Item1),
                        LPPIHelper.Enc(reviewUrl),
                        encToken,
                        encBaseUrl);
                }
                msg.Append("</ul>");
            }

            if (mailtoLinks.Count > 0)
            {
                msg.Append("<p class=\"muted\" style=\"margin-top:8px;\">")
                   .Append("<strong>Client email mode is active.</strong> ")
                   .Append("Your email client will open with ")
                   .Append(mailtoLinks.Count == 1 ? "a pre-filled message"
                                                  : mailtoLinks.Count + " pre-filled messages")
                   .Append(" — please review and send each one manually.</p>");

                var js = new StringBuilder();
                foreach (var link in mailtoLinks)
                    js.Append("window.open('")
                      .Append(link.Replace("\\", "\\\\").Replace("'", "\\'"))
                      .Append("', '_blank');");
                ScriptManager.RegisterStartupScript(this, GetType(), "lppiMailto",
                    js.ToString(), true);
            }

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
                LPPIHelper.P("@due", due));
            int packageId = Convert.ToInt32(idObj);

            LPPIHelper.ExecuteNonQuery(@"
                INSERT INTO tblLPPI_ReviewPackageDocuments (PackageID, DocumentID)
                SELECT @p, MIN(d.DocumentID)
                  FROM tblLPPI_Documents d
                  LEFT JOIN tblLPPI_Reviews r
                         ON r.DocumentID = (SELECT MIN(d2.DocumentID)
                                              FROM tblLPPI_Documents d2
                                             WHERE d2.DocNoAccounting = d.DocNoAccounting)
                 WHERE d.CapabilityManagerProgram =
                       (SELECT Program FROM tblLPPI_CapabilityManagers WHERE CmID = @cm)
                   AND r.ReasonCodeID IS NULL
                 GROUP BY d.DocNoAccounting",
                LPPIHelper.P("@p",  packageId),
                LPPIHelper.P("@cm", cmId));

            return packageId;
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private void ShowMessage(string msg, string kind)
        {
            ShowMessageRaw(LPPIHelper.Enc(msg), kind);
        }

        private void ShowMessageRaw(string html, string kind)
        {
            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-").Append(kind).Append("\">")
              .Append(html).Append("</div>");
            phMessage.Controls.Add(new LiteralControl(sb.ToString()));
        }
    }
}
