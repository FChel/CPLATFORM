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
                    <div class="lbl">Documents</div>
                    <div class="val"><asp:Literal ID="litOvDocs" runat="server" Text="0"/></div>
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
                <div class="overview-card exposure">
                    <div class="lbl">Total exposure</div>
                    <div class="val">
                        <span class="currency">$</span><asp:Literal ID="litOvInterest" runat="server" Text="0.00"/>
                    </div>
                </div>
            </div>
        </section>

        <%-- ============================================================
             2. By reason code — full split with Awaiting pseudo-row.
             ============================================================ --%>
        <section class="summary-section">
            <div class="section-label">
                <span>By reason code</span>
                <label class="summary-toggle">
                    <input type="checkbox" id="chkShowAllCodes" />
                    <span>Show codes with zero documents</span>
                </label>
            </div>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptByReason" runat="server">
                    <HeaderTemplate>
                        <table class="tbl summary-tbl" id="tblByReason">
                            <thead>
                                <tr>
                                    <th>Code</th>
                                    <th>Description</th>
                                    <th>Outcome</th>
                                    <th class="num">Documents</th>
                                    <th class="num">Interest</th>
                                    <th class="num">% of total</th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr class="<%# RowClassForReason(Container.DataItem) %>"
                            data-count="<%# Eval("DocCount") %>">
                            <td><strong><%# LPPIHelper.Enc(Eval("Code")) %></strong></td>
                            <td><%# LPPIHelper.Enc(Eval("Description")) %></td>
                            <td><%# RenderOutcomePill(Eval("Outcome")) %></td>
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
                <asp:PlaceHolder ID="phNoReason" runat="server" Visible="false">
                    <div class="empty-state">No documents in scope.</div>
                </asp:PlaceHolder>
            </div>
        </section>

        <%-- ============================================================
             3. Non-payment reasons — same data filtered to NotPayable.
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
            <div class="section-label">By Capability Manager program</div>
            <div class="tbl-wrap">
                <asp:PlaceHolder ID="phProgramTable" runat="server">
                    <table class="tbl summary-tbl">
                        <thead>
                            <tr>
                                <th>Program</th>
                                <th class="num">Packages</th>
                                <th class="num">Documents</th>
                                <th>Progress</th>
                                <th class="num">POCs</th>
                                <th class="num">Interest</th>
                            </tr>
                        </thead>
                        <tbody>
                            <asp:Repeater ID="rptByProgram" runat="server">
                                <ItemTemplate>
                                    <tr>
                                        <td><%# LPPIHelper.Enc(Eval("Program")) %></td>
                                        <td class="num"><%# FormatInt(Eval("PackageCount")) %></td>
                                        <td class="num"><%# FormatInt(Eval("DocCount")) %></td>
                                        <td><%# RenderProgressBar(Eval("ReviewedCount"), Eval("DocCount")) %></td>
                                        <td class="num"><%# FormatInt(Eval("PocCount")) %></td>
                                        <td class="num"><%# FormatMoneyCell(Eval("Interest")) %></td>
                                    </tr>
                                </ItemTemplate>
                            </asp:Repeater>
                        </tbody>
                        <tfoot>
                            <tr class="summary-row-total">
                                <td><strong>Total</strong></td>
                                <td class="num"><strong><asp:Literal ID="litProgTotPackages" runat="server" Text="0"/></strong></td>
                                <td class="num"><strong><asp:Literal ID="litProgTotDocs"     runat="server" Text="0"/></strong></td>
                                <td><%= RenderProgressBar(ProgTotReviewed, ProgTotDocs) %></td>
                                <td class="num"><strong><asp:Literal ID="litProgTotPocs"     runat="server" Text="0"/></strong><span class="summary-foot-marker">*</span></td>
                                <td class="num"><strong><asp:Literal ID="litProgTotInterest" runat="server" Text="$0.00"/></strong></td>
                            </tr>
                        </tfoot>
                    </table>
                </asp:PlaceHolder>
                <asp:PlaceHolder ID="phNoProgram" runat="server" Visible="false">
                    <div class="empty-state">No programs in scope.</div>
                </asp:PlaceHolder>
            </div>
            <asp:PlaceHolder ID="phNoPocNote" runat="server" Visible="false">
                <p class="summary-foot-note summary-foot-note-warn">
                    <asp:Literal ID="litNoPocCount" runat="server" />		
                </p>
            </asp:PlaceHolder>
        </section>

        <%-- ============================================================
             5. By POC — top 10 outstanding, sorted by doc count.
             ============================================================ --%>
        <section class="summary-section">
            <div class="section-label">By POC &mdash; top 10 outstanding</div>
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
            <p class="summary-foot-note">
                "Outstanding" = documents in scope with no reason code.
            </p>
        </section>

        <p class="summary-foot-note">
            Numbers refresh on every page load. Detailed reporting will be developed in Power BI.
        </p>

    </main>

    <footer class="lppi-footer">
        <span>LPPI Review &middot; <%= CurrentEnv %></span>
    </footer>
</div>
</form>

<script>
// Show / hide zero-count rows in the By-reason-code table.
// Vanilla, no postback, no jQuery — matches the rest of the codebase.
(function () {
    var toggle = document.getElementById('chkShowAllCodes');
    var tbl    = document.getElementById('tblByReason');
    if (!toggle || !tbl) return;

    function apply() {
        var rows = tbl.querySelectorAll('tbody tr');
        for (var i = 0; i < rows.length; i++) {
            var c = parseInt(rows[i].getAttribute('data-count') || '0', 10);
            // Always show rows with count > 0 and the Awaiting pseudo-row.
            // Hide the rest unless the toggle is on.
            if (c > 0) {
                rows[i].style.display = '';
            } else {
                rows[i].style.display = toggle.checked ? '' : 'none';
            }
        }
    }

    toggle.addEventListener('change', apply);
    apply();
})();
</script>
</body>
</html>
