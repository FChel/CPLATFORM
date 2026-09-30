<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="PORD_Admin.aspx.cs" Inherits="CPlatform.PORD.PORD_Admin" %>
<%@ Import Namespace="CPlatform.PORD" %>
<!DOCTYPE html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>PO Review — Dashboard</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <link rel="stylesheet" href="../css/pord.css" />
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("dashboard") %>

    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">PO Review · <%= PORDHelper.CheckName %></div>
                <h1>Dashboard</h1>
                <p class="lead">Current cycle: <strong><%= PORDHelper.Enc(CycleLabel) %></strong>. Open purchase orders whose payment terms breach policy or differ from the supplier master record.</p>
            </div>
            <div class="btn-row">
                <asp:Button ID="btnReset" runat="server" CssClass="btn btn-ghost" Text="Reset demo data"
                            OnClick="btnReset_Click" OnClientClick="return confirm('Reset the demo to its starting state? Everyone viewing the demo will see the reset.');" />
                <a class="btn btn-secondary" href="PORD_SendOuts.aspx">Send-outs</a>
                <a class="btn btn-primary" href="PORD_Load.aspx">Load new file</a>
            </div>
        </div>

        <%= RenderDemoNotice() %>
        <asp:PlaceHolder ID="phMsg" runat="server" />

        <%-- ============ Cycle loop ============ --%>
        <div class="pord-loop" aria-label="Monthly review cycle">
            <div class="step done">
                <div class="k">1 · Loaded</div>
                <div class="v"><%= PORDHelper.Num(FlaggedCount) %></div>
                <div class="s">flagged from <%= PORDHelper.Num(RowsInFile) %> rows · <%= PORDHelper.Date(LoadedDate) %></div>
            </div>
            <div class="step <%= SentPackages == PackageCount ? "done" : "" %>">
                <div class="k">2 · Sent</div>
                <div class="v"><%= SentPackages %> / <%= PackageCount %></div>
                <div class="s">packages issued to AS Fin + POCs</div>
            </div>
            <div class="step <%= ReviewedCount == FlaggedCount ? "done" : (SentPackages > 0 ? "now" : "") %>">
                <div class="k">3 · Responses</div>
                <div class="v"><%= PORDHelper.Pct(ReviewedCount, FlaggedCount) %>%</div>
                <div class="s"><%= PORDHelper.Num(ReviewedCount) %> of <%= PORDHelper.Num(FlaggedCount) %> POs answered</div>
            </div>
            <div class="step <%= FinalisedPackages == PackageCount ? "done" : "" %>">
                <div class="k">4 · Finalised</div>
                <div class="v"><%= FinalisedPackages %> / <%= PackageCount %></div>
                <div class="s">by AS Fin</div>
            </div>
            <div class="step">
                <div class="k">5 · Verified next load</div>
                <div class="v"><%= PORDHelper.Num(ResolvedLastCycle) %></div>
                <div class="s">amended POs confirmed fixed last cycle</div>
            </div>
        </div>

        <%-- ============ Exceptions by category ============ --%>
        <section class="pord-section">
            <div class="pord-section-label">Exceptions this cycle</div>
            <div class="pord-hero-grid">
                <div class="pord-hero">
                    <div class="lbl">Open POs flagged</div>
                    <div class="val"><%= PORDHelper.Num(FlaggedCount) %></div>
                    <div class="sub"><strong>$<%= PORDHelper.MoneyShort(ValueStillToDeliver) %></strong> still to deliver across <strong><%= DistinctBps %></strong> suppliers and <strong><%= PackageCount %></strong> programs</div>
                </div>
                <%= RenderCategoryCards() %>
            </div>
        </section>

        <div class="stat-grid pord-stats6">
            <div class="stat"><div class="lbl">Open packages</div><div class="val"><%= OpenPackages %></div><div class="sub">not yet finalised</div></div>
            <div class="stat warn"><div class="lbl">Due soon</div><div class="val"><%= DueSoon %></div><div class="sub">within <%= PORDHelper.ReminderWindowDays %> days</div></div>
            <div class="stat err"><div class="lbl">Overdue</div><div class="val"><%= Overdue %></div><div class="sub">past respond-by date</div></div>
            <div class="stat err"><div class="lbl">Repeat offenders</div><div class="val"><%= RepeatCount %></div><div class="sub">Review Nbr &ge; <%= PORDHelper.EscalateAtReview %></div></div>
            <div class="stat"><div class="lbl">Active exclusions</div><div class="val"><%= ActiveExclusions %></div><div class="sub">skipped at load</div></div>
            <div class="stat warn"><div class="lbl">BP master issues</div><div class="val"><%= BpIssues %></div><div class="sub">to send to DFIM</div></div>
        </div>

        <div class="pord-mix pord-section">
            <div class="card-head"><h2 style="font-size:16px;">Responses</h2><span class="muted" style="font-size:12px;">current cycle · <%= PORDHelper.Num(ReviewedCount) %> of <%= PORDHelper.Num(FlaggedCount) %> answered</span></div>
            <%= RenderResponseMix() %>
        </div>

        <%-- ============ Packages ============ --%>
        <div class="card">
            <div class="card-head">
                <h2>Review packages</h2>
                <a class="btn btn-ghost btn-sm" href="PORD_SendOuts.aspx">Manage on Send-outs &rarr;</a>
            </div>
            <p class="card-lead">One package per Delivery Manager program. AS Fin sees the whole package and finalises it; each PO contact sees only their own POs.</p>
            <div class="tbl-wrap">
                <table class="tbl">
                    <thead><tr>
                        <th>Program</th><th>AS Fin</th><th class="num">POs</th><th class="num">Repeat</th>
                        <th>Responses</th><th>Due</th><th>Status</th>
                    </tr></thead>
                    <tbody>
                    <asp:Repeater ID="rptPackages" runat="server">
                        <ItemTemplate>
                            <tr>
                                <td><strong><%# PORDHelper.Enc(P(Container.DataItem).DmProgram) %></strong><span class="sub">#<%# P(Container.DataItem).PackageID %></span></td>
                                <td><%# AsFinCell(P(Container.DataItem).DmProgram) %></td>
                                <td class="num"><%# PoCount(P(Container.DataItem).PackageID) %></td>
                                <td class="num"><%# RepeatFor(P(Container.DataItem).PackageID) %></td>
                                <td><%# ProgressCell(P(Container.DataItem).PackageID) %></td>
                                <td class="nowrap"><%# PORDHelper.Date(P(Container.DataItem).DueDate) %></td>
                                <td class="nowrap"><%# PORDHelper.StatusPill(P(Container.DataItem).Status, P(Container.DataItem).DueDate) %></td>
                            </tr>
                        </ItemTemplate>
                    </asp:Repeater>
                    </tbody>
                </table>
            </div>
        </div>

        <div class="pord-cols" style="margin-top:16px;">
            <div class="card">
                <div class="card-head"><h2>Repeat exceptions</h2><span class="muted" style="font-size:12px;">flagged in 2+ rounds</span></div>
                <p class="card-lead">POs that were flagged before and are still non-compliant. These escalate to AS Fin.</p>
                <div class="tbl-wrap" style="max-height:420px;">
                    <table class="tbl tbl-compact">
                        <thead><tr><th>PO</th><th>Supplier</th><th>Terms</th><th class="num">Nbr</th><th>Program</th></tr></thead>
                        <tbody>
                        <asp:Repeater ID="rptRepeat" runat="server">
                            <ItemTemplate>
                                <tr>
                                    <td class="nowrap"><%# PORDHelper.PoLinkHtml(Po(Container.DataItem).PoNumber) %></td>
                                    <td><%# PORDHelper.Enc(Po(Container.DataItem).BpName) %></td>
                                    <td class="nowrap"><%# TermsHtml(Po(Container.DataItem)) %></td>
                                    <td class="num"><%# PORDHelper.ReviewNbrChip(Po(Container.DataItem).ReviewNbr) %></td>
                                    <td><%# PORDHelper.Enc(Po(Container.DataItem).DmProgram) %></td>
                                </tr>
                            </ItemTemplate>
                        </asp:Repeater>
                        </tbody>
                    </table>
                </div>
            </div>
            <div class="card">
                <div class="card-head"><h2>Recent loads</h2><a class="btn btn-ghost btn-sm" href="PORD_Load.aspx">Load file &rarr;</a></div>
                <p class="card-lead">Each monthly load re-checks every open PO. POs that were due to be amended and have dropped out are counted as resolved.</p>
                <div class="tbl-wrap">
                    <table class="tbl tbl-compact">
                        <thead><tr><th>File</th><th>Loaded</th><th class="num">Flagged</th><th class="num">Excluded</th><th class="num">Repeat</th><th class="num">Resolved</th></tr></thead>
                        <tbody>
                        <asp:Repeater ID="rptBatches" runat="server">
                            <ItemTemplate>
                                <tr>
                                    <td><%# PORDHelper.Enc(B(Container.DataItem).FileName) %><span class="sub">by <%# PORDHelper.Enc(B(Container.DataItem).LoadedBy) %></span></td>
                                    <td class="nowrap"><%# PORDHelper.Date(B(Container.DataItem).LoadedDate) %></td>
                                    <td class="num"><%# B(Container.DataItem).Flagged %></td>
                                    <td class="num"><%# B(Container.DataItem).Excluded %></td>
                                    <td class="num"><%# B(Container.DataItem).Repeat %></td>
                                    <td class="num" style="color:var(--ok);font-weight:600;"><%# B(Container.DataItem).Resolved %></td>
                                </tr>
                            </ItemTemplate>
                        </asp:Repeater>
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
    </main>

    <%= RenderFooter() %>
</div>
</form>
</body>
</html>
