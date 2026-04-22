<%@ Page Language="C#" AutoEventWireup="true" ValidateRequest="false"
    CodeFile="LPPI_Review.aspx.cs" Inherits="CPlatform.LPPI.LPPI_Review" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review</title>
    <link rel="stylesheet" href="../css/lppi.css" />
</head>
<body class="review-body">
<asp:PlaceHolder ID="phError" runat="server" Visible="false">
    <div class="lppi-shell">
        <main class="lppi-main">
            <div class="card card-error">
                <h1>Link not available</h1>
                <p>This review link is not valid. It may have expired, been closed, or been typed incorrectly. Please contact the person who sent you the email for a fresh link.</p>
                <p class="muted">Support: <%= LPPIHelper.Enc(LPPIHelper.Setting("LPPI.SupportContact", "DFG Finance Support")) %></p>
            </div>
        </main>
    </div>
</asp:PlaceHolder>

<asp:PlaceHolder ID="phReview" runat="server" Visible="false">
<form id="form1" runat="server">
<input type="hidden" id="reviewToken" value="<%= LPPIHelper.Enc(TokenForClient) %>" />
<input type="hidden" id="sapBaseUrl" value="<%= LPPIHelper.Enc(LPPIHelper.Setting("LPPI.SapBaseUrl", "")) %>" />

