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
                    <input type="text" id="searchBox" class="input" placeholder="Search vendor, doc no, PO, WBS…" />
                </div>
                <select id="statusFilter" class="input">
                    <option value="">All</option>
                    <option value="not-reviewed">Not reviewed</option>
                    <option value="reviewed">Reviewed</option>
                    <option value="payable">Payable</option>
                    <option value="notpayable">Not payable</option>
                    <option value="needs-comments">Needs comments</option>
                </select>
            </div>
            <div class="toolbar-right">
                <div class="view-toggle" role="tablist" aria-label="View">
                    <button type="button" id="viewCards" class="active" role="tab" aria-selected="true">Cards</button>
                    <button type="button" id="viewTable" role="tab" aria-selected="false">Table</button>
                </div>
                <button type="button" id="markFinalBtn" class="btn btn-secondary">Mark all as final</button>
            </div>
        </div>
    </header>

    <main class="review-main">
        <div id="doneBanner" class="done-banner" role="status">
            <strong>All done.</strong> Every document in this package has a reason code. Thanks for your help.
        </div>

        <!-- Card view -->
        <section class="cards" aria-label="Documents (card view)">
            <asp:Repeater ID="rptCards" runat="server">
                <ItemTemplate>
                    <article class="doc-card"
                             data-doc-id='<%# Eval("DocumentID") %>'
                             data-search='<%# LPPIHelper.Enc((string)Eval("SearchBlob")) %>'>
                        <header class="doc-head">
                            <div>
                                <div class="doc-vendor"><%# LPPIHelper.Enc(Eval("VendorName")) %></div>
                                <div class="doc-sub"><%# LPPIHelper.Enc(Eval("DocNoAccounting")) %> · PO <%# LPPIHelper.Enc(Eval("PoNumber")) %></div>
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
                                <textarea class="comments-input input" rows="2" placeholder="Required for the two 'Other' reason codes."><%# LPPIHelper.Enc(Eval("Comments")) %></textarea>
                            </label>
                            <label class="field field-narrow">
                                <span>Objective reference</span>
                                <input type="text" class="objref-input input" value='<%# LPPIHelper.Enc(Eval("ObjectiveReference")) %>' maxlength="100" />
                            </label>
                            <label class="field field-narrow checkbox-field">
                                <input type="checkbox" class="rowselect" data-doc-id='<%# Eval("DocumentID") %>' />
                                <span>Select</span>
                            </label>
                        </div>

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
        <section class="review-table" aria-label="Documents (table view)">
            <div class="tbl-wrap">
                <asp:Repeater ID="rptTable" runat="server">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th></th>
                                    <th>Doc No</th>
                                    <th>Vendor</th>
                                    <th>PO</th>
                                    <th>Invoice</th>
                                    <th>Payment</th>
                                    <th class="num">Days</th>
                                    <th class="num">Interest</th>
                                    <th>Reason code</th>
                                    <th>Comments</th>
                                    <th>Obj ref</th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr class="doc-row" data-doc-id='<%# Eval("DocumentID") %>'>
                            <td><input type="checkbox" class="rowselect" data-doc-id='<%# Eval("DocumentID") %>' /></td>
                            <td><%# LPPIHelper.Enc(Eval("DocNoAccounting")) %></td>
                            <td><%# LPPIHelper.Enc(Eval("VendorName")) %></td>
                            <td><%# LPPIHelper.Enc(Eval("PoNumber")) %></td>
                            <td><%# LPPIHelper.FormatDate(Eval("InvoiceDate")) %></td>
                            <td><%# LPPIHelper.FormatDate(Eval("PaymentRunDate")) %></td>
                            <td class="num"><%# Eval("DaysVariance") %></td>
                            <td class="num"><%# LPPIHelper.FormatMoney(Eval("InterestPayable")) %></td>
                            <td>
                                <select class="reason-select input">
                                    <option value="" data-outcome="" data-requires="0">—</option>
                                    <%# BuildReasonOptions(Eval("SelectedReasonCodeID")) %>
                                </select>
                            </td>
                            <td><textarea class="comments-input input" rows="1"><%# LPPIHelper.Enc(Eval("Comments")) %></textarea></td>
                            <td><input type="text" class="objref-input input" value='<%# LPPIHelper.Enc(Eval("ObjectiveReference")) %>' maxlength="100" /></td>
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
