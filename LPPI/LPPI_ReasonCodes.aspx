<%@ Page Language="C#" Debug="true" AutoEventWireup="true"
    CodeFile="LPPI_ReasonCodes.aspx.cs" Inherits="CPlatform.LPPI.LPPI_ReasonCodes" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review — Reason Codes</title>
    <link rel="stylesheet" href="../css/lppi.css" />
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("reasons") %>

    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Reason Codes</h1>
                <p class="lead">Maintain the reason codes reviewers pick when classifying each document.</p>
            </div>
        </div>

        <asp:PlaceHolder ID="phMessage" runat="server" />

        <div class="card">
            <h2>Add or update a reason code</h2>
            <div class="form-grid">
                <div class="form-row">
                    <label for="txtCode">Code</label>
                    <asp:TextBox ID="txtCode" runat="server" CssClass="input" MaxLength="20" placeholder="e.g. RC17" />
                </div>
                <div class="form-row form-row-wide">
                    <label for="txtDesc">Description</label>
                    <asp:TextBox ID="txtDesc" runat="server" CssClass="input" MaxLength="500" />
                </div>
                <div class="form-row">
                    <label for="ddlOutcome">Outcome</label>
                    <asp:DropDownList ID="ddlOutcome" runat="server" CssClass="input">
                        <asp:ListItem Value="Payable">Payable</asp:ListItem>
                        <asp:ListItem Value="NotPayable">Not Payable</asp:ListItem>
                    </asp:DropDownList>
                </div>
                <div class="form-row">
                    <label for="txtOrder">Display order</label>
                    <asp:TextBox ID="txtOrder" runat="server" CssClass="input" MaxLength="5" />
                </div>
                <div class="form-row form-row-check">
                    <label><asp:CheckBox ID="chkRequires" runat="server" /> Requires comments</label>
                </div>
                <div class="form-row form-row-check">
                    <label><asp:CheckBox ID="chkActive" runat="server" Checked="true" /> Active</label>
                </div>
                <div class="form-row form-row-actions">
                    <asp:Button ID="btnSave" runat="server" CssClass="btn btn-primary" Text="Save reason code" OnClick="btnSave_Click" />
                    <asp:Button ID="btnClear" runat="server" CssClass="btn btn-secondary" Text="Clear" OnClick="btnClear_Click" CausesValidation="false" />
                    <asp:HiddenField ID="hfId" runat="server" />
                </div>
            </div>
        </div>

        <div class="card">
            <h2>Current reason codes</h2>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptCodes" runat="server" OnItemCommand="rptCodes_ItemCommand">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th class="num">Order</th>
                                    <th>Code</th>
                                    <th>Description</th>
                                    <th>Outcome</th>
                                    <th>Requires comments</th>
                                    <th>Status</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr>
                            <td class="num"><%# Eval("DisplayOrder") %></td>
                            <td><strong><%# LPPIHelper.Enc(Eval("Code")) %></strong></td>
                            <td><%# LPPIHelper.Enc(Eval("Description")) %></td>
                            <td>
                                <%# string.Equals((string)Eval("Outcome"), "Payable")
                                    ? "<span class=\"pill pill-payable\">Payable</span>"
                                    : "<span class=\"pill pill-notpayable\">Not Payable</span>" %>
                            </td>
                            <td><%# (bool)Eval("RequiresComments") ? "Yes" : "No" %></td>
                            <td>
                                <%# (bool)Eval("IsActive")
                                    ? "<span class=\"pill pill-open\">Active</span>"
                                    : "<span class=\"pill pill-closed\">Inactive</span>" %>
                            </td>
                            <td class="num">
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost" Text="Edit"
                                    CommandName="Edit" CommandArgument='<%# Eval("ReasonCodeID") %>' />
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost"
                                    Text='<%# (bool)Eval("IsActive") ? "Disable" : "Enable" %>'
                                    CommandName="Toggle" CommandArgument='<%# Eval("ReasonCodeID") %>' />
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
        <span>LPPI Review · <%= CurrentEnv %></span>
    </footer>
</div>
</form>
</body>
</html>
