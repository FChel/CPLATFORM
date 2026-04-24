using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Web.UI;
using System.Web.UI.HtmlControls;
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

                // Optional deep-link: ?cm=<id> opens that group's Manage panel
                // directly (used by "Configure now" links from the Load page
                // and send-outs warning banner).
                string cmArg = Request.QueryString["cm"];
                int cmId;
                if (!string.IsNullOrEmpty(cmArg) && int.TryParse(cmArg, out cmId))
                {
                    ShowEmailsFor(cmId);
                }
            }
        }

        // -------------------------------------------------------------------
        // Main CM group list
        // -------------------------------------------------------------------

        private void BindCms()
        {
            // Columns consumed by rptCms Eval() bindings:
            //   CmID, Program, DisplayName, ToList, CcList, OpenDocs
            // IsActive is not surfaced in the list — all groups are BODS-driven
            // and the UI no longer exposes enable/disable.
            const string sql = @"
                SELECT cm.CmID, cm.Program, cm.DisplayName,
                       ISNULL(STUFF((SELECT ', ' + e.Email
                                       FROM tblLPPI_CapabilityManagerEmails e
                                      WHERE e.CmID = cm.CmID AND e.IsCC = 0
                                      FOR XML PATH('')), 1, 2, ''), '') AS ToList,
                       ISNULL(STUFF((SELECT ', ' + e.Email
                                       FROM tblLPPI_CapabilityManagerEmails e
                                      WHERE e.CmID = cm.CmID AND e.IsCC = 1
                                      FOR XML PATH('')), 1, 2, ''), '') AS CcList,
                       (SELECT COUNT(DISTINCT d.DocNoAccounting)
                          FROM tblLPPI_Documents d
                          LEFT JOIN tblLPPI_Reviews r
                                 ON r.DocumentID = (SELECT MIN(d2.DocumentID)
                                                      FROM tblLPPI_Documents d2
                                                     WHERE d2.DocNoAccounting = d.DocNoAccounting)
                         WHERE d.CapabilityManagerProgram = cm.Program
                           AND r.ReasonCodeID IS NULL) AS OpenDocs
                FROM tblLPPI_CapabilityManagers cm
                ORDER BY cm.Program";
            rptCms.DataSource = LPPIHelper.ExecuteTable(sql);
            rptCms.DataBind();
        }

        // -------------------------------------------------------------------
        // rptCms event handlers
        // -------------------------------------------------------------------

        /// <summary>
        /// Handles the per-row "Manage" LinkButton. Opens the Manage panel for
        /// the selected CM group.
        /// </summary>
        protected void rptCms_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            int cmId;
            if (!int.TryParse(Convert.ToString(e.CommandArgument), out cmId)) return;

            if (e.CommandName == "Manage")
            {
                ShowEmailsFor(cmId);
            }
        }

        /// <summary>
        /// Applies the is-editing row highlight to the CM currently open in the
        /// Manage panel.
        /// </summary>
        protected void rptCms_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            if (e.Item.ItemType != ListItemType.Item &&
                e.Item.ItemType != ListItemType.AlternatingItem)
                return;

            int editingCmId;
            if (!pnlEmails.Visible || !int.TryParse(hfCmId.Value, out editingCmId))
                return;

            var row = e.Item.DataItem as DataRowView;
            if (row == null) return;

            int thisCmId = Convert.ToInt32(row["CmID"]);
            if (thisCmId != editingCmId) return;

            var tr = e.Item.FindControl("trRow") as HtmlTableRow;
            if (tr != null) tr.Attributes["class"] = "is-editing";

            var flag = e.Item.FindControl("litEditFlag") as Literal;
            if (flag != null) flag.Text = "<span class=\"edit-flag\">(editing)</span>";
        }

        // -------------------------------------------------------------------
        // Manage panel — open / close
        // -------------------------------------------------------------------

        private void ShowEmailsFor(int cmId)
        {
            pnlEmails.Visible = true;
            hfCmId.Value      = cmId.ToString();

            DataTable cm = LPPIHelper.ExecuteTable(
                "SELECT Program, ISNULL(DisplayName, '') AS DisplayName FROM tblLPPI_CapabilityManagers WHERE CmID = @id",
                LPPIHelper.P("@id", cmId));

            if (cm.Rows.Count == 1)
            {
                litCmProgram.Text     = LPPIHelper.Enc(cm.Rows[0]["Program"]);
                litCmDisplayName.Text = LPPIHelper.Enc(cm.Rows[0]["DisplayName"]);
                txtDisplayName.Text   = Convert.ToString(cm.Rows[0]["DisplayName"]);
            }

            rptEmails.DataSource = LPPIHelper.GetCmEmails(cmId);
            rptEmails.DataBind();

            // Re-bind the main list so the row-highlight logic sees the new hfCmId.
            BindCms();
        }

        protected void btnCloseEmails_Click(object sender, EventArgs e)
        {
            pnlEmails.Visible = false;
            hfCmId.Value      = "";
            txtDisplayName.Text = "";
            BindCms();
        }

        // -------------------------------------------------------------------
        // Display-name save
        // -------------------------------------------------------------------

        protected void btnSaveDisplayName_Click(object sender, EventArgs e)
        {
            int cmId;
            if (!int.TryParse(hfCmId.Value, out cmId))
            {
                ShowMessage("No group selected.", "err");
                return;
            }

            string displayName = (txtDisplayName.Text ?? "").Trim();

            LPPIHelper.ExecuteNonQuery(
                "UPDATE tblLPPI_CapabilityManagers SET DisplayName = @dn, ModifiedDate = SYSDATETIME() WHERE CmID = @id",
                LPPIHelper.P("@dn", displayName.Length > 0 ? (object)displayName : DBNull.Value),
                LPPIHelper.P("@id", cmId));

            ShowMessage("Display name saved.", "ok");
            ShowEmailsFor(cmId);
        }

        // -------------------------------------------------------------------
        // Add / delete recipients
        // -------------------------------------------------------------------

        protected void btnAddEmail_Click(object sender, EventArgs e)
        {
            int cmId;
            if (!int.TryParse(hfCmId.Value, out cmId))
            {
                ShowMessage("No group selected.", "err");
                return;
            }

            string raw = (txtEmail.Text ?? "").Trim();
            if (raw.Length == 0)
            {
                ShowMessage("Please enter at least one email address.", "err");
                return;
            }

            bool isCC = chkCc.Checked;

            var addresses = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var added    = new List<string>();
            var existing = new List<string>();
            var invalid  = new List<string>();

            foreach (var addr in addresses)
            {
                var a = addr.Trim();
                if (a.Length == 0) continue;

                // Lightweight format check
                int at = a.IndexOf('@');
                if (at <= 0 || at >= a.Length - 2)
                {
                    invalid.Add(a);
                    continue;
                }

                // Duplicate check
                object dup = LPPIHelper.ExecuteScalar(
                    "SELECT COUNT(*) FROM tblLPPI_CapabilityManagerEmails WHERE CmID = @id AND Email = @e",
                    LPPIHelper.P("@id", cmId),
                    LPPIHelper.P("@e",  a));
                if (Convert.ToInt32(dup) > 0)
                {
                    existing.Add(a);
                    continue;
                }

                LPPIHelper.ExecuteNonQuery(
                    "INSERT INTO tblLPPI_CapabilityManagerEmails (CmID, Email, IsCC) VALUES (@id, @e, @cc)",
                    LPPIHelper.P("@id", cmId),
                    LPPIHelper.P("@e",  a),
                    LPPIHelper.P("@cc", isCC ? 1 : 0));
                added.Add(a);
            }

            txtEmail.Text = "";
            chkCc.Checked = false;

            if (added.Count > 0)
                ShowMessage(added.Count + " recipient" + (added.Count == 1 ? "" : "s") + " added.", "ok");
            if (existing.Count > 0)
                ShowMessage("Already configured (skipped): " + LPPIHelper.Enc(string.Join(", ", existing)), "warn");
            if (invalid.Count > 0)
                ShowMessage("Invalid address" + (invalid.Count == 1 ? "" : "es") + " (skipped): " + LPPIHelper.Enc(string.Join(", ", invalid)), "err");

            ShowEmailsFor(cmId);
        }

        protected void rptEmails_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            int emailId;
            if (!int.TryParse(Convert.ToString(e.CommandArgument), out emailId)) return;

            if (e.CommandName == "Delete")
            {
                LPPIHelper.ExecuteNonQuery(
                    "DELETE FROM tblLPPI_CapabilityManagerEmails WHERE CmEmailID = @id",
                    LPPIHelper.P("@id", emailId));
                ShowMessage("Recipient deleted.", "ok");
            }

            int cmId;
            if (int.TryParse(hfCmId.Value, out cmId)) ShowEmailsFor(cmId);
        }

        // -------------------------------------------------------------------
        // Shared helpers
        // -------------------------------------------------------------------

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
