<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="PORD_AsFin.aspx.cs" Inherits="CPlatform.PORD.PORD_AsFin" %>
<%@ Import Namespace="CPlatform.PORD" %>
<!DOCTYPE html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>PO Review — AS Fin groups</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <link rel="stylesheet" href="../css/pord.css" />
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("asfin") %>
    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">PO Review</div>
                <h1>AS Fin groups</h1>
                <p class="lead">Each Delivery Manager program's package goes to its AS Fin team mailbox. The same mailbox is the sender (and reply-to) of the PO contact emails, so questions land with the right team.</p>
            </div>
        </div>
        <%= RenderDemoNotice() %>
        <asp:PlaceHolder ID="phMsg" runat="server" />

        <div class="card">
            <div class="tbl-wrap" style="max-height:none;">
                <table class="tbl">
                    <thead><tr><th>Delivery Manager program</th><th>AS Fin mailbox</th><th>Display name</th><th class="num">POs this cycle</th><th>Status</th><th></th></tr></thead>
                    <tbody>
                    <asp:Repeater ID="rptGroups" runat="server" OnItemCommand="rptGroups_ItemCommand">
                        <ItemTemplate>
                        <tr>
                            <td><strong><%# PORDHelper.Enc(G(Container.DataItem).DmProgram) %></strong></td>
                            <td style="min-width:260px;"><asp:TextBox ID="txtEmail" runat="server" CssClass="input" style="width:100%;padding:7px 10px;border:1px solid var(--line);border-radius:8px;" Text='<%# G(Container.DataItem).Email %>' placeholder="team.mailbox@defence.gov.au" /></td>
                            <td style="min-width:200px;"><asp:TextBox ID="txtName" runat="server" CssClass="input" style="width:100%;padding:7px 10px;border:1px solid var(--line);border-radius:8px;" Text='<%# G(Container.DataItem).DisplayName %>' placeholder="AS Fin …" /></td>
                            <td class="num"><%# PoCount(G(Container.DataItem).DmProgram) %></td>
                            <td><%# G(Container.DataItem).IsConfigured ? "<span class=\"pill finalised\">Ready</span>" : "<span class=\"pill overdue\">Not configured</span>" %></td>
                            <td><asp:Button runat="server" CssClass="btn btn-sm btn-secondary" Text="Save" CommandName="save" CommandArgument='<%# G(Container.DataItem).DmProgram %>' /></td>
                        </tr>
                        </ItemTemplate>
                    </asp:Repeater>
                    </tbody>
                </table>
            </div>
            <p class="muted" style="font-size:12px;margin:12px 0 0;">Programs appear here automatically when a loaded file contains them. A package cannot be sent until its program has a mailbox and display name.</p>
        </div>
    </main>
    <%= RenderFooter() %>
</div>
</form>
</body>
</html>
