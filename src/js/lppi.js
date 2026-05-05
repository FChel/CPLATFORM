/* =============================================================================
   LPPI Review — reviewer page interactions
   Vanilla JS, no jQuery, no frameworks.

   Behaviour:
   * Two top-level tabs: "Reason code entry" (Tab 1) and "All lines" (Tab 2).
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
   * beforeunload warning if the dirty set is non-empty.
   * Filters (search, status, facets) apply to both Tab 1 rows and Tab 2 rows.
   * Expand chevron on each doc row opens an inline sub-row showing all
     lines for that document (read from the rptDetail DOM — no extra
     server call).
   * Comments textarea expands on focus, contracts on blur.
   * Primary key: DocNoAccounting (data-doc-no).
   ============================================================================= */
(function () {
    'use strict';

    var SAVE_URL = 'LPPI_Review_Save.ashx';
    var token    = (document.getElementById('reviewToken') || {}).value || '';
    var readOnly = ((document.getElementById('reviewReadOnly') || {}).value || '0') === '1';

    var allMain   = [];
    var mainByDoc = {};   // docNo -> .doc-main <tr>
    var allDetail = [];   // .detail-row elements (Tab 2)

    var dirtySet      = {};   // docNo -> true, while there are unsaved local changes
    var saveInFlight  = false;
    var totalDocs     = 0;
    var reviewedDocs  = 0;

    var saveIndicator = document.getElementById('saveIndicator');
    var saveButton    = document.getElementById('saveAllBtn');
    var saveLabel     = document.getElementById('saveAllBtnLabel');

    var FACETS = [
        { id: 'filterDm',  attr: 'data-dm'  },
        { id: 'filterPoc', attr: 'data-poc' },
        { id: 'filterWbs', attr: 'data-wbs' },
        { id: 'filterPc',  attr: 'data-pc'  }
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
            var sel = row.querySelector('.reason-select');
            if (sel && sel.value) row.classList.add('reviewed');
        });
        reviewedDocs = allMain.filter(function (r) { return r.classList.contains('reviewed'); }).length;
        updateProgress();
        updateDoneState();

        bindRowControls(allMain);
        allMain.forEach(evaluateNeeds);
        bindExpandChevrons();
        bindCommentsExpand();

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
        var tabReason = document.getElementById('tabReason');
        var tabLines  = document.getElementById('tabLines');
        if (tabReason) tabReason.addEventListener('click', function () { setTab('reason'); });
        if (tabLines)  tabLines.addEventListener('click',  function () { setTab('lines');  });

        // Save button
        if (saveButton) saveButton.addEventListener('click', onSaveClick);

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
       ========================================================================= */
    function setTab(tab) {
        var paneReason = document.getElementById('paneReason');
        var paneLines  = document.getElementById('paneLines');
        var tabReason  = document.getElementById('tabReason');
        var tabLines   = document.getElementById('tabLines');
        if (!paneReason || !paneLines) return;

        if (tab === 'lines') {
            paneReason.classList.remove('active');
            paneLines.classList.add('active');
            if (tabLines)  { tabLines.classList.add('active');    tabLines.setAttribute('aria-selected', 'true');  }
            if (tabReason) { tabReason.classList.remove('active'); tabReason.setAttribute('aria-selected', 'false'); }
        } else {
            paneLines.classList.remove('active');
            paneReason.classList.add('active');
            if (tabReason) { tabReason.classList.add('active');   tabReason.setAttribute('aria-selected', 'true');  }
            if (tabLines)  { tabLines.classList.remove('active'); tabLines.setAttribute('aria-selected', 'false'); }
        }
        applyFilter();
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
       ========================================================================= */
    function bindExpandChevrons() {
        document.addEventListener('click', function (e) {
            var btn = e.target && e.target.closest ? e.target.closest('.btn-expand') : null;
            if (!btn) return;

            var docNo     = btn.getAttribute('data-doc-no');
            var mainRow   = mainByDoc[docNo];
            if (!mainRow) return;

            // Find the expand panel row (always the third sibling after doc-main)
            var panelRow  = mainRow.nextElementSibling && mainRow.nextElementSibling.nextElementSibling;
            if (!panelRow || !panelRow.classList.contains('doc-expand-panel')) return;

            var isOpen = panelRow.style.display !== 'none';

            if (isOpen) {
                panelRow.style.display = 'none';
                btn.setAttribute('aria-expanded', 'false');
                btn.classList.remove('is-open');
            } else {
                // Build content from rptDetail rows for this docNo
                var lines = allDetail.filter(function (r) {
                    return r.getAttribute('data-doc-no') === docNo;
                });
                var inner = panelRow.querySelector('.expand-panel-inner');
                if (inner) {
                    if (lines.length <= 1) {
                        inner.innerHTML = '<p class="muted" style="padding:8px 0;font-size:12px;">This document has only one line.</p>';
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

    function buildDetailPanel(rows) {
        var html = '<table class="tbl tbl-expand-detail"><thead><tr>'
            + '<th>Line</th><th>WBS</th><th>GL Account</th><th>Profit Centre</th>'
            + '<th>Tax Code</th><th>DM Program</th><th>POC</th>'
            + '<th class="num">Days Late</th><th class="num">Interest</th>'
            + '</tr></thead><tbody>';

        rows.forEach(function (r) {
            function cell(cls) {
                var el = r.querySelector('td.' + cls);
                return el ? el.textContent.trim() : '';
            }
            html += '<tr>'
                + '<td><span class="seq-chip">' + esc(cell('col-seq').replace(/\D/g,'')) + '</span></td>'
                + '<td title="' + attr(r.getAttribute('data-wbs') || '') + '">' + esc(cell('col-wbs')) + '</td>'
                + '<td>' + esc(cell('col-gl')) + '</td>'
                + '<td>' + esc(cell('col-pc')) + '</td>'
                + '<td>' + esc(cell('col-tax')) + '</td>'
                + '<td>' + esc(cell('col-dm')) + '</td>'
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
        updateDoneState();

        // If the package status flipped to Complete, mark the page as
        // read-only so the user cannot keep editing.
        if (resp.packageStatus === 'Complete' || resp.packageStatus === 'Cancelled') {
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
            updateDoneState();
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

    function updateDoneState() {
        var banner = document.getElementById('doneBanner');
        if (banner) banner.style.display = (totalDocs > 0 && reviewedDocs >= totalDocs) ? '' : 'none';
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
