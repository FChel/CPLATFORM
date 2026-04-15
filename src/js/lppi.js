/* =============================================================================
   LPPI Review — reviewer page interactions
   Vanilla JS, no jQuery, no frameworks.
   ========================================================================== */
(function () {
    'use strict';

    var SAVE_DEBOUNCE_MS = 600;
    var SAVE_URL = 'LPPI_Review_Save.ashx';

    var token = (document.getElementById('reviewToken') || {}).value || '';
    var allCards = [];
    var allRows = [];           // table rows
    var pendingTimers = {};
    var dirtyRows = new Set ? new Set() : null;
    var savingCount = 0;
    var totalDocs = 0;
    var reviewedDocs = 0;
    var saveIndicator = document.getElementById('saveIndicator');

    /* ---------- bootstrap ---------- */
    function init() {
        allCards = Array.prototype.slice.call(document.querySelectorAll('.doc-card'));
        allRows  = Array.prototype.slice.call(document.querySelectorAll('.review-table .doc-row'));
        totalDocs = allCards.length;

        // count already reviewed
        reviewedDocs = allCards.filter(function (c) { return c.classList.contains('reviewed'); }).length;
        updateProgress();

        // Wire reason-code dropdowns
        bindRowControls(allCards, '.doc-card');
        bindRowControls(allRows, '.doc-row');

        // Search/filter
        var search = document.getElementById('searchBox');
        if (search) search.addEventListener('input', debounce(applyFilter, 200));
        var statusFilter = document.getElementById('statusFilter');
        if (statusFilter) statusFilter.addEventListener('change', applyFilter);

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

        // Keyboard nav in table
        bindKeyboard();

        // Re-evaluate done banner
        updateDoneState();
    }

    function bindRowControls(items, rootSel) {
        items.forEach(function (item) {
            var docId = item.getAttribute('data-doc-id');
            var sel = item.querySelector('.reason-select');
            var ta  = item.querySelector('.comments-input');
            var obj = item.querySelector('.objref-input');

            if (sel) sel.addEventListener('change', function () { onChange(item, docId); });
            if (ta)  ta.addEventListener('input',  function () { onChange(item, docId); });
            if (obj) obj.addEventListener('input',  function () { onChange(item, docId); });
        });
    }

    function onChange(item, docId) {
        // Visual: needs-comment validation
        var sel = item.querySelector('.reason-select');
        var ta  = item.querySelector('.comments-input');
        var requires = false;
        if (sel && sel.value) {
            var opt = sel.options[sel.selectedIndex];
            requires = opt && opt.getAttribute('data-requires') === '1';
        }
        var hasComment = ta && ta.value && ta.value.trim().length > 0;
        if (requires && !hasComment) {
            item.classList.add('needs-comment');
        } else {
            item.classList.remove('needs-comment');
        }

        // Debounce save per row
        if (pendingTimers[docId]) clearTimeout(pendingTimers[docId]);
        pendingTimers[docId] = setTimeout(function () {
            saveRow(item, docId);
            delete pendingTimers[docId];
        }, SAVE_DEBOUNCE_MS);
    }

    function saveRow(item, docId) {
        var sel = item.querySelector('.reason-select');
        var ta  = item.querySelector('.comments-input');
        var obj = item.querySelector('.objref-input');

        var reasonId = sel ? (sel.value || '') : '';
        var requires = false;
        if (sel && sel.value) {
            var opt = sel.options[sel.selectedIndex];
            requires = opt && opt.getAttribute('data-requires') === '1';
        }
        var comments = ta ? ta.value : '';
        var objref   = obj ? obj.value : '';

        // Block save if mandatory comment missing
        if (requires && !comments.trim()) {
            // Don't save partial. Keep needs-comment visual.
            return;
        }

        savingCount++;
        setIndicator('saving', 'Saving…');

        var fd = new FormData();
        fd.append('token', token);
        fd.append('docId', docId);
        fd.append('reasonId', reasonId);
        fd.append('comments', comments);
        fd.append('objref', objref);

        var xhr = new XMLHttpRequest();
        xhr.open('POST', SAVE_URL, true);
        xhr.onreadystatechange = function () {
            if (xhr.readyState !== 4) return;
            savingCount--;
            if (xhr.status === 200) {
                var ok = false;
                try { ok = JSON.parse(xhr.responseText).ok === true; } catch (e) {}
                if (ok) {
                    showRowSaved(item);
                    if (reasonId) {
                        if (!item.classList.contains('reviewed')) {
                            item.classList.add('reviewed');
                            reviewedDocs++;
                        }
                    } else {
                        if (item.classList.contains('reviewed')) {
                            item.classList.remove('reviewed');
                            reviewedDocs--;
                        }
                    }
                    updateProgress();
                    updateDoneState();
                    if (savingCount === 0) setIndicator('saved', 'All changes saved');
                } else {
                    setIndicator('error', 'Save failed');
                }
            } else {
                setIndicator('error', 'Save failed');
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
        saveIndicator.className = state;
        saveIndicator.textContent = text;
    }

    function updateProgress() {
        var bar = document.getElementById('progressBar');
        var lbl = document.getElementById('progressLabel');
        var pct = totalDocs === 0 ? 0 : Math.round((reviewedDocs / totalDocs) * 100);
        if (bar) bar.style.width = pct + '%';
        if (lbl) lbl.textContent = reviewedDocs + ' of ' + totalDocs + ' reviewed (' + pct + '%)';
    }

    function updateDoneState() {
        var done = document.getElementById('doneBanner');
        if (!done) return;
        if (reviewedDocs >= totalDocs && totalDocs > 0) done.classList.add('show');
        else done.classList.remove('show');
    }

    /* ---------- filter ---------- */
    function applyFilter() {
        var q = (document.getElementById('searchBox').value || '').trim().toLowerCase();
        var status = (document.getElementById('statusFilter').value || '');
        var visible = 0;
        allCards.forEach(function (c) {
            var hay = (c.getAttribute('data-search') || '').toLowerCase();
            var isReviewed = c.classList.contains('reviewed');
            var sel = c.querySelector('.reason-select');
            var outcome = '';
            if (sel && sel.value) {
                var opt = sel.options[sel.selectedIndex];
                outcome = opt ? (opt.getAttribute('data-outcome') || '') : '';
            }
            var needsComment = c.classList.contains('needs-comment');

            var matchQ = q === '' || hay.indexOf(q) >= 0;
            var matchS = true;
            switch (status) {
                case 'reviewed':    matchS = isReviewed; break;
                case 'not-reviewed':matchS = !isReviewed; break;
                case 'payable':     matchS = outcome === 'Payable'; break;
                case 'notpayable':  matchS = outcome === 'NotPayable'; break;
                case 'needs-comments': matchS = needsComment; break;
            }
            if (matchQ && matchS) {
                c.classList.remove('hidden');
                visible++;
            } else {
                c.classList.add('hidden');
            }
        });

        // mirror to table rows
        allRows.forEach(function (r) {
            var did = r.getAttribute('data-doc-id');
            var matched = allCards.some(function (c) { return c.getAttribute('data-doc-id') === did && !c.classList.contains('hidden'); });
            r.style.display = matched ? '' : 'none';
        });
    }

    function setView(mode) {
        var cards = document.querySelector('.cards');
        var table = document.querySelector('.review-table');
        var btnC = document.getElementById('viewCards');
        var btnT = document.getElementById('viewTable');
        if (mode === 'table') {
            cards.classList.add('hidden');
            table.classList.add('active');
            btnT.classList.add('active');
            btnC.classList.remove('active');
        } else {
            cards.classList.remove('hidden');
            table.classList.remove('active');
            btnC.classList.add('active');
            btnT.classList.remove('active');
        }
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
                updateBulkBar();
            }
        });

        if (apply) apply.addEventListener('click', function () {
            var rid = bulkSel.value;
            if (!rid) return;
            var opt = bulkSel.options[bulkSel.selectedIndex];
            var requires = opt.getAttribute('data-requires') === '1';
            if (requires) {
                if (!confirm('This reason code requires a comment for each row. Apply anyway? You will still need to add comments before each row will save.')) return;
            }
            var picked = document.querySelectorAll('.rowselect:checked');
            picked.forEach(function (cb) {
                var docId = cb.getAttribute('data-doc-id');
                var card = document.querySelector('.doc-card[data-doc-id="' + docId + '"]');
                if (card) {
                    var sel = card.querySelector('.reason-select');
                    if (sel) {
                        sel.value = rid;
                        var ev = document.createEvent('Event');
                        ev.initEvent('change', true, true);
                        sel.dispatchEvent(ev);
                    }
                }
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
        var n = picked.length;
        if (n > 0) {
            bar.classList.add('show');
            var c = document.getElementById('bulkCount');
            if (c) c.textContent = n + ' selected';
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
            // Only on .doc-row in table view
            var row = t.closest ? t.closest('.doc-row') : null;
            if (!row) return;
            e.preventDefault();
            var rows = Array.prototype.slice.call(document.querySelectorAll('.doc-row'));
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
