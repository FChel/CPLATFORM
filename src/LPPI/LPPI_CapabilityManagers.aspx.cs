using System;
using System.Collections.Generic;
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

        /// <summary>
        /// TO-DO #2 — accept a comma-separated list of addresses and insert
        /// each one individually. Skips blank entries and entries that already
        /// exist for this CM group (active or not), reporting both back to the
        /// operator rather than silently failing.
        /// </summary>
        protected void btnAddEmail_Click(object sender, EventArgs e)
        {
            int cmId;
            if (!int.TryParse(hfCmId.Value, out cmId)) { return; }

            string raw = (txtEmail.Text ?? "").Trim();
            if (raw.Length == 0)
            {
                ShowMessage("Please enter at least one email address.", "err");
                ShowEmailsFor(cmId);
                return;
            }

            // Split on comma or semicolon, trim whitespace, drop blanks.
            var addresses = new List<string>();
            foreach (var part in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var addr = part.Trim();
                if (addr.Length > 0)
                    addresses.Add(addr);
            }

            bool isCC = chkCc.Checked;
            var added    = new List<string>();
            var invalid  = new List<string>();
            var existing = new List<string>();

            foreach (var addr in addresses)
            {
                // Basic format check.
                if (addr.IndexOf('@') <= 0)
                {
                    invalid.Add(addr);
                    continue;
                }

                // Duplicate check — reject if the same address already exists for
                // this CM group (regardless of active/CC status).
                object dup = LPPIHelper.ExecuteScalar(
                    "SELECT 1 FROM tblLPPI_CapabilityManagerEmails WHERE CmID = @cm AND Email = @em",
                    LPPIHelper.P("@cm", cmId),
                    LPPIHelper.P("@em", addr));
                if (dup != null)
                {
                    existing.Add(addr);
                    continue;
                }

                LPPIHelper.ExecuteNonQuery(@"
                    INSERT INTO tblLPPI_CapabilityManagerEmails (CmID, Email, IsCC, IsActive)
                    VALUES (@cm, @em, @cc, 1)",
                    LPPIHelper.P("@cm", cmId),
                    LPPIHelper.P("@em", addr),
                    LPPIHelper.P("@cc", isCC ? 1 : 0));
                added.Add(addr);
            }

            txtEmail.Text = "";
            chkCc.Checked = false;

            // Report outcomes.
            if (added.Count > 0)
                ShowMessage(added.Count + " recipient" + (added.Count == 1 ? "" : "s") + " added.", "ok");
            if (existing.Count > 0)
                ShowMessage("Already configured (skipped): " + LPPIHelper.Enc(string.Join(", ", existing)), "warn");
            if (invalid.Count > 0)
                ShowMessage("Invalid address" + (invalid.Count == 1 ? "" : "es") + " (skipped): " + LPPIHelper.Enc(string.Join(", ", invalid)), "err");

            BindCms();
            ShowEmailsFor(cmId);
        }

        /// <summary>
        /// Handles Toggle (enable/disable) and Delete commands from the email repeater.
        /// TO-DO #3 — Delete permanently removes the row.
        /// </summary>
        protected void rptEmails_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            int emailId = Convert.ToInt32(e.CommandArgument);

            if (e.CommandName == "Toggle")
            {
                LPPIHelper.ExecuteNonQuery(
                    "UPDATE tblLPPI_CapabilityManagerEmails SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END WHERE CmEmailID = @id",
                    LPPIHelper.P("@id", emailId));
            }
            else if (e.CommandName == "Delete")
            {
                // TO-DO #3: hard-delete the email address record.
                LPPIHelper.ExecuteNonQuery(
                    "DELETE FROM tblLPPI_CapabilityManagerEmails WHERE CmEmailID = @id",
                    LPPIHelper.P("@id", emailId));
                ShowMessage("Recipient deleted.", "ok");
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
