<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="LPPI_AdminUsers.aspx.cs" Inherits="CPlatform.LPPI.LPPI_AdminUsers" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review — Admin users</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <style>
        /* Edit-row highlight when a user is being edited above. */
        .tbl tr.is-editing td { background: var(--orange-soft); }
        .edit-flag { color: var(--orange-deep); font-size: 11px; margin-left: 6px; }
    </style>
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("adminusers") %>

    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Admin users</h1>
                <p class="lead">
                    Active users in this list have full access to the LPPI admin pages.
                    Users not listed here are directed to the LPPI info page.
                </p>
            </div>
        </div>

        <asp:PlaceHolder ID="phMessage" runat="server" />

        <%-- ================================================================
             Edit panel — surfaces above the list when operator clicks Edit.
             Only the IsActive flag is editable now that DisplayName / Email
             have been removed; toggling active state can also be done from
             the row's Deactivate / Reactivate button, but the edit panel is
             retained for symmetry and future fields.
             ================================================================ --%>
        <asp:Panel ID="pnlEdit" runat="server" Visible="false" CssClass="card" Style="margin-bottom:16px;">
            <h2>Edit user — <asp:Literal ID="litEditUserId" runat="server" /></h2>
            <div class="form-grid">
                <div class="form-row form-row-check">
                    <label>
                        <asp:CheckBox ID="chkEditActive" runat="server" Checked="true" />
                        Active
                    </label>
                </div>
                <div class="form-row form-row-actions">
                    <asp:HiddenField ID="hfEditId" runat="server" />
                    <asp:Button ID="btnSaveEdit" runat="server" CssClass="btn btn-primary" Text="Save" OnClick="btnSaveEdit_Click" />
                    <asp:Button ID="btnCancelEdit" runat="server" CssClass="btn btn-ghost" Text="Cancel" OnClick="btnCancelEdit_Click" CausesValidation="false" />
                </div>
            </div>
        </asp:Panel>

        <%-- ================================================================
             Add user panel
             ================================================================ --%>
        <div class="card" style="margin-bottom:16px;">
            <h2>Add user</h2>
            <p class="muted" style="font-size:13px;">Enter the Windows username (e.g. <code>DRN\firstname.lastname</code>). Multiple usernames can be added at once, separated by commas or semicolons.</p>
            <div class="form-grid">
                <div class="form-row form-row-wide">
                    <label for="txtAddUserIds">Username</label>
                    <asp:TextBox ID="txtAddUserIds" runat="server" CssClass="input" MaxLength="500" placeholder="DRN\firstname.lastname" />
                </div>
                <div class="form-row form-row-actions">
                    <asp:Button ID="btnAdd" runat="server" CssClass="btn btn-primary" Text="Add user(s)" OnClick="btnAdd_Click" />
                </div>
            </div>
        </div>

        <%-- ================================================================
             User list
             ================================================================ --%>
        <div class="card">
            <h2>Current admin users</h2>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptUsers" runat="server" OnItemCommand="rptUsers_ItemCommand" OnItemDataBound="rptUsers_ItemDataBound">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th>Username</th>
                                    <th>Status</th>
                                    <th>Added</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr runat="server" id="trRow">
                            <td>
                                <code><%# LPPIHelper.Enc(Eval("UserId")) %></code>
                                <asp:Literal runat="server" ID="litEditFlag" />
                            </td>
                            <td>
                                <%# (bool)Eval("IsActive")
                                    ? "<span class=\"pill pill-active\">Active</span>"
                                    : "<span class=\"pill pill-inactive\">Inactive</span>" %>
                            </td>
                            <td><%# LPPIHelper.FormatDate(Eval("CreatedDate"), "dd/MM/yyyy") %></td>
                            <td class="actions">
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost"
                                    Text="Edit"
                                    CommandName="Edit" CommandArgument='<%# Eval("AdminUserID") %>' />
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost"
                                    Text='<%# (bool)Eval("IsActive") ? "Deactivate" : "Reactivate" %>'
                                    CommandName="Toggle" CommandArgument='<%# Eval("AdminUserID") %>'
                                    OnClientClick='<%# (bool)Eval("IsActive") ? "return confirm(\"Deactivate this user?\");" : "return confirm(\"Reactivate this user?\");" %>' />
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost btn-danger"
                                    Text="Delete"
                                    CommandName="Delete" CommandArgument='<%# Eval("AdminUserID") %>'
                                    OnClientClick="return confirm('Permanently delete this user?');" />
                            </td>
                        </tr>
                    </ItemTemplate>
                    <FooterTemplate>
                            </tbody>
                        </table>
                    </FooterTemplate>
                </asp:Repeater>
            </div>
        </div>

    </main>

    <footer class="lppi-footer">
        <span>LPPI Review &middot; <%= CurrentEnv %></span>
    </footer>
</div>
</form>
</body>
</html>
