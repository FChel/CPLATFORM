/* =============================================================================
   LPPI Review — reviewer page interactions
   Vanilla JS, no jQuery, no frameworks.

   Behaviour:
   * Three top-level tabs: "Instructions" (Tab 0), "Reason code entry" (Tab 1)
     and "All lines" (Tab 2). Tab 1 is the default-active pane on page load;
     the Instructions tab is informational only.
   * Tab 1 — editable table, one row per document. EXPLICIT save model:
     editing fields marks the row dirty; nothing is sent until the user
     clicks the "Save changes" button. The button is disabled when there
     are no unsaved changes.
   * Optimistic locking: each row carries a data-version attribute
     reflecting the ReviewedDate the row was loaded with. The save handler
     refuses any row whose loaded version no longer matches the current
     value in the DB ("someone else has saved this since you opened it"),
     and returns the latest values so the user can reload the row.
   * Bulk-apply stages dirty rows rather than saving immediately. Click
     Save to commit.
   * Mandatory-field rules enforced client-side (server enforces authoritatively):
       - RequiresComments: Comments must be non-empty.
       - NotPayable: both Comments and Objective Reference required.
   * Action button (single slot in the toolbar): toggles between Finalise
     and Unfinalise based on the package status. Editable states show a
     green Finalise button; Finalised shows an orange Unfinalise button;
     Exported / Cancelled show no button. Confirmation is a confirm()
     dialog re-stating the action; on success the page is reloaded so
     the new status comes from the server-rendered HTML.
   * beforeunload warning if the dirty set is non-empty.
   * Filters (search, status, facets) apply to both Tab 1 rows and Tab 2 rows.
     The Instructions tab hides the toolbar entirely.
   * Expand chevron on each doc row opens an inline sub-row showing line
     detail for that document (read from the rptDetail DOM — no extra
     server call). The chevron now works on every row, not just multi-line
     documents — single-line docs render the same panel layout with one row.
   * Comments textarea expands on focus, contracts on blur.
   * Primary key: DocNoAccounting (data-doc-no).
   ============================================================================= */
