using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    public partial class LPPI_SendOuts : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                txtDueDate.Text = DateTime.Today.AddDays(LPPIHelper.DefaultDueDays).ToString("yyyy-MM-dd");
                BindGroups();
                BindRecent();
            }
        }

        private void BindGroups()
        {
            const string sql = @"
                SELECT cm.CmID, cm.Program,
                       (SELECT COUNT(*) FROM tblLPPI_CapabilityManagerEmails e
                        WHERE e.CmID = cm.CmID AND e.IsActive = 1 AND e.IsCC = 0) AS ToCount,
                       ISNULL(STUFF((SELECT ', ' + e.Email
                                     FROM tblLPPI_CapabilityManagerEmails e
                                     WHERE e.CmID = cm.CmID AND e.IsActive = 1
                                     FOR XML PATH('')), 1, 2, ''), '') AS ToList,
                       (SELECT COUNT(*) FROM tblLPPI_Documents d
                        LEFT JOIN tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
                        WHERE d.CapabilityManagerProgram = cm.Program
                          AND r.ReasonCodeID IS NULL) AS UnreviewedDocs,
                       (SELECT TOP 1 p.PackageID FROM tblLPPI_ReviewPackages p
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
            const string sql = @"
                SELECT TOP (50) p.PackageID, cm.Program, p.CreatedDate, p.DueDate, p.Status,
                       (SELECT COUNT(*) FROM tblLPPI_ReviewPackageDocuments pd WHERE pd.PackageID = p.PackageID) AS TotalDocs,
                       (SELECT COUNT(*) FROM tblLPPI_ReviewPackageDocuments pd
                            INNER JOIN tblLPPI_Reviews r ON r.DocumentID = pd.DocumentID
                        WHERE pd.PackageID = p.PackageID AND r.ReasonCodeID IS NOT NULL) AS ReviewedDocs,
                       (SELECT MAX(el.SentDate) FROM tblLPPI_EmailLog el WHERE el.PackageID = p.PackageID) AS LastEmailDate
                FROM tblLPPI_ReviewPackages p
                INNER JOIN tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
                ORDER BY p.CreatedDate DESC";
            rptRecent.DataSource = LPPIHelper.ExecuteTable(sql);
            rptRecent.DataBind();
        }

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
                var hf = item.FindControl("hfCmId") as HiddenField;
                if (chk != null && hf != null && chk.Checked)
                {
                    int id;
                    if (int.TryParse(hf.Value, out id)) { selectedCmIds.Add(id); }
                }
            }

            if (selectedCmIds.Count == 0)
            {
                ShowMessage("Select at least one group to send.", "err");
                return;
            }

            var sb = new StringBuilder();
            int created = 0, emailed = 0, failed = 0;

            foreach (int cmId in selectedCmIds)
            {
                int packageId = CreatePackage(cmId, due);
                if (packageId == 0)
                {
                    sb.Append("<li>CmID ").Append(cmId).Append(": no outstanding documents, skipped.</li>");
                    continue;
                }
                created++;
                var result = LPPIEmail.SendInitial(packageId);
                if (result.Success) { emailed++; }
                else
                {
                    failed++;
                    sb.Append("<li>Package #").Append(packageId).Append(": ")
                      .Append(LPPIHelper.Enc(result.ErrorMessage)).Append("</li>");
                }
            }

            string kind = failed == 0 ? "ok" : "warn";
            var msg = new StringBuilder();
            msg.Append(created).Append(" package(s) created, ").Append(emailed).Append(" email(s) sent.");
            if (failed > 0) { msg.Append(" ").Append(failed).Append(" failure(s)."); }
            if (sb.Length > 0) { msg.Append("<ul class=\"bare\">").Append(sb).Append("</ul>"); }
            ShowMessageRaw(msg.ToString(), kind);

            BindGroups();
            BindRecent();
        }

        private int CreatePackage(int cmId, DateTime due)
        {
            // Collect unreviewed docs for this CM's program.
            const string selectDocs = @"
                SELECT d.DocumentID
                FROM tblLPPI_Documents d
                LEFT JOIN tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
                WHERE d.CapabilityManagerProgram =
                      (SELECT Program FROM tblLPPI_CapabilityManagers WHERE CmID = @cm)
                  AND r.ReasonCodeID IS NULL";
            DataTable dt = LPPIHelper.ExecuteTable(selectDocs, LPPIHelper.P("@cm", cmId));
            if (dt.Rows.Count == 0) { return 0; }

            string token = LPPIHelper.GenerateToken();
            object idObj = LPPIHelper.ExecuteScalar(@"
                INSERT INTO tblLPPI_ReviewPackages (CmID, Token, CreatedDate, CreatedBy, DueDate, Status)
                OUTPUT inserted.PackageID
                VALUES (@cm, @tok, SYSDATETIME(), @by, @due, 'Open')",
                LPPIHelper.P("@cm", cmId),
                LPPIHelper.P("@tok", token),
                LPPIHelper.P("@by", LPPIHelper.CurrentUserDisplayName()),
                LPPIHelper.P("@due", due));
            int packageId = Convert.ToInt32(idObj);

            foreach (DataRow r in dt.Rows)
            {
                LPPIHelper.ExecuteNonQuery(@"
                    INSERT INTO tblLPPI_ReviewPackageDocuments (PackageID, DocumentID)
                    VALUES (@p, @d)",
                    LPPIHelper.P("@p", packageId),
                    LPPIHelper.P("@d", r["DocumentID"]));
            }
            return packageId;
        }

        private void ShowMessage(string msg, string kind)
        {
            ShowMessageRaw(LPPIHelper.Enc(msg), kind);
        }

        private void ShowMessageRaw(string html, string kind)
        {
            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-").Append(kind).Append("\">").Append(html).Append("</div>");
            phMessage.Controls.Add(new LiteralControl(sb.ToString()));
        }
    }
}
