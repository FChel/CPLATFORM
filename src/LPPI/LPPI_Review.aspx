<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="LPPI_Review.aspx.cs" Inherits="CPlatform.LPPI.LPPI_Review" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <style>
        /* Read-only mode — a single hook on the shell disables every form
           input on the page. The save handler is the authoritative gate;
           this is purely a UX hint so admin QA viewers cannot accidentally
           type into fields they are not meant to edit. */
        .review-shell[data-readonly="1"] select,
        .review-shell[data-readonly="1"] textarea,
        .review-shell[data-readonly="1"] input[type="text"],
        .review-shell[data-readonly="1"] input[type="checkbox"] {
            pointer-events: none;
            background: #f8f8f8;
            color: var(--ink-3);
        }
        .review-shell[data-readonly="1"] #saveAllBtn,
        .review-shell[data-readonly="1"] #bulkBar {
            display: none !important;
        }
    </style>
</head>
<body>

<asp:PlaceHolder ID="phError" runat="server">
<div class="review-shell">
    <div class="review-head">
        <div class="review-brand">
            <div class="brand-mark">
                <svg viewBox="0 0 24 24" width="20" height="20" stroke="#fff" fill="none" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                    <path d="M14 2v6h6"/><circle cx="12" cy="15" r="3"/><path d="M12 13v2l1 1"/>
                </svg>
            </div>
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Review link invalid or expired</h1>
            </div>
        </div>
    </div>
    <p style="padding:0 4px;">This review link is no longer active.
    It may have expired, already been used, or the package may be closed. Please contact your Capability Manager if you believe this is an error.</p>
</div>
</asp:PlaceHolder>

<asp:PlaceHolder ID="phReview" runat="server" Visible="false">
<input type="hidden" id="reviewToken" value="<%= LPPIHelper.Enc(TokenForClient) %>" />
<input type="hidden" id="reviewReadOnly" value="<%= IsReadOnly ? "1" : "0" %>" />

