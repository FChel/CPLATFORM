<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="PORD_Exclusions.aspx.cs" Inherits="CPlatform.PORD.PORD_Exclusions" %>
<%@ Import Namespace="CPlatform.PORD" %>
<!DOCTYPE html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>PO Review — Exclusions</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <link rel="stylesheet" href="../css/pord.css" />
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("exclusions") %>
    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">PO Review</div>
                <h1>Exclusions</h1>
                <p class="lead">POs with an accepted valid reason are skipped at load, so owners are not asked the same question every month.
                An exclusion is tied to the PO, the check <em>and</em> the value that was accepted (for payment terms, the term key): if that changes, the PO is checked again. Exclusions last <%= PORDHelper.ExclusionMonths %> months.</p>
            </div>
        </div>
        <%= RenderDemoNotice() %>
        <asp:PlaceHolder ID="phMsg" runat="server" />

        <div class="stat-grid">
            <div class="stat ok"><div class="lbl">Active</div><div class="val"><%= ActiveCount %></div></div>
            <div class="stat warn"><div class="lbl">Expiring within 30 days</div><div class="val"><%= ExpiringCount %></div></div>
            <div class="stat"><div class="lbl">Expired</div><div class="val"><%= ExpiredCount %></div></div>
            <div class="stat"><div class="lbl">Revoked</div><div class="val"><%= RevokedCount %></div></div>
        </div>

        <div class="card">
            <div class="tbl-wrap">
                <table class="tbl">
                    <thead><tr><th>PO</th><th>Check</th><th>Supplier</th><th>Key</th><th>Program</th><th>Reason</th><th>Evidence</th><th>Granted</th><th>Expires</th><th>Status</th><th></th></tr></thead>
                    <tbody>
                    <asp:Repeater ID="rpt" runat="server" OnItemCommand="rpt_ItemCommand"><ItemTemplate>
                        <tr>
                            <td class="nowrap"><%# PORDHelper.PoLinkHtml(X(Container.DataItem).PoNumber) %></td>
                            <td><%# PORDHelper.Enc(PORDChecks.Get(X(Container.DataItem).CheckType).ShortName) %></td>
                            <td><%# PORDHelper.Enc(X(Container.DataItem).BpName) %></td>
                            <td><span class="pord-term"><%# PORDHelper.Enc(X(Container.DataItem).PoTermKey) %></span></td>
                            <td><%# PORDHelper.Enc(X(Container.DataItem).DmProgram) %></td>
                            <td><code><%# PORDHelper.Enc(X(Container.DataItem).ReasonCode) %></code></td>
                            <td><%# PORDHelper.Enc(X(Container.DataItem).EvidenceRef) %></td>
                            <td class="nowrap"><%# PORDHelper.Date(X(Container.DataItem).GrantedDate) %><span class="sub"><%# PORDHelper.Enc(X(Container.DataItem).GrantedBy) %></span></td>
                            <td class="nowrap"><%# PORDHelper.Date(X(Container.DataItem).ExpiryDate) %></td>
                            <td><%# StatusPill(X(Container.DataItem)) %></td>
                            <td><asp:Button runat="server" CssClass="btn btn-sm btn-danger" Text="Revoke" CommandName="revoke"
                                    CommandArgument='<%# X(Container.DataItem).PoNumber %>' Visible='<%# !X(Container.DataItem).IsRevoked %>'
                                    OnClientClick="return confirm('Revoke this exclusion? The PO will be flagged again at the next load if it is still non-compliant.');" /></td>
                        </tr>
                    </ItemTemplate></asp:Repeater>
                    </tbody>
                </table>
            </div>
        </div>
    </main>
    <%= RenderFooter() %>
</div>
</form>
</body>
</html>
