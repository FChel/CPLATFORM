<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="PORD_ReasonCodes.aspx.cs" Inherits="CPlatform.PORD.PORD_ReasonCodes" %>
<%@ Import Namespace="CPlatform.PORD" %>
<!DOCTYPE html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>PO Review — Reason codes</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <link rel="stylesheet" href="../css/pord.css" />
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("reasons") %>
    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">PO Review</div>
                <h1>Valid reason codes</h1>
                <p class="lead">Reasons a PO may keep non-standard payment terms. Reviewers choose one when they respond <em>Valid reason</em>.</p>
            </div>
        </div>
        <%= RenderDemoNotice() %>
        <div class="card">
            <div class="tbl-wrap" style="max-height:none;">
                <table class="tbl">
                    <thead><tr><th>Code</th><th>Description</th><th>Evidence ref</th><th>Comments</th><th>Excludes PO from future reviews</th><th class="num">Used this cycle</th><th>Status</th></tr></thead>
                    <tbody>
                    <asp:Repeater ID="rpt" runat="server"><ItemTemplate>
                        <tr>
                            <td><code><%# PORDHelper.Enc(R(Container.DataItem).Code) %></code></td>
                            <td><strong><%# PORDHelper.Enc(R(Container.DataItem).Description) %></strong></td>
                            <td><%# Tick(R(Container.DataItem).RequiresEvidence, "Required") %></td>
                            <td><%# Tick(R(Container.DataItem).RequiresComments, "Required") %></td>
                            <td><%# Tick(R(Container.DataItem).CreatesExclusion, PORDHelper.ExclusionMonths + " months") %></td>
                            <td class="num"><%# Used(R(Container.DataItem).Code) %></td>
                            <td><%# R(Container.DataItem).IsActive ? "<span class=\"pill finalised\">Active</span>" : "<span class=\"pill closed\">Inactive</span>" %></td>
                        </tr>
                    </ItemTemplate></asp:Repeater>
                    </tbody>
                </table>
            </div>
            <p class="muted" style="font-size:12px;margin:12px 0 0;">
                The first three come from the business requirement. <code>VR99 Other</code> is proposed so genuine edge cases are captured rather than forced into the wrong code; it does not create an exclusion, so the PO is reconsidered next month.
                Editing codes will be enabled with the PORD schema (demo is read only).
            </p>
        </div>
    </main>
    <%= RenderFooter() %>
</div>
</form>
</body>
</html>