<div class="review-shell">
    <header class="review-head">
        <div class="review-head-inner">
            <div class="review-brand">
                <div class="brand-mark" aria-hidden="true">
                    <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                        <path d="M14 2v6h6"/>
                        <path d="M9 14h6"/>
                        <path d="M9 18h4"/>
                    </svg>
                </div>
                <div>
                    <div class="crumb">LPPI Review</div>
                    <h1><%= LPPIHelper.Enc(ProgramName) %></h1>
                </div>
            </div>
            <div class="review-meta">
                <div class="meta-item">
                    <span class="meta-label">Due</span>
                    <span class="meta-value"><%= LPPIHelper.FormatDate(DueDate) %></span>
                    <span class="meta-sub <%= DueCssClass %>"><%= DueCountdownText %></span>
                </div>
                <div class="meta-item">
                    <span class="meta-label">Progress</span>
                    <span class="meta-value" id="progressLabel"><%= ReviewedCount %> of <%= TotalCount %></span>
                    <div class="progress-track"><div class="progress-bar" id="progressBar" style="width: <%= ProgressPercent %>%"></div></div>
                </div>
                <div class="meta-item">
                    <span class="meta-label">Save</span>
                    <span id="saveIndicator" class="save-indicator" role="status" aria-live="polite">All changes saved</span>
                </div>
                <div class="meta-item">
                    <a href="https://www.finance.gov.au/publications/resource-management-guides/supplier-pay-time-or-pay-interest-policy-rmg-417"
                       target="_blank" rel="noopener" class="policy-link">Policy reference: RMG-417 (Supplier Pay On-Time or Pay Interest)</a>
                </div>
            </div>
        </div>

        <div class="toolbar">
            <div class="toolbar-left">
                <div class="search-wrap">
                    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                        <circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>
                    </svg>
                    <input type="text" id="searchBox" class="input" placeholder="Search vendor, doc no, PO, WBS, DM program, POC, profit centre…" />
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
                <select id="filterPoc" class="input filter-facet" title="POC">
                    <option value="">All POCs</option>
                    <%= BuildFacetOptions("poc") %>
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
                <div class="view-toggle" role="tablist" aria-label="View">
                    <button type="button" id="viewMain" role="tab" aria-selected="true" class="active">Main</button>
                    <button type="button" id="viewDetail" role="tab" aria-selected="false">Detail</button>
                </div>
                <button type="button" id="markFinalBtn" class="btn btn-secondary">Mark all as final</button>
                <button type="button" id="saveAllBtn" class="btn btn-primary" title="Save every row that has been edited but not yet saved">
                    <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                        <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                        <polyline points="17 21 17 13 7 13 7 21"/>
                        <polyline points="7 3 7 8 15 8"/>
                    </svg>
                    <span>Save</span>
                </button>
            </div>
        </div>
    </header>

    <main class="review-main">
        <div id="doneBanner" class="done-banner" role="status">
            <strong>All done.</strong> Every document in this package has a reason code. Thanks for your help.
        </div>

        <!-- ================================================================
             MAIN VIEW — one row per DocNoAccounting, editable
             ================================================================ -->
        <section class="mains active" aria-label="Documents (main view)">
            <div class="tbl-wrap">
                <asp:Repeater ID="rptMain" runat="server">
                    <HeaderTemplate>
                        <table class="tbl tbl-review">
                            <thead>
                                <tr>
                                    <th class="col-sel"></th>
                                    <th class="col-doc">Doc No</th>
                                    <th class="col-vendor">Vendor</th>
                                    <th class="col-po">PO</th>
                                    <th class="col-lines num">Lines</th>
                                    <th class="col-wbs">WBS</th>
                                    <th class="col-pc">Profit centre</th>
                                    <th class="col-dm">Delivery Manager Program</th>
                                    <th class="col-poc">POC</th>
                                    <th class="col-date">Payment</th>
                                    <th class="col-days num">Days Late</th>
                                    <th class="col-int num">Interest</th>
                                    <th class="col-reason">Reason code</th>
                                    <th class="col-comments">Comments</th>
                                    <th class="col-obj">Evidence (Obj ref)</th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr class="doc-main"
                            data-doc-no='<%# LPPIHelper.Enc(Eval("DocNoAccounting")) %>'
                            data-first-line-id='<%# Eval("FirstLineDocumentID") %>'
                            data-search='<%# LPPIHelper.Enc((string)Eval("SearchBlob")) %>'
                            data-dm='<%# LPPIHelper.Enc(Eval("DeliveryManagerProgram") == DBNull.Value ? "(mixed)" : Eval("DeliveryManagerProgram")) %>'
                            data-poc='<%# LPPIHelper.Enc(Eval("PocEmail") == DBNull.Value ? "(mixed)" : Eval("PocEmail")) %>'
                            data-wbs='<%# LPPIHelper.Enc(Eval("WbsElement") == DBNull.Value ? "(mixed)" : Eval("WbsElement")) %>'
                            data-pc='<%# LPPIHelper.Enc(Eval("ProfitCentre") == DBNull.Value ? "(mixed)" : Eval("ProfitCentre")) %>'>
                            <td class="col-sel">
                                <input type="checkbox" class="rowselect" data-doc-no='<%# LPPIHelper.Enc(Eval("DocNoAccounting")) %>' />
                            </td>
                            <td class="col-doc">
                                <%# LPPIHelper.SapFiNumberHtml(Eval("DocNoAccounting"), Eval("CompanyCode"), Eval("ClearingMonth")) %>
                            </td>
                            <td class="col-vendor"><%# LPPIHelper.Enc(Eval("VendorName")) %><br/><span class="muted small"><%# LPPIHelper.Enc(Eval("VendorNum")) %></span></td>
                            <td class="col-po"><%# LPPIHelper.SapPoNumberHtml(Eval("PoNumber")) %></td>
                            <td class="col-lines num"><span class="lines-chip"><%# Eval("LineCount") %></span></td>
                            <td class="col-wbs" title='<%# LPPIHelper.Enc(Eval("WbsDesc") == DBNull.Value ? "" : Eval("WbsDesc")) %>'><%# MixedOrEnc(Eval("WbsElement")) %></td>
                            <td class="col-pc"><%# MixedOrEnc(Eval("ProfitCentre")) %></td>
                            <td class="col-dm">
                                <%# MixedOrEnc(Eval("DeliveryManagerProgram")) %>
                                <%# Eval("DeliveryManagerName") != DBNull.Value ? "<br/><span class=\"muted small\">" + LPPIHelper.Enc(Eval("DeliveryManagerName")) + "</span>" : "" %>
                            </td>
                            <td class="col-poc"><%# MixedOrEnc(Eval("PocEmail")) %></td>
                            <td class="col-date"><%# LPPIHelper.FormatDate(Eval("PaymentRunDate")) %></td>
                            <td class="col-days num"><%# Eval("DaysVariance") %></td>
                            <td class="col-int num"><%# LPPIHelper.FormatMoney(Eval("TotalInterest")) %></td>
                            <td class="col-reason">
                                <select class="reason-select input">
                                    <option value="" data-outcome="" data-requires="0">—</option>
                                    <%# BuildReasonOptions(Eval("SelectedReasonCodeID")) %>
                                </select>
                            </td>
                            <td class="col-comments"><textarea class="comments-input input" rows="1"><%# LPPIHelper.Enc(Eval("Comments")) %></textarea></td>
                            <td class="col-obj"><input type="text" class="objref-input input" value='<%# LPPIHelper.Enc(Eval("ObjectiveReference")) %>' maxlength="100" /></td>
                        </tr>
                        <tr class="doc-main-msg" data-doc-no='<%# LPPIHelper.Enc(Eval("DocNoAccounting")) %>'>
                            <td colspan="15"><div class="row-msg" role="alert"></div></td>
                        </tr>
                    </ItemTemplate>
                    <FooterTemplate>
                            </tbody>
                        </table>
                    </FooterTemplate>
                </asp:Repeater>
            </div>

            <asp:PlaceHolder ID="phEmpty" runat="server" Visible="false">
                <div class="empty-state">
                    <h2>Nothing to review</h2>
                    <p>This package does not contain any outstanding documents.</p>
                </div>
            </asp:PlaceHolder>
        </section>

        <!-- ================================================================
             DETAIL VIEW — one row per line, read-only review fields
             ================================================================ -->
        <section class="review-detail" aria-label="Documents (detail view)">
            <div class="tbl-wrap">
                <asp:Repeater ID="rptDetail" runat="server">
                    <HeaderTemplate>
                        <table class="tbl tbl-review tbl-detail">
                            <thead>
                                <tr>
                                    <th class="col-doc">Doc No</th>
                                    <th class="col-seq num">Line</th>
                                    <th class="col-vendor">Vendor</th>
                                    <th class="col-po">PO</th>
                                    <th class="col-wbs">WBS</th>
                                    <th class="col-gl">GL Account</th>
                                    <th class="col-pc">Profit centre</th>
                                    <th class="col-tax">Tax code</th>
                                    <th class="col-dm">DM Program</th>
                                    <th class="col-poc">POC</th>
                                    <th class="col-date">Payment</th>
                                    <th class="col-days num">Days Late</th>
                                    <th class="col-int num">Interest</th>
                                    <th class="col-reason">Reason code <span class="readonly-note">(from document)</span></th>
                                    <th class="col-comments">Comments <span class="readonly-note">(from document)</span></th>
                                    <th class="col-obj">Obj ref <span class="readonly-note">(from document)</span></th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr class="doc-row detail-row"
                            data-doc-no='<%# LPPIHelper.Enc(Eval("DocNoAccounting")) %>'
                            data-first-line-id='<%# Eval("FirstLineDocumentID") %>'
                            data-search='<%# LPPIHelper.Enc((string)Eval("SearchBlob")) %>'
                            data-dm='<%# LPPIHelper.Enc(Eval("DeliveryManagerProgram")) %>'
                            data-poc='<%# LPPIHelper.Enc(Eval("PocEmail")) %>'
                            data-wbs='<%# LPPIHelper.Enc(Eval("WbsElement")) %>'
                            data-pc='<%# LPPIHelper.Enc(Eval("ProfitCentre")) %>'>
                            <td class="col-doc">
                                <%# LPPIHelper.SapFiNumberHtml(Eval("DocNoAccounting"), Eval("CompanyCode"), Eval("ClearingMonth")) %>
                            </td>
                            <td class="col-seq num"><span class="seq-chip"><%# string.Format("{0:000}", Eval("ItemSequence")) %></span></td>
                            <td class="col-vendor"><%# LPPIHelper.Enc(Eval("VendorName")) %></td>
                            <td class="col-po"><%# LPPIHelper.SapPoNumberHtml(Eval("PoNumber")) %></td>
                            <td class="col-wbs" title='<%# LPPIHelper.Enc(Eval("WbsDesc")) %>'><%# LPPIHelper.Enc(Eval("WbsElement")) %></td>
                            <td class="col-gl"><%# LPPIHelper.Enc(Eval("GlAccount")) %></td>
                            <td class="col-pc"><%# LPPIHelper.Enc(Eval("ProfitCentre")) %></td>
                            <td class="col-tax"><%# LPPIHelper.Enc(Eval("TaxCode")) %></td>
                            <td class="col-dm"><%# LPPIHelper.Enc(Eval("DeliveryManagerProgram")) %></td>
                            <td class="col-poc"><%# LPPIHelper.Enc(Eval("PocEmail")) %></td>
                            <td class="col-date"><%# LPPIHelper.FormatDate(Eval("PaymentRunDate")) %></td>
                            <td class="col-days num"><%# Eval("DaysVariance") %></td>
                            <td class="col-int num"><%# LPPIHelper.FormatMoney(Eval("InterestPayable")) %></td>
                            <td class="col-reason readonly-field" title="Edit in Main view"><%# LPPIHelper.Enc(Eval("ReasonCode")) %></td>
                            <td class="col-comments readonly-field" title="Edit in Main view"><%# LPPIHelper.Enc(Eval("Comments")) %></td>
                            <td class="col-obj readonly-field" title="Edit in Main view"><%# LPPIHelper.Enc(Eval("ObjectiveReference")) %></td>
                        </tr>
                    </ItemTemplate>
                    <FooterTemplate>
                            </tbody>
                        </table>
                    </FooterTemplate>
                </asp:Repeater>
            </div>
        </section>
    </main>

    <div id="bulkBar" class="bulk-bar" aria-live="polite">
        <span><span id="bulkCount">0</span> selected</span>
        <select id="bulkReason" class="input">
            <option value="" data-outcome="" data-requires="0">Apply reason code…</option>
            <%= BuildReasonOptions(null) %>
        </select>
        <button type="button" id="bulkApply" class="btn btn-primary">Apply</button>
        <button type="button" id="bulkClear" class="btn btn-ghost">Clear</button>
    </div>
</div>
</form>
</asp:PlaceHolder>

<script src="../js/lppi.js"></script>
</body>
</html>
