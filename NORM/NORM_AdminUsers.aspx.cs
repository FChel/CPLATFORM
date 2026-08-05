using System;
using System.Data;
using System.Web.UI.WebControls;

namespace CPlatform.NORM
{
    public partial class NORM_AdminUsers : NORMBasePage
    {
        protected override bool RequiresAdministratorAccess { get { return true; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) { BindUsers(); }
        }

        protected void AddButton_Click(object sender, EventArgs e)
        {
            string userId = (UserIdTextBox.Text ?? "").Trim();
            string displayName = (DisplayNameTextBox.Text ?? "").Trim();
            string role = RoleDropDown.SelectedValue == "Administrator" ? "Administrator" : "Preparer";
            if (userId.Length == 0)
            {
                ShowMessage("Enter a Windows user ID.", false);
                return;
            }
            if (displayName.Length == 0) { displayName = userId; }

            object existing = NORMHelper.Scalar(
                "SELECT AdminUserId FROM dbo.tblNORM_AdminUser WHERE LOWER(UserId) = LOWER(@user)",
                NORMHelper.P("@user", userId));
            if (existing == null)
            {
                NORMHelper.Exec(
                    "INSERT dbo.tblNORM_AdminUser (UserId,DisplayName,RoleCode,IsDeactivated,CreatedBy) " +
                    "VALUES (@user,@name,@role,0,@by)",
                    NORMHelper.P("@user", userId), NORMHelper.P("@name", displayName),
                    NORMHelper.P("@role", role), NORMHelper.P("@by", NORMHelper.CurrentUserId()));
                ShowMessage("NORM access added for " + userId + ".", true);
            }
            else
            {
                NORMHelper.Exec(
                    "UPDATE dbo.tblNORM_AdminUser SET DisplayName=@name,RoleCode=@role,IsDeactivated=0 " +
                    "WHERE AdminUserId=@id",
                    NORMHelper.P("@name", displayName), NORMHelper.P("@role", role),
                    NORMHelper.P("@id", Convert.ToInt32(existing)));
                ShowMessage("Existing access was updated and reactivated.", true);
            }
            UserIdTextBox.Text = "";
            DisplayNameTextBox.Text = "";
            BindUsers();
        }

        protected void UsersRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            int id;
            if (!Int32.TryParse(Convert.ToString(e.CommandArgument), out id)) { return; }
            DataTable row = NORMHelper.Query(
                "SELECT AdminUserId,UserId,RoleCode,IsDeactivated FROM dbo.tblNORM_AdminUser WHERE AdminUserId=@id",
                NORMHelper.P("@id", id));
            if (row.Rows.Count == 0) { ShowMessage("That access entry no longer exists.", false); return; }

            string role = Convert.ToString(row.Rows[0]["RoleCode"]);
            bool deactivated = Convert.ToBoolean(row.Rows[0]["IsDeactivated"]);
            if (e.CommandName == "ToggleRole")
            {
                if (role == "Administrator" && !deactivated && ActiveAdministratorCount() <= 1)
                {
                    ShowMessage("The last active administrator cannot be changed to a preparer.", false);
                    return;
                }
                string nextRole = role == "Administrator" ? "Preparer" : "Administrator";
                NORMHelper.Exec("UPDATE dbo.tblNORM_AdminUser SET RoleCode=@role WHERE AdminUserId=@id",
                    NORMHelper.P("@role", nextRole), NORMHelper.P("@id", id));
                ShowMessage("Role updated to " + nextRole + ".", true);
            }
            else if (e.CommandName == "ToggleActive")
            {
                if (role == "Administrator" && !deactivated && ActiveAdministratorCount() <= 1)
                {
                    ShowMessage("The last active administrator cannot be deactivated.", false);
                    return;
                }
                NORMHelper.Exec("UPDATE dbo.tblNORM_AdminUser SET IsDeactivated=@value WHERE AdminUserId=@id",
                    NORMHelper.P("@value", deactivated ? 0 : 1), NORMHelper.P("@id", id));
                ShowMessage(deactivated ? "Access reactivated." : "Access deactivated.", true);
            }
            BindUsers();
        }

        private int ActiveAdministratorCount()
        {
            object value = NORMHelper.Scalar(
                "SELECT COUNT(1) FROM dbo.tblNORM_AdminUser WHERE RoleCode='Administrator' AND IsDeactivated=0");
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private void BindUsers()
        {
            UsersRepeater.DataSource = NORMHelper.Query(
                "SELECT AdminUserId,UserId,COALESCE(NULLIF(DisplayName,''),UserId) AS DisplayName," +
                "RoleCode,IsDeactivated,CreatedUtc FROM dbo.tblNORM_AdminUser " +
                "ORDER BY IsDeactivated,CASE WHEN RoleCode='Administrator' THEN 0 ELSE 1 END,UserId");
            UsersRepeater.DataBind();
        }

        private void ShowMessage(string message, bool success)
        {
            MessagePanel.Visible = true;
            MessagePanel.CssClass = success ? "norm-panel norm-message-success" : "norm-panel norm-message-error";
            MessageLiteral.Text = Server.HtmlEncode(message);
        }
    }
}
