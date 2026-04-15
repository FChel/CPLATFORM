using System;
using System.Data;
using System.Data.OleDb;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    public partial class LPPI_CapabilityManagers : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                BindCms();
                string cmArg = Request.QueryString["cm"];
                int cmId;
                if (!string.IsNullOrEmpty(cmArg) && int.TryParse(cmArg, out cmId))
                {
                    ShowEmailsFor(cmId);
                }
            }
        }

        private void BindCms()
        {
            const string sql = @"
                SELECT cm.CmID, cm.Program, cm.DisplayName, cm.IsActive,
                       ISNULL(STUFF((SELECT ', ' + e.Email
                                     FROM tblLPPI_CapabilityManagerEmails e
                                     WHERE e.CmID = cm.CmID AND e.IsCC = 0 AND e.IsActive = 1
                                     FOR XML PATH('')), 1, 2, ''), '') AS ToList,
                       ISNULL(STUFF((SELECT ', ' + e.Email
                                     FROM tblLPPI_CapabilityManagerEmails e
                                     WHERE e.CmID = cm.CmID AND e.IsCC = 1 AND e.IsActive = 1
                                     FOR XML PATH('')), 1, 2, ''), '') AS CcList,
                       (SELECT COUNT(*) FROM tblLPPI_Documents d
                        LEFT JOIN tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
                        WHERE d.CapabilityManagerProgram = cm.Program
                          AND r.ReasonCodeID IS NULL) AS OpenDocs
                FROM tblLPPI_CapabilityManagers cm
                ORDER BY cm.Program";
            rptCms.DataSource = LPPIHelper.ExecuteTable(sql);
            rptCms.DataBind();
        }

        protected void btnSaveCm_Click(object sender, EventArgs e)
        {
            string program = (txtProgram.Text ?? "").Trim();
            if (program.Length == 0)
            {
                ShowMessage("Program is required.", "err");
                return;
            }
            LPPIHelper.UpsertCapabilityManager(program, (txtDisplayName.Text ?? "").Trim(), chkActive.Checked);
            txtProgram.Text = "";
            txtDisplayName.Text = "";
            chkActive.Checked = true;
            ShowMessage("Group saved.", "ok");
            BindCms();
        }

        protected void rptCms_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            int cmId = Convert.ToInt32(e.CommandArgument);
            if (e.CommandName == "Manage")
            {
                ShowEmailsFor(cmId);
            }
            else if (e.CommandName == "Toggle")
            {
                LPPIHelper.ExecuteNonQuery(
                    "UPDATE tblLPPI_CapabilityManagers SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END, ModifiedDate = SYSDATETIME() WHERE CmID = @id",
                    LPPIHelper.P("@id", cmId));
                ShowMessage("Group updated.", "ok");
                BindCms();
            }
        }

        private void ShowEmailsFor(int cmId)
        {
            pnlEmails.Visible = true;
            hfCmId.Value = cmId.ToString();

            object nameObj = LPPIHelper.ExecuteScalar(
                "SELECT Program + ISNULL(' — ' + NULLIF(DisplayName, ''), '') FROM tblLPPI_CapabilityManagers WHERE CmID = @id",
                LPPIHelper.P("@id", cmId));
            litCmName.Text = LPPIHelper.Enc(nameObj);

            rptEmails.DataSource = LPPIHelper.GetCmEmails(cmId);
            rptEmails.DataBind();
        }

        protected void btnAddEmail_Click(object sender, EventArgs e)
        {
            int cmId;
            if (!int.TryParse(hfCmId.Value, out cmId)) { return; }

            string email = (txtEmail.Text ?? "").Trim();
            if (email.Length == 0 || email.IndexOf('@') <= 0)
            {
                ShowMessage("A valid email address is required.", "err");
                ShowEmailsFor(cmId);
                return;
            }

            LPPIHelper.ExecuteNonQuery(@"
                INSERT INTO tblLPPI_CapabilityManagerEmails (CmID, Email, IsCC, IsActive)
                VALUES (@cm, @em, @cc, 1)",
                LPPIHelper.P("@cm", cmId),
                LPPIHelper.P("@em", email),
                LPPIHelper.P("@cc", chkCc.Checked ? 1 : 0));

            txtEmail.Text = "";
            chkCc.Checked = false;
            ShowMessage("Recipient added.", "ok");
            BindCms();
            ShowEmailsFor(cmId);
        }

        protected void rptEmails_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            int emailId = Convert.ToInt32(e.CommandArgument);
            if (e.CommandName == "Toggle")
            {
                LPPIHelper.ExecuteNonQuery(
                    "UPDATE tblLPPI_CapabilityManagerEmails SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END WHERE CmEmailID = @id",
                    LPPIHelper.P("@id", emailId));
            }
            int cmId;
            if (int.TryParse(hfCmId.Value, out cmId)) { ShowEmailsFor(cmId); }
            BindCms();
        }

        private void ShowMessage(string msg, string kind)
        {
            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-").Append(kind).Append("\">")
              .Append(LPPIHelper.Enc(msg))
              .Append("</div>");
            phMessage.Controls.Add(new LiteralControl(sb.ToString()));
        }
    }
}
