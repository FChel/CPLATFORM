<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="LPPI_Summary.aspx.cs" Inherits="CPlatform.LPPI.LPPI_Summary" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review &mdash; Summary</title>
    <link rel="stylesheet" href="../css/lppi.css" />
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell summary-shell">
    <%= RenderHeader("summary") %>

    <main class="lppi-main">

        <%-- Page head + scope picker --%>
        <div class="page-head">
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Summary</h1>
                <p class="lead">In-flight visibility of the current review cycle &mdash; counts, reason-code split, and outstanding work by program and POC.</p>
            </div>
            <div class="btn-row">
                <asp:Button ID="btnExport" runat="server" CssClass="btn btn-primary"
                    Text="Export full data" OnClick="btnExport_Click" />
                <asp:Button ID="btnExportNoPoc" runat="server" CssClass="btn btn-secondary"
                    Text="Export no-POC lines" OnClick="btnExportNoPoc_Click"
                    ToolTip="Lines in scope where the LPPI file did not supply a POC email. Send to AS Fin for triage." />
            </div>
        </div>

        <%-- Scope + CM pickers — independent filters. Scope picks the
             universe of packages (current cycle / all active / a specific
             batch); CM narrows within that universe to a single program. --%>
        <section class="summary-scope-bar">
            <div class="summary-scope-field">
                <label class="summary-scope-label" for="<%= ddlCm.ClientID %>">Capability Manager</label>
                <asp:DropDownList ID="ddlCm" runat="server"
                    AutoPostBack="true"
                    CssClass="summary-scope-select"
                    OnSelectedIndexChanged="ddlCm_SelectedIndexChanged" />
            </div>
            <div class="summary-scope-field">
                <label class="summary-scope-label" for="<%= ddlScope.ClientID %>">Scope</label>
                <asp:DropDownList ID="ddlScope" runat="server"
                    AutoPostBack="true"
                    CssClass="summary-scope-select"
                    OnSelectedIndexChanged="ddlScope_SelectedIndexChanged" />
            </div>
            <div class="summary-scope-meta">
                <asp:Literal ID="litScopeMeta" runat="server" />
            </div>
        </section>

        <asp:PlaceHolder ID="phMessage" runat="server" />

        <%-- ============================================================
             1. Scope header — package / doc counts, reviewed progress,
                dollar exposure.
             ============================================================ --%>
        <section class="summary-section">
            <div class="section-label">Cycle overview</div>
            <div class="summary-overview-grid">
                <div class="overview-card">
                    <div class="lbl">Packages</div>
                    <div class="val"><asp:Literal ID="litOvPackages" runat="server" Text="0"/></div>
                </div>
                <div class="overview-card">
                    <div class="lbl">Reviewed</div>
                    <div class="val">
                        <asp:Literal ID="litOvReviewed" runat="server" Text="0"/>
                        <span class="sub">of <asp:Literal ID="litOvDocs2" runat="server" Text="0"/></span>
                    </div>
                    <div class="overview-bar">
                        <div class="track"><div class="fill" style="width: <%= OvReviewedPct %>%"></div></div>
                        <div class="pct"><%= OvReviewedPct %>% complete</div>
                    </div>
                </div>
                <div class="overview-card split">
                    <div class="split-row">
                        <span class="lbl">Payable (confirmed)</span>
                        <span class="val"><span class="currency">$</span><asp:Literal ID="litOvPayable" runat="server" Text="0.00"/></span>
                    </div>
                    <div class="split-row">
                        <span class="lbl">Not payable</span>
                        <span class="val"><span class="currency">$</span><asp:Literal ID="litOvNotPayable" runat="server" Text="0.00"/></span>
                    </div>
                </div>
                <div class="overview-card exposure">
                    <div class="lbl">Total exposure</div>
                    <div class="val">
                        <span class="currency">$</span><asp:Literal ID="litOvInterest" runat="server" Text="0.00"/>
                    </div>
                </div>
            </div>
        </section>

        <%-- ============================================================
             2. Payable reasons — Payable-outcome reason codes with at
                least one in-scope document. Code-behind filters the
                shared by-reason-code result in memory.
             ============================================================ --%>
        <section class="summary-section">
            <div class="section-label">Payable reasons</div>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptByPayable" runat="server">
                    <HeaderTemplate>
                        <table class="tbl summary-tbl">
                            <thead>
                                <tr>
                                    <th>Code</th>
                                    <th>Description</th>
                                    <th class="num">Documents</th>
                                    <th class="num">Interest</th>
                                    <th class="num">% of total</th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr>
                            <td><strong><%# LPPIHelper.Enc(Eval("Code")) %></strong></td>
                            <td><%# LPPIHelper.Enc(Eval("Description")) %></td>
                            <td class="num"><%# FormatInt(Eval("DocCount")) %></td>
                            <td class="num"><%# FormatMoneyCell(Eval("Interest")) %></td>
                            <td class="num"><%# FormatPctCell(Eval("PctOfTotal")) %></td>
                        </tr>
                    </ItemTemplate>
                    <FooterTemplate>
                            </tbody>
                        </table>
                    </FooterTemplate>
                </asp:Repeater>
                <asp:PlaceHolder ID="phNoPayable" runat="server" Visible="false">
                    <div class="empty-state">No documents coded as Payable in scope.</div>
                </asp:PlaceHolder>
            </div>
        </section>

        <%-- ============================================================
             3. Non-payment reasons — NotPayable-outcome reason codes.
             ============================================================ --%>
        <section class="summary-section">
            <div class="section-label">Non-payment reasons</div>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptByNonPayment" runat="server">
                    <HeaderTemplate>
                        <table class="tbl summary-tbl">
                            <thead>
                                <tr>
                                    <th>Code</th>
                                    <th>Description</th>
                                    <th class="num">Documents</th>
                                    <th class="num">Interest</th>
                                    <th class="num">% of total</th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr>
                            <td><strong><%# LPPIHelper.Enc(Eval("Code")) %></strong></td>
                            <td><%# LPPIHelper.Enc(Eval("Description")) %></td>
                            <td class="num"><%# FormatInt(Eval("DocCount")) %></td>
                            <td class="num"><%# FormatMoneyCell(Eval("Interest")) %></td>
                            <td class="num"><%# FormatPctCell(Eval("PctOfTotal")) %></td>
                        </tr>
                    </ItemTemplate>
                    <FooterTemplate>
                            </tbody>
                        </table>
                    </FooterTemplate>
                </asp:Repeater>
                <asp:PlaceHolder ID="phNoNonPayment" runat="server" Visible="false">
                    <div class="empty-state">No documents coded as Not Payable in scope.</div>
                </asp:PlaceHolder>
            </div>
        </section>

        <%-- ============================================================
             4. By CM program — full width, with totals row.
                Code-behind builds the totals row and feeds it via the
                separate tfoot literals so the Repeater itself does not
                need an inline pivot.
             ============================================================ --%>
        <section class="summary-section">
            <div class="section-label">Progress By Capability Manager program</div>
            <div class="tbl-wrap">
                <asp:PlaceHolder ID="phProgramTable" runat="server">
                    <table class="tbl summary-tbl">
                        <thead>
                            <tr>
                                <th>Program</th>
                                <th class="num">Packages</th>
                                <th>Status</th>
                                <th>Progress</th>
                                <th class="num">POCs</th>
                                <th class="num">Flagged for reload</th>
                                <th class="num">Deactivated</th>
                                <th class="num">Interest</th>
                            </tr>
                        </thead>
                        <tbody>
                            <asp:Repeater ID="rptByProgram" runat="server">
                                <ItemTemplate>
                                    <tr>
                                        <td><%# LPPIHelper.Enc(Eval("Program")) %></td>
                                        <td class="num"><%# FormatInt(Eval("PackageCount")) %></td>
                                        <td><%# RenderStatusPills(Eval("Statuses")) %></td>
                                        <td><%# RenderProgressBar(Eval("ReviewedCount"), Eval("DocCount")) %></td>
                                        <td class="num"><%# FormatInt(Eval("PocCount")) %></td>
                                        <td class="num"><%# FormatReloadCell(Eval("FlaggedReloadCount")) %></td>
                                        <td class="num"><%# FormatReloadCell(Eval("DeactivatedCount")) %></td>
                                        <td class="num"><%# FormatMoneyCell(Eval("Interest")) %></td>
                                    </tr>
                                </ItemTemplate>
                            </asp:Repeater>
                        </tbody>
                        <tfoot>
                            <tr class="summary-row-total">
                                <td><strong>Total</strong></td>
                                <td class="num"><strong><asp:Literal ID="litProgTotPackages" runat="server" Text="0"/></strong></td>
                                <td></td>
                                <td><%= RenderProgressBar(ProgTotReviewed, ProgTotDocs) %></td>
                                <td class="num"><strong><asp:Literal ID="litProgTotPocs"     runat="server" Text="0"/></strong><span class="summary-foot-marker">*</span></td>
                                <td class="num"><strong><asp:Literal ID="litProgTotReload"   runat="server" Text="0"/></strong></td>
                                <td class="num"><strong><asp:Literal ID="litProgTotDeact"    runat="server" Text="0"/></strong></td>
                                <td class="num"><strong><asp:Literal ID="litProgTotInterest" runat="server" Text="$0.00"/></strong><span class="summary-foot-marker">*</span></td>
                            </tr>
                        </tfoot>
                    </table>
                </asp:PlaceHolder>
                <asp:PlaceHolder ID="phNoProgram" runat="server" Visible="false">
                    <div class="empty-state">No programs in scope.</div>
                </asp:PlaceHolder>
            </div>
            <p class="summary-foot-note">
                <span class="summary-foot-marker">*</span> POCs and Interest are per-program rollups, as is the document
                count shown in the Progress column. A document, POC or amount that spans more than one program is counted
                under each, so the Total row can exceed the distinct figures on the Cycle overview cards.
            </p>
            <asp:PlaceHolder ID="phReloadNote" runat="server" Visible="false">
                <p class="summary-foot-note summary-foot-note-warn">
                    <asp:Literal ID="litReloadNote" runat="server" />
                </p>
            </asp:PlaceHolder>
            <asp:PlaceHolder ID="phNoPocNote" runat="server" Visible="false">
                <p class="summary-foot-note summary-foot-note-warn">
                    <asp:Literal ID="litNoPocCount" runat="server" />
                </p>
            </asp:PlaceHolder>
        </section>

        <%-- ============================================================
             5. By POC — top 10 outstanding by document count.
                Useful for chase-up triage by volume — POCs sitting on
                the most pending decisions surface first.
             ============================================================ --%>
        <section class="summary-section">
            <div class="section-label">Top 10 outstanding by count</div>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptByPoc" runat="server">
                    <HeaderTemplate>
                        <table class="tbl summary-tbl">
                            <thead>
                                <tr>
                                    <th>POC email</th>
                                    <th class="num">Documents outstanding</th>
                                    <th class="num">Interest outstanding</th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr>
                            <td><%# LPPIHelper.Enc(Eval("PocEmail")) %></td>
                            <td class="num"><%# FormatInt(Eval("DocCount")) %></td>
                            <td class="num"><%# FormatMoneyCell(Eval("Interest")) %></td>
                        </tr>
                    </ItemTemplate>
                    <FooterTemplate>
                            </tbody>
                        </table>
                    </FooterTemplate>
                </asp:Repeater>
                <asp:PlaceHolder ID="phNoPoc" runat="server" Visible="false">
                    <div class="empty-state">Nothing outstanding &mdash; all in-scope documents are reviewed.</div>
                </asp:PlaceHolder>
            </div>
        </section>

        <%-- ============================================================
             6. By POC — top 10 outstanding by VALUE.
                Surfaces the highest-dollar pending exposure so big-ticket
                POCs are chased before the package is defaulted.
             ============================================================ --%>
        <section class="summary-section">
            <div class="section-label">Top 10 outstanding by value</div>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptByPocValue" runat="server">
                    <HeaderTemplate>
                        <table class="tbl summary-tbl">
                            <thead>
                                <tr>
                                    <th>POC email</th>
                                    <th class="num">Documents outstanding</th>
                                    <th class="num">Interest outstanding</th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr>
                            <td><%# LPPIHelper.Enc(Eval("PocEmail")) %></td>
                            <td class="num"><%# FormatInt(Eval("DocCount")) %></td>
                            <td class="num"><%# FormatMoneyCell(Eval("Interest")) %></td>
                        </tr>
                    </ItemTemplate>
                    <FooterTemplate>
                            </tbody>
                        </table>
                    </FooterTemplate>
                </asp:Repeater>
                <asp:PlaceHolder ID="phNoPocValue" runat="server" Visible="false">
                    <div class="empty-state">Nothing outstanding &mdash; all in-scope documents are reviewed.</div>
                </asp:PlaceHolder>
            </div>
            <p class="summary-foot-note">
                "Outstanding" = documents in scope with no reason code.
                Blank POC emails are grouped as <em>(no POC)</em> -
                use the <em>Export no-POC lines</em> button above to pull the underlying lines.
            </p>
        </section>
    </main>

    <footer class="lppi-footer">
        <span>LPPI Review &middot; <%= CurrentEnv %></span>
    </footer>
</div>
</form>
</body>
</html>
