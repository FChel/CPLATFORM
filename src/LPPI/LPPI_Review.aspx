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
            </div>
        </div>

        <div class="toolbar">
            <div class="toolbar-left">
                <div class="search-wrap">
                    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                        <circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>
                    </svg>
                    <input type="text" id="searchBox" class="input" placeholder="Search vendor, doc no, PO, WBS, DM, POC, profit centre…" />
                </div>
                <select id="statusFilter" class="input" title="Review status">
                    <option value="">All statuses</option>
                    <option value="not-reviewed">Not reviewed</option>
                    <option value="reviewed">Reviewed</option>
                    <option value="payable">Payable</option>
                    <option value="notpayable">Not payable</option>
                    <option value="needs-comments">Needs attention</option>
                </select>
                <select id="filterDm" class="input filter-facet" title="Delivery Manager">
                    <option value="">All DMs</option>
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
                    <button type="button" id="viewCards" role="tab" aria-selected="false">Cards</button>
                    <button type="button" id="viewTable" class="active" role="tab" aria-selected="true">Table</button>
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

        <!-- Card view -->
        <section class="cards hidden" aria-label="Documents (card view)">
            <asp:Repeater ID="rptCards" runat="server">
                <ItemTemplate>
                    <article class="doc-card"
                             data-doc-id='<%# Eval("DocumentID") %>'
                             data-search='<%# LPPIHelper.Enc((string)Eval("SearchBlob")) %>'
                             data-dm='<%# LPPIHelper.Enc(Eval("DeliveryManagerName")) %>'
                             data-poc='<%# LPPIHelper.Enc(Eval("PocEmail")) %>'
                             data-wbs='<%# LPPIHelper.Enc(Eval("WbsElement")) %>'
                             data-pc='<%# LPPIHelper.Enc(Eval("ProfitCentre")) %>'>
                        <header class="doc-head">
                            <div>
                                <div class="doc-vendor"><%# LPPIHelper.Enc(Eval("VendorName")) %></div>
                                <div class="doc-sub">
                                    <%# LPPIHelper.SapFiNumberHtml(Eval("DocNoAccounting"), Eval("CompanyCode"), Eval("ClearingMonth")) %>
                                    <span class="sep"> · PO </span>
                                    <%# LPPIHelper.SapPoNumberHtml(Eval("PoNumber")) %>
                                </div>
                            </div>
                            <div class="doc-interest">
                                <span class="label">Interest</span>
                                <span class="value"><%# LPPIHelper.FormatMoney(Eval("InterestPayable")) %></span>
                            </div>
                        </header>

                        <div class="doc-fields">
                            <div><span class="fk">Invoice date</span><span class="fv"><%# LPPIHelper.FormatDate(Eval("InvoiceDate")) %></span></div>
                            <div><span class="fk">Payment date</span><span class="fv"><%# LPPIHelper.FormatDate(Eval("PaymentRunDate")) %></span></div>
                            <div><span class="fk">Days late</span><span class="fv"><%# Eval("DaysVariance") %></span></div>
                            <div><span class="fk">WBS</span><span class="fv"><%# LPPIHelper.Enc(Eval("WbsElement")) %></span></div>
                            <div><span class="fk">Profit centre</span><span class="fv"><%# LPPIHelper.Enc(Eval("ProfitCentre")) %></span></div>
                            <div><span class="fk">Delivery manager</span><span class="fv"><%# LPPIHelper.Enc(Eval("DeliveryManagerName")) %></span></div>
                            <div><span class="fk">POC email</span><span class="fv"><%# LPPIHelper.Enc(Eval("PocEmail")) %></span></div>
                        </div>

                        <div class="doc-controls">
                            <label class="field">
                                <span>Reason code</span>
                                <select class="reason-select input">
                                    <option value="" data-outcome="" data-requires="0">— select —</option>
                                    <%# BuildReasonOptions(Eval("SelectedReasonCodeID")) %>
                                </select>
                            </label>
                            <label class="field">
                                <span>Comments</span>
                                <textarea class="comments-input input" rows="2" placeholder="Required for the two 'Other' reason codes, and for any Not-Payable outcome."><%# LPPIHelper.Enc(Eval("Comments")) %></textarea>
                            </label>
                            <label class="field field-narrow">
                                <span>Objective reference</span>
                                <input type="text" class="objref-input input" value='<%# LPPIHelper.Enc(Eval("ObjectiveReference")) %>' maxlength="100" placeholder="Required for Not-Payable outcomes." />
                            </label>
                            <label class="field field-narrow checkbox-field">
                                <input type="checkbox" class="rowselect" data-doc-id='<%# Eval("DocumentID") %>' />
                                <span>Select</span>
                            </label>
                        </div>

                        <div class="row-msg" role="alert"></div>
                        <div class="row-saved" aria-hidden="true">Saved ✓</div>
                    </article>
                </ItemTemplate>
            </asp:Repeater>

            <asp:PlaceHolder ID="phEmpty" runat="server" Visible="false">
                <div class="empty-state">
                    <h2>Nothing to review</h2>
                    <p>This package does not contain any outstanding documents.</p>
                </div>
            </asp:PlaceHolder>
        </section>

        <!-- Table view -->
        <section class="review-table active" aria-label="Documents (table view)">
            <div class="tbl-wrap">
                <asp:Repeater ID="rptTable" runat="server">
                    <HeaderTemplate>
                        <table class="tbl tbl-review">
                            <thead>
                                <tr>
                                    <th class="col-sel"></th>
                                    <th class="col-doc">Doc No</th>
                                    <th class="col-vendor">Vendor</th>
                                    <th class="col-po">PO</th>
                                    <th class="col-wbs">WBS</th>
                                    <th class="col-pc">Profit centre</th>
                                    <th class="col-dm">Delivery manager</th>
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
                        <tr class="doc-row"
                            data-doc-id='<%# Eval("DocumentID") %>'
                            data-search='<%# LPPIHelper.Enc((string)Eval("SearchBlob")) %>'
                            data-dm='<%# LPPIHelper.Enc(Eval("DeliveryManagerName")) %>'
                            data-poc='<%# LPPIHelper.Enc(Eval("PocEmail")) %>'
                            data-wbs='<%# LPPIHelper.Enc(Eval("WbsElement")) %>'
                            data-pc='<%# LPPIHelper.Enc(Eval("ProfitCentre")) %>'>
                            <td class="col-sel"><input type="checkbox" class="rowselect" data-doc-id='<%# Eval("DocumentID") %>' /></td>
                            <td class="col-doc"><%# LPPIHelper.SapFiNumberHtml(Eval("DocNoAccounting"), Eval("CompanyCode"), Eval("ClearingMonth")) %></td>
                            <td class="col-vendor" title='<%# LPPIHelper.Enc(Eval("VendorName")) %>'><%# LPPIHelper.Enc(Eval("VendorName")) %></td>
                            <td class="col-po"><%# LPPIHelper.SapPoNumberHtml(Eval("PoNumber")) %></td>
                            <td class="col-wbs" title='<%# LPPIHelper.WbsTooltip(Eval("WbsElement"), Eval("WbsDesc")) %>'><%# LPPIHelper.Enc(Eval("WbsElement")) %></td>
                            <td class="col-pc" title='<%# LPPIHelper.Enc(Eval("ProfitCentre")) %>'><%# LPPIHelper.Enc(Eval("ProfitCentre")) %></td>
                            <td class="col-dm" title='<%# LPPIHelper.Enc(Eval("DeliveryManagerName")) %>'><%# LPPIHelper.Enc(Eval("DeliveryManagerName")) %></td>
                            <td class="col-poc" title='<%# LPPIHelper.Enc(Eval("PocEmail")) %>'><%# LPPIHelper.Enc(Eval("PocEmail")) %></td>
                            <td class="col-date"><%# LPPIHelper.FormatDate(Eval("PaymentRunDate")) %></td>
                            <td class="col-days num"><%# Eval("DaysVariance") %></td>
                            <td class="col-int num"><%# LPPIHelper.FormatMoney(Eval("InterestPayable")) %></td>
                            <td class="col-reason">
                                <select class="reason-select input">
                                    <option value="" data-outcome="" data-requires="0">—</option>
                                    <%# BuildReasonOptions(Eval("SelectedReasonCodeID")) %>
                                </select>
                            </td>
                            <td class="col-comments"><textarea class="comments-input input" rows="1"><%# LPPIHelper.Enc(Eval("Comments")) %></textarea></td>
                            <td class="col-obj"><input type="text" class="objref-input input" value='<%# LPPIHelper.Enc(Eval("ObjectiveReference")) %>' maxlength="100" /></td>
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
