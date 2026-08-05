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
 *
 * Nov 2026 — DisplayName and Email columns dropped from the schema. The
 * only thing the access gate needs is a Windows username; the rest was
 * never used anywhere else in the codebase. Deletion is now a hard
 * delete (the table has no FKs and audit is via CreatedBy on other
 * tables, not the AdminUserID), so the previous "deactivation is
 * preferred over delete for audit trail" comment no longer applies —
 * deactivation is still available as a softer option, but Delete is
 * the wired button on the row.
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
        //   AdminUserID, UserId, IsActive, CreatedDate
        // -------------------------------------------------------------------

        private void BindUsers()
        {
            const string sql = @"
                SELECT AdminUserID,
                       UserId,
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

            string createdBy = LPPIHelper.CurrentUserDisplayName();

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
                        (UserId, IsActive, CreatedBy)
                    VALUES (@u, 1, @cb)",
                    LPPIHelper.P("@u",  userId),
                    LPPIHelper.P("@cb", createdBy));

                added++;
            }

            var msg = new StringBuilder();
            if (added   > 0) msg.AppendFormat("{0} user{1} added. ", added, added == 1 ? "" : "s");
            if (skipped > 0) msg.AppendFormat("{0} already existed and {1} skipped.",
                skipped, skipped == 1 ? "was" : "were");

            ShowMessage(msg.ToString().Trim(), added > 0 ? "ok" : "warn");

            txtAddUserIds.Text = "";

            BindUsers();
        }

        // -------------------------------------------------------------------
        // rptUsers — ItemCommand (Edit / Toggle / Delete)
        // -------------------------------------------------------------------

        protected void rptUsers_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            int id;
            if (!int.TryParse(Convert.ToString(e.CommandArgument), out id)) return;

            if (e.CommandName == "Edit")
            {
                DataTable dt = LPPIHelper.ExecuteTable(
                    "SELECT AdminUserID, UserId, IsActive FROM dbo.tblLPPI_AdminUsers WHERE AdminUserID = @id",
                    LPPIHelper.P("@id", id));

                if (dt.Rows.Count != 1) return;

                DataRow r = dt.Rows[0];
                hfEditId.Value        = id.ToString();
                litEditUserId.Text    = LPPIHelper.Enc(r["UserId"]);
                chkEditActive.Checked = Convert.ToBoolean(r["IsActive"]);
                pnlEdit.Visible       = true;

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
            else if (e.CommandName == "Delete")
            {
                // Hard delete. Safe because:
                //   - No FK references AdminUserID anywhere in the schema.
                //   - "Added by" / "Created by" audit on other tables
                //     stores the username as a string, not a FK.
                //   - The client-side confirm() guards against accidents;
                //     the action is irreversible from the UI.
                int rows = LPPIHelper.ExecuteNonQuery(
                    "DELETE FROM dbo.tblLPPI_AdminUsers WHERE AdminUserID = @id",
                    LPPIHelper.P("@id", id));

                // Clear cached access result — the deleted user may be the
                // current user (self-deletion, edge case).
                if (HttpContext.Current != null)
                    HttpContext.Current.Items.Remove("LPPI_IsAdmin");

                // If the edit panel was open on this user, close it.
                int editingId;
                if (pnlEdit.Visible
                    && int.TryParse(hfEditId.Value, out editingId)
                    && editingId == id)
                {
                    CloseEditPanel();
                }

                ShowMessage(rows == 1 ? "User deleted." : "User not found (already removed).",
                            rows == 1 ? "ok" : "warn");

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

            bool isActive = chkEditActive.Checked;

            LPPIHelper.ExecuteNonQuery(@"
                UPDATE dbo.tblLPPI_AdminUsers
                SET IsActive     = @act,
                    ModifiedDate = SYSDATETIME()
                WHERE AdminUserID = @id",
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
            pnlEdit.Visible       = false;
            hfEditId.Value        = "";
            litEditUserId.Text    = "";
            chkEditActive.Checked = true;
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
