<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="PORD_Review.aspx.cs" Inherits="CPlatform.PORD.PORD_Review" %>
<%@ Import Namespace="CPlatform.PORD" %>
<!DOCTYPE html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>PO Review</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <link rel="stylesheet" href="../css/pord.css" />
</head>
<body class="pord-review-page">

<%-- ===================== Invalid / expired link ===================== --%>
<asp:PlaceHolder ID="phError" runat="server">
<div class="review-shell">
    <div class="pord-rv-head">
        <div class="pord-rv-brand">
            <div class="brand-mark"><svg viewBox="0 0 24 24" width="20" height="20" stroke="#fff" fill="none" stroke-width="2"><path d="M6 2h9l5 5v13a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z"/><path d="M14 2v6h6"/></svg></div>
            <div><div class="crumb">PO Review</div><h1>Review link invalid or expired</h1></div>
        </div>
        <p style="margin:16px 0 0;">This review link is no longer active. It may have been replaced by a newer review, or the package may be closed.
        Reply to the email that brought you here and your AS Fin team will send you a current link.</p>
    </div>
</div>
</asp:PlaceHolder>

<%-- ===================== Review ===================== --%>
<asp:PlaceHolder ID="phReview" runat="server" Visible="false">
<input type="hidden" id="pordToken" value="<%= PORDHelper.Attr(Token) %>" />
<input type="hidden" id="pordReadOnly" value="<%= IsReadOnly ? "1" : "0" %>" />
<input type="hidden" id="pordIsPoc" value="<%= IsPoc ? "1" : "0" %>" />