(function () {
    'use strict';

    var SAVE_URL       = 'LPPI_Review_Save.ashx';
    var FINALISE_URL   = 'LPPI_Review_Finalise.ashx';
    var UNFINALISE_URL = 'LPPI_Review_Unfinalise.ashx';
    var token    = (document.getElementById('reviewToken') || {}).value || '';
    var readOnly = ((document.getElementById('reviewReadOnly') || {}).value || '0') === '1';

    var allMain   = [];
    var mainByDoc = {};   // docNo -> .doc-main <tr>
    var allDetail = [];   // .detail-row elements (Tab 2)

    var dirtySet         = {};   // docNo -> true, while there are unsaved local changes
    var saveInFlight     = false;
    var actionInFlight   = false;
    var totalDocs        = 0;
    var reviewedDocs     = 0;

    var saveIndicator = document.getElementById('saveIndicator');
    var saveButton    = document.getElementById('saveAllBtn');
    var saveLabel     = document.getElementById('saveAllBtnLabel');

    // Action button — single slot that holds either Finalise (editable
    // states) or Unfinalise (Finalised). May be null on Exported /
    // Cancelled / empty-package, in which case there is no action to take.
    // Distinguish via the data-action attribute set in the markup.
    var actionButton = document.getElementById('actionBtn');
    var actionLabel  = document.getElementById('actionBtnLabel');

    var FACETS = [
        { id: 'filterDm',  attr: 'data-dm'  },
        { id: 'filterPoc', attr: 'data-poc' },
        { id: 'filterWbs', attr: 'data-wbs' },
        { id: 'filterCm',  attr: 'data-cm'  }
    ];

    /* =========================================================================
       Bootstrap
       ========================================================================= */
    function init() {
        allMain   = Array.prototype.slice.call(document.querySelectorAll('#paneReason .doc-main'));
        allDetail = Array.prototype.slice.call(document.querySelectorAll('#paneLines .detail-row'));
        totalDocs = allMain.length;

        allMain.forEach(function (row) {
            var dn = row.getAttribute('data-doc-no');
            if (dn) mainByDoc[dn] = row;
        });

        // Seed reviewed class from pre-selected reason codes.
        allMain.forEach(function (row) {
            // A row counts as reviewed if EITHER:
            //   (a) the server stamped it reviewed via data-reviewed='1' — the
            //       authoritative source, especially in read-only mode where
            //       auto-applied RC-NR isn't in the dropdown option list
            //       (IsActive=0), OR
            //   (b) the dropdown has a value — needed in editable mode where
            //       the user has just made a selection that hasn't been saved
            //       yet (no data-reviewed update until reload).
            var sel = row.querySelector('.reason-select');
            var serverReviewed = row.getAttribute('data-reviewed') === '1';
            if (serverReviewed || (sel && sel.value)) row.classList.add('reviewed');
        });
        reviewedDocs = allMain.filter(function (r) { return r.classList.contains('reviewed'); }).length;
        updateProgress();
        updateReadyBanner();

        bindRowControls(allMain);
        allMain.forEach(evaluateNeeds);
        bindExpandChevrons();
        bindCommentsExpand();
        bindInstructionsTocLinks();

        // Filters
        var search = document.getElementById('searchBox');
        if (search) search.addEventListener('input', debounce(applyFilter, 200));
        var statusFilter = document.getElementById('statusFilter');
        if (statusFilter) statusFilter.addEventListener('change', applyFilter);
        FACETS.forEach(function (f) {
            var el = document.getElementById(f.id);
            if (el) el.addEventListener('change', applyFilter);
        });

        // Tabs
        var tabInstructions = document.getElementById('tabInstructions');
        var tabReason       = document.getElementById('tabReason');
        var tabLines        = document.getElementById('tabLines');
        if (tabInstructions) tabInstructions.addEventListener('click', function () { setTab('instructions'); });
        if (tabReason)       tabReason.addEventListener('click',       function () { setTab('reason'); });
        if (tabLines)        tabLines.addEventListener('click',        function () { setTab('lines');  });

        // Save button
        if (saveButton) saveButton.addEventListener('click', onSaveClick);

        // Action button — Finalise OR Unfinalise. The handler dispatches
        // by the data-action attribute set in the markup so a single
        // listener handles both directions.
        if (actionButton) actionButton.addEventListener('click', onActionClick);

        bindBulk();
        bindKeyboard();

        // Beforeunload warning
        window.addEventListener('beforeunload', function (e) {
            if (Object.keys(dirtySet).length > 0) {
                var msg = 'You have unsaved review changes. Leave anyway?';
                e.returnValue = msg;
                return msg;
            }
        });

        updateSaveButton();
    }

    /* =========================================================================
       Tab switching

       Three tabs: instructions / reason / lines. The toolbar (search +
       facets + save) and the bulk action bar are visible only on the
       Reason code entry tab. The Instructions tab is informational only
       and hides those controls; the All lines tab keeps the search/facet
       filters live but hides the bulk bar (no editable rows there).
       ========================================================================= */
    function setTab(tab) {
        var paneInstructions = document.getElementById('paneInstructions');
        var paneReason       = document.getElementById('paneReason');
        var paneLines        = document.getElementById('paneLines');
        var tabInstructions  = document.getElementById('tabInstructions');
        var tabReason        = document.getElementById('tabReason');
        var tabLines         = document.getElementById('tabLines');
        if (!paneReason || !paneLines) return;

        // Reset all panes / tabs first.
        if (paneInstructions) paneInstructions.classList.remove('active');
        paneReason.classList.remove('active');
        paneLines.classList.remove('active');

        if (tabInstructions) { tabInstructions.classList.remove('active'); tabInstructions.setAttribute('aria-selected', 'false'); }
        if (tabReason)       { tabReason.classList.remove('active');       tabReason.setAttribute('aria-selected',       'false'); }
        if (tabLines)        { tabLines.classList.remove('active');        tabLines.setAttribute('aria-selected',        'false'); }

        // Toolbar + bulk bar visibility.
        var toolbar = document.querySelector('.review-shell > .toolbar');
        var bulkBar = document.getElementById('bulkBar');

        if (tab === 'instructions') {
            if (paneInstructions) paneInstructions.classList.add('active');
            if (tabInstructions)  { tabInstructions.classList.add('active'); tabInstructions.setAttribute('aria-selected', 'true'); }
            if (toolbar) toolbar.style.display = 'none';
            if (bulkBar) bulkBar.style.display = 'none';
        } else if (tab === 'lines') {
            paneLines.classList.add('active');
            if (tabLines) { tabLines.classList.add('active'); tabLines.setAttribute('aria-selected', 'true'); }
            if (toolbar) toolbar.style.display = '';
            // Bulk bar hidden on All lines (no editable rows there). The CSS
            // class .bulk-bar is hidden by default and shown via .show; we
            // strip .show here so it cannot leak onto the lines tab.
            if (bulkBar) {
                bulkBar.classList.remove('show');
                bulkBar.style.display = '';
            }
        } else {
            paneReason.classList.add('active');
            if (tabReason) { tabReason.classList.add('active'); tabReason.setAttribute('aria-selected', 'true'); }
            if (toolbar) toolbar.style.display = '';
            if (bulkBar) bulkBar.style.display = '';
        }

        applyFilter();
    }

    /* =========================================================================
       Instructions tab — TOC links

       The Instructions pane has a sticky in-pane TOC sidebar. Clicking a
       TOC link should scroll smoothly to the target section. Browser
       default jump is jarring inside a scrolling pane; smooth-scroll
       softens it. CSS scroll-margin-top in lppi.css keeps section
       headings clear of the sticky shell header on jump.
       ========================================================================= */
    function bindInstructionsTocLinks() {
        var pane = document.getElementById('paneInstructions');
        if (!pane) return;
        var links = pane.querySelectorAll('.instructions-toc a[href^="#"]');
        Array.prototype.forEach.call(links, function (a) {
            a.addEventListener('click', function (e) {
                var href = a.getAttribute('href') || '';
                if (href.length < 2) return;
                var target = document.getElementById(href.slice(1));
                if (!target) return;
                e.preventDefault();
                target.scrollIntoView({ behavior: 'smooth', block: 'start' });
            });
        });
    }

    /* =========================================================================
       Row controls
       ========================================================================= */
    function bindRowControls(rows) {
        rows.forEach(function (row) {
            var docNo = row.getAttribute('data-doc-no');

            var sel = row.querySelector('.reason-select');
            if (sel) {
                sel.addEventListener('change', function () {
                    markDirty(row, docNo);
                    evaluateNeeds(row);
                });
            }

            var ta = row.querySelector('.comments-input');
            if (ta) {
                ta.addEventListener('input', function () {
                    markDirty(row, docNo);
                    evaluateNeeds(row);
                });
            }

            var inp = row.querySelector('.objref-input');
            if (inp) {
                inp.addEventListener('input', function () {
                    markDirty(row, docNo);
                    evaluateNeeds(row);
                });
            }
        });
    }

    /* =========================================================================
       Comments textarea — expand on focus, contract on blur
       ========================================================================= */
    function bindCommentsExpand() {
        document.addEventListener('focusin', function (e) {
            if (e.target && e.target.classList.contains('comments-input')) {
                e.target.classList.add('comments-expanded');
            }
        });
        document.addEventListener('focusout', function (e) {
            if (e.target && e.target.classList.contains('comments-input')) {
                e.target.classList.remove('comments-expanded');
            }
        });
    }

    /* =========================================================================
       Chevron expand/collapse — inline line-detail sub-row

       The expand panel is now built for EVERY document, not just multi-line
       ones. Single-line docs render the same panel layout with their one
       line — useful because the panel exposes detail (Delivery Manager,
       Capability Manager, GL account, POC) that the main row doesn't show.
       ========================================================================= */
    function bindExpandChevrons() {
        document.addEventListener('click', function (e) {
            var btn = e.target && e.target.closest ? e.target.closest('.btn-expand') : null;
            if (!btn) return;

            var docNo     = btn.getAttribute('data-doc-no');
            var mainRow   = mainByDoc[docNo];
            if (!mainRow) return;

            // Find the expand panel row (always the third sibling after doc-main:
            // mainRow -> doc-main-msg -> doc-expand-panel)
            var panelRow  = mainRow.nextElementSibling && mainRow.nextElementSibling.nextElementSibling;
            if (!panelRow || !panelRow.classList.contains('doc-expand-panel')) return;

            var isOpen = panelRow.style.display !== 'none';

            if (isOpen) {
                panelRow.style.display = 'none';
                btn.setAttribute('aria-expanded', 'false');
                btn.classList.remove('is-open');
            } else {
                // Build content from rptDetail rows for this docNo. Every doc has
                // at least one line so the panel is always populated — no special
                // case for single-line documents.
                var lines = allDetail.filter(function (r) {
                    return r.getAttribute('data-doc-no') === docNo;
                });
                var inner = panelRow.querySelector('.expand-panel-inner');
                if (inner) {
                    if (lines.length === 0) {
                        // Defensive — shouldn't happen given how the package is
                        // built, but better than rendering an empty table.
                        inner.innerHTML = '<p class="muted" style="padding:8px 0;font-size:12px;">No line detail available for this document.</p>';
                    } else {
                        inner.innerHTML = buildDetailPanel(lines);
                    }
                }
                panelRow.style.display = '';
                btn.setAttribute('aria-expanded', 'true');
                btn.classList.add('is-open');
            }
        });
    }

    /* -------------------------------------------------------------------------
       buildDetailPanel — renders the inline expand panel for a document.

       Column order:
         Line | GL Account | WBS | Capability Manager | Delivery Manager |
         DM Program | POC | Days Late | Interest

       Capability Manager and Delivery Manager show the NUMBER (cell text),
       with the NAME in the title attribute. The CM tooltip is propagated
       directly from the All Lines td title — formatted upstream as
       "LPPI Charge Cost Centre: <number> (<name>)" — so the tooltip is
       authored once in the .aspx markup and inherited everywhere.

       DM Program is sourced from the detail-row's data-dm attribute (not
       a column cell), because DM Program is no longer rendered as its own
       column on the All Lines tab.
       ------------------------------------------------------------------------- */
    function buildDetailPanel(rows) {
        var html = '<table class="tbl tbl-expand-detail"><thead><tr>'
            + '<th>Line</th>'
            + '<th>GL Account</th>'
            + '<th>WBS</th>'
            + '<th title="LPPI Charge Cost Centre">Capability Manager</th>'
            + '<th>Delivery Manager</th>'
            + '<th>DM Program</th>'
            + '<th>POC</th>'
            + '<th class="num">Days Late</th>'
            + '<th class="num">Interest</th>'
            + '</tr></thead><tbody>';

        rows.forEach(function (r) {
            // Pull cell text by class — robust against column reordering as
            // long as the class names stay aligned with LPPI_Review.aspx.
            function cell(cls) {
                var el = r.querySelector('td.' + cls);
                return el ? el.textContent.trim() : '';
            }
            // Pull the title attribute off a cell — used to surface the
            // CM/DM name as a tooltip without re-querying the DB.
            function cellTitle(cls) {
                var el = r.querySelector('td.' + cls);
                if (!el) return '';
                var t = el.getAttribute('title');
                return t ? t : '';
            }

            html += '<tr>'
                + '<td><span class="seq-chip">' + esc(cell('col-seq').replace(/\D/g,'')) + '</span></td>'
                + '<td>' + esc(cell('col-gl')) + '</td>'
                + '<td title="' + attr(r.getAttribute('data-wbs') || '') + '">' + esc(cell('col-wbs')) + '</td>'
                + '<td title="' + attr(cellTitle('col-cm')) + '">' + esc(cell('col-cm')) + '</td>'
                + '<td title="' + attr(cellTitle('col-dm')) + '">' + esc(cell('col-dm')) + '</td>'
                + '<td>' + esc(r.getAttribute('data-dm') || '') + '</td>'
                + '<td>' + esc(cell('col-poc')) + '</td>'
                + '<td class="num">' + esc(cell('col-days')) + '</td>'
                + '<td class="num">' + esc(cell('col-int')) + '</td>'
                + '</tr>';
        });

        html += '</tbody></table>';
        return html;
    }

    function esc(s) {
        if (!s) return '';
        return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
    }
    function attr(s) {
        if (!s) return '';
        return s.replace(/&/g,'&amp;').replace(/"/g,'&quot;');
    }

    /* =========================================================================
       Dirty state
       ========================================================================= */
    function markDirty(row, docNo) {
        dirtySet[docNo] = true;
        row.classList.add('dirty');
        // Clear any prior stale flag — the user is editing again.
        clearRowMessage(row);
        updateSaveButton();
    }

    function clearDirty(row, docNo) {
        delete dirtySet[docNo];
        row.classList.remove('dirty');
    }

    function updateSaveButton() {
        var n = Object.keys(dirtySet).length;
        if (!saveButton) return;

        if (readOnly) {
            saveButton.disabled = true;
            if (saveLabel) saveLabel.textContent = 'Save changes';
            setIndicator('saved', '');
            return;
        }

        if (saveInFlight) {
            saveButton.disabled = true;
            if (saveLabel) saveLabel.textContent = 'Saving…';
            setIndicator('saving', 'Saving…');
            return;
        }

        if (n === 0) {
            saveButton.disabled = true;
            if (saveLabel) saveLabel.textContent = 'Save changes';
            setIndicator('saved', 'No changes to save');
        } else {
            saveButton.disabled = false;
            if (saveLabel) saveLabel.textContent = 'Save changes (' + n + ')';
            setIndicator('saving', n + ' unsaved change' + (n === 1 ? '' : 's'));
        }
    }

    /* =========================================================================
       Mandatory-field validation (client-side hint)
       ========================================================================= */
    function evaluateNeeds(row) {
        var sel    = row.querySelector('.reason-select');
        var ta     = row.querySelector('.comments-input');
        var inp    = row.querySelector('.objref-input');
        var docNo  = row.getAttribute('data-doc-no');
        var msgEl  = document.querySelector('.doc-main-msg[data-doc-no="' + escAttr(docNo) + '"] .row-msg');

        if (!sel) return;
        var opt     = sel.options[sel.selectedIndex] || {};
        var outcome = opt.getAttribute ? (opt.getAttribute('data-outcome') || '') : '';
        var req     = opt.getAttribute ? opt.getAttribute('data-requires') === '1' : false;
        var needs   = (req || outcome === 'NotPayable') && ta && !ta.value.trim();

        row.classList.toggle('needs-comment', needs);

        if (msgEl && !msgEl.classList.contains('row-msg-stale')) {
            var msgs = [];
            if (req  && ta  && !ta.value.trim())  msgs.push('A comment is required for this reason code.');
            if (outcome === 'NotPayable') {
                if (ta  && !ta.value.trim())  msgs.push('Not-Payable requires a comment.');
                if (inp && !inp.value.trim()) msgs.push('Not-Payable requires an objective reference.');
            }
            msgEl.textContent = msgs.join(' ');
        }
    }

    function clearRowMessage(row) {
        var docNo = row.getAttribute('data-doc-no');
        var msgEl = document.querySelector('.doc-main-msg[data-doc-no="' + escAttr(docNo) + '"] .row-msg');
        if (!msgEl) return;
        msgEl.classList.remove('row-msg-stale', 'row-msg-error');
        msgEl.textContent = '';
        // Remove any reload button that was injected.
        var actions = msgEl.parentNode.querySelector('.row-msg-actions');
        if (actions) actions.parentNode.removeChild(actions);
    }

    /* =========================================================================
       Save flow — explicit, batch
       ========================================================================= */
    function onSaveClick() {
        if (saveInFlight) return;
        if (readOnly) return;
        var keys = Object.keys(dirtySet);
        if (keys.length === 0) return;

        // Client-side validation pre-filter — rows that fail are kept dirty
        // and surfaced via evaluateNeeds; we still send all dirty rows so
        // the server has the final say.
        var payload = new FormData();
        payload.append('token',   token);
        payload.append('action',  'save');
        payload.append('rowCount', keys.length);

        keys.forEach(function (docNo, idx) {
            var row = mainByDoc[docNo];
            if (!row) return;
            var sel = row.querySelector('.reason-select');
            var ta  = row.querySelector('.comments-input');
            var inp = row.querySelector('.objref-input');
            var ver = row.getAttribute('data-version') || '';

            payload.append('rows[' + idx + '].docNo',        docNo);
            payload.append('rows[' + idx + '].reasonCodeId', sel ? sel.value : '');
            payload.append('rows[' + idx + '].comments',     ta  ? ta.value  : '');
            payload.append('rows[' + idx + '].objref',       inp ? inp.value : '');
            payload.append('rows[' + idx + '].version',      ver);
        });

        saveInFlight = true;
        updateSaveButton();

        var xhr = new XMLHttpRequest();
        xhr.open('POST', SAVE_URL, true);
        xhr.onreadystatechange = function () {
            if (xhr.readyState !== 4) return;
            saveInFlight = false;

            if (xhr.status !== 200) {
                setIndicator('error', 'Save failed (HTTP ' + xhr.status + ')');
                updateSaveButton();
                return;
            }

            var resp;
            try { resp = JSON.parse(xhr.responseText); }
            catch (ex) {
                setIndicator('error', 'Save failed (invalid response)');
                updateSaveButton();
                return;
            }

            handleSaveResponse(resp);
        };
        xhr.onerror = function () {
            saveInFlight = false;
            setIndicator('error', 'Save failed (network)');
            updateSaveButton();
        };
        xhr.send(payload);
    }

    function handleSaveResponse(resp) {
        if (!resp) {
            setIndicator('error', 'Save failed');
            updateSaveButton();
            return;
        }

        // Top-level error (e.g. invalid token, package read-only) — surface
        // and stop. Don't clear any dirty state.
        if (resp.error) {
            setIndicator('error', resp.error);
            updateSaveButton();
            return;
        }

        var results = resp.results || [];
        var staleCount = 0;
        var validationCount = 0;
        var savedCount = 0;
        var serverErrCount = 0;

        results.forEach(function (r) {
            var row = mainByDoc[r.docNo];
            if (!row) return;

            if (r.ok) {
                // Update version + reviewed marker, clear dirty.
                row.setAttribute('data-version', r.newVersion || '');
                clearDirty(row, r.docNo);
                clearRowMessage(row);
                applyServerValuesToRow(row, r);
                row.classList.toggle('reviewed', !!r.newReasonCodeId);
                if (r.errorCode !== 'noChange') {
                    flashSaved(row);
                    savedCount++;
                }
            } else {
                if (r.errorCode === 'stale') {
                    staleCount++;
                    showStale(row, r);
                } else if (r.errorCode === 'validation') {
                    validationCount++;
                    showRowError(row, r.error || 'Validation failed.');
                } else if (r.errorCode === 'notInPackage') {
                    showRowError(row, r.error || 'Document is not in this package.');
                    serverErrCount++;
                } else {
                    showRowError(row, r.error || 'Save failed.');
                    serverErrCount++;
                }
            }
        });

        // Recalculate reviewed count + progress.
        reviewedDocs = allMain.filter(function (r) { return r.classList.contains('reviewed'); }).length;
        updateProgress();
        updateReadyBanner();

        // If the package status flipped to a terminal state (Finalised,
        // Exported, Cancelled) the server has set the read-only gate;
        // mirror that on the client so the user cannot keep editing.
        // Note: a successful save against a NotSent/Sent/InReview package
        // never moves to a terminal state via the save endpoint — only
        // the dedicated finalise / export / cancel paths can do that —
        // but defending here is cheap.
        if (resp.packageStatus === 'Finalised'
            || resp.packageStatus === 'Exported'
            || resp.packageStatus === 'Cancelled') {
            readOnly = true;
            var shell = document.querySelector('.review-shell');
            if (shell) shell.setAttribute('data-readonly', '1');
        }

        // Indicator summary.
        if (staleCount + validationCount + serverErrCount === 0) {
            setIndicator('saved', savedCount > 0
                ? 'Saved ' + savedCount + ' change' + (savedCount === 1 ? '' : 's')
                : 'No changes to save');
        } else {
            var bits = [];
            if (savedCount      > 0) bits.push(savedCount      + ' saved');
            if (staleCount      > 0) bits.push(staleCount      + ' out of date');
            if (validationCount > 0) bits.push(validationCount + ' need attention');
            if (serverErrCount  > 0) bits.push(serverErrCount  + ' failed');
            setIndicator('error', bits.join(', '));
        }

        updateSaveButton();
    }

    function applyServerValuesToRow(row, r) {
        var sel = row.querySelector('.reason-select');
        var ta  = row.querySelector('.comments-input');
        var inp = row.querySelector('.objref-input');

        if (sel) sel.value = r.newReasonCodeId == null ? '' : String(r.newReasonCodeId);
        if (ta)  ta.value  = r.newComments || '';
        if (inp) inp.value = r.newObjectiveReference || '';

        // Refresh the data-outcome / data-requires attributes from the
        // currently selected option so subsequent validation hints are
        // accurate.
        if (sel) {
            var opt = sel.options[sel.selectedIndex];
            if (opt) {
                row.setAttribute('data-outcome',  opt.getAttribute('data-outcome')  || '');
                row.setAttribute('data-requires', opt.getAttribute('data-requires') || '0');
            } else {
                row.setAttribute('data-outcome',  '');
                row.setAttribute('data-requires', '0');
            }
        }
        evaluateNeeds(row);
    }

    function flashSaved(row) {
        row.classList.add('just-saved');
        setTimeout(function () { row.classList.remove('just-saved'); }, 1800);
    }

    function showStale(row, r) {
        var docNo = row.getAttribute('data-doc-no');
        var msgRow = document.querySelector('.doc-main-msg[data-doc-no="' + escAttr(docNo) + '"]');
        if (!msgRow) return;
        var msgEl = msgRow.querySelector('.row-msg');
        if (!msgEl) return;

        msgEl.classList.add('row-msg-stale');
        msgEl.textContent = (r.error || 'This document has been updated by someone else since you opened the page.')
            + (r.newReviewedByName ? ' Last saved by: ' + r.newReviewedByName + '.' : '');

        // Inject a Reload button next to the message. Replace any existing
        // actions block.
        var existing = msgRow.querySelector('.row-msg-actions');
        if (existing) existing.parentNode.removeChild(existing);

        var actions = document.createElement('span');
        actions.className = 'row-msg-actions';
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn btn-ghost btn-sm';
        btn.textContent = 'Reload row';
        btn.addEventListener('click', function () {
            // Apply server-supplied current values to the row, refresh the
            // version, clear dirty + stale.
            applyServerValuesToRow(row, r);
            row.setAttribute('data-version', r.newVersion || '');
            row.classList.toggle('reviewed', !!r.newReasonCodeId);
            clearDirty(row, docNo);
            clearRowMessage(row);
            evaluateNeeds(row);
            reviewedDocs = allMain.filter(function (rr) { return rr.classList.contains('reviewed'); }).length;
            updateProgress();
            updateReadyBanner();
            updateSaveButton();
        });
        actions.appendChild(btn);
        msgEl.appendChild(document.createTextNode(' '));
        msgEl.appendChild(actions);
    }

    function showRowError(row, message) {
        var docNo = row.getAttribute('data-doc-no');
        var msgEl = document.querySelector('.doc-main-msg[data-doc-no="' + escAttr(docNo) + '"] .row-msg');
        if (!msgEl) return;
        msgEl.classList.remove('row-msg-stale');
        msgEl.classList.add('row-msg-error');
        msgEl.textContent = message;
    }

    /* =========================================================================
       Action button — Finalise / Unfinalise

       The action button has a single slot in the toolbar and toggles
       between Finalise (editable states) and Unfinalise (Finalised state)
       via the data-action attribute set by the server in the markup. This
       function dispatches by that attribute so a single click handler
       covers both directions.

       Both actions:
         - Refuse if there are unsaved local edits (mixing the two would
           make the audit trail ambiguous).
         - Show a confirm() dialog explaining what will happen.
         - POST to the appropriate endpoint with the package token.
         - On success, reload the page so the new banner / status pill /
           form-field locks come from the server-rendered HTML.

       Finalise auto-applies RC-NR to undecided documents and locks the
       form. Unfinalise wipes those auto-applied codes and reopens the
       form for further editing. Both write history rows so the audit
       trail captures every direction change.
       ========================================================================= */
    function onActionClick() {
        if (actionInFlight) return;
        if (!actionButton) return;

        var action = actionButton.getAttribute('data-action') || '';
        if (action === 'finalise') {
            doFinalise();
        } else if (action === 'unfinalise') {
            doUnfinalise();
        }
    }

    function doFinalise() {
        // Refuse if there are unsaved changes.
        var dirtyCount = Object.keys(dirtySet).length;
        if (dirtyCount > 0) {
            alert('Please save your changes first. ' +
                  dirtyCount + ' row' + (dirtyCount === 1 ? ' has' : 's have') + ' unsaved edits.');
            return;
        }

        var undecided = totalDocs - reviewedDocs;
        var undecidedLine;
        if (undecided <= 0) {
            undecidedLine = 'Every document already has a reason code, so no defaults will be applied.';
        } else if (undecided === 1) {
            undecidedLine = '1 document still has no reason code. It will be marked as RC-NR (Payable — no response received from CM).';
        } else {
            undecidedLine = undecided + ' documents still have no reason code. They will be marked as RC-NR (Payable — no response received from CM).';
        }

        var confirmMsg =
            'Finalise this package?\n\n' +
            undecidedLine + '\n\n' +
            'After finalising, the form fields are locked. ' +
            'You can unfinalise the package at any time before it is exported to ERP.';

        if (!confirm(confirmMsg)) return;

        runActionRequest(FINALISE_URL, 'finalise', 'Finalising…', 'Finalise failed');
    }

    function doUnfinalise() {
        // Editing is locked while Finalised, so there should never be
        // dirty rows. Belt and braces though.
        var dirtyCount = Object.keys(dirtySet).length;
        if (dirtyCount > 0) {
            alert('There are unsaved edits in flight. Please reload the page and try again.');
            return;
        }

        var confirmMsg =
            'Unfinalise this package?\n\n' +
            'The auto-applied "no response" codes will be cleared and the package will return to In Review. ' +
            'You can finalise again at any time.';

        if (!confirm(confirmMsg)) return;

        runActionRequest(UNFINALISE_URL, 'unfinalise', 'Unfinalising…', 'Unfinalise failed');
    }

    /* Common runner for both finalise and unfinalise — the only differences
       are URL, posted action value, and the in-flight label. */
    function runActionRequest(url, actionValue, busyLabel, failurePrefix) {
        actionInFlight = true;
        if (actionButton) actionButton.disabled = true;
        if (actionLabel)  actionLabel.textContent = busyLabel;

        var payload = new FormData();
        payload.append('token',  token);
        payload.append('action', actionValue);

        var xhr = new XMLHttpRequest();
        xhr.open('POST', url, true);
        xhr.onreadystatechange = function () {
            if (xhr.readyState !== 4) return;
            actionInFlight = false;

            if (xhr.status !== 200) {
                resetActionButton();
                alert(failurePrefix + ' (HTTP ' + xhr.status + '). Please try again or contact the LPPI administrator.');
                return;
            }

            var resp;
            try { resp = JSON.parse(xhr.responseText); }
            catch (ex) {
                resetActionButton();
                alert(failurePrefix + ' (invalid server response). Please try again or contact the LPPI administrator.');
                return;
            }

            if (!resp.ok) {
                resetActionButton();
                alert(failurePrefix + ': ' + (resp.error || 'Unknown error.'));
                return;
            }

            // Success. Reload the page so the new banner, status pill,
            // form-field locks and toggled action button all come from
            // the server-rendered HTML.
            window.location.reload();
        };
        xhr.onerror = function () {
            actionInFlight = false;
            resetActionButton();
            alert(failurePrefix + ' (network). Please try again or contact the LPPI administrator.');
        };
        xhr.send(payload);
    }

    function resetActionButton() {
        if (!actionButton) return;
        actionButton.disabled = false;
        if (actionLabel) {
            // Restore the label that matches the current data-action.
            var action = actionButton.getAttribute('data-action') || '';
            actionLabel.textContent = action === 'unfinalise' ? 'Unfinalise' : 'Finalise';
        }
    }

    /* =========================================================================
       Filtering
       ========================================================================= */
    function applyFilter() {
        var searchVal = ((document.getElementById('searchBox')    || {}).value || '').toLowerCase();
        var statusVal =  (document.getElementById('statusFilter') || {}).value || '';

        var facetVals = {};
        FACETS.forEach(function (f) {
            var el = document.getElementById(f.id);
            facetVals[f.attr] = el ? el.value : '';
        });

        allMain.forEach(function (row) {
            var show   = matchesRow(row, searchVal, statusVal, facetVals, true);
            row.style.display = show ? '' : 'none';
            var docNo  = row.getAttribute('data-doc-no');
            var msgRow = document.querySelector('.doc-main-msg[data-doc-no="' + escAttr(docNo) + '"]');
            if (msgRow) msgRow.style.display = show ? '' : 'none';
            var panelRow = document.querySelector('.doc-expand-panel[data-doc-no="' + escAttr(docNo) + '"]');
            if (panelRow && !show) panelRow.style.display = 'none';
        });

        allDetail.forEach(function (row) {
            row.style.display = matchesRow(row, searchVal, '', facetVals, false) ? '' : 'none';
        });
    }

    function matchesRow(row, searchVal, statusVal, facetVals, checkStatus) {
        if (searchVal) {
            var blob = (row.getAttribute('data-search') || '').toLowerCase();
            if (blob.indexOf(searchVal) === -1) return false;
        }

        if (checkStatus && statusVal) {
            var isReviewed = row.classList.contains('reviewed');
            var needsAttn  = row.classList.contains('needs-comment');
            var sel        = row.querySelector ? row.querySelector('.reason-select') : null;
            var outcome    = '';
            if (sel && sel.selectedIndex >= 0) {
                var opt = sel.options[sel.selectedIndex];
                outcome = opt ? (opt.getAttribute('data-outcome') || '') : '';
            }
            if (statusVal === 'not-reviewed'  && isReviewed)              return false;
            if (statusVal === 'reviewed'       && !isReviewed)            return false;
            if (statusVal === 'payable'        && outcome !== 'Payable')  return false;
            if (statusVal === 'notpayable'     && outcome !== 'NotPayable') return false;
            if (statusVal === 'needs-comments' && !needsAttn)             return false;
        }

        for (var i = 0; i < FACETS.length; i++) {
            var fv = facetVals[FACETS[i].attr];
            if (!fv) continue;
            if ((row.getAttribute(FACETS[i].attr) || '') !== fv) return false;
        }

        return true;
    }

    /* =========================================================================
       Bulk select — stages dirty rows; user clicks Save to commit
       ========================================================================= */
    function bindBulk() {
        var bar     = document.getElementById('bulkBar');
        var bulkSel = document.getElementById('bulkReason');
        var apply   = document.getElementById('bulkApply');
        var clear   = document.getElementById('bulkClear');
        if (!bar) return;

        document.addEventListener('change', function (e) {
            if (e.target && e.target.classList.contains('rowselect')) updateBulkBar();
        });

        if (apply) apply.addEventListener('click', function () {
            var rid = bulkSel.value;
            if (!rid) return;
            var opt      = bulkSel.options[bulkSel.selectedIndex];
            var outcome  = opt.getAttribute('data-outcome') || '';
            var requires = opt.getAttribute('data-requires') === '1';
            if (requires || outcome === 'NotPayable') {
                var msg = outcome === 'NotPayable'
                    ? 'Not-Payable needs a Comment and Objective Reference on every selected row. Stage anyway?'
                    : 'This reason code requires a comment. Stage anyway?';
                if (!confirm(msg)) return;
            }
            var seen = {};
            document.querySelectorAll('.rowselect:checked').forEach(function (cb) {
                var docNo = cb.getAttribute('data-doc-no');
                if (seen[docNo]) return;
                seen[docNo] = true;
                var row = mainByDoc[docNo];
                if (!row) return;
                var s = row.querySelector('.reason-select');
                if (!s) return;
                s.value = rid;
                markDirty(row, docNo);
                evaluateNeeds(row);
            });
            // Hint to the user that nothing has hit the server yet.
            updateSaveButton();
        });

        if (clear) clear.addEventListener('click', function () {
            document.querySelectorAll('.rowselect:checked').forEach(function (cb) { cb.checked = false; });
            updateBulkBar();
        });
    }

    function updateBulkBar() {
        var bar = document.getElementById('bulkBar');
        if (!bar) return;
        var seen = {};
        document.querySelectorAll('.rowselect:checked').forEach(function (cb) {
            seen[cb.getAttribute('data-doc-no')] = true;
        });
        var n = Object.keys(seen).length;
        bar.classList.toggle('show', n > 0);
        var c = document.getElementById('bulkCount');
        if (c) c.textContent = n;
    }

    /* =========================================================================
       Progress
       ========================================================================= */
    function updateProgress() {
        var label = document.getElementById('progressLabel');
        if (label) label.textContent = reviewedDocs + ' of ' + totalDocs;
        var bar = document.getElementById('progressBar');
        if (bar) bar.style.width = (totalDocs === 0 ? 0 : Math.round(100 * reviewedDocs / totalDocs)) + '%';
    }

    /* "Ready to finalise" hint — replaces the old auto-Complete done banner.
       Shown when every document has a reason code BUT the package is still
       editable (i.e. Finalise has not yet been clicked). On a Finalised /
       Exported / Cancelled package the banner stays hidden — the status
       banner at the top of the page handles those cases. */
    function updateReadyBanner() {
        var banner = document.getElementById('readyBanner');
        if (!banner) return;
        var ready = !readOnly && totalDocs > 0 && reviewedDocs >= totalDocs;
        banner.classList.toggle('show', ready);
    }

    /* =========================================================================
       Indicator
       ========================================================================= */
    function setIndicator(state, text) {
        if (!saveIndicator) return;
        saveIndicator.className   = 'save-indicator ' + state;
        saveIndicator.textContent = text || '';
    }

    /* =========================================================================
       Keyboard navigation
       ========================================================================= */
    function bindKeyboard() {
        document.addEventListener('keydown', function (e) {
            if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
            var t = e.target;
            if (!t || t.tagName === 'TEXTAREA') return;
            var row = t.closest ? t.closest('tr.doc-main') : null;
            if (!row) return;
            e.preventDefault();
            var visible = allMain.filter(function (r) { return r.style.display !== 'none'; });
            var idx     = visible.indexOf(row);
            var target  = visible[e.key === 'ArrowDown' ? idx + 1 : idx - 1];
            if (target) {
                var f = target.querySelector('select,textarea,input');
                if (f) f.focus();
            }
        });
    }

    /* =========================================================================
       Utilities
       ========================================================================= */
    function debounce(fn, delay) {
        var timer;
        return function () { clearTimeout(timer); timer = setTimeout(fn, delay); };
    }

    function escAttr(s) {
        return s ? s.replace(/"/g, '&quot;').replace(/'/g, '&#39;') : '';
    }

    /* =========================================================================
       Boot
       ========================================================================= */
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

}());
