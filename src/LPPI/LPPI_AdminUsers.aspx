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
        /* Row highlight for the user currently being edited */
        .tbl tr.is-editing > td {
            background: #eef4ff;
            box-shadow: inset 3px 0 0 #3b82f6;
        }
        .edit-flag {
            margin-left: 0.5em;
            font-size: 0.85em;
            color: #1d4ed8;
            font-weight: 500;
        }
        .pill-active   { background: #dcfce7; color: #166534; }
        .pill-inactive { background: #fee2e2; color: #991b1b; }
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
                    Manage who has access to the LPPI admin pages. Any authenticated FinHub
                    user not listed here is directed to the LPPI info page instead.
                </p>
            </div>
        </div>

        <asp:PlaceHolder ID="phMessage" runat="server" />

        <%-- ================================================================
             Edit panel — surfaces above the list when operator clicks Edit.
             ================================================================ --%>
        <asp:Panel ID="pnlEdit" runat="server" Visible="false" CssClass="card" Style="margin-bottom:16px;">
            <h2>Edit user — <asp:Literal ID="litEditUserId" runat="server" /></h2>
            <div class="form-grid">
                <div class="form-row">
                    <label for="txtEditDisplayName">Display name</label>
                    <asp:TextBox ID="txtEditDisplayName" runat="server" CssClass="input" MaxLength="200" />
                </div>
                <div class="form-row">
                    <label for="txtEditEmail">Email</label>
                    <asp:TextBox ID="txtEditEmail" runat="server" CssClass="input" MaxLength="200" TextMode="Email" />
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
            <p class="muted" style="font-size:13px;">Enter the Windows username (e.g. <code>DEFENCE\jsmith</code>). Separate multiple usernames with commas.</p>
            <div class="form-grid">
                <div class="form-row form-row-wide">
                    <label for="txtAddUserIds">Username(s)</label>
                    <asp:TextBox ID="txtAddUserIds" runat="server" CssClass="input" MaxLength="500" placeholder="DEFENCE\username" />
                </div>
                <div class="form-row">
                    <label for="txtAddDisplayName">Display name <span class="muted">(optional — single user only)</span></label>
                    <asp:TextBox ID="txtAddDisplayName" runat="server" CssClass="input" MaxLength="200" />
                </div>
                <div class="form-row">
                    <label for="txtAddEmail">Email <span class="muted">(optional — single user only)</span></label>
                    <asp:TextBox ID="txtAddEmail" runat="server" CssClass="input" MaxLength="200" TextMode="Email" />
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
                                    <th>Display name</th>
                                    <th>Email</th>
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
                            <td><%# LPPIHelper.Enc(Eval("DisplayName")) %></td>
                            <td><%# LPPIHelper.Enc(Eval("Email")) %></td>
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
