/* =============================================================================
   LPPI Review — reviewer page interactions
   Vanilla JS, no jQuery, no frameworks.

   Behaviour summary:

   * Two views: Main (one row per document, editable) and Detail (one row per
     line, review fields read-only). Toggle buttons: "Main" / "Detail".
   * Auto-save fires on BLUR of text/textarea and on CHANGE of the reason-code
     select. One save per edit.
   * A "Save" button in the toolbar flushes every dirty row.
   * Mandatory-field rules:
       - RequiresComments flag (RC07/RC16): Comments must be non-empty.
       - Outcome = NotPayable: BOTH Comments and Objective Reference required.
     Client-side check blocks the save and shows a row-level message; the
     server enforces the same rules authoritatively.
   * Four facet filters: Delivery Manager Program, POC, WBS, Profit Centre.
     All filters AND together with free-text search and the status dropdown.
   * Primary key throughout is DocNoAccounting (data-doc-no).
   ========================================================================== */
(function () {
    'use strict';

    var SAVE_URL = 'LPPI_Review_Save.ashx';

    var token = (document.getElementById('reviewToken') || {}).value || '';

    var allMain   = [];
    var mainByDoc = {};   // docNo -> .doc-main <tr>

    var allDetail = [];   // detail rows — used for filter pass-through only

    var dirtyDocNos  = {};
    var savingCount  = 0;
    var totalDocs    = 0;
    var reviewedDocs = 0;
    var currentView  = 'main';
    var saveIndicator = document.getElementById('saveIndicator');

    var FACETS = [
        { id: 'filterDm',  attr: 'data-dm'  },
        { id: 'filterPoc', attr: 'data-poc' },
        { id: 'filterWbs', attr: 'data-wbs' },
        { id: 'filterPc',  attr: 'data-pc'  }
    ];

    /* ---------- bootstrap ---------- */
    function init() {
        allMain   = Array.prototype.slice.call(document.querySelectorAll('.mains .doc-main'));
        allDetail = Array.prototype.slice.call(document.querySelectorAll('.review-detail .detail-row'));
        totalDocs = allMain.length;

        allMain.forEach(function (row) {
            var dn = row.getAttribute('data-doc-no');
            if (dn) mainByDoc[dn] = row;
        });

        // Seed "reviewed" class from pre-selected reason codes
        allMain.forEach(function (row) {
            var sel = row.querySelector('.reason-select');
            if (sel && sel.value) row.classList.add('reviewed');
        });
        Object.keys(mainByDoc).forEach(function (dn) {
            if (mainByDoc[dn].classList.contains('reviewed')) reviewedDocs++;
        });
        updateProgress();

        bindRowControls(allMain);
        allMain.forEach(evaluateNeeds);

        var search = document.getElementById('searchBox');
        if (search) search.addEventListener('input', debounce(applyFilter, 200));
        var statusFilter = document.getElementById('statusFilter');
        if (statusFilter) statusFilter.addEventListener('change', applyFilter);
        FACETS.forEach(function (f) {
            var el = document.getElementById(f.id);
            if (el) el.addEventListener('change', applyFilter);
        });

        var btnMain   = document.getElementById('viewMain');
        var btnDetail = document.getElementById('viewDetail');
        if (btnMain && btnDetail) {
            btnMain.addEventListener('click',   function () { setView('main');   });
            btnDetail.addEventListener('click', function () { setView('detail'); });
        }

        bindBulk();

        var finalBtn = document.getElementById('markFinalBtn');
        if (finalBtn) finalBtn.addEventListener('click', markAllFinal);

        var saveAllBtn = document.getElementById('saveAllBtn');
        if (saveAllBtn) saveAllBtn.addEventListener('click', saveAllDirty);

        window.addEventListener('beforeunload', function (e) {
            if (Object.keys(dirtyDocNos).length > 0) {
                var msg = 'You have unsaved review changes. Leave anyway?';
                e.returnValue = msg;
                return msg;
            }
        });

        bindKeyboard();
        updateDoneState();
        applyFilter();
    }

    /* ---------- event wiring ---------- */
    function bindRowControls(items) {
        items.forEach(function (item) {
            var docNo = item.getAttribute('data-doc-no');
            var sel   = item.querySelector('.reason-select');
            var ta    = item.querySelector('.comments-input');
            var obj   = item.querySelector('.objref-input');

            if (sel) {
                sel.addEventListener('change', function () {
                    markDirty(item, docNo);
                    evaluateNeeds(item);
                    queueSave(docNo);
                });
            }
            if (ta) {
                ta.addEventListener('input', function () {
                    markDirty(item, docNo);
                    evaluateNeeds(item);
                });
                ta.addEventListener('blur', function () {
                    if (dirtyDocNos[docNo]) queueSave(docNo);
                });
            }
            if (obj) {
                obj.addEventListener('input', function () {
                    markDirty(item, docNo);
                    evaluateNeeds(item);
                });
                obj.addEventListener('blur', function () {
                    if (dirtyDocNos[docNo]) queueSave(docNo);
                });
            }
        });
    }

    function markDirty(item, docNo) {
        dirtyDocNos[docNo] = true;
        if (saveIndicator && savingCount === 0) setIndicator('saving', 'Unsaved changes');
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
            var opt  = sel.options[sel.selectedIndex];
            outcome  = opt ? (opt.getAttribute('data-outcome')  || '') : '';
            requires = opt ? opt.getAttribute('data-requires') === '1' : false;
        }
        var hasComment = !!(ta  && ta.value  && ta.value.trim().length  > 0);
        var hasObjRef  = !!(obj && obj.value && obj.value.trim().length > 0);

        var bad = (requires && !hasComment) ||
                  (outcome === 'NotPayable' && (!hasComment || !hasObjRef));
        item.classList.toggle('needs-comment', bad);

        var docNo  = item.getAttribute('data-doc-no');
        var msgRow = document.querySelector('.doc-main-msg[data-doc-no="' + escAttr(docNo) + '"]');
        var msgDiv = msgRow ? msgRow.querySelector('.row-msg') : null;
        if (msgDiv) {
            if (outcome === 'NotPayable' && !hasComment && !hasObjRef) {
                msgDiv.textContent = 'Not-Payable needs both a comment and an Objective Reference.';
            } else if (outcome === 'NotPayable' && !hasComment) {
                msgDiv.textContent = 'Not-Payable needs a comment.';
            } else if (outcome === 'NotPayable' && !hasObjRef) {
                msgDiv.textContent = 'Not-Payable needs an Objective Reference.';
            } else if (requires && !hasComment) {
                msgDiv.textContent = 'This reason code needs a comment.';
            } else {
                msgDiv.textContent = '';
            }
        }
    }

    /* ---------- saving ---------- */
    function queueSave(docNo) {
        var item = mainByDoc[docNo];
        if (item) saveRow(item, docNo);
    }

    function saveAllDirty() {
        var keys = Object.keys(dirtyDocNos);
        if (keys.length === 0) { setIndicator('saved', 'Nothing to save'); return; }
        keys.forEach(function (dn) { queueSave(dn); });
    }

    function saveRow(item, docNo) {
        var sel = item.querySelector('.reason-select');
        var ta  = item.querySelector('.comments-input');
        var obj = item.querySelector('.objref-input');

        var reasonId = sel ? (sel.value || '') : '';
        var outcome  = '';
        var requires = false;
        if (sel && sel.value) {
            var opt  = sel.options[sel.selectedIndex];
            outcome  = opt ? (opt.getAttribute('data-outcome')  || '') : '';
            requires = opt ? opt.getAttribute('data-requires') === '1' : false;
        }
        var comments = ta  ? ta.value.trim()  : '';
        var objref   = obj ? obj.value.trim() : '';

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
        fd.append('docNo',    docNo);
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
                    ok  = j.ok === true;
                    err = j.error || '';
                } catch (ex) { err = 'Bad server response.'; }
            } else {
                err = 'HTTP ' + xhr.status;
            }

            if (ok) {
                delete dirtyDocNos[docNo];
                item.classList.toggle('reviewed', reasonId.length > 0);
                showRowSaved(docNo);
                reviewedDocs = Object.keys(mainByDoc).filter(function (dn) {
                    return mainByDoc[dn].classList.contains('reviewed');
                }).length;
                updateProgress();
                updateDoneState();
            } else {
                evaluateNeeds(item);
            }

            if (savingCount === 0) {
                if (ok && Object.keys(dirtyDocNos).length === 0) {
                    setIndicator('saved', 'All changes saved');
                } else if (!ok) {
                    setIndicator('error', err || 'Save failed');
                }
            }
        };
        xhr.send(fd);
    }

    /* ---------- view toggle ---------- */
    function setView(mode) {
        var mainSection   = document.querySelector('.mains');
        var detailSection = document.querySelector('.review-detail');
        var btnM = document.getElementById('viewMain');
        var btnD = document.getElementById('viewDetail');
        if (!mainSection || !detailSection) return;

        if (mode === 'detail') {
            mainSection.classList.remove('active');
            detailSection.classList.add('active');
            if (btnD) { btnD.classList.add('active');    btnD.setAttribute('aria-selected', 'true'); }
            if (btnM) { btnM.classList.remove('active'); btnM.setAttribute('aria-selected', 'false'); }
            currentView = 'detail';
        } else {
            mainSection.classList.add('active');
            detailSection.classList.remove('active');
            if (btnM) { btnM.classList.add('active');    btnM.setAttribute('aria-selected', 'true'); }
            if (btnD) { btnD.classList.remove('active'); btnD.setAttribute('aria-selected', 'false'); }
            currentView = 'main';
        }

        applyFilter();
    }

    /* ---------- filtering ---------- */
    function applyFilter() {
        var searchVal = ((document.getElementById('searchBox')    || {}).value || '').toLowerCase();
        var statusVal =  (document.getElementById('statusFilter') || {}).value || '';

        var facetVals = {};
        FACETS.forEach(function (f) {
            var el = document.getElementById(f.id);
            facetVals[f.attr] = el ? el.value : '';
        });

        allMain.forEach(function (row) {
            var show = matchesMain(row, searchVal, statusVal, facetVals);
            row.style.display = show ? '' : 'none';
            var docNo  = row.getAttribute('data-doc-no');
            var msgRow = document.querySelector('.doc-main-msg[data-doc-no="' + escAttr(docNo) + '"]');
            if (msgRow) msgRow.style.display = show ? '' : 'none';
        });

        // Detail rows mirror their document's Main-row visibility
        allDetail.forEach(function (row) {
            var docNo   = row.getAttribute('data-doc-no');
            var mainRow = mainByDoc[docNo];
            row.style.display = (mainRow && mainRow.style.display !== 'none') ? '' : 'none';
        });
    }

    function matchesMain(row, searchVal, statusVal, facetVals) {
        if (searchVal) {
            var blob = (row.getAttribute('data-search') || '').toLowerCase();
            if (blob.indexOf(searchVal) === -1) return false;
        }

        if (statusVal) {
            var isReviewed = row.classList.contains('reviewed');
            var needsAttn  = row.classList.contains('needs-comment');
            var sel        = row.querySelector('.reason-select');
            var outcome    = '';
            if (sel && sel.value) {
                var opt = sel.options[sel.selectedIndex];
                outcome = opt ? (opt.getAttribute('data-outcome') || '') : '';
            }
            if (statusVal === 'not-reviewed'  && isReviewed)               return false;
            if (statusVal === 'reviewed'       && !isReviewed)              return false;
            if (statusVal === 'payable'        && outcome !== 'Payable')    return false;
            if (statusVal === 'notpayable'     && outcome !== 'NotPayable') return false;
            if (statusVal === 'needs-comments' && !needsAttn)              return false;
        }

        for (var i = 0; i < FACETS.length; i++) {
            var fv = facetVals[FACETS[i].attr];
            if (!fv) continue;
            if ((row.getAttribute(FACETS[i].attr) || '') !== fv) return false;
        }

        return true;
    }

    /* ---------- bulk select ---------- */
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
                    ? 'Not-Payable needs a Comment and an Objective Reference on every row. Apply anyway? Those rows will not save until both are filled.'
                    : 'This reason code requires a comment for each row. Apply anyway? Rows will not save until comments are added.';
                if (!confirm(msg)) return;
            }
            var seen = {};
            document.querySelectorAll('.rowselect:checked').forEach(function (cb) {
                var docNo = cb.getAttribute('data-doc-no');
                if (seen[docNo]) return;
                seen[docNo] = true;
                var item = mainByDoc[docNo];
                if (!item) return;
                var s = item.querySelector('.reason-select');
                if (!s) return;
                s.value = rid;
                markDirty(item, docNo);
                evaluateNeeds(item);
                queueSave(docNo);
            });
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

    /* ---------- mark all final ---------- */
    function markAllFinal() {
        if (!confirm('Mark all reviewed rows as final? You can still update them afterwards if needed.')) return;
        var fd = new FormData();
        fd.append('token',  token);
        fd.append('action', 'markFinal');
        var xhr = new XMLHttpRequest();
        xhr.open('POST', SAVE_URL, true);
        xhr.onreadystatechange = function () {
            if (xhr.readyState !== 4) return;
            setIndicator(xhr.status === 200 ? 'saved' : 'error',
                         xhr.status === 200 ? 'Marked as final ✓' : 'Could not mark final');
        };
        xhr.send(fd);
    }

    /* ---------- progress / done state ---------- */
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

    /* ---------- indicator / row flash ---------- */
    function setIndicator(state, text) {
        if (!saveIndicator) return;
        saveIndicator.className = 'save-indicator ' + state;
        saveIndicator.textContent = text;
    }

    function showRowSaved(docNo) {
        var row = mainByDoc[docNo];
        if (!row) return;
        row.classList.add('just-saved');
        setTimeout(function () { row.classList.remove('just-saved'); }, 1800);
    }

    /* ---------- keyboard nav ---------- */
    function bindKeyboard() {
        document.addEventListener('keydown', function (e) {
            if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
            var t = e.target;
            if (!t || t.tagName === 'TEXTAREA') return;
            var row = t.closest ? t.closest('tr.doc-main') : null;
            if (!row) return;
            e.preventDefault();
            var rows   = Array.prototype.slice.call(document.querySelectorAll('.mains .doc-main'));
            var idx    = rows.indexOf(row);
            var target = rows[e.key === 'ArrowDown' ? idx + 1 : idx - 1];
            if (target) {
                var focusable = target.querySelector('select,textarea,input');
                if (focusable) focusable.focus();
            }
        });
    }

    /* ---------- utilities ---------- */
    function debounce(fn, delay) {
        var timer;
        return function () { clearTimeout(timer); timer = setTimeout(fn, delay); };
    }

    function escAttr(s) {
        return s ? s.replace(/"/g, '&quot;').replace(/'/g, '&#39;') : '';
    }

    /* ---------- boot ---------- */
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

}());
