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
                    Manage who has access to the LPPI admin pages. Any authenticated CPLATFORM
                    user not listed here is directed to the LPPI info page instead.
                </p>
            </div>
        </div>

        <asp:PlaceHolder ID="phMessage" runat="server" />

        <%-- ================================================================
             Edit panel — surfaces above the list when operator clicks Edit.
             ================================================================ --%>
        <asp:Panel ID="pnlEdit" runat="server" Visible="false" CssClass="card">
            <div class="page-head" style="margin-bottom: 1rem;">
                <div>
                    <h2 style="margin: 0;">
                        Editing:
                        <asp:Literal ID="litEditUserId" runat="server" />
                    </h2>
                </div>
                <div>
                    <asp:Button ID="btnCancelEdit" runat="server" CssClass="btn btn-ghost"
                        Text="Cancel" OnClick="btnCancelEdit_Click" CausesValidation="false" />
                </div>
            </div>

            <div class="form-grid">
                <div class="form-row form-row-wide">
                    <label for="txtEditDisplayName">Display name</label>
                    <asp:TextBox ID="txtEditDisplayName" runat="server" CssClass="input" MaxLength="200"
                        placeholder="Full name (optional)" />
                </div>
                <div class="form-row form-row-wide">
                    <label for="txtEditEmail">Email</label>
                    <asp:TextBox ID="txtEditEmail" runat="server" CssClass="input" MaxLength="200"
                        placeholder="name@defence.gov.au (optional)" />
                </div>
                <div class="form-row form-row-check">
                    <label><asp:CheckBox ID="chkEditActive" runat="server" /> Active</label>
                </div>
                <div class="form-row form-row-actions">
                    <asp:Button ID="btnSaveEdit" runat="server" CssClass="btn btn-primary"
                        Text="Save changes" OnClick="btnSaveEdit_Click" />
                    <asp:HiddenField ID="hfEditId" runat="server" />
                </div>
            </div>
        </asp:Panel>

        <%-- ================================================================
             Add user card
             ================================================================ --%>
        <div class="card">
            <h2>Add admin user(s)</h2>
            <p class="muted" style="font-size:13px;">
                Enter one or more <code>DOMAIN\username</code> values separated by commas.
                Existing users are skipped.
            </p>
            <div class="form-grid">
                <div class="form-row form-row-wide">
                    <label for="txtAddUserIds">Windows username(s)</label>
                    <asp:TextBox ID="txtAddUserIds" runat="server" CssClass="input" MaxLength="2000"
                        placeholder="DRN\jsmith, DRN\abrown" />
                </div>
                <div class="form-row form-row-wide">
                    <label for="txtAddDisplayName">Display name <span class="muted">(optional — applies to first username only if adding one)</span></label>
                    <asp:TextBox ID="txtAddDisplayName" runat="server" CssClass="input" MaxLength="200"
                        placeholder="Full name" />
                </div>
                <div class="form-row form-row-wide">
                    <label for="txtAddEmail">Email <span class="muted">(optional — applies to first username only if adding one)</span></label>
                    <asp:TextBox ID="txtAddEmail" runat="server" CssClass="input" MaxLength="200"
                        placeholder="name@defence.gov.au" />
                </div>
                <div class="form-row form-row-actions">
                    <asp:Button ID="btnAdd" runat="server" CssClass="btn btn-primary"
                        Text="Add user(s)" OnClick="btnAdd_Click" />
                </div>
            </div>
        </div>

        <%-- ================================================================
             Users list
             ================================================================ --%>
        <div class="card">
            <h2>All admin users</h2>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptUsers" runat="server"
                              OnItemCommand="rptUsers_ItemCommand"
                              OnItemDataBound="rptUsers_ItemDataBound">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th>Windows username</th>
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
                                <strong><%# LPPIHelper.Enc(Eval("UserId")) %></strong>
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
                            <td class="num">
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost"
                                    Text="Edit"
                                    CommandName="Edit"
                                    CommandArgument='<%# Eval("AdminUserID") %>' />
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost btn-danger"
                                    Text='<%# (bool)Eval("IsActive") ? "Deactivate" : "Activate" %>'
                                    CommandName="Toggle"
                                    CommandArgument='<%# Eval("AdminUserID") %>'
                                    OnClientClick='<%# (bool)Eval("IsActive")
                                        ? "return confirm(\"Deactivate this user? They will lose access immediately.\");"
                                        : "return confirm(\"Re-activate this user?\");" %>' />
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

    <footer class="lppi-footer">Defence Finance Group · LPPI Review · <%= CurrentEnv %></footer>
</div>
</form>
</body>
</html>
