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
                <div class="crumb">PO Review · Purchase order compliance</div>
                <h1>Dashboard</h1>
                <p class="lead">Current cycle: <strong><%= PORDHelper.Enc(CycleLabel) %></strong>. Open purchase orders flagged by each compliance check, reviewed in one package per program.</p>
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

        <%-- ============ Check tabs: All checks | one tab per active check ============ --%>
        <nav class="pord-dtabs" aria-label="Compliance checks"><%= CheckTabsHtml %></nav>

        <%-- ============ Cycle loop (scoped to the selected check) ============ --%>
        <div class="pord-loop" aria-label="Monthly review cycle">
            <div class="step done">
                <div class="k">1 · Loaded</div>
                <div class="v"><%= PORDHelper.Num(FlaggedCount) %></div>
                <div class="s"><%= PORDHelper.Enc(LoadedText) %></div>
            </div>
            <div class="step <%= SentPackages == PackageCount ? "done" : "" %>">
                <div class="k">2 · Sent</div>
                <div class="v"><%= SentPackages %> / <%= PackageCount %></div>
                <div class="s">packages issued to AS Fin + POCs</div>
            </div>
            <div class="step <%= ReviewedCount == FlaggedCount ? "done" : (SentPackages > 0 ? "now" : "") %>">
                <div class="k">3 · Responses</div>
                <div class="v"><%= PORDHelper.Pct(ReviewedCount, FlaggedCount) %>%</div>
                <div class="s"><%= PORDHelper.Num(ReviewedCount) %> of <%= PORDHelper.Num(FlaggedCount) %> exceptions answered</div>
            </div>
            <div class="step <%= FinalisedPackages == PackageCount ? "done" : "" %>">
                <div class="k">4 · Finalised</div>
                <div class="v"><%= FinalisedPackages %> / <%= PackageCount %></div>
                <div class="s">by AS Fin</div>
            </div>
            <div class="step">
                <div class="k">5 · Verified next load</div>
                <div class="v"><%= PORDHelper.Num(ResolvedLastCycle) %></div>
                <div class="s">fixes confirmed by the latest load</div>
            </div>
        </div>

        <% if (IsAllView) { %>
        <%-- ============ All checks: one card per registered check ============ --%>
        <section class="pord-section">
            <div class="pord-section-label">Checks</div>
            <div class="pord-checks"><%= CheckCardsHtml %></div>
        </section>
        <% } else { %>
        <%-- ============ One check: exceptions by issue ============ --%>
        <section class="pord-section">
            <div class="pord-section-label"><%= PORDHelper.Enc(Check.Name) %> — exceptions this cycle</div>
            <div class="pord-hero-grid" style="grid-template-columns:minmax(260px,1.35fr) repeat(<%= Math.Max(1, Check.Issues.Count) %>, 1fr);">
                <div class="pord-hero">
                    <div class="lbl">Open POs flagged</div>
                    <div class="val"><%= PORDHelper.Num(FlaggedCount) %></div>
                    <div class="sub"><strong>$<%= PORDHelper.MoneyShort(ValueStillToDeliver) %></strong> still to deliver across <strong><%= DistinctBps %></strong> suppliers</div>
                </div>
                <%= IssueCardsHtml %>
            </div>
        </section>
        <% } %>

        <div class="stat-grid pord-stats6">
            <div class="stat"><div class="lbl">Open packages</div><div class="val"><%= OpenPackages %></div><div class="sub">not yet finalised</div></div>
            <div class="stat warn"><div class="lbl">Due soon</div><div class="val"><%= DueSoon %></div><div class="sub">within <%= PORDHelper.ReminderWindowDays %> days</div></div>
            <div class="stat err"><div class="lbl">Overdue</div><div class="val"><%= Overdue %></div><div class="sub">past respond-by date</div></div>
            <div class="stat err"><div class="lbl">Repeat offenders</div><div class="val"><%= RepeatCount %></div><div class="sub">Review Nbr &ge; <%= PORDHelper.EscalateAtReview %></div></div>
            <div class="stat"><div class="lbl">Active exclusions</div><div class="val"><%= ActiveExclusions %></div><div class="sub">skipped at load</div></div>
            <%= SixthTileHtml %>
        </div>

        <div class="pord-mix pord-section">
            <div class="card-head"><h2 style="font-size:16px;">Responses</h2><span class="muted" style="font-size:12px;"><%= PORDHelper.Enc(ScopeLabel) %> · <%= PORDHelper.Num(ReviewedCount) %> of <%= PORDHelper.Num(FlaggedCount) %> answered</span></div>
            <%= RenderResponseMix() %>
        </div>

        <%-- ============ Packages ============ --%>
        <div class="card">
            <div class="card-head">
                <h2>Review packages</h2>
                <a class="btn btn-ghost btn-sm" href="PORD_SendOuts.aspx">Manage on Send-outs &rarr;</a>
            </div>
            <p class="card-lead">One package per Delivery Manager program, covering every check. AS Fin gets one email and one review page with a tab per check; each PO contact sees only their own POs.</p>
            <div class="tbl-wrap">
                <table class="tbl">
                    <thead><tr>
                        <th>Program</th><th>AS Fin</th><%= CheckColumnHeads %><th class="num">Repeat</th>
                        <th>Responses<%= IsAllView ? "" : " (" + PORDHelper.Enc(Check.ShortName) + ")" %></th><th>Due</th><th>Status</th>
                    </tr></thead>
                    <tbody>
                    <asp:Repeater ID="rptPackages" runat="server">
                        <ItemTemplate>
                            <tr>
                                <td><strong><%# PORDHelper.Enc(P(Container.DataItem).DmProgram) %></strong><span class="sub">#<%# P(Container.DataItem).PackageID %></span></td>
                                <td><%# AsFinCell(P(Container.DataItem).DmProgram) %></td>
                                <%# CheckColumnCells(P(Container.DataItem).PackageID) %>
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
                <p class="card-lead">Exceptions that were flagged before and are still not fixed. These escalate to AS Fin.</p>
                <div class="tbl-wrap" style="max-height:420px;">
                    <table class="tbl tbl-compact">
                        <thead><tr><th>PO</th><th>Supplier</th><th>Issue</th><th class="num">Nbr</th><th>Program</th></tr></thead>
                        <tbody>
                        <asp:Repeater ID="rptRepeat" runat="server">
                            <ItemTemplate>
                                <tr>
                                    <td class="nowrap"><%# PORDHelper.PoLinkHtml(Po(Container.DataItem).PoNumber) %><span class="sub"><%# PORDHelper.Enc(PORDChecks.Get(Po(Container.DataItem).CheckType).ShortName) %></span></td>
                                    <td><%# PORDHelper.Enc(Po(Container.DataItem).BpName) %></td>
                                    <td class="nowrap"><%# PORDChecks.Get(Po(Container.DataItem).CheckType).IssueCell(Po(Container.DataItem)) %></td>
                                    <td class="num"><%# PORDHelper.ReviewNbrChip(Po(Container.DataItem).ReviewNbr) %></td>
                                    <td><%# PORDHelper.Enc(Po(Container.DataItem).DmProgram) %></td>
                                </tr>
                            </ItemTemplate>
                        </asp:Repeater>
                        </tbody>
                    </table>
                    <asp:PlaceHolder ID="phNoRepeat" runat="server" Visible="false"><div class="pord-empty">No repeat exceptions for this check yet.</div></asp:PlaceHolder>
                </div>
            </div>
            <div class="card">
                <div class="card-head"><h2>Recent loads</h2><a class="btn btn-ghost btn-sm" href="PORD_Load.aspx">Load file &rarr;</a></div>
                <p class="card-lead">Each check has its own monthly extract. Every load re-checks all open POs; exceptions that were due to be fixed and have dropped out count as resolved.</p>
                <div class="tbl-wrap">
                    <table class="tbl tbl-compact">
                        <thead><tr><th>File</th><th>Loaded</th><th class="num">Flagged</th><th class="num">Excluded</th><th class="num">Repeat</th><th class="num">Resolved</th></tr></thead>
                        <tbody>
                        <asp:Repeater ID="rptBatches" runat="server">
                            <ItemTemplate>
                                <tr>
                                    <td><%# PORDHelper.Enc(B(Container.DataItem).FileName) %><span class="sub"><%# PORDHelper.Enc(PORDChecks.Get(B(Container.DataItem).CheckType).ShortName) %> · by <%# PORDHelper.Enc(B(Container.DataItem).LoadedBy) %></span></td>
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
