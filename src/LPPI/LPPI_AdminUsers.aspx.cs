/*
 * LPPI_AdminUsers.aspx.cs
 *
 * Access model:
 *   Reviewer page  = token-based (no Windows identity check).
 *   Everything else = gated by tblLPPI_AdminUsers.
 *   Admin           = full access to all LPPI admin pages and actions.
 *   Non-admin       = LPPI_Review.aspx only (via token link received by email).
 *
 * This page manages the tblLPPI_AdminUsers table.
 * Deactivation (IsActive = 0) is used in preference to hard delete so that
 * the audit trail is preserved.
 */

using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Web;
using System.Web.UI;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    public partial class LPPI_AdminUsers : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                BindUsers();
            }
        }

        // -------------------------------------------------------------------
        // Bind user list
        // Columns consumed by rptUsers Eval() bindings:
        //   AdminUserID, UserId, DisplayName, Email, IsActive, CreatedDate
        // -------------------------------------------------------------------

        private void BindUsers()
        {
            const string sql = @"
                SELECT AdminUserID,
                       UserId,
                       ISNULL(DisplayName, '') AS DisplayName,
                       ISNULL(Email, '')       AS Email,
                       IsActive,
                       CreatedDate
                FROM dbo.tblLPPI_AdminUsers
                ORDER BY IsActive DESC, UserId";

            rptUsers.DataSource = LPPIHelper.ExecuteTable(sql);
            rptUsers.DataBind();
        }

        // -------------------------------------------------------------------
        // Add user(s)
        // Accepts comma-separated usernames. Loose validation — reject
        // empty/whitespace only; do not require DOMAIN\ prefix.
        // DisplayName and Email are applied only when a single username is
        // supplied (ambiguous which to apply when multiple are given).
        // -------------------------------------------------------------------

        protected void btnAdd_Click(object sender, EventArgs e)
        {
            string raw = (txtAddUserIds.Text ?? "").Trim();
            if (string.IsNullOrEmpty(raw))
            {
                ShowMessage("Please enter at least one Windows username.", "err");
                return;
            }

            // Split on comma or semicolon.
            var parts = new List<string>();
            foreach (var p in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string u = p.Trim();
                if (!string.IsNullOrEmpty(u)) parts.Add(u);
            }

            if (parts.Count == 0)
            {
                ShowMessage("No valid usernames found.", "err");
                return;
            }

            string displayName = parts.Count == 1 ? (txtAddDisplayName.Text ?? "").Trim() : null;
            string email       = parts.Count == 1 ? (txtAddEmail.Text ?? "").Trim() : null;
            string createdBy   = LPPIHelper.CurrentUserDisplayName();

            int added   = 0;
            int skipped = 0;

            foreach (string userId in parts)
            {
                // Check for existing row (case-insensitive).
                object exists = LPPIHelper.ExecuteScalar(
                    "SELECT COUNT(1) FROM dbo.tblLPPI_AdminUsers WHERE LOWER(UserId) = LOWER(@u)",
                    LPPIHelper.P("@u", userId));

                if (exists != null && Convert.ToInt32(exists) > 0)
                {
                    skipped++;
                    continue;
                }

                LPPIHelper.ExecuteNonQuery(@"
                    INSERT INTO dbo.tblLPPI_AdminUsers
                        (UserId, DisplayName, Email, IsActive, CreatedBy)
                    VALUES (@u, @dn, @em, 1, @cb)",
                    LPPIHelper.P("@u",  userId),
                    LPPIHelper.P("@dn", string.IsNullOrEmpty(displayName) ? (object)DBNull.Value : displayName),
                    LPPIHelper.P("@em", string.IsNullOrEmpty(email)       ? (object)DBNull.Value : email),
                    LPPIHelper.P("@cb", createdBy));

                added++;
            }

            var msg = new StringBuilder();
            if (added   > 0) msg.AppendFormat("{0} user{1} added. ", added, added == 1 ? "" : "s");
            if (skipped > 0) msg.AppendFormat("{0} already existed and {1} skipped.",
                skipped, skipped == 1 ? "was" : "were");

            ShowMessage(msg.ToString().Trim(), added > 0 ? "ok" : "warn");

            txtAddUserIds.Text    = "";
            txtAddDisplayName.Text = "";
            txtAddEmail.Text       = "";

            BindUsers();
        }

        // -------------------------------------------------------------------
        // rptUsers — ItemCommand (Edit / Toggle)
        // -------------------------------------------------------------------

        protected void rptUsers_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            int id;
            if (!int.TryParse(Convert.ToString(e.CommandArgument), out id)) return;

            if (e.CommandName == "Edit")
            {
                DataTable dt = LPPIHelper.ExecuteTable(
                    "SELECT AdminUserID, UserId, ISNULL(DisplayName,'') AS DisplayName, ISNULL(Email,'') AS Email, IsActive FROM dbo.tblLPPI_AdminUsers WHERE AdminUserID = @id",
                    LPPIHelper.P("@id", id));

                if (dt.Rows.Count != 1) return;

                DataRow r = dt.Rows[0];
                hfEditId.Value           = id.ToString();
                litEditUserId.Text       = LPPIHelper.Enc(r["UserId"]);
                txtEditDisplayName.Text  = Convert.ToString(r["DisplayName"]);
                txtEditEmail.Text        = Convert.ToString(r["Email"]);
                chkEditActive.Checked    = Convert.ToBoolean(r["IsActive"]);
                pnlEdit.Visible          = true;

                BindUsers();
            }
            else if (e.CommandName == "Toggle")
            {
                LPPIHelper.ExecuteNonQuery(@"
                    UPDATE dbo.tblLPPI_AdminUsers
                    SET IsActive     = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
                        ModifiedDate = SYSDATETIME()
                    WHERE AdminUserID = @id",
                    LPPIHelper.P("@id", id));

                // Clear cached access result — the toggled user may be the
                // current user (self-deactivation, edge case).
                if (HttpContext.Current != null)
                    HttpContext.Current.Items.Remove("LPPI_IsAdmin");

                BindUsers();
            }
        }

        // -------------------------------------------------------------------
        // rptUsers — ItemDataBound (row highlight for currently-edited user)
        // -------------------------------------------------------------------

        protected void rptUsers_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            if (e.Item.ItemType != ListItemType.Item &&
                e.Item.ItemType != ListItemType.AlternatingItem)
                return;

            int editingId;
            if (!pnlEdit.Visible || !int.TryParse(hfEditId.Value, out editingId))
                return;

            var row = e.Item.DataItem as DataRowView;
            if (row == null) return;

            if (Convert.ToInt32(row["AdminUserID"]) != editingId) return;

            var tr = e.Item.FindControl("trRow") as HtmlTableRow;
            if (tr != null) tr.Attributes["class"] = "is-editing";

            var flag = e.Item.FindControl("litEditFlag") as Literal;
            if (flag != null) flag.Text = "<span class=\"edit-flag\">(editing)</span>";
        }

        // -------------------------------------------------------------------
        // Edit panel — save / cancel
        // -------------------------------------------------------------------

        protected void btnSaveEdit_Click(object sender, EventArgs e)
        {
            int id;
            if (!int.TryParse(hfEditId.Value, out id))
            {
                ShowMessage("No user selected.", "err");
                return;
            }

            string displayName = (txtEditDisplayName.Text ?? "").Trim();
            string email       = (txtEditEmail.Text ?? "").Trim();
            bool   isActive    = chkEditActive.Checked;

            LPPIHelper.ExecuteNonQuery(@"
                UPDATE dbo.tblLPPI_AdminUsers
                SET DisplayName  = @dn,
                    Email        = @em,
                    IsActive     = @act,
                    ModifiedDate = SYSDATETIME()
                WHERE AdminUserID = @id",
                LPPIHelper.P("@dn",  string.IsNullOrEmpty(displayName) ? (object)DBNull.Value : displayName),
                LPPIHelper.P("@em",  string.IsNullOrEmpty(email)       ? (object)DBNull.Value : email),
                LPPIHelper.P("@act", isActive ? 1 : 0),
                LPPIHelper.P("@id",  id));

            // Clear cached access result in case active status changed.
            if (HttpContext.Current != null)
                HttpContext.Current.Items.Remove("LPPI_IsAdmin");

            ShowMessage("User updated.", "ok");
            CloseEditPanel();
            BindUsers();
        }

        protected void btnCancelEdit_Click(object sender, EventArgs e)
        {
            CloseEditPanel();
            BindUsers();
        }

        private void CloseEditPanel()
        {
            pnlEdit.Visible         = false;
            hfEditId.Value          = "";
            litEditUserId.Text      = "";
            txtEditDisplayName.Text = "";
            txtEditEmail.Text       = "";
            chkEditActive.Checked   = true;
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
