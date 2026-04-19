/* =============================================================================
   LPPI Review — reviewer page interactions
   Vanilla JS, no jQuery, no frameworks.

   Behaviour summary:

   * Auto-save fires on BLUR of text/textarea and on CHANGE of the reason-code
     select. (Used to debounce on every keystroke; too chatty and caused
     races against slower saves.) One save per edit.
   * A visible "Save" button in the toolbar forces a flush of every row that
     is still dirty.
   * State survives view switching. Card and table repeaters bind to the
     same DataTable so every DocumentID appears in both DOMs; on view toggle
     we copy the edited values across before showing the new view, and we
     also live-mirror during typing.
   * Mandatory-field rules:
       - RequiresComments flag (RC07/RC16): Comments must be non-empty.
       - Outcome = NotPayable: BOTH Comments and Objective Reference required.
     A client-side check blocks the save and shows a row-level message; the
     server enforces the same rules authoritatively.
   * Four separate facet filters: Delivery Manager, POC, WBS, Profit Centre.
     Each narrows against its own data-* attribute. All filters AND together
     with the free-text search and the status dropdown.
   ========================================================================== */
(function () {
    'use strict';

    var SAVE_URL = 'LPPI_Review_Save.ashx';

    var token = (document.getElementById('reviewToken') || {}).value || '';
    var allCards = [];
    var allRows  = [];           // table rows
    var cardByDoc = {};          // docId -> .doc-card
    var rowByDoc  = {};          // docId -> .doc-row
    var dirtyDocIds = {};        // map: docId -> true when unsaved
    var savingCount = 0;
    var totalDocs = 0;
    var reviewedDocs = 0;
    var currentView = 'table';   // default; matches .active on markup
    var saveIndicator = document.getElementById('saveIndicator');

    // (id -> data-attribute) for the four facet filters
    var FACETS = [
        { id: 'filterDm',  attr: 'data-dm'  },
        { id: 'filterPoc', attr: 'data-poc' },
        { id: 'filterWbs', attr: 'data-wbs' },
        { id: 'filterPc',  attr: 'data-pc'  }
    ];

    /* ---------- bootstrap ---------- */
    function init() {
        allCards = Array.prototype.slice.call(document.querySelectorAll('.doc-card'));
        allRows  = Array.prototype.slice.call(document.querySelectorAll('.review-table .doc-row'));
        totalDocs = allCards.length;

        allCards.forEach(function (c) {
            var id = c.getAttribute('data-doc-id');
            if (id) cardByDoc[id] = c;
        });
        allRows.forEach(function (r) {
            var id = r.getAttribute('data-doc-id');
            if (id) rowByDoc[id] = r;
        });

        // Seed "reviewed" class on both views from the pre-selected reason code
        // so visuals match what the server already knows.
        reviewedDocs = 0;
        allCards.concat(allRows).forEach(function (item) {
            var sel = item.querySelector('.reason-select');
            if (sel && sel.value) {
                item.classList.add('reviewed');
            }
        });
        Object.keys(cardByDoc).forEach(function (id) {
            if (cardByDoc[id].classList.contains('reviewed')) reviewedDocs++;
        });
        updateProgress();

        // Wire reason-code dropdowns on both views
        bindRowControls(allCards);
        bindRowControls(allRows);

        // Initial needs-attention evaluation
        allCards.concat(allRows).forEach(evaluateNeeds);

        // Search / status / four facet filters
        var search = document.getElementById('searchBox');
        if (search) search.addEventListener('input', debounce(applyFilter, 200));
        var statusFilter = document.getElementById('statusFilter');
        if (statusFilter) statusFilter.addEventListener('change', applyFilter);
        FACETS.forEach(function (f) {
            var el = document.getElementById(f.id);
            if (el) el.addEventListener('change', applyFilter);
        });

        // View toggle
        var btnCards = document.getElementById('viewCards');
        var btnTable = document.getElementById('viewTable');
        if (btnCards && btnTable) {
            btnCards.addEventListener('click', function () { setView('cards'); });
            btnTable.addEventListener('click', function () { setView('table'); });
        }

        // Bulk
        bindBulk();

        // Mark-final
        var finalBtn = document.getElementById('markFinalBtn');
        if (finalBtn) finalBtn.addEventListener('click', markAllFinal);

        // Save (flush all dirty rows)
        var saveAllBtn = document.getElementById('saveAllBtn');
        if (saveAllBtn) saveAllBtn.addEventListener('click', saveAllDirty);

        // Before unload: warn if dirty rows exist
        window.addEventListener('beforeunload', function (e) {
            if (Object.keys(dirtyDocIds).length > 0) {
                var msg = 'You have unsaved review changes. Leave anyway?';
                e.returnValue = msg;
                return msg;
            }
        });

        // Keyboard nav in table
        bindKeyboard();

        updateDoneState();
        applyFilter();
    }

    /* ---------- event wiring ----------
       Selects save on change; text/textarea save on blur. No keystroke
       debounce — edits flush when the reviewer commits by tabbing out or
       clicking elsewhere. */
    function bindRowControls(items) {
        items.forEach(function (item) {
            var docId = item.getAttribute('data-doc-id');
            var sel = item.querySelector('.reason-select');
            var ta  = item.querySelector('.comments-input');
            var obj = item.querySelector('.objref-input');

            if (sel) {
                sel.addEventListener('change', function () {
                    markDirty(item, docId);
                    evaluateNeeds(item);
                    mirrorField(docId, 'reason', sel.value);
                    queueSave(docId);
                });
            }

            if (ta) {
                ta.addEventListener('input', function () {
                    markDirty(item, docId);
                    evaluateNeeds(item);
                    mirrorField(docId, 'comments', ta.value);
                });
                ta.addEventListener('blur', function () {
                    if (dirtyDocIds[docId]) queueSave(docId);
                });
            }

            if (obj) {
                obj.addEventListener('input', function () {
                    markDirty(item, docId);
                    evaluateNeeds(item);
                    mirrorField(docId, 'objref', obj.value);
                });
                obj.addEventListener('blur', function () {
                    if (dirtyDocIds[docId]) queueSave(docId);
                });
            }
        });
    }

    function markDirty(item, docId) {
        dirtyDocIds[docId] = true;
        if (saveIndicator && savingCount === 0) {
            setIndicator('saving', 'Unsaved changes');
        }
    }

    /* ---------- view mirroring ---------- */
    function mirrorField(docId, kind, value) {
        var other = (currentView === 'cards') ? rowByDoc[docId] : cardByDoc[docId];
        if (!other) return;
        if (kind === 'reason') {
            var s = other.querySelector('.reason-select');
            if (s && s.value !== value) s.value = value;
        } else if (kind === 'comments') {
            var t = other.querySelector('.comments-input');
            if (t && t.value !== value) t.value = value;
        } else if (kind === 'objref') {
            var o = other.querySelector('.objref-input');
            if (o && o.value !== value) o.value = value;
        }
        evaluateNeeds(other);
    }

    /* ---------- needs-attention evaluation ---------- */
    function evaluateNeeds(item) {
        if (!item) return;
        var sel = item.querySelector('.reason-select');
        var ta  = item.querySelector('.comments-input');
        var obj = item.querySelector('.objref-input');

        var outcome  = '';
        var requires = false;
        if (sel && sel.value) {
            var opt = sel.options[sel.selectedIndex];
            outcome  = opt ? (opt.getAttribute('data-outcome') || '') : '';
            requires = opt && opt.getAttribute('data-requires') === '1';
        }
        var hasComment = !!(ta  && ta.value  && ta.value.trim().length  > 0);
        var hasObjRef  = !!(obj && obj.value && obj.value.trim().length > 0);

        var needsComment = requires && !hasComment;
        var needsNotPayablePair =
            outcome === 'NotPayable' && (!hasComment || !hasObjRef);

        var bad = needsComment || needsNotPayablePair;
        item.classList.toggle('needs-comment', bad);

        var msg = item.querySelector('.row-msg');
        if (msg) {
            if (needsNotPayablePair && !hasComment && !hasObjRef) {
                msg.textContent = 'Not-Payable needs both a comment and an Objective Reference.';
            } else if (needsNotPayablePair && !hasComment) {
                msg.textContent = 'Not-Payable needs a comment.';
            } else if (needsNotPayablePair && !hasObjRef) {
                msg.textContent = 'Not-Payable needs an Objective Reference.';
            } else if (needsComment) {
                msg.textContent = 'This reason code needs a comment.';
            } else {
                msg.textContent = '';
            }
        }
    }

    /* ---------- saving ---------- */
    function queueSave(docId) {
        var item = cardByDoc[docId] || rowByDoc[docId];
        if (!item) return;
        saveRow(item, docId);
    }

    function saveAllDirty() {
        var ids = Object.keys(dirtyDocIds);
        if (ids.length === 0) {
            setIndicator('saved', 'Nothing to save');
            return;
        }
        ids.forEach(function (id) { queueSave(id); });
    }

    function saveRow(item, docId) {
        var sel = item.querySelector('.reason-select');
        var ta  = item.querySelector('.comments-input');
        var obj = item.querySelector('.objref-input');

        var reasonId = sel ? (sel.value || '') : '';
        var outcome  = '';
        var requires = false;
        if (sel && sel.value) {
            var opt = sel.options[sel.selectedIndex];
            outcome  = opt ? (opt.getAttribute('data-outcome') || '') : '';
            requires = opt && opt.getAttribute('data-requires') === '1';
        }
        var comments = ta ? ta.value.trim() : '';
        var objref   = obj ? obj.value.trim() : '';

        // Client-side gating — keep the dirty state so Save retries later.
        if (requires && !comments) {
            setIndicator('error', 'A row needs a comment before it can save.');
            return;
        }
        if (outcome === 'NotPayable' && (!comments || !objref)) {
            setIndicator('error', 'A Not-Payable row needs both a comment and an Objective Reference.');
            return;
        }

        savingCount++;
        setIndicator('saving', 'Saving…');

        var fd = new FormData();
        fd.append('token',    token);
        fd.append('docId',    docId);
        fd.append('reasonId', reasonId);
        fd.append('comments', comments);
        fd.append('objref',   objref);

        var xhr = new XMLHttpRequest();
        xhr.open('POST', SAVE_URL, true);
        xhr.onreadystatechange = function () {
            if (xhr.readyState !== 4) return;
            savingCount--;
            var ok = false, err = '';
            if (xhr.status === 200) {
                try {
                    var j = JSON.parse(xhr.responseText);
                    ok = j.ok === true;
                    err = j.error || '';
                } catch (e) { err = 'Bad server response.'; }
            } else {
                err = 'HTTP ' + xhr.status;
            }

            if (ok) {
                delete dirtyDocIds[docId];

                var pair = [cardByDoc[docId], rowByDoc[docId]];
                pair.forEach(function (p) {
                    if (!p) return;
                    showRowSaved(p);
                    if (reasonId) p.classList.add('reviewed');
                    else          p.classList.remove('reviewed');
                });

                reviewedDocs = 0;
                Object.keys(cardByDoc).forEach(function (id) {
                    if (cardByDoc[id].classList.contains('reviewed')) reviewedDocs++;
                });
                updateProgress();
                updateDoneState();

                if (savingCount === 0 && Object.keys(dirtyDocIds).length === 0) {
                    setIndicator('saved', 'All changes saved');
                } else if (savingCount === 0) {
                    setIndicator('saving', 'Unsaved changes');
                }
            } else {
                setIndicator('error', err || 'Save failed');
            }
        };
        xhr.send(fd);
    }

    function showRowSaved(item) {
        var s = item.querySelector('.row-saved');
        if (!s) return;
        s.classList.add('show');
        setTimeout(function () { s.classList.remove('show'); }, 1500);
    }

    function setIndicator(state, text) {
        if (!saveIndicator) return;
        saveIndicator.className = 'save-indicator ' + state;
        saveIndicator.textContent = text;
    }

    function updateProgress() {
        var bar = document.getElementById('progressBar');
        var lbl = document.getElementById('progressLabel');
        var pct = totalDocs === 0 ? 0 : Math.round((reviewedDocs / totalDocs) * 100);
        if (bar) bar.style.width = pct + '%';
        if (lbl) lbl.textContent = reviewedDocs + ' of ' + totalDocs;
    }

    function updateDoneState() {
        var done = document.getElementById('doneBanner');
        if (!done) return;
        if (reviewedDocs >= totalDocs && totalDocs > 0) done.classList.add('show');
        else done.classList.remove('show');
    }

    /* ---------- filter ----------
       All inputs AND together:
         - free-text search against data-search
         - status (reviewed / outcome / needs-attention)
         - each facet (data-dm / data-poc / data-wbs / data-pc) */
    function applyFilter() {
        var searchEl = document.getElementById('searchBox');
        var statusEl = document.getElementById('statusFilter');

        var q      = searchEl ? (searchEl.value || '').trim().toLowerCase() : '';
        var status = statusEl ? (statusEl.value || '') : '';

        // Collect the four facet values (blank == wildcard)
        var facetVals = FACETS.map(function (f) {
            var el = document.getElementById(f.id);
            return { attr: f.attr, value: el ? (el.value || '') : '' };
        });

        function matches(item) {
            var hay = (item.getAttribute('data-search') || '').toLowerCase();
            var isReviewed     = item.classList.contains('reviewed');
            var needsAttention = item.classList.contains('needs-comment');
            var sel = item.querySelector('.reason-select');
            var outcome = '';
            if (sel && sel.value) {
                var opt = sel.options[sel.selectedIndex];
                outcome = opt ? (opt.getAttribute('data-outcome') || '') : '';
            }

            if (q && hay.indexOf(q) < 0) return false;

            switch (status) {
                case 'reviewed':        if (!isReviewed)              return false; break;
                case 'not-reviewed':    if (isReviewed)               return false; break;
                case 'payable':         if (outcome !== 'Payable')    return false; break;
                case 'notpayable':      if (outcome !== 'NotPayable') return false; break;
                case 'needs-comments':  if (!needsAttention)          return false; break;
            }

            for (var i = 0; i < facetVals.length; i++) {
                var f = facetVals[i];
                if (!f.value) continue;   // wildcard
                var v = item.getAttribute(f.attr) || '';
                if (v !== f.value) return false;
            }

            return true;
        }

        allCards.forEach(function (c) {
            if (matches(c)) c.classList.remove('hidden');
            else c.classList.add('hidden');
        });

        // Use the card's verdict as the source of truth for the row.
        allRows.forEach(function (r) {
            var did = r.getAttribute('data-doc-id');
            var card = cardByDoc[did];
            var show = card ? !card.classList.contains('hidden') : matches(r);
            r.style.display = show ? '' : 'none';
        });
    }

    /* ---------- view toggle ---------- */
    function setView(mode) {
        var cards = document.querySelector('.cards');
        var table = document.querySelector('.review-table');
        var btnC = document.getElementById('viewCards');
        var btnT = document.getElementById('viewTable');
        if (!cards || !table) return;

        syncAllToSibling(currentView);

        if (mode === 'table') {
            cards.classList.add('hidden');
            table.classList.add('active');
            if (btnT) { btnT.classList.add('active');    btnT.setAttribute('aria-selected', 'true'); }
            if (btnC) { btnC.classList.remove('active'); btnC.setAttribute('aria-selected', 'false'); }
            currentView = 'table';
        } else {
            cards.classList.remove('hidden');
            table.classList.remove('active');
            if (btnC) { btnC.classList.add('active');    btnC.setAttribute('aria-selected', 'true'); }
            if (btnT) { btnT.classList.remove('active'); btnT.setAttribute('aria-selected', 'false'); }
            currentView = 'cards';
        }

        applyFilter();
    }

    function syncAllToSibling(fromView) {
        var srcMap = (fromView === 'cards') ? cardByDoc : rowByDoc;
        var dstMap = (fromView === 'cards') ? rowByDoc  : cardByDoc;

        Object.keys(srcMap).forEach(function (id) {
            var src = srcMap[id];
            var dst = dstMap[id];
            if (!src || !dst) return;

            var sSel = src.querySelector('.reason-select');
            var dSel = dst.querySelector('.reason-select');
            var sTa  = src.querySelector('.comments-input');
            var dTa  = dst.querySelector('.comments-input');
            var sObj = src.querySelector('.objref-input');
            var dObj = dst.querySelector('.objref-input');

            if (sSel && dSel && dSel.value !== sSel.value) dSel.value = sSel.value;
            if (sTa  && dTa  && dTa.value  !== sTa.value ) dTa.value  = sTa.value;
            if (sObj && dObj && dObj.value !== sObj.value) dObj.value = sObj.value;

            dst.classList.toggle('reviewed', src.classList.contains('reviewed'));
            evaluateNeeds(dst);
        });
    }

    /* ---------- bulk select ---------- */
    function bindBulk() {
        var bar = document.getElementById('bulkBar');
        var bulkSel = document.getElementById('bulkReason');
        var apply = document.getElementById('bulkApply');
        var clear = document.getElementById('bulkClear');
        if (!bar) return;

        document.addEventListener('change', function (e) {
            if (e.target && e.target.classList && e.target.classList.contains('rowselect')) {
                var did = e.target.getAttribute('data-doc-id');
                var checked = e.target.checked;
                document.querySelectorAll('.rowselect[data-doc-id="' + did + '"]').forEach(function (cb) {
                    if (cb !== e.target) cb.checked = checked;
                });
                updateBulkBar();
            }
        });

        if (apply) apply.addEventListener('click', function () {
            var rid = bulkSel.value;
            if (!rid) return;
            var opt = bulkSel.options[bulkSel.selectedIndex];
            var outcome  = opt.getAttribute('data-outcome') || '';
            var requires = opt.getAttribute('data-requires') === '1';
            if (requires || outcome === 'NotPayable') {
                var msg = (outcome === 'NotPayable')
                    ? 'Not-Payable needs a Comment and an Objective Reference on every row. Apply anyway? Those rows will not save until both are filled.'
                    : 'This reason code requires a comment for each row. Apply anyway? Rows will not save until comments are added.';
                if (!confirm(msg)) return;
            }
            var picked = document.querySelectorAll('.rowselect:checked');
            var seen = {};
            picked.forEach(function (cb) {
                var docId = cb.getAttribute('data-doc-id');
                if (seen[docId]) return;
                seen[docId] = true;
                var item = cardByDoc[docId] || rowByDoc[docId];
                if (!item) return;
                var sel = item.querySelector('.reason-select');
                if (!sel) return;
                sel.value = rid;
                markDirty(item, docId);
                evaluateNeeds(item);
                mirrorField(docId, 'reason', rid);
                queueSave(docId);
            });
        });

        if (clear) clear.addEventListener('click', function () {
            var picked = document.querySelectorAll('.rowselect:checked');
            picked.forEach(function (cb) { cb.checked = false; });
            updateBulkBar();
        });
    }

    function updateBulkBar() {
        var bar = document.getElementById('bulkBar');
        if (!bar) return;
        var picked = document.querySelectorAll('.rowselect:checked');
        var seen = {};
        picked.forEach(function (cb) { seen[cb.getAttribute('data-doc-id')] = true; });
        var n = Object.keys(seen).length;
        if (n > 0) {
            bar.classList.add('show');
            var c = document.getElementById('bulkCount');
            if (c) c.textContent = n;
        } else {
            bar.classList.remove('show');
        }
    }

    /* ---------- mark all final ---------- */
    function markAllFinal() {
        if (!confirm('Mark all reviewed rows as final? You can still update them afterwards if needed.')) return;
        var fd = new FormData();
        fd.append('token', token);
        fd.append('action', 'markFinal');
        var xhr = new XMLHttpRequest();
        xhr.open('POST', SAVE_URL, true);
        xhr.onreadystatechange = function () {
            if (xhr.readyState !== 4) return;
            if (xhr.status === 200) {
                setIndicator('saved', 'Marked as final ✓');
            } else {
                setIndicator('error', 'Could not mark final');
            }
        };
        xhr.send(fd);
    }

    /* ---------- keyboard ---------- */
    function bindKeyboard() {
        document.addEventListener('keydown', function (e) {
            if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
            var t = e.target;
            if (!t || !t.tagName) return;
            if (t.tagName === 'TEXTAREA') return;
            var row = t.closest ? t.closest('.doc-row') : null;
            if (!row) return;
            e.preventDefault();
            var rows = Array.prototype.slice.call(document.querySelectorAll('.doc-row'))
                .filter(function (r) { return r.style.display !== 'none'; });
            var idx = rows.indexOf(row);
            var next = e.key === 'ArrowDown' ? rows[idx + 1] : rows[idx - 1];
            if (next) {
                var ctrl = next.querySelector('.reason-select') || next.querySelector('input');
                if (ctrl) ctrl.focus();
            }
        });
    }

    /* ---------- utils ---------- */
    function debounce(fn, ms) {
        var t;
        return function () {
            var args = arguments, ctx = this;
            clearTimeout(t);
            t = setTimeout(function () { fn.apply(ctx, args); }, ms);
        };
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
