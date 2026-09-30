<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="PORD_Outcomes.aspx.cs" Inherits="CPlatform.PORD.PORD_Outcomes" %>
<%@ Import Namespace="CPlatform.PORD" %>
<!DOCTYPE html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>PO Review — Outcomes</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <link rel="stylesheet" href="../css/pord.css" />
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("outcomes") %>
    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">PO Review</div>
                <h1>Outcomes &amp; closing the loop</h1>
                <p class="lead">What goes out at the end of each cycle: the outcomes report for the FSO Compliance Team, supplier master-data fixes for DFIM,
                and proof from the next load that committed changes were made.</p>
            </div>
        </div>
        <%= RenderDemoNotice() %>
        <asp:PlaceHolder ID="phMsg" runat="server" />

        <%-- 1. Outcomes report --%>
        <div class="card">
            <div class="card-head">
                <h2>1 · Outcomes report — FSO Compliance Team</h2>
                <a class="btn btn-secondary btn-sm" href="PORD_Outcomes_Export.ashx?kind=outcomes">Download CSV</a>
            </div>
            <p class="card-lead">One row per exception (PO × check) with its check, response, reason, evidence and fix-by date. Issue it once every package is finalised; issuing closes the cycle's packages.</p>
            <div class="tbl-wrap" style="max-height:none;">
                <table class="tbl">
                    <thead><tr><th>Program</th><th>Status</th><th class="num">POs</th><th class="num">Will fix</th><th class="num">Valid reason</th><th class="num">Reassign</th><th class="num">No response</th><th class="num">Awaiting</th></tr></thead>
                    <tbody><%= OutcomeRowsHtml %></tbody>
                </table>
            </div>
            <div class="btn-row" style="margin-top:14px;align-items:center;">
                <asp:Button ID="btnClose" runat="server" CssClass="btn btn-primary" Text="Issue outcomes &amp; close finalised packages" OnClick="btnClose_Click"
                    OnClientClick="return confirm('Close every finalised package? Their review links become read only.');" />
                <span class="muted" style="font-size:12px;"><%= FinalisedCount %> of <%= PackageCount %> packages finalised</span>
            </div>
        </div>

        <%-- 2. BP master data --%>
        <div class="card">
            <div class="card-head">
                <h2>2 · Supplier master data — DFIM</h2>
                <a class="btn btn-secondary btn-sm" href="PORD_Outcomes_Export.ashx?kind=bp">Download CSV</a>
            </div>
            <p class="card-lead">A safety net. BP master records should always carry standard Defence terms (20 days, or 5 days for PEPPOL). Any supplier listed here has non-standard master terms, which every new PO copies, so DFIM needs to correct it.</p>
            <asp:PlaceHolder ID="phBpNone" runat="server" Visible="false"><div class="alert ok" style="margin:0 0 12px;">All supplier master records in this cycle carry standard terms. Nothing to send to DFIM.</div></asp:PlaceHolder>
            <div class="tbl-wrap">
                <table class="tbl">
                    <thead><tr><th style="width:34px;"></th><th>BP</th><th>Supplier</th><th>Master terms</th><th class="num">Open POs</th><th class="num">Still to deliver</th><th>DFIM status</th></tr></thead>
                    <tbody>
                    <asp:Repeater ID="rptBp" runat="server"><ItemTemplate>
                        <tr>
                            <td><input type="checkbox" name="bp" value="<%# PORDHelper.Attr(Bp(Container.DataItem).BpNumber) %>" <%# Bp(Container.DataItem).Status == "New" ? "" : "disabled" %> /></td>
                            <td><%# PORDHelper.Enc(Bp(Container.DataItem).BpNumber) %></td>
                            <td><strong><%# PORDHelper.Enc(Bp(Container.DataItem).BpName) %></strong></td>
                            <td><span class="pord-term bad"><%# PORDHelper.Enc(Bp(Container.DataItem).BpTermKey) %><span class="d"><%# Bp(Container.DataItem).BpTermDays %>d</span></span></td>
                            <td class="num"><%# Bp(Container.DataItem).OpenPoCount %></td>
                            <td class="num">$<%# PORDHelper.Money(Bp(Container.DataItem).StillToDeliver) %></td>
                            <td><%# Bp(Container.DataItem).Status == "New" ? "<span class=\"pill duesoon\">To notify</span>" : "<span class=\"pill sent\">" + PORDHelper.Enc(Bp(Container.DataItem).Status) + "</span>" %></td>
                        </tr>
                    </ItemTemplate></asp:Repeater>
                    </tbody>
                </table>
            </div>
            <div class="btn-row" style="margin-top:14px;">
                <asp:Button ID="btnNotify" runat="server" CssClass="btn btn-secondary" Text="Mark selected as sent to DFIM" OnClick="btnNotify_Click" />
            </div>
        </div>

        <%-- 3. Verified at next load --%>
        <div class="card">
            <div class="card-head"><h2>3 · Verified at next load</h2><span class="muted" style="font-size:12px;">from the <%= PORDHelper.Date(LastLoad) %> load</span></div>
            <p class="card-lead">POs the owner committed to amend last cycle that no longer breach policy. POs that are <em>still</em> flagged come back with a higher Review Nbr and appear under Repeat exceptions on the Dashboard.</p>
            <div class="tbl-wrap" style="max-height:360px;">
                <table class="tbl tbl-compact">
                    <thead><tr><th>PO</th><th>Supplier</th><th>Program</th><th>Was</th><th>Committed</th><th>Verified</th></tr></thead>
                    <tbody>
                    <asp:Repeater ID="rptResolved" runat="server"><ItemTemplate>
                        <tr>
                            <td class="nowrap"><%# PORDHelper.PoLinkHtml(Rs(Container.DataItem).PoNumber) %></td>
                            <td><%# PORDHelper.Enc(Rs(Container.DataItem).BpName) %></td>
                            <td><%# PORDHelper.Enc(Rs(Container.DataItem).DmProgram) %></td>
                            <td><span class="pord-term"><%# PORDHelper.Enc(Rs(Container.DataItem).OldTermKey) %></span></td>
                            <td class="nowrap"><%# PORDHelper.Date(Rs(Container.DataItem).CommittedDate) %></td>
                            <td class="nowrap"><span class="pill finalised">Resolved <%# PORDHelper.Date(Rs(Container.DataItem).VerifiedDate) %></span></td>
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