<div class="review-shell<%= IsReadOnly ? " pord-readonly" : "" %>">

    <%= StatusBannerHtml %>
    <% if (PORDHelper.DemoMode) { %>
    <div class="alert info" style="align-items:center;"><strong>Demonstration.</strong>&nbsp;Suppliers, people and values are fictional. Saves are held in memory only.</div>
    <% } %>

    <div class="pord-rv-head">
        <div class="pord-rv-top">
            <div class="pord-rv-brand">
                <div class="brand-mark"><svg viewBox="0 0 24 24" width="20" height="20" stroke="#fff" fill="none" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M6 2h9l5 5v13a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z"/><path d="M14 2v6h6"/><path d="M8 13h5M8 17h3"/></svg></div>
                <div>
                    <div class="crumb">PO Review · Non-standard payment terms</div>
                    <h1><%= PORDHelper.Enc(Package.DmProgram) %></h1>
                </div>
            </div>
            <div class="pord-rv-who">
                <strong><%= PORDHelper.Enc(ViewerName) %></strong>
                <%= PORDHelper.Enc(ViewerEmail) %><br />
                <span class="role <%= IsPoc ? "poc" : "" %>"><%= IsPoc ? "PO contact — your POs only" : "AS Fin — full package" %></span>
            </div>
        </div>

        <div class="pord-rv-meta">
            <div>
                <div class="k">Respond by</div>
                <div class="v"><%= PORDHelper.Date(Package.DueDate) %></div>
                <div class="s <%= DueCss %>"><%= PORDHelper.Enc(DueText) %></div>
            </div>
            <div>
                <div class="k">Progress</div>
                <div class="v" id="pordProgressLabel"><%= ReviewedCount %> of <%= TotalCount %></div>
                <div class="progress-track"><div class="progress-bar" id="pordProgressBar" style="width:<%= PORDHelper.Pct(ReviewedCount, TotalCount) %>%"></div></div>
            </div>
            <div>
                <div class="k">Issues</div>
                <div class="v"><%= TotalCount %> <span style="font-size:13px;font-weight:600;color:var(--ink-3);">POs · $<%= PORDHelper.MoneyShort(OpenValue) %> open</span></div>
                <div class="catbar"><%= CategoryBarHtml %></div>
            </div>
            <div class="policy">
                <div class="k">Policy</div>
                <a href="<%= PORDHelper.PolicyUrl %>" target="_blank" rel="noopener">RMG-417 — Supplier Pay On-Time or Pay Interest Policy</a>
                <div class="s">Standard terms: 20 days, or 5 days for PEPPOL e-invoicing suppliers. Foreign currency: at least 14 days.</div>
            </div>
        </div>
    </div>

    <div class="pord-tabs" role="tablist">
        <button type="button" class="pord-tab" data-pane="paneInstr" role="tab">Instructions</button>
        <button type="button" class="pord-tab active" data-pane="paneReview" role="tab">Review POs <span class="cnt"><%= TotalCount %></span></button>
        <button type="button" class="pord-tab" data-pane="paneSummary" role="tab">Summary</button>
    </div>

    <%-- ===================== Instructions ===================== --%>
    <div id="paneInstr" class="pord-pane" role="tabpanel">
        <div class="pord-instr">
            <nav class="pord-toc"><div class="t">On this page</div>
                <ol>
                    <li><a href="#i-about">Why you are here</a></li>
                    <li><a href="#i-how">How to respond</a></li>
                    <li><a href="#i-cats">What the issues mean</a></li>
                    <li><a href="#i-reasons">Valid reasons</a></li>
                    <li><a href="#i-next">What happens next</a></li>
                    <li><a href="#i-faq">FAQ</a></li>
                </ol>
            </nav>
            <div class="pord-doc">
                <section id="i-about">
                    <h2>Why you are here</h2>
                    <p>Each month Defence Finance Group checks every open purchase order against the
                    <a href="<%= PORDHelper.PolicyUrl %>" target="_blank" rel="noopener">Supplier Pay On-Time or Pay Interest Policy (RMG-417)</a>.
                    The POs listed have payment terms that are not the Commonwealth standard, or that differ from the supplier's
                    Business Partner (BP) master record. Non-standard terms put Defence at risk of late payment interest and slow payments to suppliers.</p>
                    <p><%= IsPoc ? "You are listed as the contact for these POs. Your AS Fin team can see the whole package and will finalise it." : "You can see every PO in the package. PO contacts see only their own. When responses are in, finalise the package." %></p>
                </section>
                <section id="i-how">
                    <h2>How to respond</h2>
                    <p>For each PO, choose one <strong>Response</strong>, then click <strong>Save changes</strong>. Nothing is saved until you do.</p>
                    <div class="opts">
                        <div class="opt amend"><strong>Will amend PO terms</strong>Change the PO to standard terms in ERP. Give the date you expect it done. Next month's check confirms the change automatically.</div>
                        <div class="opt reason"><strong>Valid reason</strong>The terms should stay. Choose the reason and add the Objective reference for your evidence. Accepted reasons stop the PO being flagged for <%= PORDHelper.ExclusionMonths %> months.</div>
                        <div class="opt reassign"><strong>Not mine – reassign</strong>Tell us who owns the PO in Comments. The PO is redirected to them next cycle.</div>
                    </div>
                    <p>To apply the same response to several POs, tick them and use the bar at the bottom of the screen.</p>
                </section>
                <section id="i-cats">
                    <h2>What the issues mean</h2>
                    <table class="ref">
                        <tr><th>Issue</th><th>Meaning</th><th>Typical fix</th></tr>
                        <tr><td><%= PORDHelper.CategoryPill(PordCategory.NonStandard) %></td><td>PO terms are not 5 or 20 days, and the supplier master has the same terms. This is rare because supplier masters carry standard terms.</td><td>Amend the PO. The admin team will ask DFIM to correct the supplier master.</td></tr>
                        <tr><td><%= PORDHelper.CategoryPill(PordCategory.Override) %></td><td>PO terms were changed from the supplier master, for example 5 days on a supplier that does not invoice through PEPPOL.</td><td>Reset the PO terms to the master value.</td></tr>
                        <tr><td><%= PORDHelper.CategoryPill(PordCategory.NonStandardAndOverride) %></td><td>PO terms are not 5 or 20 days, so they differ from the supplier master.</td><td>Reset the PO terms to the master value.</td></tr>
                        <tr><td><%= PORDHelper.CategoryPill(PordCategory.ForeignCurrency) %></td><td>Foreign-currency PO with terms under 14 days. DFG cannot reliably make a foreign-currency payment within 5 days.</td><td>Use 14-day terms (PEPPOL arrangements included).</td></tr>
                    </table>
                    <p>The <strong>Nbr</strong> column shows how many monthly reviews the PO has appeared in. A red number means it was flagged before and is still not fixed.</p>
                </section>
                <section id="i-reasons">
                    <h2>Valid reasons</h2>
                    <table class="ref">
                        <tr><th>Code</th><th>Reason</th><th>Needs</th></tr>
                        <asp:Repeater ID="rptReasonRef" runat="server"><ItemTemplate>
                            <tr><td><code><%# PORDHelper.Enc(R(Container.DataItem).Code) %></code></td><td><%# PORDHelper.Enc(R(Container.DataItem).Description) %></td>
                                <td><%# (R(Container.DataItem).RequiresEvidence ? "Objective reference" : "") + (R(Container.DataItem).RequiresEvidence && R(Container.DataItem).RequiresComments ? " + " : "") + (R(Container.DataItem).RequiresComments ? "comments" : "") %></td></tr>
                        </ItemTemplate></asp:Repeater>
                    </table>
                </section>
                <section id="i-next">
                    <h2>What happens next</h2>
                    <ol>
                        <li>AS Fin checks the responses and <strong>finalises</strong> the package by the due date. POs with no response are recorded as <em>No response</em>.</li>
                        <li>Next month's extract is the check. A PO you committed to amend that no longer appears is marked <strong>Resolved</strong>.
                            If it is still non-compliant it comes back with a higher <strong>Nbr</strong> and is escalated.</li>
                        <li>POs with an accepted valid reason are excluded from future reviews until their terms change or the exclusion expires.</li>
                    </ol>
                </section>
                <section id="i-faq">
                    <h2>FAQ</h2>
                    <dl>
                        <dt>Where do I change the payment terms?</dt>
                        <dd>Click the PO number to open it in ERP, then change the payment terms on the PO header. If the supplier master is also wrong, note it in Comments.</dd>
                        <dt>The supplier insists on shorter terms.</dt>
                        <dd>Shorter terms are allowed only for a valid reason, for example a demonstrated benefit such as an early-payment discount. Record it as a valid reason with evidence.</dd>
                        <dt>The PO is finished and should be closed.</dt>
                        <dd>Choose <em>Will amend</em>, close the PO in ERP, and say so in Comments. Closed POs drop out of the next extract.</dd>
                    </dl>
                </section>
            </div>
        </div>
    </div>

    <%-- ===================== Review ===================== --%>
    <div id="paneReview" class="pord-pane active" role="tabpanel">
        <div class="toolbar pord-toolbar">
            <div class="toolbar-left">
                <div class="search-wrap">
                    <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>
                    <input type="text" id="pordSearch" class="input" placeholder="Search PO, supplier, BP, terms…" />
                </div>
                <select id="pordFilterResp" title="Response">
                    <option value="">All responses</option>
                    <option value="awaiting">Awaiting response</option>
                    <option value="Amend">Will amend</option>
                    <option value="Reason">Valid reason</option>
                    <option value="Reassign">Reassign</option>
                    <option value="NoResponse">No response</option>
                    <option value="attention">Needs attention</option>
                </select>
                <% if (!IsPoc) { %>
                <select id="pordFilterPoc" title="PO contact">
                    <option value="">All PO contacts</option>
                    <%= PocOptionsHtml %>
                </select>
                <% } %>
                <select id="pordFilterRn" title="Review Nbr">
                    <option value="">Any Review Nbr</option>
                    <option value="repeat">Repeat only (Nbr ≥ <%= PORDHelper.EscalateAtReview %>)</option>
                    <option value="first">First time</option>
                </select>
            </div>
            <div class="toolbar-right">
                <span id="pordSaveInd" class="pord-save-ind saved" role="status" aria-live="polite"><%= IsReadOnly ? "Read only" : "All changes saved" %></span>
                <button type="button" id="pordSaveBtn" class="btn btn-primary" disabled>
                    <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><polyline points="17 21 17 13 7 13 7 21"/><polyline points="7 3 7 8 15 8"/></svg>
                    <span id="pordSaveLbl">Save changes</span>
                </button>
                <% if (ShowFinalise) { %>
                <button type="button" id="pordFinaliseBtn" class="btn" data-action="finalise"
                        title="Close off the package. POs without a response are recorded as No response.">
                    <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="20 6 9 17 4 12"/></svg>
                    Finalise
                </button>
                <% } else if (ShowReopen) { %>
                <button type="button" id="pordFinaliseBtn" class="btn reopen" data-action="unfinalise"
                        title="Reopen for further changes. No-response markers are cleared.">Reopen</button>
                <% } %>
            </div>
        </div>

        <div class="pord-chips" id="pordChips">
            <button type="button" class="pord-chip on" data-cat="">All issues <span class="n"><%= TotalCount %></span></button>
            <%= CategoryChipsHtml %>
        </div>

        <div id="pordReady" class="pord-ready<%= IsAllReviewed && !IsReadOnly ? " show" : "" %>" role="status">
            <svg viewBox="0 0 24 24" width="28" height="28" fill="none" stroke="#fff" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="16 9 10.5 15 8 12.5"/></svg>
            <div>
                <% if (IsPoc) { %>
                <strong>All done — thanks.</strong><span>Every PO assigned to you has a response. You can close this page. AS Fin will finalise the package.</span>
                <% } else { %>
                <strong>Every PO has a response — ready to finalise.</strong><span>Check the responses, then click Finalise. You can reopen it later if needed.</span>
                <% } %>
            </div>
        </div>

        <asp:PlaceHolder ID="phEmpty" runat="server" Visible="false">
            <div class="empty-state"><h2>Nothing to review</h2><p>There are no POs assigned to you in this package.</p></div>
        </asp:PlaceHolder>

        <div class="tbl-wrap pord-rv-wrap">
        <table class="tbl tbl-pord" id="pordTable">
            <thead>
                <tr>
                    <th class="col-sel"><input type="checkbox" id="pordSelAll" title="Select all visible" /></th>
                    <th class="col-po">PO</th>
                    <th class="col-bp">Supplier</th>
                    <th>Terms (PO vs BP) &amp; issue</th>
                    <th class="num col-val" title="Value still to be delivered">Open value</th>
                    <th class="num" title="Number of monthly reviews this PO has appeared in">Nbr</th>
                    <% if (!IsPoc) { %><th>PO contact</th><% } %>
                    <th class="col-resp">Response</th>
                    <th class="col-detail">Detail</th>
                    <th class="col-cmt">Comments</th>
                    <th class="col-exp"></th>
                </tr>
            </thead>
            <tbody>
            <asp:Repeater ID="rptPos" runat="server">
                <ItemTemplate>
                <tr class="po-row" data-id="<%# Po(Container.DataItem).PoID %>"
                    data-version="<%# PORDHelper.Attr(PORDHelper.VersionOf(Po(Container.DataItem).ReviewedDate)) %>"
                    data-cat="<%# PORDRules.CategoryKey(Po(Container.DataItem).Category) %>"
                    data-rn="<%# Po(Container.DataItem).ReviewNbr %>"
                    data-poc="<%# PORDHelper.Attr(Po(Container.DataItem).PocEmail) %>"
                    data-search="<%# PORDHelper.Attr(SearchBlob(Po(Container.DataItem))) %>">
                    <td class="col-sel"><input type="checkbox" class="po-sel" /></td>
                    <td class="col-po"><%# PORDHelper.PoLinkHtml(Po(Container.DataItem).PoNumber) %><span class="sub">Created <%# PORDHelper.Date(Po(Container.DataItem).PoCreatedDate) %></span></td>
                    <td class="col-bp"><span class="nm"><%# PORDHelper.Enc(Po(Container.DataItem).BpName) %></span><span class="sub">BP <%# PORDHelper.Enc(Po(Container.DataItem).BpNumber) %></span></td>
                    <td class="col-terms"><%# PORD_Admin.TermsHtmlStatic(Po(Container.DataItem)) %><div style="margin-top:6px;"><%# PORDHelper.CategoryPill(Po(Container.DataItem).Category) %></div></td>
                    <td class="num col-val"><%# PORDHelper.Money(Po(Container.DataItem).StillToDeliver) %><span class="pord-ccy<%# PORDRules.IsForeignCurrency(Po(Container.DataItem).Currency) ? " fx" : "" %>"><%# PORDHelper.Enc(Po(Container.DataItem).Currency) %></span></td>
                    <td class="num"><%# PORDHelper.ReviewNbrChip(Po(Container.DataItem).ReviewNbr) %></td>
                    <%# IsPoc ? "" : "<td><span title=\"" + PORDHelper.Attr(Po(Container.DataItem).PocEmail) + "\">" + PORDHelper.Enc(Po(Container.DataItem).PocName) + "</span></td>" %>
                    <td class="col-resp">
                        <select class="input resp-select" data-v="<%# PORDHelper.Attr(Po(Container.DataItem).Response) %>" aria-label="Response">
                            <%# ResponseOptions(Po(Container.DataItem).Response) %>
                        </select>
                    </td>
                    <td class="col-detail">
                        <div class="detail-field f-target<%# Po(Container.DataItem).Response == PordResponse.Amend ? " show" : "" %>">
                            <label>Amend by</label>
                            <input type="date" class="input in-target" value="<%# PORDHelper.IsoDate(Po(Container.DataItem).TargetDate) %>" />
                        </div>
                        <div class="detail-field f-reason<%# Po(Container.DataItem).Response == PordResponse.Reason ? " show" : "" %>">
                            <label>Reason</label>
                            <select class="input in-reason"><%# ReasonOptions(Po(Container.DataItem).ReasonCode) %></select>
                        </div>
                        <div class="detail-field f-evidence<%# Po(Container.DataItem).Response == PordResponse.Reason ? " show" : "" %>">
                            <label>Evidence (Objective ref)</label>
                            <input type="text" class="input in-evidence" maxlength="100" placeholder="e.g. BN12345678" value="<%# PORDHelper.Attr(Po(Container.DataItem).EvidenceRef) %>" />
                        </div>
                        <div class="detail-hint" style="<%# HintVisible(Po(Container.DataItem)) ? "" : "display:none" %>"><%# DetailHint(Po(Container.DataItem)) %></div>
                    </td>
                    <td class="col-cmt"><textarea class="input in-comments" rows="1" maxlength="1000" placeholder="<%# Po(Container.DataItem).Response == PordResponse.Reassign ? "Who owns this PO?" : "Optional" %>"><%# PORDHelper.Enc(Po(Container.DataItem).Comments) %></textarea></td>
                    <td class="col-exp"><button type="button" class="btn-chev" aria-expanded="false" title="PO details"><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="9 18 15 12 9 6"/></svg></button></td>
                </tr>
                <tr class="po-msg" data-id="<%# Po(Container.DataItem).PoID %>"><td colspan="<%# ColumnCount %>"><div class="row-msg" role="alert"></div></td></tr>
                <tr class="po-exp" data-id="<%# Po(Container.DataItem).PoID %>"><td colspan="<%# ColumnCount %>"><%# DetailPanel(Po(Container.DataItem)) %></td></tr>
                </ItemTemplate>
            </asp:Repeater>
            </tbody>
        </table>
        </div>
        <div id="pordNoMatch" class="pord-empty" style="display:none;">No POs match these filters.</div>

        <div id="pordBulk" class="bulk-bar pord-bulk" aria-live="polite">
            <span><span id="pordBulkCount" class="count">0</span> selected</span>
            <select id="pordBulkResp">
                <option value="">Set response…</option>
                <option value="Amend">Will amend PO terms</option>
                <option value="Reason">Valid reason</option>
            </select>
            <input type="date" id="pordBulkDate" style="display:none;" title="Amend by" />
            <select id="pordBulkReason" style="display:none;"><%= ReasonOptions(null) %></select>
            <button type="button" id="pordBulkApply" class="btn btn-primary">Apply</button>
            <button type="button" id="pordBulkClear" class="btn btn-ghost" style="color:#fff;">Clear</button>
        </div>
    </div>

    <%-- ===================== Summary ===================== --%>
    <div id="paneSummary" class="pord-pane" role="tabpanel">
        <div class="pord-cols">
            <div class="card">
                <h2>By issue</h2>
                <table class="tbl tbl-compact">
                    <thead><tr><th>Issue</th><th class="num">POs</th><th class="num">Open value</th><th class="num">Responded</th></tr></thead>
                    <tbody><%= SummaryByCategoryHtml %></tbody>
                </table>
            </div>
            <div class="card">
                <h2><%= IsPoc ? "By response" : "By PO contact" %></h2>
                <table class="tbl tbl-compact">
                    <%= SummarySecondHtml %>
                </table>
            </div>
        </div>
    </div>
</div>
<script src="../js/pord.js"></script>
</asp:PlaceHolder>
</body>
</html>
