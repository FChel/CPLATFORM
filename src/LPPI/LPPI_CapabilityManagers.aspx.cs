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

                // Optional deep-link: ?cm=<id> opens that group's email panel
                // directly (used by the "Configure now" link on the dashboard
                // and by the unconfigured-programs warning on the Load page).
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
            //   CmID, Program, DisplayName, IsActive, ToList, CcList, OpenDocs
            // Note: the per-recipient IsActive flag on the emails table was
            // dropped — every recipient row is now considered active until
            // deleted.
            //
            // "OpenDocs" counts DISTINCT DocNoAccounting where the document
            // has no review on its first line (option-1 first-line-review).
            // The previous LEFT JOIN per DocumentID miscounted lines 2..N of
            // reviewed multi-line documents as "open" because only the first
            // line carries the review.
            const string sql = @"
                SELECT cm.CmID, cm.Program, cm.DisplayName, cm.IsActive,
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

        /// <summary>
        /// Handles the per-row "Manage emails" LinkButton on the main CM list.
        /// Wired via OnItemCommand="rptCms_ItemCommand" in the markup — without
        /// that wiring the click posts back but bubbles to nowhere.
        /// </summary>
        protected void rptCms_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            int cmId;
            if (!int.TryParse(Convert.ToString(e.CommandArgument), out cmId)) { return; }

            if (e.CommandName == "Manage")
            {
                ShowEmailsFor(cmId);
            }
        }

        /// <summary>
        /// On each row render, decide whether this is the CM whose emails the
        /// operator is currently editing. If so, apply the is-editing class to
        /// the row and surface a small "(editing)" flag next to the program.
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
        // Email management panel
        // -------------------------------------------------------------------

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

            // Re-bind the main list so the row-highlight logic in
            // rptCms_ItemDataBound sees the new hfCmId value.
            BindCms();
        }

        protected void btnCloseEmails_Click(object sender, EventArgs e)
        {
            pnlEmails.Visible = false;
            hfCmId.Value = "";
            txtEmail.Text = "";
            chkCc.Checked = false;
            BindCms();
        }

        /// <summary>
        /// Accepts a comma or semicolon separated list of addresses and inserts
        /// each individually. Skips blanks; reports duplicates and obviously
        /// invalid entries back to the operator rather than silently failing.
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
                // Basic format check — must contain '@' not at the start.
                if (addr.IndexOf('@') <= 0)
                {
                    invalid.Add(addr);
                    continue;
                }

                // Duplicate check — reject if the same address already exists
                // for this CM group.
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
                    INSERT INTO tblLPPI_CapabilityManagerEmails (CmID, Email, IsCC)
                    VALUES (@cm, @em, @cc)",
                    LPPIHelper.P("@cm", cmId),
                    LPPIHelper.P("@em", addr),
                    LPPIHelper.P("@cc", isCC ? 1 : 0));
                added.Add(addr);
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

        /// <summary>
        /// Handles Delete commands from the email repeater. Permanently removes
        /// the row from tblLPPI_CapabilityManagerEmails; the confirm() prompt
        /// is rendered client-side via OnClientClick on the Delete LinkButton.
        /// </summary>
        protected void rptEmails_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            int emailId;
            if (!int.TryParse(Convert.ToString(e.CommandArgument), out emailId)) { return; }

            if (e.CommandName == "Delete")
            {
                LPPIHelper.ExecuteNonQuery(
                    "DELETE FROM tblLPPI_CapabilityManagerEmails WHERE CmEmailID = @id",
                    LPPIHelper.P("@id", emailId));
                ShowMessage("Recipient deleted.", "ok");
            }
            else
            {
                return;
            }

            // Refresh both the email panel and the main list so the recipient
            // columns on the CM list reflect the change immediately.
            int cmId;
            if (int.TryParse(hfCmId.Value, out cmId)) { ShowEmailsFor(cmId); }
        }

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