<div class="review-shell" data-readonly="<%= IsReadOnly ? "1" : "0" %>">

    <%-- Status banner — only rendered for non-active package statuses --%>
    <%= StatusBannerHtml %>

    <%-- Review header --%>
    <div class="review-head">
        <div class="review-head-inner">
            <div class="review-brand">
                <div class="brand-mark">
                    <svg viewBox="0 0 24 24" width="20" height="20" stroke="#fff" fill="none" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                        <path d="M14 2v6h6"/><circle cx="12" cy="15" r="3"/><path d="M12 13v2l1 1"/>
                    </svg>
                </div>
                <div>
                    <div class="crumb">LPPI Review</div>
                    <h1><%= LPPIHelper.Enc(ProgramName) %></h1>
                </div>
            </div>
        </div>
        <div class="review-meta">
            <div class="meta-item">
                <span class="meta-label">Due date</span>
                <span class="meta-value"><%= LPPIHelper.FormatDate(DueDate) %></span>
                <span class="meta-sub <%= DueCssClass %>"><%= DueCountdownText %></span>
            </div>
            <div class="meta-item">
                <span class="meta-label">Progress</span>
                <span class="meta-value" id="progressLabel"><%= ReviewedCount %> of <%= TotalCount %></span>
                <div class="progress-track"><div class="progress-bar" id="progressBar" style="width: <%= ProgressPercent %>%"></div></div>
            </div>
            <div class="meta-item">
                <a href="https://www.finance.gov.au/publications/resource-management-guides/supplier-pay-time-or-pay-interest-policy-rmg-417"
                   target="_blank" rel="noopener" class="policy-link">RMG-417 — Supplier Pay On-Time or Pay Interest Policy</a>
            </div>
        </div>
    </div>

    <%-- Tab strip --%>
    <div class="review-tabs" role="tablist" aria-label="Review views">
        <button type="button" id="tabReason" class="review-tab active" role="tab" aria-selected="true"  aria-controls="paneReason">Reason code entry</button>
        <button type="button" id="tabLines"  class="review-tab"        role="tab" aria-selected="false" aria-controls="paneLines">All lines</button>
    </div>

    <%-- Toolbar — outside both panes --%>
    <div class="toolbar">
        <div class="toolbar-left">
            <div class="search-wrap">
                <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                    <circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>
                </svg>
                <input type="text" id="searchBox" class="input" placeholder="Search vendor, doc no, PO, WBS, DM program…" />
            </div>
            <select id="statusFilter" class="input" title="Review status">
                <option value="">All statuses</option>
                <option value="not-reviewed">Not reviewed</option>
                <option value="reviewed">Reviewed</option>
                <option value="payable">Payable</option>
                <option value="notpayable">Not payable</option>
                <option value="needs-comments">Needs attention</option>
            </select>
            <select id="filterDm" class="input filter-facet" title="Delivery Manager Program">
                <option value="">All DM Programs</option>
                <%= BuildFacetOptions("dm") %>
            </select>
            <select id="filterWbs" class="input filter-facet" title="WBS element">
                <option value="">All WBS</option>
                <%= BuildFacetOptions("wbs") %>
            </select>
            <select id="filterPc" class="input filter-facet" title="Profit centre">
                <option value="">All profit centres</option>
                <%= BuildFacetOptions("pc") %>
            </select>
        </div>
        <div class="toolbar-right">
            <span id="saveIndicator" class="save-indicator" role="status" aria-live="polite">All changes saved</span>
            <button type="button" id="saveAllBtn" class="btn btn-primary" title="Save every unsaved row">
                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                    <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                    <polyline points="17 21 17 13 7 13 7 21"/>
                    <polyline points="7 3 7 8 15 8"/>
                </svg>
                <span>Save</span>
            </button>
        </div>
    </div>

    <%-- Done banner --%>
    <div id="doneBanner" class="done-banner" role="status">
        <strong>All done.</strong> Every document in this package has a reason code. Thanks for completing the review.
    </div>

    <%-- ================================================================
         TAB 1 — Reason code entry
         ================================================================ --%>
    <div id="paneReason" class="review-pane active" role="tabpanel" aria-labelledby="tabReason">

        <asp:PlaceHolder ID="phEmpty" runat="server" Visible="false">
            <div class="empty-state">
                <h2>Nothing to review</h2>
                <p>This package does not contain any outstanding documents.</p>
            </div>
        </asp:PlaceHolder>

        <div class="tbl-wrap review-table-wrap">
            <asp:Repeater ID="rptMain" runat="server">
                <HeaderTemplate>
                    <table class="tbl tbl-review">
                        <thead>
                            <tr>
                                <th class="col-sel"></th>
                                <th class="col-doc">Document</th>
                                <th class="col-vendor">Vendor</th>
                                <th class="col-po">PO</th>
                                <th class="col-wbs">WBS Element</th>
                                <th class="col-pc">Profit Centre</th>
                                <th class="col-dm">Delivery Manager Program</th>
                                <th class="col-days num">Days Late</th>
                                <th class="col-int num">Interest Payable</th>
                                <th class="col-reason">Reason Code</th>
                                <th class="col-comments">Comments</th>
                                <th class="col-obj">Evidence (Obj Ref)</th>
                                <th class="col-expand"></th>
                            </tr>
                        </thead>
                        <tbody>
                </HeaderTemplate>
                <ItemTemplate>
                    <tr class="doc-main"
                        data-doc-no='<%# LPPIHelper.Enc(Eval("DocNoAccounting")) %>'
                        data-first-line-id='<%# Eval("FirstLineDocumentID") %>'
                        data-search='<%# LPPIHelper.Enc((string)Eval("SearchBlob")) %>'
                        data-dm='<%# LPPIHelper.Enc(Eval("DeliveryManagerProgram")) %>'
                        data-poc='<%# LPPIHelper.Enc(Eval("PocEmail")) %>'
                        data-wbs='<%# LPPIHelper.Enc(Eval("WbsElement")) %>'
                        data-pc='<%# LPPIHelper.Enc(Eval("ProfitCentre")) %>'
                        data-outcome='<%# LPPIHelper.Enc(Eval("ReasonOutcome")) %>'
                        data-requires='<%# Convert.ToBoolean(Eval("RequiresComments")) ? "1" : "0" %>'>
                        <td class="col-sel">
                            <input type="checkbox" class="rowselect" data-doc-no='<%# LPPIHelper.Enc(Eval("DocNoAccounting")) %>' />
                        </td>
                        <td class="col-doc">
                            <%# LPPIHelper.SapFiNumberHtml(Eval("DocNoAccounting"), Eval("CompanyCode"), Eval("ClearingMonth")) %>
                            <span class="line-count-inline muted">(<%# Eval("LineCount") %> line<%# Convert.ToInt32(Eval("LineCount")) == 1 ? "" : "s" %>)</span>
                        </td>
                        <td class="col-vendor" title='<%# LPPIHelper.Enc(Eval("VendorName")) + " (" + LPPIHelper.Enc(Eval("VendorNum")) + ")" %>'>
                            <%# LPPIHelper.Enc(Eval("VendorName")) %>
                        </td>
                        <td class="col-po"><%# LPPIHelper.SapPoNumberHtml(Eval("PoNumber")) %></td>
                        <td class="col-wbs" title='<%# LPPIHelper.Enc(Eval("WbsDesc")) %>'><%# LPPIHelper.Enc(Eval("WbsElement")) %></td>
                        <td class="col-pc"><%# LPPIHelper.Enc(Eval("ProfitCentre")) %></td>
                        <td class="col-dm" title='<%# LPPIHelper.Enc(Eval("DeliveryManagerName")) %>'>
                            <%# LPPIHelper.Enc(Eval("DeliveryManagerProgram")) %>
                        </td>
                        <td class="col-days num"><%# Eval("DaysVariance") %></td>
                        <td class="col-int num"><%# LPPIHelper.FormatMoney(Eval("TotalInterest")) %></td>
                        <td class="col-reason">
                            <select class="reason-select input">
                                <option value="" data-outcome="" data-requires="0">—</option>
                                <%# BuildReasonOptions(Eval("SelectedReasonCodeID")) %>
                            </select>
                        </td>
                        <td class="col-comments">
                            <textarea class="comments-input input" rows="1" title='<%# LPPIHelper.Enc(Eval("Comments")) %>'><%# LPPIHelper.Enc(Eval("Comments")) %></textarea>
                        </td>
                        <td class="col-obj">
                            <input type="text" class="objref-input input" value='<%# LPPIHelper.Enc(Eval("ObjectiveReference")) %>' maxlength="100" title='<%# LPPIHelper.Enc(Eval("ObjectiveReference")) %>' />
                        </td>
                        <td class="col-expand">
                            <button type="button" class="btn-expand" data-doc-no='<%# LPPIHelper.Enc(Eval("DocNoAccounting")) %>'
                                    title="Show all lines for this document" aria-expanded="false">
                                <svg class="chevron-icon" viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                                    <polyline points="9 18 15 12 9 6"/>
                                </svg>
                            </button>
                        </td>
                    </tr>
                    <tr class="doc-main-msg" data-doc-no='<%# LPPIHelper.Enc(Eval("DocNoAccounting")) %>'>
                        <td colspan="13"><div class="row-msg" role="alert"></div></td>
                    </tr>
                    <%-- Expand panel — populated from rptDetail DOM by JS --%>
                    <tr class="doc-expand-panel" data-doc-no='<%# LPPIHelper.Enc(Eval("DocNoAccounting")) %>' style="display:none;">
                        <td colspan="13" class="expand-panel-cell">
                            <div class="expand-panel-inner"></div>
                        </td>
                    </tr>
                </ItemTemplate>
                <FooterTemplate>
                        </tbody>
                    </table>
                </FooterTemplate>
            </asp:Repeater>
        </div>

        <%-- Bulk action bar --%>
        <div id="bulkBar" class="bulk-bar" aria-live="polite">
            <span><span id="bulkCount">0</span> selected</span>
            <select id="bulkReason" class="input">
                <option value="" data-outcome="" data-requires="0">Apply reason code…</option>
                <%= BuildReasonOptions(null) %>
            </select>
            <button type="button" id="bulkApply" class="btn btn-primary">Apply</button>
            <button type="button" id="bulkClear" class="btn btn-ghost">Clear</button>
        </div>

    </div><%-- /paneReason --%>

    <%-- ================================================================
         TAB 2 — All lines (read-only detail)
         ================================================================ --%>
    <div id="paneLines" class="review-pane" role="tabpanel" aria-labelledby="tabLines">
        <div class="detail-scroll-wrap">
            <asp:Repeater ID="rptDetail" runat="server">
                <HeaderTemplate>
                    <table class="tbl tbl-detail">
                        <thead>
                            <tr>
                                <th class="col-doc">Document No.</th>
                                <th class="col-seq num">Line</th>
                                <th class="col-vendor">Vendor</th>
                                <th class="col-po">PO Number</th>
                                <th class="col-wbs">WBS Element</th>
                                <th class="col-gl">GL Account</th>
                                <th class="col-pc">Profit Centre</th>
                                <th class="col-tax">Tax Code</th>
                                <th class="col-dm">DM Program</th>
                                <th class="col-poc">POC Email</th>
                                <th class="col-date">Payment Date</th>
                                <th class="col-days num">Days Late</th>
                                <th class="col-int num">Interest Payable</th>
                                <th class="col-reason">Reason Code <span class="readonly-note">(from document)</span></th>
                                <th class="col-comments">Comments <span class="readonly-note">(from document)</span></th>
                                <th class="col-obj">Obj Ref <span class="readonly-note">(from document)</span></th>
                            </tr>
                        </thead>
                        <tbody>
                </HeaderTemplate>
                <ItemTemplate>
                    <tr class="detail-row"
                        data-doc-no='<%# LPPIHelper.Enc(Eval("DocNoAccounting")) %>'
                        data-search='<%# LPPIHelper.Enc((string)Eval("SearchBlob")) %>'
                        data-dm='<%# LPPIHelper.Enc(Eval("DeliveryManagerProgram")) %>'
                        data-poc='<%# LPPIHelper.Enc(Eval("PocEmail")) %>'
                        data-wbs='<%# LPPIHelper.Enc(Eval("WbsElement")) %>'
                        data-pc='<%# LPPIHelper.Enc(Eval("ProfitCentre")) %>'>
                        <td class="col-doc">
                            <%# LPPIHelper.SapFiNumberHtml(Eval("DocNoAccounting"), Eval("CompanyCode"), Eval("ClearingMonth")) %>
                        </td>
                        <td class="col-seq num"><span class="seq-chip"><%# string.Format("{0:000}", Eval("ItemSequence")) %></span></td>
                        <td class="col-vendor" title='<%# LPPIHelper.Enc(Eval("VendorName")) %>'><%# LPPIHelper.Enc(Eval("VendorName")) %></td>
                        <td class="col-po"><%# LPPIHelper.SapPoNumberHtml(Eval("PoNumber")) %></td>
                        <td class="col-wbs" title='<%# LPPIHelper.Enc(Eval("WbsDesc")) %>'><%# LPPIHelper.Enc(Eval("WbsElement")) %></td>
                        <td class="col-gl"><%# LPPIHelper.Enc(Eval("GlAccount")) %></td>
                        <td class="col-pc"><%# LPPIHelper.Enc(Eval("ProfitCentre")) %></td>
                        <td class="col-tax"><%# LPPIHelper.Enc(Eval("TaxCode")) %></td>
                        <td class="col-dm" title='<%# LPPIHelper.Enc(Eval("DeliveryManagerName")) %>'><%# LPPIHelper.Enc(Eval("DeliveryManagerProgram")) %></td>
                        <td class="col-poc"><%# LPPIHelper.Enc(Eval("PocEmail")) %></td>
                        <td class="col-date"><%# LPPIHelper.FormatDate(Eval("PaymentRunDate")) %></td>
                        <td class="col-days num"><%# Eval("DaysVariance") %></td>
                        <td class="col-int num"><%# LPPIHelper.FormatMoney(Eval("InterestPayable")) %></td>
                        <td class="col-reason readonly-field" title='<%# LPPIHelper.Enc(Eval("ReasonCode")) %>'><%# LPPIHelper.Enc(Eval("ReasonCode")) %></td>
                        <td class="col-comments readonly-field" title='<%# LPPIHelper.Enc(Eval("Comments")) %>'><%# LPPIHelper.Enc(Eval("Comments")) %></td>
                        <td class="col-obj readonly-field" title='<%# LPPIHelper.Enc(Eval("ObjectiveReference")) %>'><%# LPPIHelper.Enc(Eval("ObjectiveReference")) %></td>
                    </tr>
                </ItemTemplate>
                <FooterTemplate>
                        </tbody>
                    </table>
                </FooterTemplate>
            </asp:Repeater>
        </div>
    </div><%-- /paneLines --%>

</div><%-- /review-shell --%>
</asp:PlaceHolder>

<script src="../js/lppi.js"></script>
</body>
</html>
