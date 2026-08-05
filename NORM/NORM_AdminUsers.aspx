<%@ Page Language="C#" AutoEventWireup="true" CodeFile="NORM_AdminUsers.aspx.cs" Inherits="CPlatform.NORM.NORM_AdminUsers" %>
<!doctype html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>NORM - Access administration</title>
    <link rel="stylesheet" href="../css/norm.css" />
</head>
<body class="norm-page">
<form id="form1" runat="server">
    <header class="norm-topbar">
        <a class="norm-brand" href="NORM_Workspace.aspx"><span class="norm-brand-mark">N</span><span><strong>NORM</strong><small>Access administration</small></span></a>
        <nav class="norm-top-actions"><a href="../Default.aspx">FinHub</a><a href="NORM_Workspace.aspx">Workspace</a><a href="NORM_Statements.aspx">Statements</a><a href="NORM_Help.aspx">Help</a><span class="norm-chip norm-env"><%= Server.HtmlEncode(CurrentEnvironment) %></span><span class="norm-user"><%= Server.HtmlEncode(CurrentUser) %></span></nav>
    </header>
    <main class="norm-workspace">
        <section class="norm-workspace-hero">
            <div><span class="norm-kicker">Administration</span><h1>Control who can prepare and administer NORM.</h1><p>Access is granted to named Windows identities. Deactivation preserves the access history and the last active administrator is protected.</p></div>
        </section>

        <asp:Panel ID="MessagePanel" runat="server" Visible="false" CssClass="norm-panel" style="margin-bottom:18px">
            <asp:Literal ID="MessageLiteral" runat="server" />
        </asp:Panel>

        <section class="norm-workspace-grid">
            <div class="norm-panel">
                <div class="norm-panel-head"><div><span class="norm-kicker">Add access</span><h2>Named user</h2></div></div>
                <div class="norm-field">
                    <label for="UserIdTextBox">Windows user ID</label>
                    <asp:TextBox ID="UserIdTextBox" runat="server" MaxLength="160" placeholder="DOMAIN\user.name" />
                </div>
                <div class="norm-field">
                    <label for="DisplayNameTextBox">Display name</label>
                    <asp:TextBox ID="DisplayNameTextBox" runat="server" MaxLength="200" placeholder="Optional" />
                </div>
                <div class="norm-field">
                    <label for="RoleDropDown">Role</label>
                    <asp:DropDownList ID="RoleDropDown" runat="server">
                        <asp:ListItem Value="Preparer">Preparer</asp:ListItem>
                        <asp:ListItem Value="Administrator">Administrator</asp:ListItem>
                    </asp:DropDownList>
                </div>
                <div class="norm-form-actions"><asp:Button ID="AddButton" runat="server" Text="Add user" CssClass="norm-button" OnClick="AddButton_Click" /></div>
            </div>

            <aside class="norm-panel">
                <div class="norm-panel-head"><div><span class="norm-kicker">Role model</span><h2>What access means</h2></div></div>
                <p><strong>Preparer</strong> can import source files, calculate runs and review the workspace.</p>
                <p><strong>Administrator</strong> has preparer access and can maintain this access list.</p>
                <p>Completed statements remain read-only and broadly visible.</p>
            </aside>
        </section>

        <section class="norm-panel" style="margin-top:18px">
            <div class="norm-panel-head"><div><span class="norm-kicker">Current access</span><h2>NORM users</h2></div></div>
            <div class="norm-table-wrap">
                <table class="norm-table">
                    <thead><tr><th>User</th><th>Role</th><th>Status</th><th>Created</th><th>Actions</th></tr></thead>
                    <tbody>
                    <asp:Repeater ID="UsersRepeater" runat="server" OnItemCommand="UsersRepeater_ItemCommand">
                        <ItemTemplate>
                            <tr>
                                <td><strong><%# Server.HtmlEncode(Convert.ToString(Eval("DisplayName"))) %></strong><br /><small><%# Server.HtmlEncode(Convert.ToString(Eval("UserId"))) %></small></td>
                                <td><%# Server.HtmlEncode(Convert.ToString(Eval("RoleCode"))) %></td>
                                <td><%# Convert.ToBoolean(Eval("IsDeactivated")) ? "Deactivated" : "Active" %></td>
                                <td><%# Convert.ToDateTime(Eval("CreatedUtc")).ToString("dd MMM yyyy") %></td>
                                <td>
                                    <asp:LinkButton runat="server" CommandName="ToggleRole" CommandArgument='<%# Eval("AdminUserId") %>' Text='<%# Convert.ToString(Eval("RoleCode")) == "Administrator" ? "Make preparer" : "Make administrator" %>' />
                                    &nbsp;·&nbsp;
                                    <asp:LinkButton runat="server" CommandName="ToggleActive" CommandArgument='<%# Eval("AdminUserId") %>' Text='<%# Convert.ToBoolean(Eval("IsDeactivated")) ? "Reactivate" : "Deactivate" %>' />
                                </td>
                            </tr>
                        </ItemTemplate>
                    </asp:Repeater>
                    </tbody>
                </table>
            </div>
        </section>
    </main>
</form>
</body>
</html>
