/* =============================================================================
   PO Review — reviewer page interactions (PORD_Review.aspx)
   Vanilla JS, no frameworks — same conventions as lppi.js.

   * Explicit save model: edits mark rows dirty; nothing is sent until Save.
     A row that is edited back to its loaded state is no longer dirty.
   * Response drives the Detail cell:
       Amend    → "Amend by" date (required, not in the past)
       Reason   → reason code (required) + Objective ref (+ comments when the
                  reason requires them)
       Reassign → comments required (who owns the PO)
     The server re-validates everything (PordDemoStore.Validate).
   * Optimistic locking via data-version (ReviewedDate at load).
   * Filters (search, response, POC, Review Nbr, category chips) hide the row,
     its message row and its detail row together.
   * Bulk bar stages changes on the selected rows; Save commits them.
   * Ctrl/Cmd+S saves. beforeunload warns while anything is unsaved.
   ============================================================================= */
(function () {
    'use strict';

    var SAVE_URL = 'PORD_Review_Save.ashx';
    var FINAL_URL = 'PORD_Review_Finalise.ashx';

    var $ = function (id) { return document.getElementById(id); };
    var tokenEl = $('pordToken');
    if (!tokenEl) return;                                   // invalid-link page

    var token    = tokenEl.value;
    var readOnly = ($('pordReadOnly') || {}).value === '1';
    var isPoc    = ($('pordIsPoc') || {}).value === '1';

    var rows = [];               // { id, tr, msg, exp, orig }
    var byId = {};
    var dirty = {};
    var busy = false;
    var catFilter = '';

    var saveBtn = $('pordSaveBtn'), saveLbl = $('pordSaveLbl'), ind = $('pordSaveInd');

    // ------------------------------------------------------------------ init
    function init() {
        bindTabs();
        var trs = document.querySelectorAll('#pordTable tr.po-row');
        for (var i = 0; i < trs.length; i++) {
            var tr = trs[i], id = tr.getAttribute('data-id');
            var r = {
                id: id, tr: tr,
                msg: document.querySelector('tr.po-msg[data-id="' + id + '"]'),
                exp: document.querySelector('tr.po-exp[data-id="' + id + '"]')
            };
            r.orig = read(r);
            rows.push(r); byId[id] = r;
            bindRow(r);
        }
        if (readOnly) {
            var inputs = document.querySelectorAll('#pordTable .input');
            for (var k = 0; k < inputs.length; k++) inputs[k].disabled = true;
        }
        bindFilters(); bindBulk(); bindSave(); bindFinalise();
        window.addEventListener('beforeunload', function (e) {
            if (Object.keys(dirty).length) { e.preventDefault(); e.returnValue = ''; return ''; }
        });
        document.addEventListener('keydown', function (e) {
            if ((e.ctrlKey || e.metaKey) && (e.key === 's' || e.key === 'S')) { e.preventDefault(); if (!saveBtn.disabled) save(); }
        });
        updateProgress();
    }

    // ------------------------------------------------------------------ tabs
    function bindTabs() {
        var tabs = document.querySelectorAll('.pord-tab');
        for (var i = 0; i < tabs.length; i++) {
            tabs[i].addEventListener('click', function () {
                for (var j = 0; j < tabs.length; j++) tabs[j].classList.remove('active');
                this.classList.add('active');
                var panes = document.querySelectorAll('.pord-pane');
                for (var k = 0; k < panes.length; k++) panes[k].classList.toggle('active', panes[k].id === this.getAttribute('data-pane'));
            });
        }
        var toc = document.querySelectorAll('.pord-toc a');
        for (var t = 0; t < toc.length; t++) {
            toc[t].addEventListener('click', function (e) {
                var el = document.querySelector(this.getAttribute('href'));
                if (el) { e.preventDefault(); el.scrollIntoView({ behavior: 'smooth', block: 'start' }); }
            });
        }
    }

    // ------------------------------------------------------------------ rows
    function q(r, sel) { return r.tr.querySelector(sel); }

    function read(r) {
        return {
            response: q(r, '.resp-select').value,
            reason:   q(r, '.in-reason').value,
            target:   q(r, '.in-target').value,
            evidence: q(r, '.in-evidence').value.trim(),
            comments: q(r, '.in-comments').value.trim()
        };
    }

    function same(a, b) {
        if (a.response !== b.response || a.comments !== b.comments) return false;
        if (a.response === 'Amend')  return a.target === b.target;
        if (a.response === 'Reason') return a.reason === b.reason && a.evidence === b.evidence;
        return true;
    }

    function bindRow(r) {
        var sel = q(r, '.resp-select');
        sel.addEventListener('change', function () { layout(r); touched(r); });
        var inputs = r.tr.querySelectorAll('.in-reason, .in-target, .in-evidence, .in-comments');
        for (var i = 0; i < inputs.length; i++) {
            inputs[i].addEventListener('input', function () { touched(r); });
            inputs[i].addEventListener('change', function () { touched(r); });
        }
        q(r, '.btn-chev').addEventListener('click', function () {
            var open = this.getAttribute('aria-expanded') !== 'true';
            this.setAttribute('aria-expanded', open ? 'true' : 'false');
            r.exp.classList.toggle('open', open);
        });
        q(r, '.po-sel').addEventListener('change', updateBulk);
    }

    function layout(r) {
        var v = q(r, '.resp-select').value;
        q(r, '.resp-select').setAttribute('data-v', v);
        q(r, '.f-target').classList.toggle('show', v === 'Amend');
        q(r, '.f-reason').classList.toggle('show', v === 'Reason');
        q(r, '.f-evidence').classList.toggle('show', v === 'Reason');
        var hint = q(r, '.detail-hint');
        hint.style.display = (v === 'Amend' || v === 'Reason') ? 'none' : '';
        if (v === 'Reassign') hint.textContent = 'Say who owns it in Comments.';
        else if (v === '') hint.textContent = 'Choose a response.';
        q(r, '.in-comments').placeholder = v === 'Reassign' ? 'Who owns this PO?' : (needsComments(r) ? 'Required for this reason' : 'Optional');
        if (v === 'Amend' && !q(r, '.in-target').value) {
            var d = new Date(); d.setDate(d.getDate() + 14);
            q(r, '.in-target').value = d.toISOString().slice(0, 10);
        }
    }

    function needsComments(r) {
        var o = q(r, '.in-reason').selectedOptions;
        return q(r, '.resp-select').value === 'Reason' && o && o[0] && o[0].getAttribute('data-comments') === '1';
    }

    function touched(r) {
        if (readOnly) return;
        var cur = read(r);
        if (same(cur, r.orig)) { delete dirty[r.id]; r.tr.classList.remove('dirty'); clearMsg(r); }
        else { dirty[r.id] = true; r.tr.classList.add('dirty'); validate(r, false); }
        refreshSaveState();
        updateProgress();
    }

    /** Returns an error message or null. When show is false only clears stale highlights. */
    function validate(r, show) {
        var v = read(r), err = null, bad = [];
        var today = new Date(); today.setHours(0, 0, 0, 0);
        if (v.response === 'Amend') {
            if (!v.target) { err = 'Enter the date you expect the PO terms to be amended by.'; bad.push('.in-target'); }
            else if (new Date(v.target + 'T00:00:00') < today) { err = 'The amendment date cannot be in the past.'; bad.push('.in-target'); }
        } else if (v.response === 'Reason') {
            var opt = q(r, '.in-reason').selectedOptions[0];
            if (!v.reason) { err = 'Choose the reason that applies.'; bad.push('.in-reason'); }
            else {
                if (opt.getAttribute('data-evidence') === '1' && !v.evidence) { err = 'Add the Objective reference for your evidence.'; bad.push('.in-evidence'); }
                if (opt.getAttribute('data-comments') === '1' && !v.comments) { err = err || 'Comments are required for this reason.'; bad.push('.in-comments'); }
            }
        } else if (v.response === 'Reassign' && !v.comments) {
            err = 'Tell us who owns this PO (name or email) in Comments.'; bad.push('.in-comments');
        }
        var all = r.tr.querySelectorAll('.input');
        for (var i = 0; i < all.length; i++) all[i].classList.remove('invalid');
        if (show) {
            for (var j = 0; j < bad.length; j++) q(r, bad[j]).classList.add('invalid');
            if (err) showMsg(r, err, false); else clearMsg(r);
        } else if (!err) {
            clearMsg(r);
        }
        r.tr.setAttribute('data-invalid', err ? '1' : '0');
        return err;
    }

    function showMsg(r, text, stale) {
        var m = r.msg.querySelector('.row-msg');
        m.textContent = text; m.classList.add('show'); m.classList.toggle('stale', !!stale);
    }
    function clearMsg(r) {
        var m = r.msg.querySelector('.row-msg');
        m.textContent = ''; m.classList.remove('show', 'stale');
    }

    // ------------------------------------------------------------------ save
    function refreshSaveState(state, text) {
        var n = Object.keys(dirty).length;
        saveBtn.disabled = readOnly || busy || n === 0;
        saveLbl.textContent = n ? 'Save ' + n + ' change' + (n === 1 ? '' : 's') : 'Save changes';
        if (state) { ind.className = 'pord-save-ind ' + state; ind.textContent = text; return; }
        if (readOnly) { ind.className = 'pord-save-ind'; ind.textContent = 'Read only'; }
        else if (n) { ind.className = 'pord-save-ind dirty'; ind.textContent = n + ' unsaved'; }
        else { ind.className = 'pord-save-ind saved'; ind.textContent = 'All changes saved'; }
    }

    function bindSave() { if (saveBtn) saveBtn.addEventListener('click', save); }

    function save() {
        if (busy || readOnly) return;
        var ids = Object.keys(dirty), invalid = 0, first = null;
        for (var i = 0; i < ids.length; i++) {
            if (validate(byId[ids[i]], true)) { invalid++; first = first || byId[ids[i]]; }
        }
        if (invalid) {
            refreshSaveState('error', 'Fix ' + invalid + ' row' + (invalid === 1 ? '' : 's') + ' first');
            first.tr.scrollIntoView({ behavior: 'smooth', block: 'center' });
            return;
        }
        var body = ['token=' + enc(token), 'rowCount=' + ids.length];
        for (var k = 0; k < ids.length; k++) {
            var r = byId[ids[k]], v = read(r), p = 'rows[' + k + '].';
            body.push(p + 'poId=' + enc(r.id), p + 'response=' + enc(v.response), p + 'reason=' + enc(v.reason),
                      p + 'target=' + enc(v.target), p + 'evidence=' + enc(v.evidence), p + 'comments=' + enc(v.comments),
                      p + 'version=' + enc(r.tr.getAttribute('data-version')));
        }
        busy = true; refreshSaveState('saving', 'Saving…');
        post(SAVE_URL, body.join('&'), function (err, resp) {
            busy = false;
            if (err || !resp || !resp.ok) { refreshSaveState('error', (resp && resp.error) || 'Save failed — check your connection and try again'); return; }
            var failed = 0;
            for (var j = 0; j < resp.results.length; j++) {
                var res = resp.results[j], row = byId[String(res.poId)];
                if (!row) continue;
                if (res.ok) {
                    if (res.version) row.tr.setAttribute('data-version', res.version);
                    row.orig = read(row); delete dirty[row.id];
                    row.tr.classList.remove('dirty'); clearMsg(row);
                    row.tr.classList.remove('saved-flash'); void row.tr.offsetWidth; row.tr.classList.add('saved-flash');
                } else {
                    failed++;
                    showMsg(row, res.message || 'Could not save this PO.', res.code === 'stale');
                }
            }
            if (failed) refreshSaveState('error', failed + ' row' + (failed === 1 ? '' : 's') + ' not saved');
            else refreshSaveState();
            updateProgress();
        });
    }

    function post(url, body, cb) {
        var x = new XMLHttpRequest();
        x.open('POST', url, true);
        x.setRequestHeader('Content-Type', 'application/x-www-form-urlencoded; charset=UTF-8');
        x.onreadystatechange = function () {
            if (x.readyState !== 4) return;
            var json = null;
            try { json = JSON.parse(x.responseText); } catch (e) { }
            cb(x.status !== 200 ? x.status : null, json);
        };
        x.send(body);
    }
    function enc(s) { return encodeURIComponent(s == null ? '' : s); }

    // ------------------------------------------------------------------ finalise
    function bindFinalise() {
        var btn = $('pordFinaliseBtn');
        if (!btn) return;
        btn.addEventListener('click', function () {
            if (Object.keys(dirty).length) { alertUnsaved(); return; }
            var action = btn.getAttribute('data-action');
            var msg;
            if (action === 'finalise') {
                var open = rows.filter(function (r) { return !r.orig.response; }).length;
                msg = 'Finalise this package?\n\n' + (open ? open + ' PO' + (open === 1 ? '' : 's') + ' without a response will be recorded as "No response" and carried into next month\'s review.\n\n' : 'Every PO has a response.\n\n')
                    + 'Accepted valid reasons will exclude those POs from future reviews. You can reopen the package later.';
            } else {
                msg = 'Reopen this package for changes?\n\n"No response" markers applied at finalise will be cleared.';
            }
            if (!window.confirm(msg)) return;
            btn.disabled = true;
            post(FINAL_URL, 'token=' + enc(token) + '&action=' + enc(action), function (err, resp) {
                if (err || !resp || !resp.ok) { btn.disabled = false; refreshSaveState('error', (resp && resp.error) || 'Request failed'); return; }
                window.location.reload();
            });
        });
    }
    function alertUnsaved() {
        refreshSaveState('error', 'Save your changes first');
        saveBtn.focus();
    }

    // ------------------------------------------------------------------ progress
    function updateProgress() {
        var total = rows.length, done = 0;
        for (var i = 0; i < rows.length; i++) if (rows[i].orig.response) done++;
        var lbl = $('pordProgressLabel'), bar = $('pordProgressBar'), ready = $('pordReady');
        if (lbl) lbl.textContent = done + ' of ' + total;
        if (bar) bar.style.width = (total ? Math.round(done * 100 / total) : 0) + '%';
        if (ready && !readOnly) ready.classList.toggle('show', total > 0 && done === total && !Object.keys(dirty).length);
    }

    // ------------------------------------------------------------------ filters
    function bindFilters() {
        var s = $('pordSearch'), fr = $('pordFilterResp'), fp = $('pordFilterPoc'), fn = $('pordFilterRn');
        var t;
        if (s) s.addEventListener('input', function () { clearTimeout(t); t = setTimeout(applyFilter, 120); });
        [fr, fp, fn].forEach(function (el) { if (el) el.addEventListener('change', applyFilter); });
        var chips = document.querySelectorAll('#pordChips .pord-chip');
        for (var i = 0; i < chips.length; i++) {
            chips[i].addEventListener('click', function () {
                for (var j = 0; j < chips.length; j++) chips[j].classList.remove('on');
                this.classList.add('on');
                catFilter = this.getAttribute('data-cat');
                applyFilter();
            });
        }
        var all = $('pordSelAll');
        if (all) all.addEventListener('change', function () {
            for (var k = 0; k < rows.length; k++) if (!rows[k].tr.classList.contains('hidden')) q(rows[k], '.po-sel').checked = all.checked;
            updateBulk();
        });
    }

    function applyFilter() {
        var s = (($('pordSearch') || {}).value || '').toLowerCase().trim();
        var resp = ($('pordFilterResp') || {}).value || '';
        var poc = ($('pordFilterPoc') || {}).value || '';
        var rn = ($('pordFilterRn') || {}).value || '';
        var shown = 0;
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i], tr = r.tr, v = read(r), ok = true;
            if (s && tr.getAttribute('data-search').indexOf(s) < 0) ok = false;
            if (ok && catFilter && tr.getAttribute('data-cat') !== catFilter) ok = false;
            if (ok && poc && tr.getAttribute('data-poc') !== poc) ok = false;
            if (ok && rn === 'repeat' && +tr.getAttribute('data-rn') < 2) ok = false;
            if (ok && rn === 'first' && +tr.getAttribute('data-rn') !== 1) ok = false;
            if (ok && resp) {
                if (resp === 'awaiting') ok = !v.response;
                else if (resp === 'attention') ok = tr.getAttribute('data-invalid') === '1' || !!r.msg.querySelector('.row-msg.show');
                else ok = v.response === resp;
            }
            tr.classList.toggle('hidden', !ok);
            r.msg.classList.toggle('hidden', !ok);
            r.exp.classList.toggle('hidden', !ok);
            if (!ok) q(r, '.po-sel').checked = false;
            if (ok) shown++;
        }
        $('pordNoMatch').style.display = shown || !rows.length ? 'none' : '';
        updateBulk();
    }

    // ------------------------------------------------------------------ bulk
    function selected() { return rows.filter(function (r) { return q(r, '.po-sel').checked; }); }

    function updateBulk() {
        var n = selected().length, bar = $('pordBulk');
        if (!bar) return;
        $('pordBulkCount').textContent = n;
        bar.classList.toggle('show', n > 0 && !readOnly);
    }

    function bindBulk() {
        var resp = $('pordBulkResp'), date = $('pordBulkDate'), reason = $('pordBulkReason');
        if (!resp) return;
        resp.addEventListener('change', function () {
            date.style.display = resp.value === 'Amend' ? '' : 'none';
            reason.style.display = resp.value === 'Reason' ? '' : 'none';
            if (resp.value === 'Amend' && !date.value) { var d = new Date(); d.setDate(d.getDate() + 14); date.value = d.toISOString().slice(0, 10); }
        });
        $('pordBulkApply').addEventListener('click', function () {
            if (!resp.value) { resp.focus(); return; }
            var sel = selected();
            for (var i = 0; i < sel.length; i++) {
                var r = sel[i];
                q(r, '.resp-select').value = resp.value;
                if (resp.value === 'Amend') q(r, '.in-target').value = date.value;
                if (resp.value === 'Reason' && reason.value) q(r, '.in-reason').value = reason.value;
                layout(r); touched(r);
                q(r, '.po-sel').checked = false;
            }
            if ($('pordSelAll')) $('pordSelAll').checked = false;
            updateBulk();
            refreshSaveState('dirty', sel.length + ' staged — review, then Save');
        });
        $('pordBulkClear').addEventListener('click', function () {
            for (var i = 0; i < rows.length; i++) q(rows[i], '.po-sel').checked = false;
            if ($('pordSelAll')) $('pordSelAll').checked = false;
            updateBulk();
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
