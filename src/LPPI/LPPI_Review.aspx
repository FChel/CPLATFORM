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
        /* Read-only mode — disables data-entry controls inside the review
           table only. The toolbar (search box, status filter, facet
           selects) stays live in every state so users can navigate and
           inspect a Finalised or Exported package as freely as an
           editable one.

           The save handler is the authoritative gate; this is purely a
           UX hint so admin QA viewers cannot accidentally type into
           fields they are not meant to edit.

           Note: #actionBtn is NOT in the read-only display:none list,
           because on a Finalised package the action button shows
           "Unfinalise" — we WANT it visible. The button is rendered or
           omitted by the server based on status (ShowActionButton); when
           it is present, it should be clickable. */
        .review-shell[data-readonly="1"] .tbl-review select,
        .review-shell[data-readonly="1"] .tbl-review textarea,
        .review-shell[data-readonly="1"] .tbl-review input[type="text"],
        .review-shell[data-readonly="1"] .tbl-review input[type="checkbox"] {
            pointer-events: none;
            background: #f8f8f8;
            color: var(--ink-3);
        }
        .review-shell[data-readonly="1"] #saveAllBtn,
        .review-shell[data-readonly="1"] #bulkBar {
            display: none !important;
        }

        /* Export-to-Excel toolbar above the All Lines tab */
        .lines-toolbar {
            display: flex;
            justify-content: flex-end;
            margin-bottom: 8px;
        }

        /* ============================================================
           Reviewer-page exposure cell
           Sits inside the existing .review-meta grid alongside Due /
           Progress / Policy. Compact — value in 18px orange, then a
           horizontally-stacked segmented bar (green/red/amber) showing
           the share of total for each outcome.
           ============================================================ */
        .review-meta .meta-exposure .meta-value {
            color: var(--orange);
            font-variant-numeric: tabular-nums;
            font-size: 18px;
            letter-spacing: -0.005em;
        }
        .review-meta .meta-exposure .meta-value .currency {
            font-size: 12px;
            font-weight: 600;
            color: var(--orange-deep);
            margin-right: 1px;
            vertical-align: 2px;
        }

        .exposure-stack-bar {
            display: flex;
            height: 6px;
            background: var(--line-2);
            border-radius: 999px;
            overflow: hidden;
            margin-top: 8px;
        }
        .exposure-stack-bar .seg {
            height: 100%;
            transition: width 0.4s ease;
        }
        .exposure-stack-bar .seg.payable    { background: var(--ok); }
        .exposure-stack-bar .seg.notpayable { background: var(--err); }
        .exposure-stack-bar .seg.awaiting   { background: var(--warn); }

        .exposure-legend {
            display: flex;
            gap: 10px;
            margin-top: 6px;
            font-size: 11px;
            color: var(--ink-3);
            font-variant-numeric: tabular-nums;
            flex-wrap: wrap;
        }
        .exposure-legend .item {
            display: inline-flex;
            align-items: center;
            gap: 4px;
            white-space: nowrap;
        }
        .exposure-legend .dot {
            width: 7px; height: 7px;
            border-radius: 50%;
            flex-shrink: 0;
        }
        .exposure-legend .dot.payable    { background: var(--ok); }
        .exposure-legend .dot.notpayable { background: var(--err); }
        .exposure-legend .dot.awaiting   { background: var(--warn); }
        .exposure-legend .amount { font-weight: 600; color: var(--ink-2); }

        /* ============================================================
           Action button — single slot in the toolbar that toggles
           between Finalise and Unfinalise based on package status.

           Two variants:
             .action-finalise   — green, positive close-off action
             .action-unfinalise — orange, reversal action

           Same shape and footprint either way, so the toolbar layout
           does not shift between states.
           ============================================================ */
        #actionBtn {
            font-size: 13px;
            white-space: nowrap;
        }
        #actionBtn.action-finalise {
            background: var(--ok);
            color: #fff;
            border-color: var(--ok);
            box-shadow: 0 2px 6px rgba(46,125,50,0.25);
        }
        #actionBtn.action-finalise:hover:not(:disabled) {
            filter: brightness(0.95);
        }
        #actionBtn.action-unfinalise {
            background: var(--orange);
            color: #fff;
            border-color: var(--orange);
            box-shadow: 0 2px 6px rgba(215,91,7,0.25);
        }
        #actionBtn.action-unfinalise:hover:not(:disabled) {
            background: var(--orange-deep);
            border-color: var(--orange-deep);
        }
        #actionBtn:disabled,
        #actionBtn[disabled] {
            opacity: 0.45;
            cursor: not-allowed;
            box-shadow: none;
            filter: grayscale(0.2);
        }
        #actionBtn svg { stroke: currentColor; flex: 0 0 auto; }

        /* The "ready to finalise" hint replaces the old "all done" banner
           when every doc has been coded but the package is not yet
           Finalised. Different colour (orange-deep gradient) so it cannot
           be confused with the green Finalised status banner above. */
        .ready-banner {
            background: linear-gradient(135deg, var(--orange) 0%, var(--orange-deep) 100%);
            color: #fff;
            padding: 16px 24px;
            border-radius: var(--r-lg);
            margin-bottom: 16px;
            box-shadow: var(--shadow);
            display: none;
            align-items: center;
            justify-content: space-between;
            gap: 16px;
        }
        .ready-banner.show { display: flex; }
        .ready-banner .ready-text strong { font-size: 15px; display: block; margin-bottom: 2px; }
        .ready-banner .ready-text span   { font-size: 13px; color: rgba(255,255,255,0.92); }
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
<input type="hidden" id="reviewStatus" value="<%= LPPIHelper.Enc(CurrentStatus) %>" />

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
                <span class="meta-label">Progress (by document)</span>
                <span class="meta-value" id="progressLabel"><%= ReviewedCount %> of <%= TotalCount %></span>
                <div class="progress-track"><div class="progress-bar" id="progressBar" style="width: <%= ProgressPercent %>%"></div></div>
            </div>
            <div class="meta-item meta-exposure">
                <span class="meta-label">Exposure (by $)</span>
                <span class="meta-value">
                    <span class="currency">$</span><%= ExposureTotalText %>
                </span>
                <div class="exposure-stack-bar" title="Payable / Not payable / Awaiting">
                    <div class="seg payable"    style="width: <%= ExposurePayablePct    %>%"></div>
                    <div class="seg notpayable" style="width: <%= ExposureNotPayablePct %>%"></div>
                    <div class="seg awaiting"   style="width: <%= ExposureAwaitingPct   %>%"></div>
                </div>
                <div class="exposure-legend">
                    <span class="item"><span class="dot payable"></span>Payable <span class="amount">$<%= ExposurePayableTextShort %></span></span>
                    <span class="item"><span class="dot notpayable"></span>Not pay <span class="amount">$<%= ExposureNotPayableTextShort %></span></span>
                    <span class="item"><span class="dot awaiting"></span>Awaiting <span class="amount">$<%= ExposureAwaitingTextShort %></span></span>
                </div>
            </div>
            <div class="meta-item">
                <a href="https://www.finance.gov.au/publications/resource-management-guides/supplier-pay-time-or-pay-interest-policy-rmg-417"
                   target="_blank" rel="noopener" class="policy-link">RMG-417 — Supplier Pay On-Time or Pay Interest Policy</a>
            </div>
        </div>
    </div>

    <%-- Tab strip — Instructions tab is first but Reason code entry is the
         default-active tab on page load. --%>
    <div class="review-tabs" role="tablist" aria-label="Review views">
        <button type="button" id="tabInstructions" class="review-tab"        role="tab" aria-selected="false" aria-controls="paneInstructions">Instructions</button>
        <button type="button" id="tabReason"       class="review-tab active" role="tab" aria-selected="true"  aria-controls="paneReason">Reason code entry</button>
        <button type="button" id="tabLines"        class="review-tab"        role="tab" aria-selected="false" aria-controls="paneLines">All lines</button>
    </div>

    <%-- Toolbar — outside all panes. Hidden when the Instructions tab is
         active (no rows to filter or save while reading instructions).

         The "Save changes" button is hidden by CSS when the page is
         read-only (Finalised, Exported, Cancelled). The action button
         (Finalise / Unfinalise) is rendered conditionally by the server
         based on status — green Finalise when editable, orange Unfinalise
         when Finalised, omitted entirely when terminal. --%>
    <div class="toolbar">
        <div class="toolbar-left">
            <div class="search-wrap">
                <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                    <circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>
                </svg>
                <input type="text" id="searchBox" class="input" placeholder="Search vendor, doc no, PO, WBS, DM program, CM…" />
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
            <select id="filterPoc" class="input filter-facet" title="POC email">
                <option value="">All POCs</option>
                <%= BuildFacetOptions("poc") %>
            </select>
            <select id="filterWbs" class="input filter-facet" title="WBS element">
                <option value="">All WBS</option>
                <%= BuildFacetOptions("wbs") %>
            </select>
            <select id="filterCm" class="input filter-facet" title="Capability Manager (LPPI Charge Cost Centre)">
                <option value="">All Capability Managers</option>
                <%= BuildFacetOptions("cm") %>
            </select>
        </div>
        <div class="toolbar-right">
            <span id="saveIndicator" class="save-indicator saved" role="status" aria-live="polite">No changes to save</span>
            <button type="button" id="saveAllBtn" class="btn btn-primary" disabled title="Save unsaved changes">
                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                    <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                    <polyline points="17 21 17 13 7 13 7 21"/>
                    <polyline points="7 3 7 8 15 8"/>
                </svg>
                <span id="saveAllBtnLabel">Save changes</span>
            </button>
            <% if (ShowActionButton) { %>
              <% if (IsFinalised) { %>
                <button type="button" id="actionBtn" class="btn action-unfinalise"
                        data-action="unfinalise"
                        title="Reopen this package for further edits. Auto-applied 'no response' codes will be cleared.">
                    <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                        <path d="M3 12a9 9 0 1 0 3-6.7"/>
                        <polyline points="3 4 3 10 9 10"/>
                    </svg>
                    <span id="actionBtnLabel">Unfinalise</span>
                </button>
              <% } else { %>
                <button type="button" id="actionBtn" class="btn action-finalise"
                        data-action="finalise"
                        title="Finalise this package — locks all rows, defaults any undecided documents to RC-NR (Payable, no response).">
                    <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                        <polyline points="20 6 9 17 4 12"/>
                    </svg>
                    <span id="actionBtnLabel">Finalise</span>
                </button>
              <% } %>
            <% } %>
        </div>
    </div>

    <%-- "Ready to finalise" hint — shown when every document has been
         coded but the package is still in flight (i.e. not yet Finalised).
         Distinct from the green Finalised status banner at the top of
         the page; this one is a call-to-action while the page is
         editable. JS toggles the .show class based on dirty state. --%>
    <div id="readyBanner" class="ready-banner<%= (IsAllReviewed || IsPocAllReviewed) ? " show" : "" %>"
         data-poc="<%= IsPocView ? "1" : "0" %>" role="status">
      <% if (IsPocView) { %>
        <div class="ready-text">
            <strong>All done.</strong>
            <span>Every document assigned to you has a reason code. Your selections are saved automatically — you can close this window. AS Fin will finalise the package.</span>
        </div>
      <% } else { %>
        <div class="ready-text">
            <strong>All reviewed — ready to finalise.</strong>
            <span>Every document in this package has a reason code. Click Finalise above when you are ready to close it off; you can unfinalise later if you need to make changes.</span>
        </div>
      <% } %>
    </div>

    <%-- ================================================================
         TAB 0 — Instructions / about this page
         (First in tab order, NOT the default-active pane.)

         Layout: full-width container with a sticky in-pane TOC sidebar on
         the left and the body content on the right. Mirrors the admin
         Help page pattern so the two help surfaces feel consistent. The
         body wraps each section in <section id="..."> blocks so the TOC
         can deep-link with smooth scroll (handled by lppi.js
         bindInstructionsTocLinks; CSS scroll-margin-top keeps section
         tops clear of the sticky shell header on jump).
         ================================================================ --%>
    <div id="paneInstructions" class="review-pane" role="tabpanel" aria-labelledby="tabInstructions">
        <div class="instructions-pane">

            <%-- Sticky in-pane TOC --%>
            <nav class="instructions-toc" aria-label="Instructions contents">
                <div class="toc-title">On this page</div>
                <ol>
                    <li><a href="#instr-about">About this review</a></li>
                    <li><a href="#instr-howto">How to complete your review</a></li>
                    <li><a href="#instr-columns">Columns explained</a></li>
                    <li><a href="#instr-alllines">The All lines tab</a></li>
                    <li><a href="#instr-faq">Frequently asked questions</a></li>
                    <li><a href="#instr-help">Need help?</a></li>
                </ol>
            </nav>

            <%-- Body --%>
            <div class="instructions-body">

                <section id="instr-about">
                    <h2>About this review</h2>
                    <p>
                        This page lists payments that have incurred Late Payment Penalty Interest (LPPI) under
                        <a href="https://www.finance.gov.au/publications/resource-management-guides/supplier-pay-time-or-pay-interest-policy-rmg-417" target="_blank" rel="noopener">RMG-417 &mdash; Supplier Pay On-Time or Pay Interest Policy</a>.
                        For each document, please decide whether the LPPI is <strong>payable</strong> or <strong>not payable</strong>
                        by selecting a Reason Code. Once every document has a Reason Code, you can finalise the package; the LPPI
                        charges will then be processed against the responsible cost centres on the next ERP export run.
                    </p>
                </section>

                <section id="instr-howto">
                    <h2>How to complete your review</h2>
                    <ol class="instr-steps">
                        <li>
                            <strong>Open the Reason code entry tab.</strong> Each row is one document. Use the
                            <em>chevron</em> on the right of any row to see the underlying line-item detail.
                        </li>
                        <li>
                            <strong>Pick a Reason Code</strong> from the dropdown. The colour of the code indicates the outcome:
                            <span class="instr-pill instr-pill-pay">Payable</span> means the LPPI charge will be processed,
                            <span class="instr-pill instr-pill-nopay">Not payable</span> means the charge will not be processed.
                        </li>
                        <li>
                            <strong>Add Comments and Evidence (Objective Reference)</strong> where required. The page will prompt
                            you with a red highlight when these fields are mandatory:
                            <ul>
                                <li>Comments are required for any code marked <em>Requires comments</em>.</li>
                                <li>Both Comments <em>and</em> Evidence are required when the outcome is <em>Not payable</em>.</li>
                            </ul>
                        </li>
                        <li>
                            <strong>Apply codes in bulk</strong> by ticking the checkboxes on multiple rows. Use the bulk action bar
                            at the bottom to apply the same Reason Code to every selected row at once.
                        </li>
                        <li>
                            <strong>Save your changes</strong> using the orange Save changes button at the top right. Nothing is
                            written to the database until you save. The button is disabled when there are no pending changes.
                        </li>
                        <li>
                            <strong>Finalise the package</strong> using the green Finalise button when you are done.
                            Any undecided documents will be defaulted to <em>RC-NR (Payable, no response received)</em>.
                            Finalising locks the form fields. You can unfinalise the package at any time before it is exported to ERP.
                        </li>
                    </ol>
                </section>

                <section id="instr-columns">
                    <h2>Columns explained</h2>
                    <ul class="instr-list">
                        <li><strong>Document (Lines)</strong> &mdash; SAP accounting document number. The number in brackets is the line count for that document. Click to open the SAP Fiori deep link.</li>
                        <li><strong>Vendor</strong> &mdash; the vendor that was paid late.</li>
                        <li><strong>PO</strong> &mdash; the purchase order. Click to open in SAP Fiori.</li>
                        <li><strong>WBS Element</strong> &mdash; the WBS that funded the underlying invoice.</li>
                        <li><strong>Capability Manager</strong> &mdash; the LPPI Charge Cost Centre. This is the cost centre that will be charged with the interest if the outcome is Payable. Hover for the Capability Manager name.</li>
                        <li><strong>Delivery Manager Program</strong> &mdash; the program that owns the delivery. Hover for the Delivery Manager name.</li>
                        <li><strong>Days Late</strong> / <strong>Interest Payable</strong> &mdash; the late-payment numbers.</li>
                        <li><strong>Reason Code</strong> &mdash; your decision.</li>
                        <li><strong>Comments</strong> / <strong>Evidence (Obj Ref)</strong> &mdash; supporting context for the decision.</li>
                    </ul>
                </section>

                <section id="instr-alllines">
                    <h2>The All lines tab</h2>
                    <p>
                        The <strong>All lines</strong> tab shows the full line-by-line detail for every document in the package.
                        It is read-only and useful for reviewing or exporting the underlying data. The Reason Code that you set on
                        the document level applies to every line of that document &mdash; one decision per document, applied uniformly.
                        Use the <em>Export to Excel</em> button on that tab to download the complete dataset.
                    </p>
                </section>

                <section id="instr-faq">
                    <h2>Frequently asked questions</h2>

                    <dl class="instr-faq">

                        <dt>What is LPPI Review?</dt>
                        <dd>LPPI Review is a DFG initiative driven by automated monthly data extraction and date calculations, giving Capability Manager groups and invoice POCs a central hub to perform monthly LPPI reviews in accordance with the
                            <a href="https://www.finance.gov.au/publications/resource-management-guides/supplier-pay-time-or-pay-interest-policy-rmg-417" target="_blank" rel="noopener">Supplier Pay On-Time or Pay Interest Policy (RMG&nbsp;417)</a>.
                            Data is extracted from ERP each month and surfaced here for review.</dd>

                        <dt>Why am I receiving emails to complete LPPI Review actions?</dt>
                        <dd>You were identified as the invoice POC for a late payment, or you are part of the AS Fin team accountable for the group review. An initial email is sent to all late-payment POCs and their AS Fin at Day&nbsp;1, when the LPPI review period commences. POCs have two weeks to complete their review actions. Failure to complete the review in a timely manner will trigger reminders to you and your AS Fin at Day&nbsp;7 and Day&nbsp;13. AS Fin receives emails at Day&nbsp;1, Day&nbsp;7, and Day&nbsp;13 regardless of progress, to support timely completion.</dd>

                        <dt>How was I identified as the POC?</dt>
                        <dd>POC determination is based on the POC recorded in Vendor Invoice Management (VIM). If there is no VIM POC, the POC comes from the Purchase Order (PO).</dd>

                        <dt>What action is required to complete my LPPI review?</dt>
                        <dd>Refer to the <em>How to complete your review</em> section above. You must review each document and select the appropriate Reason Code to indicate whether the LPPI is payable or not payable. Comments and Evidence (Objective Reference) are required where the outcome is Not&nbsp;payable.</dd>

                        <dt>What happens if I do not complete my actions?</dt>
                        <dd>You will receive reminder emails from the LPPI Administrator on Day&nbsp;7 and Day&nbsp;13 of the review window. If no decision is recorded by the due date, the LPPI charge will be processed automatically against the responsible cost centre.</dd>

                        <dt>What if I receive an email but I am not the correct POC?</dt>
                        <dd>If you are not the POC, or you would like someone else to complete the review on your behalf, forward the LPPI review email to the nominated alternate POC. They can access LPPI Review directly from the link in the forwarded email.</dd>

                        <dt>How are late payments identified?</dt>
                        <dd>Automated logic identifies late payments using a priority-based rule set for each payment. See <a href="#instr-faq-logic">LPPI Logic</a> for the conditions in order of priority.</dd>

                        <dt>What do each of the dates mean?</dt>
                        <dd>Automated logic is used to calculate the due date. See the <a href="#instr-faq-dates">Glossary of Dates</a> for definitions of each date field.</dd>

                        <dt>How are the days late calculated?</dt>
                        <dd>Automated logic calculates the due date and days late from the source ERP data. See <a href="#instr-faq-duedate">Due Date Calculation Logic</a> and the <a href="#instr-faq-duedate-examples">worked examples</a> for the calculation in detail.</dd>

                        <dt>The baseline date in the LPPI Report is incorrect &mdash; what should I do?</dt>
                        <dd>
                            You need to do two things:
                            <ol class="instr-list" style="margin-top:6px;">
                                <li>In LPPI Review, select the Reason Code <strong>Interest Not Payable &mdash; Incorrect data, document eligible for reload</strong> (RC-RL) against the document, and supply Comments and Evidence (Objective Reference) as for any Not&nbsp;payable outcome.</li>
                                <li>Email <a href="mailto:LPPI.Report@defence.gov.au">LPPI.Report@defence.gov.au</a> with the following details so the data can be corrected upstream:
                                    <ul class="instr-list" style="margin-top:4px;">
                                        <li>Document Number</li>
                                        <li>Correct Baseline Date</li>
                                        <li>Evidence to support the correct Baseline Date</li>
                                        <li>Evidence of AS Fin approval</li>
                                    </ul>
                                </li>
                            </ol>
                            The details emailed to LPPI.Report will be loaded into LPPI Review, and a supplementary review email will be sent to you for completion once the corrected line is available.
                        </dd>

                        <dt>I rejected the invoice off-system (e.g. email to supplier) &mdash; why has the invoice incurred LPPI?</dt>
                        <dd>LPPI determination is driven by ERP data and cannot capture actions performed outside of ERP. Record the relevant Reason Code and supporting Evidence in LPPI Review.</dd>

                        <dt>The invoice was paid late due to reasons outside of my control &mdash; what should I do?</dt>
                        <dd>Review the document in LPPI Review and select the appropriate Reason Code to indicate whether the LPPI is payable or not payable. Comments and Evidence (Objective Reference) are required where the outcome is Not&nbsp;payable.</dd>

                        <dt>I approved an invoice late &mdash; why isn't it in LPPI Review?</dt>
                        <dd>Automated logic identifies late payments using a priority-based rule set. Not every late approval triggers an LPPI charge &mdash; see <a href="#instr-faq-logic">LPPI Logic</a> for the conditions that apply.</dd>

                        <dt>I confirmed interest has already been paid for an invoice paid late &mdash; what should I do?</dt>
                        <dd>Review the document in LPPI Review and select the appropriate Reason Code. Comments and Evidence (Objective Reference) are required where the outcome is Not&nbsp;payable.</dd>

                        <dt>The invoice was paid late due to Accounts Payable processing delays or ERP issues &mdash; is interest still payable?</dt>
                        <dd>POCs should take a commercial view on LPPI &mdash; apply practical judgement to maintain a positive ongoing relationship with the Business Partner. No dispute or cost transfer (ERP or DFG) will be considered by AS Fin.</dd>

                        <dt>What account assignment is used for late payment interest?</dt>
                        <dd>LPPI is processed and charged to the responsible business unit (the LPPI Charge Cost Centre shown in the Capability Manager column).</dd>

                        <dt>What can I do to avoid LPPI in the future?</dt>
                        <dd>See the <a href="#instr-faq-best-practice">Best Practice</a> tips below for preventative controls at the contract, supplier, and process level.</dd>

                    </dl>

                    <%-- ============================================================
                         FAQ reference: LPPI Logic
                         Priority-based rule set that determines which late payments
                         are surfaced for LPPI review. Mirrors the "LPPI Logic"
                         reference in the source FAQ document.
                         ============================================================ --%>
                    <h3 id="instr-faq-logic" class="instr-faq-h3">LPPI Logic &mdash; priority-based rule set</h3>
                    <p>Automated logic identifies late payments using a priority-based rule set for each payment. The following conditions are evaluated in order of priority. Any payment matching an <em>Exclude</em> rule drops out at that point; remaining payments are reported for LPPI review.</p>
                    <ol class="instr-logic">
                        <li><span class="instr-logic-tag">Exclude</span> OA / PO created before 1 July 2022 <span class="instr-logic-note">(contract value below $1,000,000 and PO not starting with 48&hellip;)</span></li>
                        <li><span class="instr-logic-tag">Exclude</span> FOREX transactions <span class="instr-logic-note">(non-AUD currency)</span></li>
                        <li><span class="instr-logic-tag">Exclude</span> Employee Vendor Group</li>
                        <li><span class="instr-logic-tag">Exclude</span> Government and State Entity based organisation <span class="instr-logic-note">(using CASG Unified Supplier Identifier (USI) hierarchy)</span></li>
                        <li><span class="instr-logic-tag">Exclude</span> Grants and Non-procurement payments <span class="instr-logic-note">(Document types NP and GP)</span></li>
                        <li><span class="instr-logic-tag">Exclude</span> Real Estate, Rent and Leases <span class="instr-logic-note">(Document types E1 and E3)</span></li>
                        <li><span class="instr-logic-tag">Exclude</span> Recipient Created Tax Invoices <span class="instr-logic-note">(RCTI)</span></li>
                        <li><span class="instr-logic-tag">Exclude</span> GST on Foreign POs <span class="instr-logic-note">(PO account assignment GL is GST GL &mdash; although paid in AUD, relates to procuring foreign services)</span></li>
                        <li><span class="instr-logic-tag">Exclude</span> Credit Notes and Reversals</li>
                        <li><span class="instr-logic-tag instr-logic-tag-apply">Apply</span> 20 days to invoices with Non-Standard Payment Terms <span class="instr-logic-note">(except invoices with 5 days for PEPPOL or 20 days or higher)</span></li>
                        <li><span class="instr-logic-tag">Exclude</span> Calculated LPPI that is less than or equal to $100</li>
                    </ol>
                    <p class="instr-logic-outcome"><strong>LPPI Report</strong> &mdash; identified late payments and calculated LPPI for review by the business POC.</p>

                    <%-- ============================================================
                         FAQ reference: Glossary of Dates
                         Definitions of the date fields used in LPPI calculations.
                         ============================================================ --%>
                    <h3 id="instr-faq-dates" class="instr-faq-h3">Glossary of Dates</h3>
                    <table class="instr-ref-table">
                        <thead>
                            <tr><th>Date name</th><th>Description</th></tr>
                        </thead>
                        <tbody>
                            <tr>
                                <td><strong>Invoice Date</strong></td>
                                <td>The date the supplier generates the invoice. This is the date printed on the invoice.</td>
                            </tr>
                            <tr>
                                <td><strong>Invoice Received Date (IR)</strong></td>
                                <td>The date the invoice is received into <a href="mailto:invoices@defence.gov.au">invoices@defence.gov.au</a>, or the earliest date in the email chain where the invoice was received by a Defence POC.</td>
                            </tr>
                            <tr>
                                <td><strong>Goods Receipt Document Date</strong></td>
                                <td>The date the goods or services were actually received by the Defence representative. This does not impact financial postings &mdash; the expense is recognised at the Goods Receipt Entry Date.</td>
                            </tr>
                            <tr>
                                <td><strong>Goods Receipt Entry Date (GR)</strong></td>
                                <td>The date the Defence representative records the receipt of goods (posts the GR) in the system, and the date the expense is recognised. This may be days or weeks after the actual receipt of goods, but this date cannot be backdated in the system.</td>
                            </tr>
                            <tr>
                                <td><strong>Baseline Date</strong></td>
                                <td>
                                    ERP calculates baseline date as the latter of the Invoice Received Date or the Goods Receipt Entry Date.
                                    DFG propose an interim process to recognise material and non-material purchases with separate baseline date logic:
                                    <ul class="instr-list" style="margin:6px 0 0;">
                                        <li><strong>Material PO</strong> &mdash; later of IR or GR dates</li>
                                        <li><strong>Non-material PO</strong> &mdash; later of IR or Invoice Date</li>
                                    </ul>
                                </td>
                            </tr>
                            <tr>
                                <td><strong>Due Date</strong></td>
                                <td>Calculated as Baseline Date plus the Payment Terms specified on the PO or Vendor.</td>
                            </tr>
                        </tbody>
                    </table>

                    <%-- ============================================================
                         FAQ reference: Due Date Calculation Logic
                         Diagram showing how baseline date + payment terms combine
                         under the interim (1A Material, 1B Non-Material) and
                         future ERP (2) rules.
                         ============================================================ --%>
                    <h3 id="instr-faq-duedate" class="instr-faq-h3">Due Date Calculation Logic</h3>
                    <p>The due date is calculated from the relevant baseline date plus payment terms. The baseline date differs by purchase type and ERP rule version:</p>
                    <div class="instr-duedate">
                        <div class="instr-duedate-row">
                            <div class="instr-duedate-rule">
                                <span class="instr-duedate-rule-num">1A</span>
                                <span class="instr-duedate-rule-label">Interim<br />Material</span>
                            </div>
                            <div class="instr-duedate-eqn">
                                <span class="instr-duedate-cell instr-duedate-result">Due Date</span>
                                <span class="instr-duedate-op">=</span>
                                <span class="instr-duedate-cell instr-duedate-baseline"><em>Latter of</em><br />IR Date or<br />GE date</span>
                                <span class="instr-duedate-op">+</span>
                                <span class="instr-duedate-cell">Payment<br />Terms</span>
                            </div>
                        </div>
                        <div class="instr-duedate-row">
                            <div class="instr-duedate-rule">
                                <span class="instr-duedate-rule-num">1B</span>
                                <span class="instr-duedate-rule-label">Interim<br />Non-Material</span>
                            </div>
                            <div class="instr-duedate-eqn">
                                <span class="instr-duedate-cell instr-duedate-result">Due Date</span>
                                <span class="instr-duedate-op">=</span>
                                <span class="instr-duedate-cell instr-duedate-baseline">IR Date</span>
                                <span class="instr-duedate-op">+</span>
                                <span class="instr-duedate-cell">Payment<br />Terms</span>
                            </div>
                        </div>
                        <div class="instr-duedate-row">
                            <div class="instr-duedate-rule">
                                <span class="instr-duedate-rule-num">2</span>
                                <span class="instr-duedate-rule-label">Future ERP<br />(Aug&nbsp;26)</span>
                            </div>
                            <div class="instr-duedate-eqn">
                                <span class="instr-duedate-cell instr-duedate-result">Due Date</span>
                                <span class="instr-duedate-op">=</span>
                                <span class="instr-duedate-cell instr-duedate-baseline"><em>Latter of</em><br />IR Date or<br />GR date</span>
                                <span class="instr-duedate-op">+</span>
                                <span class="instr-duedate-cell">Payment<br />Terms</span>
                            </div>
                        </div>
                        <p class="instr-duedate-caption">The dashed column above is the <strong>Baseline Date</strong> &mdash; the date from which payment terms are counted.</p>
                    </div>

                    <%-- ============================================================
                         FAQ reference: Due Date Calculation Examples
                         Two worked scenarios contrasting GR-before-invoice vs
                         GR-after-invoice timing.
                         ============================================================ --%>
                    <h3 id="instr-faq-duedate-examples" class="instr-faq-h3">Due Date Calculation Examples</h3>
                    <p>Two worked examples below, contrasting how the due date is calculated when the Goods Receipt is posted before the invoice is received versus after the invoice is received. Payment Terms are 20 days in both scenarios.</p>

                    <table class="instr-ref-table instr-ref-table-examples">
                        <colgroup>
                            <col style="width:24%" />
                            <col style="width:18%" />
                            <col style="width:20%" />
                            <col style="width:19%" />
                            <col style="width:19%" />
                        </colgroup>
                        <thead>
                            <tr>
                                <th rowspan="2">Key dates</th>
                                <th rowspan="2"></th>
                                <th>ERP current</th>
                                <th colspan="2" class="instr-ref-th-group">Interim &mdash; Baseline Date Calculation</th>
                            </tr>
                            <tr>
                                <th>Baseline Date</th>
                                <th>Material</th>
                                <th>Non-Material</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr class="instr-ref-scenario-row instr-ref-scenario-ok">
                                <td colspan="5"><strong>Scenario 1</strong> &mdash; Goods Receipt is done at time of delivery or before the invoice is received.</td>
                            </tr>
                            <tr><td>Invoice Date</td><td>01/04/2026</td><td rowspan="4" class="instr-ref-shaded">Later of GR or IR<br /><strong>07/04/2026</strong></td><td rowspan="4">Per ERP<br />Later of GR &amp; IR<br /><strong>07/04/2026</strong></td><td rowspan="4">(Automation)<br />Later of Invoice Date or IR Date<br /><strong>07/04/2026</strong></td></tr>
                            <tr><td>Invoice Received Date (IR)</td><td>07/04/2026</td></tr>
                            <tr><td>Goods Received Date</td><td>31/03/2026</td></tr>
                            <tr><td>Goods Received Entry Date (GR)</td><td>31/03/2026</td></tr>
                            <tr><td>Due Date Calculated</td><td>Payment Terms 20 days</td><td><strong>27/04/2026</strong></td><td><strong>27/04/2026</strong></td><td><strong>27/04/2026</strong></td></tr>

                            <tr class="instr-ref-scenario-row instr-ref-scenario-warn">
                                <td colspan="5"><strong>Scenario 2</strong> &mdash; Goods Receipt is done after the invoice is received.</td>
                            </tr>
                            <tr><td>Invoice Date</td><td>01/04/2026</td><td rowspan="4" class="instr-ref-shaded">Later of GR or IR<br /><strong>15/04/2026</strong></td><td rowspan="4">Per ERP<br />Later of GR &amp; IR<br /><strong>15/04/2026</strong></td><td rowspan="4">(Automation)<br />Later of Invoice Date or IR Date<br /><strong>07/04/2026</strong></td></tr>
                            <tr><td>Invoice Received Date (IR)</td><td>07/04/2026</td></tr>
                            <tr><td>Goods Received Date</td><td>31/03/2026</td></tr>
                            <tr><td>Goods Received Entry Date (GR)</td><td>15/04/2026</td></tr>
                            <tr><td>Due Date Calculated</td><td>Payment Terms 20 days</td><td><strong>05/05/2026</strong></td><td><strong>05/05/2026</strong></td><td><strong>27/04/2026</strong></td></tr>
                        </tbody>
                    </table>
                    
                    <h3 id="instr-faq-best-practice" class="instr-faq-h3">Best practice: preventing Late Payment Penalty Interest</h3>

                    <p>
                        To minimise the risk of LPPI, Defence must ensure invoices are valid, correctly timed, and processed in accordance with contractual requirements. Preventative controls at the contract, supplier, and process level are essential.
                    </p>

                    <p><strong>Invoicing and contract requirements</strong></p>
                    <ul class="instr-list">
                        <li>Invoices must only be issued after goods are delivered and/or services are formally accepted.</li>
                        <li>The payment period commences only upon receipt of a valid invoice and confirmation of delivery / acceptance.</li>
                        <li>Contracts must specify mandatory invoice requirements, including correct PO number, delivery and/or acceptance date, and relevant contact details.</li>
                        <li>Invoices should be sent directly to <a href="mailto:invoices@defence.gov.au">invoices@defence.gov.au</a> by the supplier.</li>
                        <li>Invoices that are early, incomplete, or incorrect are invalid and must be rejected through VIM, resetting the payment clock.</li>
                        <li>Contracts should include clauses stating that LPPI is not payable where payment delays are caused by supplier error.</li>
                    </ul>

                    <p><strong>Contract manager accountability</strong></p>
                    <ul class="instr-list">
                        <li>Contract managers are responsible for preventing early or incorrect invoicing and ensuring acceptance and receipting occur promptly once delivery is complete.</li>
                        <li>Contract managers must actively monitor invoicing patterns and address recurring issues early to avoid LPPI, noting that LPPI costs are borne by their budgets.</li>
                        <li>For new contracts, standard invoicing and LPPI-related clauses must be included to reinforce compliance and supplier accountability.</li>
                    </ul>

                    <p><strong>Supplier accountability</strong></p>
                    <ul class="instr-list">
                        <li>Suppliers must comply with contractual invoicing requirements and only submit invoices when entitled.</li>
                        <li>Repeated invoicing non-compliance should be addressed through supplier performance management and corrective action.</li>
                    </ul>

                    <p><strong>Process and system controls</strong></p>
                    <ul class="instr-list">
                        <li>Goods receipts and service confirmations must be completed promptly in ERP upon delivery.</li>
                        <li>Invoice queues and ageing must be regularly monitored using VIM dashboards, with priority given to invoices approaching due dates.</li>
                        <li>Incorrect or premature invoices must be rejected immediately, rather than held, to avoid unnecessary LPPI risk.</li>
                        <li>Automation, exception reporting, and duplicate detection within VIM should be leveraged to reduce manual handling and delays.</li>
                    </ul>
                </section>

                <section id="instr-help">
                    <h2>Need help?</h2>
                    <p>
                        If you have any questions about this review or believe a document is in the wrong package, please contact the
                        LPPI administrator using the support link in the email that brought you here. If no decision is recorded by
                        the due date, the LPPI charge will be processed automatically against the responsible cost centre.
                    </p>
                </section>

            </div>
        </div>
    </div><%-- /paneInstructions --%>

    <%-- ================================================================
         TAB 1 — Reason code entry  (default-active pane on page load)
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
                                <th class="col-doc">Document (Lines)</th>
                                <th class="col-vendor">Vendor</th>
                                <th class="col-po">PO</th>
                                <th class="col-wbs">WBS Element</th>
                                <th class="col-cm" title="LPPI Charge Cost Centre">Capability Manager</th>
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
                        data-version='<%# LPPIHelper.Enc(FormatVersion(Eval("ReviewedVersion"))) %>'
                        data-search='<%# LPPIHelper.Enc((string)Eval("SearchBlob")) %>'
                        data-dm='<%# LPPIHelper.Enc(Eval("DeliveryManagerProgram")) %>'
                        data-poc='<%# LPPIHelper.Enc(Eval("PocEmail")) %>'
                        data-wbs='<%# LPPIHelper.Enc(Eval("WbsElement")) %>'
                        data-cm='<%# LPPIHelper.Enc(Eval("CapabilityManager")) %>'
                        data-reviewed='<%# Eval("SelectedReasonCodeID") != DBNull.Value ? "1" : "0" %>'
                        data-outcome='<%# LPPIHelper.Enc(Eval("ReasonOutcome")) %>'
                        data-requires='<%# Convert.ToBoolean(Eval("RequiresComments")) ? "1" : "0" %>'>
                        <td class="col-sel">
                            <input type="checkbox" class="rowselect" data-doc-no='<%# LPPIHelper.Enc(Eval("DocNoAccounting")) %>' />
                        </td>
                        <td class="col-doc">
                            <%# LPPIHelper.SapFiNumberHtml(Eval("DocNoAccounting"), Eval("CompanyCode"), Eval("FiscalYear")) %>
                            <span class="line-count-inline muted">(<%# Eval("LineCount") %>)</span>
                        </td>
                        <td class="col-vendor" title='<%# LPPIHelper.Enc(Eval("VendorName")) + " (" + LPPIHelper.Enc(Eval("VendorNum")) + ")" %>'>
                            <%# LPPIHelper.Enc(Eval("VendorName")) %>
                        </td>
                        <td class="col-po"><%# LPPIHelper.SapPoNumberHtml(Eval("PoNumber")) %></td>
                        <td class="col-wbs" title='<%# LPPIHelper.Enc(Eval("WbsDesc")) %>'><%# LPPIHelper.Enc(Eval("WbsElement")) %></td>
                        <td class="col-cm" title='<%# "LPPI Charge Cost Centre: " + LPPIHelper.Enc(Eval("CapabilityManager")) + " (" + LPPIHelper.Enc(Eval("CapabilityManagerName")) + ")" %>'>
                            <%# LPPIHelper.Enc(Eval("CapabilityManager")) %>
                        </td>
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

        <%-- Per-tab toolbar — Export button. Anchored to the lines pane so
             it does not appear on the entry tab. --%>
        <div class="lines-toolbar">
            <a id="exportLinesBtn" class="btn btn-secondary"
               href='<%= "LPPI_Review_Export.ashx?t=" + System.Uri.EscapeDataString(TokenForClient) %>'
               title="Download all lines as Excel">
                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
                    <polyline points="7 10 12 15 17 10"/>
                    <line x1="12" y1="15" x2="12" y2="3"/>
                </svg>
                <span>Export to Excel</span>
            </a>
        </div>

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
                                <th class="col-dm">Delivery Manager</th>
                                <th class="col-dmprog">DM Program</th>
                                <th class="col-cm" title="LPPI Charge Cost Centre">Capability Manager</th>
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
                        data-cm='<%# LPPIHelper.Enc(Eval("CapabilityManager")) %>'>
                        <td class="col-doc">
                            <%# LPPIHelper.SapFiNumberHtml(Eval("DocNoAccounting"), Eval("CompanyCode"), Eval("FiscalYear")) %>
                        </td>
                        <td class="col-seq num"><span class="seq-chip"><%# string.Format("{0:000}", Eval("ItemSequence")) %></span></td>
                        <td class="col-vendor" title='<%# LPPIHelper.Enc(Eval("VendorName")) %>'><%# LPPIHelper.Enc(Eval("VendorName")) %></td>
                        <td class="col-po"><%# LPPIHelper.SapPoNumberHtml(Eval("PoNumber")) %></td>
                        <td class="col-wbs" title='<%# LPPIHelper.Enc(Eval("WbsDesc")) %>'><%# LPPIHelper.Enc(Eval("WbsElement")) %></td>
                        <td class="col-gl"><%# LPPIHelper.Enc(Eval("GlAccount")) %></td>
                        <td class="col-dm" title='<%# LPPIHelper.Enc(Eval("DeliveryManagerName")) %>'><%# LPPIHelper.Enc(Eval("DeliveryManager")) %></td>
                        <td class="col-dmprog"><%# LPPIHelper.Enc(Eval("DeliveryManagerProgram")) %></td>
                        <td class="col-cm" title='<%# "LPPI Charge Cost Centre: " + LPPIHelper.Enc(Eval("CapabilityManager")) + " (" + LPPIHelper.Enc(Eval("CapabilityManagerName")) + ")" %>'><%# LPPIHelper.Enc(Eval("CapabilityManager")) %></td>
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
