/* ─────────────────────────────────────────────────────────────────────────
   Tab switching with AJAX pane refresh.

   The dashboard is one server-rendered page with client-side panes. To avoid
   stale data, switching to a DB-backed tab re-fetches ONLY that tab's HTML from
   the server (Default.aspx?render=<pane>) with cache:'no-store' and swaps it into
   the pane — so every visit to a tab shows current data, with no full-page reload
   or flash. Static tabs (5–7) just toggle the active class.
───────────────────────────────────────────────────────────────────────── */

/* Maps a pane index to its server render-name (only DB-backed tabs are refreshable). */
function paneRenderName(idx){
  var pane = document.getElementById('pane-' + idx);
  return pane ? pane.getAttribute('data-render') : null;   // null => not AJAX-refreshable
}

/* Switch to tab idx and (for DB-backed tabs) refresh its content from the server.
   extraParams: optional query string (e.g. "invoice=14") forwarded to the render call. */
function switchTab(idx, extraParams){
  document.querySelectorAll('.ttab').forEach(function(t,i){t.classList.toggle('active',i===idx);});
  document.querySelectorAll('.pane').forEach(function(p,i){p.classList.toggle('active',i===idx);});
  window.scrollTo({top:0,behavior:'smooth'});
  refreshPane(idx, extraParams);
}

/* AJAX-fetch the fresh HTML for a DB-backed pane and swap it in. No-op for static panes.
   onDone (optional) runs after the new HTML is in the DOM (e.g. to scroll to a section). */
function refreshPane(idx, extraParams, onDone){
  var render = paneRenderName(idx);
  if(!render) return;                       // static tab — nothing to refresh
  var pane = document.getElementById('pane-' + idx);
  var url = 'Default.aspx?render=' + encodeURIComponent(render)
          + (extraParams ? ('&' + extraParams) : '')
          + '&_=' + Date.now();             // cache-bust
  pane.classList.add('pane-loading');
  fetch(url, { credentials:'include', cache:'no-store' })
    .then(function(r){ if(!r.ok){ throw new Error('HTTP ' + r.status); } return r.text(); })
    .then(function(html){
      pane.innerHTML = html;
      pane.classList.remove('pane-loading');
      if(typeof onDone === 'function') onDone();
    })
    .catch(function(err){
      pane.classList.remove('pane-loading');
      console.error('Pane refresh failed:', err);
    });
}

/* On page load, activate (and refresh) the tab implied by the URL. A server round-trip
   that lands with ?invoice= / ?journal= opens the right tab; otherwise a #pane-N hash. */
function restoreActiveTab(){
  var qs = window.location.search;
  var idx = 0, extra = null;
  var m;
  if((m = /[?&]invoice=([^&]+)/.exec(qs)))      { idx = 1; extra = 'invoice=' + m[1]; }
  else if((m = /[?&]journal=([^&]+)/.exec(qs))) { idx = 2; extra = 'journal=' + m[1]; }
  else if(/[?&](po|vendor|project|group|openpo)=/.test(qs)) { idx = 0; extra = qs.replace(/^\?/, ''); }
  else {
    var h = /#pane-(\d+)/.exec(window.location.hash);
    if(h) idx = parseInt(h[1],10);
  }
  // pane-0 is already server-rendered on first load; only switch/refresh when needed.
  if(idx > 0 || extra) switchTab(idx, extra);
}
if(document.readyState !== 'loading') restoreActiveTab();
else document.addEventListener('DOMContentLoaded', restoreActiveTab);
/* Switch an inner sub-tab group (e.g. prefix 't2'). Highlights the clicked .stab tab and
   shows its matching #<prefix>-<idx> pane. Driven by the pane ids, not element order, so
   extra controls that also call switchInner (e.g. a "Review all" button) don't shift the
   active highlight onto the wrong tab. */
function switchInner(prefix,idx){
  // Only the .stab tabs participate in the active-highlight, matched by their own idx.
  document.querySelectorAll('.stab[onclick^="switchInner(\''+prefix+'\'"]').forEach(function(tab){
    var m=/switchInner\('[^']+',\s*(\d+)\)/.exec(tab.getAttribute('onclick'));
    if(m) tab.classList.toggle('active', parseInt(m[1],10)===idx);
  });
  // Panes are toggled by exact id, so order/extra matches can't misalign them.
  document.querySelectorAll('[id^="'+prefix+'-"]').forEach(function(p){
    if(/^[0-9]+$/.test(p.id.slice(prefix.length+1)))   // only <prefix>-<number> panes
      p.classList.toggle('active', p.id===prefix+'-'+idx);
  });
}

/* ─────────────────────────────────────────────────────────────────────────
   Tab 1 — PO Identification client behaviour
   Search/clear/open AJAX-refresh pane 0 with query-string filters (server
   re-queries the DB). Toggle/save/confirm POST to the .ashx write-back endpoint.
───────────────────────────────────────────────────────────────────────── */
var TAB1_HANDLER = 'Services/PoIdentificationHandler.ashx';

function tab1Val(id){ var el=document.getElementById(id); return el?el.value.trim():''; }

function tab1Search(){
  var qs=[];
  var po=tab1Val('f-po'), v=tab1Val('f-vendor'), p=tab1Val('f-project'), g=tab1Val('f-group');
  if(po) qs.push('po='+encodeURIComponent(po));
  if(v)  qs.push('vendor='+encodeURIComponent(v));
  if(p)  qs.push('project='+encodeURIComponent(p));
  if(g)  qs.push('group='+encodeURIComponent(g));
  switchTab(0, qs.join('&'));
}

function tab1Clear(){ switchTab(0); }

function tab1OpenSchedule(poNumber){
  switchTab(0, 'openpo=' + encodeURIComponent(poNumber));
}

/* "Existing Prepayment POs" grid action: route to the tab that matches the PO's
   amortisation status, resolving the PO -> invoice/journal server-side via ?openpo=.
     amortising / pending approval -> Tab 2 (schedule / setup)
     ready for export             -> Tab 3 (journals) */
function tab1OpenExisting(poNumber, target){
  var po = 'openpo=' + encodeURIComponent(poNumber);
  if(target === 'journals') switchTab(2, po);   // View journals
  else switchTab(1, po);                        // View schedule / Open
}

function tab1Post(action, payload, onok){
  ppmPost(TAB1_HANDLER + '?action=' + action, payload, onok);
}

/* ── Existing Prepayment POs — multi-select vendor filter (server-side) ──── */
function vfToggle(e){
  if(e) e.stopPropagation();
  var drop = document.getElementById('vf-drop');
  if(!drop) return;
  var opening = !drop.classList.contains('open');
  drop.classList.toggle('open', opening);
  if(opening){
    vfBuild();
    var inp = document.getElementById('vf-search');
    if(inp){ inp.value = ''; vfSearch(''); inp.focus(); }
  }
}

function vfBuild(){
  var vendors = {};
  document.querySelectorAll('#existing-pos-table tbody tr td.vendor').forEach(function(c){
    vendors[c.textContent.trim()] = true;
  });
  // Include active-filter vendors that may be absent from current (already-filtered) rows
  var active = typeof VF_ACTIVE !== 'undefined' && VF_ACTIVE ? VF_ACTIVE.split(',').map(function(s){ return s.trim(); }) : [];
  active.forEach(function(v){ if(v) vendors[v] = true; });

  var list = document.getElementById('vf-list');
  if(!list) return;
  list.innerHTML = '';
  Object.keys(vendors).sort().forEach(function(name){
    var lbl = document.createElement('label');
    var chk = document.createElement('input');
    chk.type = 'checkbox'; chk.value = name;
    chk.checked = active.indexOf(name) >= 0;
    lbl.appendChild(chk);
    lbl.appendChild(document.createTextNode(name));
    list.appendChild(lbl);
  });
}

function vfSearch(q){
  var lq = (q || '').toLowerCase();
  document.querySelectorAll('#vf-list label').forEach(function(l){
    l.style.display = l.textContent.toLowerCase().indexOf(lq) >= 0 ? '' : 'none';
  });
}

function vfApply(){
  var selected = [];
  document.querySelectorAll('#vf-list input[type=checkbox]:checked').forEach(function(c){ selected.push(c.value); });
  var drop = document.getElementById('vf-drop');
  if(drop) drop.classList.remove('open');
  refreshPane(0, selected.length ? 'existingvendors=' + encodeURIComponent(selected.join(',')) : '');
}

function vfClear(){
  document.querySelectorAll('#vf-list input[type=checkbox]').forEach(function(c){ c.checked = false; });
  var drop = document.getElementById('vf-drop');
  if(drop) drop.classList.remove('open');
  refreshPane(0, '');
}

// Close vendor filter dropdown when clicking outside it
document.addEventListener('click', function(e){
  var wrap = document.getElementById('vf-wrap');
  var drop = document.getElementById('vf-drop');
  if(drop && drop.classList.contains('open') && wrap && !wrap.contains(e.target)){
    drop.classList.remove('open');
  }
  // Journal tab (Part A / Part B) vendor filter dropdowns
  ['rec','amort'].forEach(function(part){
    var jw = document.getElementById('jvf-wrap-' + part);
    var jd = document.getElementById('jvf-drop-' + part);
    if(jd && jd.classList.contains('open') && jw && !jw.contains(e.target)){
      jd.classList.remove('open');
    }
  });
});

/* ── Journal tab — multi-select vendor filter (Part A = 'rec', Part B = 'amort') ── */
function jvfToggle(part, e){
  if(e) e.stopPropagation();
  var drop = document.getElementById('jvf-drop-' + part);
  if(!drop) return;
  var opening = !drop.classList.contains('open');
  drop.classList.toggle('open', opening);
  if(opening){
    jvfBuild(part);
    var inp = document.getElementById('jvf-search-' + part);
    if(inp){ inp.value = ''; jvfSearch(part, ''); inp.focus(); }
  }
}

function jvfBuild(part){
  var tbodyId = part === 'rec' ? 'jvf-rec-tbody' : 'jvf-amort-tbody';
  var activeVar = part === 'rec'
    ? (typeof JVF_ACTIVE_REC   !== 'undefined' ? JVF_ACTIVE_REC   : '')
    : (typeof JVF_ACTIVE_AMORT !== 'undefined' ? JVF_ACTIVE_AMORT : '');
  var vendors = {};
  document.querySelectorAll('#' + tbodyId + ' tr td.vendor').forEach(function(c){
    vendors[c.textContent.trim()] = true;
  });
  var active = activeVar ? activeVar.split(',').map(function(s){ return s.trim(); }) : [];
  active.forEach(function(v){ if(v) vendors[v] = true; });
  var list = document.getElementById('jvf-list-' + part);
  if(!list) return;
  list.innerHTML = '';
  Object.keys(vendors).sort().forEach(function(name){
    var lbl = document.createElement('label');
    var chk = document.createElement('input');
    chk.type = 'checkbox'; chk.value = name;
    chk.checked = active.indexOf(name) >= 0;
    lbl.appendChild(chk);
    lbl.appendChild(document.createTextNode(name));
    list.appendChild(lbl);
  });
}

function jvfSearch(part, q){
  var lq = (q || '').toLowerCase();
  document.querySelectorAll('#jvf-list-' + part + ' label').forEach(function(l){
    l.style.display = l.textContent.toLowerCase().indexOf(lq) >= 0 ? '' : 'none';
  });
}

function jvfApply(part){
  var selected = [];
  document.querySelectorAll('#jvf-list-' + part + ' input[type=checkbox]:checked').forEach(function(c){ selected.push(c.value); });
  var drop = document.getElementById('jvf-drop-' + part);
  if(drop) drop.classList.remove('open');
  var param = part === 'rec' ? 'recvendors' : 'amortvendors';
  var qs = selected.length ? param + '=' + encodeURIComponent(selected.join(',')) : '';
  // For Part B, switch back to the amortisation inner tab after the pane HTML reloads
  var onDone = part === 'amort' ? function(){ switchInner('t3', 1); } : undefined;
  refreshPane(2, qs, onDone);
}

function jvfClear(part){
  document.querySelectorAll('#jvf-list-' + part + ' input[type=checkbox]').forEach(function(c){ c.checked = false; });
  var drop = document.getElementById('jvf-drop-' + part);
  if(drop) drop.classList.remove('open');
  var param = part === 'rec' ? 'recvendors' : 'amortvendors';
  var onDone = part === 'amort' ? function(){ switchInner('t3', 1); } : undefined;
  refreshPane(2, param + '=', onDone);
}

/* §3.1: a line's flag selector changed (Prepayment / Not Prepayment / Pending). */
function tab1ToggleLine(selectEl){
  var row = selectEl.closest('tr');
  if(!row) return;
  var lineId = row.getAttribute('data-line-id');
  var flag = selectEl.value;   // 'Prepayment' | 'NotPrepayment' | 'Pending'
  var noteEl = row.querySelector('.line-note');
  var note = noteEl ? noteEl.value : null;
  tab1Post('flag', { DeliveryLineId: parseInt(lineId,10), PrepaymentFlag: flag, Note: note });
}

function tab1SaveDraft(){
  var rows = document.querySelectorAll('[data-line-id]');
  var pending = rows.length;
  if(pending === 0) return;
  rows.forEach(function(row){
    var lineId = row.getAttribute('data-line-id');
    var sel = row.querySelector('.line-flag');
    var noteEl = row.querySelector('.line-note');
    tab1Post('flag', { DeliveryLineId: parseInt(lineId,10), PrepaymentFlag: sel ? sel.value : 'Pending', Note: noteEl ? noteEl.value : null },
      function(){ if(--pending===0) showConfirmModal('success','Draft Saved','All line classifications have been saved as draft.'); });
  });
}

/* ── Modal helpers ───────────────────────────────────────────────────────────
   showConfirmModal  — result message (OK button only)   type: error|success|info
   showConfirmDialog — action confirmation (Cancel + Confirm buttons)
   Both: X button top-right, backdrop click, entrance animation.
────────────────────────────────────────────────────────────────────────── */
(function(){
  if(document.getElementById('ppm-modal-style')) return;
  var s = document.createElement('style');
  s.id = 'ppm-modal-style';
  s.textContent = '@keyframes modalIn{from{opacity:0;transform:scale(.94)}to{opacity:1;transform:scale(1)}}';
  document.head.appendChild(s);
})();

function _ppmClose(overlay){ if(overlay && overlay.parentNode) overlay.parentNode.removeChild(overlay); }

function _ppmXBtn(overlay){
  var b = document.createElement('button');
  b.innerHTML = '&times;';
  b.style.cssText = 'position:absolute;top:12px;right:16px;background:none;border:none;font-size:24px;color:#94a3b8;cursor:pointer;line-height:1;padding:2px 6px;border-radius:4px';
  b.onmouseover = function(){ b.style.color='#475569'; };
  b.onmouseout  = function(){ b.style.color='#94a3b8'; };
  b.onclick = function(){ _ppmClose(overlay); };
  return b;
}

function _ppmOverlay(){
  var o = document.createElement('div');
  o.style.cssText = 'position:fixed;inset:0;background:rgba(15,23,42,0.45);z-index:99999;display:flex;align-items:center;justify-content:center;padding:16px';
  return o;
}

function _ppmBox(){
  var b = document.createElement('div');
  b.style.cssText = 'position:relative;background:#fff;border-radius:16px;padding:36px 32px 28px;max-width:440px;width:100%;box-shadow:0 24px 64px rgba(0,0,0,0.18);text-align:center;animation:modalIn .18s ease';
  return b;
}

function showConfirmModal(type, title, message, onClose){
  var cfg = {
    error:   { iconBg:'#fee2e2', iconColor:'#dc2626', icon:'✕', btnColor:'#dc2626' },
    success: { iconBg:'#dcfce7', iconColor:'#16a34a', icon:'✓', btnColor:'#16a34a' },
    info:    { iconBg:'#dbeafe', iconColor:'#2563eb', icon:'ℹ', btnColor:'#2563eb' }
  };
  var c = cfg[type] || cfg.info;
  var overlay = _ppmOverlay();
  var box = _ppmBox();
  overlay.onclick = function(e){ if(e.target===overlay) _ppmClose(overlay); };
  var iconEl = document.createElement('div');
  iconEl.style.cssText = 'width:60px;height:60px;border-radius:50%;background:'+c.iconBg+';color:'+c.iconColor+';font-size:28px;display:flex;align-items:center;justify-content:center;margin:0 auto 18px;font-weight:700';
  iconEl.textContent = c.icon;
  var titleEl = document.createElement('div');
  titleEl.style.cssText = 'font-size:18px;font-weight:700;color:#0f172a;margin-bottom:10px';
  titleEl.textContent = title;
  var msgEl = document.createElement('div');
  msgEl.style.cssText = 'font-size:14px;color:#64748b;line-height:1.65;margin-bottom:28px';
  msgEl.textContent = message;
  var btn = document.createElement('button');
  btn.style.cssText = 'padding:10px 40px;background:'+c.btnColor+';color:#fff;border:none;border-radius:8px;font-size:14px;font-weight:600;cursor:pointer;transition:opacity .15s';
  btn.textContent = 'OK';
  btn.onmouseover = function(){ btn.style.opacity='.85'; };
  btn.onmouseout  = function(){ btn.style.opacity='1'; };
  btn.onclick = function(){ _ppmClose(overlay); if(typeof onClose==='function') onClose(); };
  box.appendChild(_ppmXBtn(overlay)); box.appendChild(iconEl); box.appendChild(titleEl); box.appendChild(msgEl); box.appendChild(btn);
  overlay.appendChild(box);
  document.body.appendChild(overlay);
  btn.focus();
}

function showConfirmDialog(title, message, confirmLabel, onConfirm){
  var overlay = _ppmOverlay();
  var box = _ppmBox();
  overlay.onclick = function(e){ if(e.target===overlay) _ppmClose(overlay); };
  var iconEl = document.createElement('div');
  iconEl.style.cssText = 'width:60px;height:60px;border-radius:50%;background:#fef9c3;color:#ca8a04;font-size:30px;display:flex;align-items:center;justify-content:center;margin:0 auto 18px';
  iconEl.textContent = '⚡';
  var titleEl = document.createElement('div');
  titleEl.style.cssText = 'font-size:18px;font-weight:700;color:#0f172a;margin-bottom:10px';
  titleEl.textContent = title;
  var msgEl = document.createElement('div');
  msgEl.style.cssText = 'font-size:14px;color:#64748b;line-height:1.65;margin-bottom:28px';
  msgEl.textContent = message;
  var btnRow = document.createElement('div');
  btnRow.style.cssText = 'display:flex;gap:12px;justify-content:center';
  var cancelBtn = document.createElement('button');
  cancelBtn.style.cssText = 'padding:10px 28px;background:#f1f5f9;color:#475569;border:1px solid #e2e8f0;border-radius:8px;font-size:14px;font-weight:600;cursor:pointer';
  cancelBtn.textContent = 'Cancel';
  cancelBtn.onclick = function(){ _ppmClose(overlay); };
  var okBtn = document.createElement('button');
  okBtn.style.cssText = 'padding:10px 28px;background:#16a34a;color:#fff;border:none;border-radius:8px;font-size:14px;font-weight:600;cursor:pointer;transition:opacity .15s';
  okBtn.textContent = confirmLabel || 'Confirm';
  okBtn.onmouseover = function(){ okBtn.style.opacity='.85'; };
  okBtn.onmouseout  = function(){ okBtn.style.opacity='1'; };
  okBtn.onclick = function(){ _ppmClose(overlay); if(typeof onConfirm==='function') onConfirm(); };
  btnRow.appendChild(cancelBtn); btnRow.appendChild(okBtn);
  box.appendChild(_ppmXBtn(overlay)); box.appendChild(iconEl); box.appendChild(titleEl); box.appendChild(msgEl); box.appendChild(btnRow);
  overlay.appendChild(box);
  document.body.appendChild(overlay);
  okBtn.focus();
}

function tab1Confirm(poId){
  tab1Post('confirm', { PoId: parseInt(poId,10) }, function(res){
    var d = res.data || {};
    if(d.status === 'Blocked'){
      showConfirmModal('error', 'Action Required',
        'Cannot confirm yet: ' + d.pending + ' line(s) are still awaiting a decision. '
        + 'Classify every line as "Prepayment" or "Not a prepayment" first.');
      return;
    }
    if(d.status === 'NothingFlagged'){
      showConfirmModal('info', 'Nothing to Move',
        'No lines on this PO are flagged as a prepayment, so there is nothing to move.');
      return;
    }
    // status === 'Ok' — jump to the Amortisation tab after the user clicks OK.
    showConfirmModal('success', 'Confirmed',
      d.invoicesLinked + ' invoice(s) on the flagged line(s) are now on the Prepayment & Amortisation tab.',
      function(){ switchTab(1); });
  });
}

/* Shared AJAX POST helper (JSON in/out, sends Windows credentials). */
function ppmPost(url, payload, onok){
  fetch(url, {
    method:'POST',
    headers:{'Content-Type':'application/json'},
    credentials:'include',
    body: JSON.stringify(payload || {})
  }).then(function(r){
    // Always try to parse the body — the handler puts the real error message in the JSON
    // even on 4xx/5xx, so we want to read it rather than discarding it.
    return r.json().then(function(body){ return { ok: r.ok, body: body }; })
             .catch(function(){ return { ok: r.ok, body: null }; });
  }).then(function(result){
    var res = result.body;
    if(result.ok && res && res.ok){ if(onok) onok(res); return; }
    var msg = (res && res.error) ? res.error : (result.ok ? 'unknown error' : 'Server error');
    showConfirmModal('error','Action Failed', msg);
  }).catch(function(err){ showConfirmModal('error','Network Error', 'Could not reach the server. ' + err); });
}

/* ─────────────────────────────────────────────────────────────────────────
   Tab 2 — Prepayment & Amortisation Setup
───────────────────────────────────────────────────────────────────────── */
var AMORT_HANDLER = 'Services/AmortisationSetupHandler.ashx';

function amortSelectInvoice(invoiceId){
  switchTab(1, 'invoice=' + encodeURIComponent(invoiceId));
}

/* "Existing Prepayment Balance Invoices" grid action: route by the invoice's status.
     invoice  -> select it here on Tab 2 (View schedule / Open)
     journals -> open that PO's journals on Tab 3 (View journals) */
function amortOpenExisting(invoiceId, poNumber, target){
  if(target === 'journals') switchTab(2, 'openpo=' + encodeURIComponent(poNumber));
  else amortSelectInvoice(invoiceId);
}

function amortCollect(){
  var v = function(id){ var e=document.getElementById(id); return e ? e.value.trim() : ''; };
  var chk = function(id){ var e=document.getElementById(id); return !!(e && e.checked); };
  return {
    InvoiceId: parseInt(v('setup-invoice-id'),10),
    AssetClassification: chk('cc-current') ? 'Current' : 'Non-current',
    ExpenditureType: chk('cap-capital') ? 'Capital' : 'Operational',
    AmortisationType: chk('atype-oneoff') ? 'OneOff' : 'Scheduled',
    StartDate: v('setup-start') || null,
    EndDate: v('setup-end') || null,
    Periods: parseInt(v('setup-periods'),10) || null,
    Frequency: 'Monthly',
    PrepaymentGlId: parseInt(v('setup-prepay-gl'),10) || null,
    ExpenseGlAccount: v('setup-expense-gl'),
    CostCentreWbs: v('setup-wbs'),
    CompanyCode: v('setup-company')
  };
}

function amortSaveDraft(){
  ppmPost(AMORT_HANDLER + '?action=savedraft', amortCollect(), function(){
    showConfirmModal('success','Draft Saved','Amortisation setup has been saved as draft.');
  });
}

function amortGenerate(){
  showConfirmDialog(
    'Generate Schedule',
    'This will generate the amortisation schedule and create journal entries based on the current setup. Do you want to continue?',
    'Generate',
    function(){
      ppmPost(AMORT_HANDLER + '?action=generate', amortCollect(), function(){
        showConfirmModal('success', 'Schedule Generated',
          'Amortisation schedule and journals have been created successfully.',
          function(){ switchTab(2); });
      });
    }
  );
}

function amortSavePeriods(invoiceId){
  var rows = document.querySelectorAll('.sched-table tbody tr[data-pid]');
  var periods = [];
  for (var i = 0; i < rows.length; i++){
    var pid = parseInt(rows[i].getAttribute('data-pid'), 10);
    var inp = rows[i].querySelector('.pa-input');
    if (!inp || !pid) continue;
    var amt = parseFloat(inp.value.replace(/,/g,''));
    if (isNaN(amt)) { showConfirmModal('error','Invalid Amount','Row ' + (i+1) + ' has an invalid amount. Please enter a number.'); return; }
    periods.push({ Id: pid, Amount: amt });
  }
  if (periods.length === 0){ showConfirmModal('info','Nothing to Save','No editable periods were found.'); return; }
  ppmPost(AMORT_HANDLER + '?action=saveperiods', { InvoiceId: invoiceId, Periods: periods }, function(){
    showConfirmModal('success','Period Edits Saved','Period amounts updated. The Admin tab will reflect any schedule/invoice total mismatch.');
  });
}

/* ─────────────────────────────────────────────────────────────────────────
   Tab 3 — Journal Generation (approval workflow)
───────────────────────────────────────────────────────────────────────── */
var JOURNAL_HANDLER = 'Services/JournalHandler.ashx';

function journalSelect(journalId){
  switchTab(2, 'journal=' + encodeURIComponent(journalId));
}

function journalComments(){
  var e = document.getElementById('approver-comments');
  return e ? e.value.trim() : '';
}

/* Refresh the Journal tab in place, keeping the selected journal in view. */
function journalReload(journalId){
  refreshPane(2, 'journal=' + encodeURIComponent(journalId));
}

function journalSubmit(journalId){
  ppmPost(JOURNAL_HANDLER + '?action=submit', { JournalId: parseInt(journalId,10) }, function(){
    showConfirmModal('success','Submitted','Journal has been submitted for approval.',
      function(){ journalReload(journalId); });
  });
}

function journalApprove(journalId){
  ppmPost(JOURNAL_HANDLER + '?action=approve', { JournalId: parseInt(journalId,10), Comments: journalComments() }, function(){
    showConfirmModal('success', 'Journal Approved', 'The journal has been approved successfully.',
      function(){ journalReload(journalId); });
  });
}

function journalReject(journalId){
  ppmPost(JOURNAL_HANDLER + '?action=reject', { JournalId: parseInt(journalId,10), Comments: journalComments() }, function(){
    showConfirmModal('error', 'Journal Rejected', 'The journal has been rejected.',
      function(){ journalReload(journalId); });
  });
}

function journalExport(journalId){
  ppmPost(JOURNAL_HANDLER + '?action=export', { JournalId: parseInt(journalId,10) }, function(res){
    var n = res.data ? res.data.exported : 0;
    showConfirmModal('success','Exported',n + ' journal(s) exported to the ERP batch.',
      function(){ journalReload(journalId); });
  });
}

function journalApproveAll(journalType){
  ppmPost(JOURNAL_HANDLER + '?action=approveall', { ReasonCode: journalType || '' }, function(res){
    var n = res.data ? res.data.approved : 0;
    showConfirmModal('success','All Approved',n + ' journal(s) approved successfully.',
      function(){ refreshPane(2); });
  });
}

/* ─────────────────────────────────────────────────────────────────────────
   Tab 4 — Admin Control Tower
───────────────────────────────────────────────────────────────────────── */
function adminExportTracker(){
  window.location = 'Services/AdminExportHandler.ashx';
}

/* §3.4 Admin actions: force-advance a stuck item, reassign approver, clear exception,
   re-export failed batch. Each POSTs to AdminActionHandler then refreshes pane 3. */
var ADMIN_PANE = 3;
var ADMIN_ACTION = 'Services/AdminActionHandler.ashx';
function adminVal(id){ var e=document.getElementById(id); return e ? (e.value||'').trim() : ''; }

function adminForceAdvance(){
  var po = adminVal('adm-stuck');
  if(!po){ showConfirmModal('info','Select Item','Please select a stuck item to advance.'); return; }
  ppmPost(ADMIN_ACTION + '?action=forceadvance', { PoNumber: po }, function(res){
    showConfirmModal('success','Advanced',(res.data ? res.data.affected : 0) + ' journal(s) force-advanced for ' + po + '.',
      function(){ refreshPane(ADMIN_PANE); });
  });
}

function adminReassignApprover(){
  var po = adminVal('adm-stuck');
  var approver = parseInt(adminVal('adm-approver'), 10) || 0;
  if(!po){ showConfirmModal('info','Select Item','Please select the PO from the stuck-item dropdown to reassign.'); return; }
  if(!approver){ showConfirmModal('info','Select Approver','Please select an approver.'); return; }
  ppmPost(ADMIN_ACTION + '?action=reassign', { PoNumber: po, ApproverUserId: approver }, function(res){
    showConfirmModal('success','Approver Reassigned','Reassigned approver on ' + (res.data ? res.data.affected : 0) + ' journal(s).',
      function(){ refreshPane(ADMIN_PANE); });
  });
}

function adminClearException(){
  var id = parseInt(adminVal('adm-exception'), 10) || 0;
  if(!id){ showConfirmModal('info','Select Exception','Please select an exception to clear.'); return; }
  var note = adminVal('adm-exc-note');
  ppmPost(ADMIN_ACTION + '?action=clear', { ExceptionId: id, Note: note }, function(){
    showConfirmModal('success','Exception Cleared','The exception has been cleared successfully.',
      function(){ refreshPane(ADMIN_PANE); });
  });
}

function adminReExportBatch(){
  var id = parseInt(adminVal('adm-batch'), 10) || 0;
  if(!id){ showConfirmModal('info','Select Batch','Please select a failed batch to re-export.'); return; }
  ppmPost(ADMIN_ACTION + '?action=reexport', { BatchId: id }, function(){
    showConfirmModal('success','Batch Re-armed','The batch has been re-armed for export.',
      function(){ refreshPane(ADMIN_PANE); });
  });
}

/* ─────────────────────────────────────────────────────────────────────────
   Tab 5 — Group Workflow Control (§3.5)
   Filter by status + search by group name/preparer re-render pane 4 with
   ?status=&q=. "View detail" drills down to the relevant tab for the group.
   Reassign / Bulk reassign POST to the GroupWorkflowHandler write-back endpoint.
───────────────────────────────────────────────────────────────────────── */
var GROUP_PANE = 4;
var GROUP_HANDLER = 'Services/GroupWorkflowHandler.ashx';

function gwSel(id){ var e=document.getElementById(id); return e ? (e.value||'').trim() : ''; }

/* Build the query string from the three data-driven dropdowns: group name, preparer, status. */
function gwFilterQuery(){
  var qs=[];
  var g=gwSel('gwGroupName'), p=gwSel('gwPreparer'), su=gwSel('gwStatus');
  if(g)  qs.push('group='+encodeURIComponent(g));
  if(p)  qs.push('preparer='+encodeURIComponent(p));
  if(su) qs.push('status='+encodeURIComponent(su));
  return qs.join('&');
}

/* Apply the filter — AJAX re-render of the Group Workflow pane (server re-queries). */
function gwApplyFilter(){
  refreshPane(GROUP_PANE, gwFilterQuery());
}

/* Clear the filter/search and re-render the full list. */
function gwClearFilter(){
  refreshPane(GROUP_PANE);
}

/* Export the (filtered) grid to CSV — honours the active status + search filter. */
function gwExportCsv(){
  var q = gwFilterQuery();
  window.location = 'Services/GroupWorkflowExportHandler.ashx' + (q ? ('?' + q) : '');
}

/* "View detail" drill-down: route to the tab that owns this group's current step. */
var GW_TARGET_TAB = { po:0, amortisation:1, journals:2, admin:3 };
function gwViewDetail(groupCode, target){
  var idx = GW_TARGET_TAB[target];
  if(idx === undefined) idx = 0;
  // Tabs 1–3 accept ?group= to filter to this delivery group; Admin (3) just opens.
  if(idx === 3) switchTab(3);
  else switchTab(idx, 'group=' + encodeURIComponent(groupCode));
}

/* Row selection helpers for bulk reassign. */
function gwToggleAll(master){
  document.querySelectorAll('.gw-row-chk').forEach(function(c){ c.checked = master.checked; });
}
function gwSelectedGroups(){
  var codes=[];
  document.querySelectorAll('tr[data-group]').forEach(function(tr){
    var c = tr.querySelector('.gw-row-chk');
    if(c && c.checked) codes.push(tr.getAttribute('data-group'));
  });
  return codes;
}

/* ── Reassign modal ──────────────────────────────────────────────────────── */
var GW_REASSIGN_GROUPS = [];

function gwPopulateUsers(){
  var prep = document.getElementById('gw-modal-preparer');
  var appr = document.getElementById('gw-modal-approver');
  if(!prep || !appr || prep.getAttribute('data-loaded') === '1'){ return Promise.resolve(); }
  return fetch(GROUP_HANDLER + '?action=users', { credentials:'include', cache:'no-store' })
    .then(function(r){ return r.json(); })
    .then(function(res){
      if(!res || !res.ok) return;
      (res.data || []).forEach(function(u){
        var label = u.displayName + (u.roleName ? (' — ' + u.roleName) : '');
        prep.appendChild(new Option(label, u.id));
        appr.appendChild(new Option(label, u.id));
      });
      prep.setAttribute('data-loaded','1');
    });
}

function gwOpenReassign(groups, title){
  GW_REASSIGN_GROUPS = groups;
  document.getElementById('gw-modal-title').textContent = title;
  document.getElementById('gw-modal-groups').textContent = 'Groups: ' + groups.join(', ');
  document.getElementById('gw-modal-preparer').value = '';
  document.getElementById('gw-modal-approver').value = '';
  gwPopulateUsers().then(function(){
    document.getElementById('gw-reassign-modal').style.display = 'flex';
  });
}

function gwReassign(groupCode){
  gwOpenReassign([groupCode], 'Reassign ' + groupCode);
}

/* §3.5 "Send reminder" — record a workflow reminder for the group's preparer. */
function gwSendReminder(groupCode){
  ppmPost(GROUP_HANDLER + '?action=reminder&group=' + encodeURIComponent(groupCode), {}, function(){
    showConfirmModal('success','Reminder Sent','A workflow reminder has been sent for ' + groupCode + '.',
      function(){ refreshPane(GROUP_PANE, gwFilterQuery()); });
  });
}

/* §3.5 "Escalate" — raise an Admin exception for the group (surfaces on the Admin tab). */
function gwEscalate(groupCode){
  showConfirmDialog('Escalate ' + groupCode + '?',
    'This will raise an Admin exception for the group and surface it on the Admin Control Tower.',
    'Escalate',
    function(){
      ppmPost(GROUP_HANDLER + '?action=escalate&group=' + encodeURIComponent(groupCode), {}, function(){
        showConfirmModal('success','Escalated', groupCode + ' has been escalated and raised on the Admin Control Tower.',
          function(){ refreshPane(GROUP_PANE, gwFilterQuery()); });
      });
    });
}

function gwBulkReassign(){
  var groups = gwSelectedGroups();
  if(groups.length === 0){ showConfirmModal('info','No Groups Selected','Please select one or more groups using the checkboxes to bulk reassign.'); return; }
  gwOpenReassign(groups, 'Bulk reassign — ' + groups.length + ' group(s)');
}

function gwCloseReassign(){
  document.getElementById('gw-reassign-modal').style.display = 'none';
}

function gwConfirmReassign(){
  var prep = parseInt(gwSel('gw-modal-preparer'),10) || 0;
  var appr = parseInt(gwSel('gw-modal-approver'),10) || 0;
  if(prep === 0 && appr === 0){ showConfirmModal('info','Selection Required','Please pick a new preparer or approver before confirming.'); return; }
  ppmPost(GROUP_HANDLER + '?action=reassign',
    { Groups: GW_REASSIGN_GROUPS, PreparerUserId: prep, ApproverUserId: appr },
    function(res){
      var n = res.data ? res.data.updated : 0;
      showConfirmModal('success','Reassigned', n + ' group(s) have been reassigned successfully.',
        function(){ gwCloseReassign(); refreshPane(GROUP_PANE, gwFilterQuery()); });
    });
}

/* ─────────────────────────────────────────────────────────────────────────
   Tab 6 — GL Balance Reconciliation (§3.6)
   Upload a SAP GL CSV → server parses + reconciles vs live FINHUB balances.
   Period + "variances only" re-render pane 5 with ?period=&variancesOnly=.
   Investigate selects a row; Mark explained / Raise adjustment POST a resolution.
───────────────────────────────────────────────────────────────────────── */
var RECON_PANE = 5;
var RECON_HANDLER = 'Services/GlReconciliationHandler.ashx';
var RECON_VARIANCES_ONLY = false;
var RECON_SELECTED = 0;

/* Build the ?period=&variancesOnly= query string from the current filter state. */
function reconQuery(extra){
  var qs=[];
  var pf=document.getElementById('recon-period-filter');
  var period = pf ? pf.value : '';
  if(period) qs.push('period='+encodeURIComponent(period));
  if(RECON_VARIANCES_ONLY) qs.push('variancesOnly=1');
  if(extra) qs.push(extra);
  return qs.join('&');
}

function reconApply(){ RECON_SELECTED = 0; refreshPane(RECON_PANE, reconQuery()); }

function reconToggleVariances(){
  RECON_VARIANCES_ONLY = !RECON_VARIANCES_ONLY;
  refreshPane(RECON_PANE, reconQuery());
}

function reconInvestigate(reconId){
  RECON_SELECTED = reconId;
  refreshPane(RECON_PANE, reconQuery('recon='+encodeURIComponent(reconId)), function(){ reconScrollToDetail(); });
}

/* Scroll the variance-investigation panel into view after the pane re-renders. */
function reconScrollToDetail(){
  var el = document.getElementById('recon-detail');
  if(el) el.scrollIntoView({ behavior:'smooth', block:'start' });
}

function reconExport(){
  var q = reconQuery();
  window.location = 'Services/GlReconExportHandler.ashx' + (q ? ('?' + q) : '');
}

function reconTemplate(){
  window.location = RECON_HANDLER + '?action=template';
}

/* Upload the chosen CSV + reporting period (multipart) → parse + reconcile server-side. */
function reconUpload(){
  var fileEl = document.getElementById('recon-file');
  var periodEl = document.getElementById('recon-period');
  if(!fileEl || !fileEl.files || fileEl.files.length === 0){ showConfirmModal('info','No File Selected','Please choose a CSV file to upload.'); return; }
  var period = (periodEl ? periodEl.value : '').trim();
  if(!/^\d{4}\/\d{2}$/.test(period)){ showConfirmModal('info','Invalid Period','Please enter the reporting period in YYYY/MM format (e.g. 2026/06).'); return; }

  var fd = new FormData();
  fd.append('file', fileEl.files[0]);
  fd.append('period', period);

  fetch(RECON_HANDLER + '?action=upload', { method:'POST', credentials:'include', body: fd })
    .then(function(r){ return r.json().then(function(b){ return { ok:r.ok, body:b }; }); })
    .then(function(res){
      if(res.ok && res.body && res.body.ok){
        var d = res.body.data || {};
        var pf = document.getElementById('recon-period-filter'); if(pf) pf.value = d.period;
        RECON_SELECTED = 0;
        showConfirmModal('success','Reconciliation Complete','Reconciled ' + d.rows + ' balance row(s) for ' + d.period + '.',
          function(){ refreshPane(RECON_PANE, 'period=' + encodeURIComponent(d.period)); });
      } else {
        showConfirmModal('error','Upload Failed', (res.body && res.body.error) || 'Unknown error occurred during upload.');
      }
    })
    .catch(function(err){ showConfirmModal('error','Network Error','Could not reach the server. ' + err); });
}

/* Record an investigation outcome (notes + assignee + action) on the selected row. */
function reconResolve(action){
  if(!RECON_SELECTED){ showConfirmModal('info','No Row Selected','Please open a row\'s variance detail first.'); return; }
  var noteEl = document.getElementById('recon-note');
  var assigneeEl = document.getElementById('recon-assignee');
  ppmPost(RECON_HANDLER + '?action=resolve',
    { ReconciliationId: RECON_SELECTED, Action: action,
      Note: noteEl ? noteEl.value : '',
      AssignedToUserId: assigneeEl ? (parseInt(assigneeEl.value, 10) || 0) : 0 },
    function(){
      var msg = action === 'MarkExplained' ? 'Variance has been marked as resolved.' : 'Adjustment has been raised successfully.';
      showConfirmModal('success', action === 'MarkExplained' ? 'Variance Resolved' : 'Adjustment Raised', msg,
        function(){ refreshPane(RECON_PANE, reconQuery('recon=' + encodeURIComponent(RECON_SELECTED)), function(){ reconScrollToDetail(); }); });
    });
}

/* ─────────────────────────────────────────────────────────────────────────
   Tab 7 — Prepayment Report by Group (§3.7).
   DB-backed, read-only. Filters (group / period / GL account / status) drive an
   AJAX pane refresh; clicking a row loads that group's drill-down; the export
   buttons stream CSV / Excel / PDF honouring the current filters.
───────────────────────────────────────────────────────────────────────── */
var REPORT_PANE = 6;
var REPORT_EXPORT = 'Services/PrepaymentReportExportHandler.ashx';
var REPORT_DRILL = 0;

/* Build the ?period=&group=&gl=&status= query string from the current filter state. */
function reportQuery(extra){
  var qs=[];
  var v;
  v = reportVal('rep-period'); if(v) qs.push('period='+encodeURIComponent(v));
  v = reportVal('rep-group');  if(v) qs.push('group='+encodeURIComponent(v));
  v = reportVal('rep-gl');     if(v) qs.push('gl='+encodeURIComponent(v));
  v = reportVal('rep-status'); if(v && v!=='All') qs.push('status='+encodeURIComponent(v));
  if(extra) qs.push(extra);
  return qs.join('&');
}
function reportVal(id){ var e=document.getElementById(id); return e ? (e.value||'').trim() : ''; }

/* Run report / change a filter → reload the grid (drops the current drill-down selection). */
function reportApply(){ REPORT_DRILL = 0; refreshPane(REPORT_PANE, reportQuery()); }

/* Click a report row → load that group's drill-down (schedule + balance movement). */
function reportDrill(groupId){
  REPORT_DRILL = groupId;
  refreshPane(REPORT_PANE, reportQuery('drill='+encodeURIComponent(groupId)), function(){ reportScrollToDrill(); });
}

function reportScrollToDrill(){
  var el = document.getElementById('report-drill');
  if(el) el.scrollIntoView({ behavior:'smooth', block:'start' });
}

/* Export the report honouring the active filters. format: 'csv' | 'excel' | 'pdf'.
   PDF opens a print-optimised page in a new tab (browser "Save as PDF"); the others download. */
function reportExport(format){
  var q = reportQuery('format='+encodeURIComponent(format||'csv'));
  var url = REPORT_EXPORT + '?' + q;
  if(format === 'pdf') window.open(url, '_blank');
  else window.location = url;
}


/* ════════════════════════════════════════════════════════════════════════
   Tab 8 — Import Data: upload the .xlsx workbook and full-replace the data.
════════════════════════════════════════════════════════════════════════ */
var IMPORT_HANDLER = 'Services/ImportHandler.ashx';

function importRun(){
  var fileEl = document.getElementById('import-file');
  if(!fileEl || !fileEl.files || fileEl.files.length === 0){
    showConfirmModal('info','No File Selected','Please choose the Excel workbook (.xlsx) to import.');
    return;
  }
  var file = fileEl.files[0];
  if(!/\.xlsx$/i.test(file.name)){
    showConfirmModal('info','Wrong File Type','Please select the .xlsx workbook (e.g. Prepayment Dashboard_2026.xlsx).');
    return;
  }

  var busy = document.getElementById('import-busy');
  var banner = document.getElementById('import-banner');
  if(busy) busy.style.display = 'block';
  if(banner) banner.style.display = 'none';

  var fd = new FormData();
  fd.append('file', file);

  fetch(IMPORT_HANDLER + '?action=import', { method:'POST', credentials:'include', body: fd })
    .then(function(r){ return r.json().then(function(b){ return { ok:r.ok, body:b }; }); })
    .then(function(res){
      if(busy) busy.style.display = 'none';
      if(res.ok && res.body && res.body.ok){
        importRenderResult(res.body.data || {});
        showConfirmModal('success','Import Complete',
          'Loaded the workbook into FINHUB. All tabs now reflect the imported data.');
      } else {
        var msg = (res.body && res.body.error) || 'Unknown error occurred during import.';
        if(banner){ banner.style.display='block'; banner.style.background='var(--err-bg)'; banner.style.color='var(--error)'; banner.textContent = '✗ ' + msg; }
        showConfirmModal('error','Import Failed', msg);
      }
    })
    .catch(function(err){
      if(busy) busy.style.display = 'none';
      showConfirmModal('error','Network Error','Could not reach the server. ' + err);
    });
}

/* Render the loaded-record-count summary table from the import result. */
function importRenderResult(d){
  var rows = [
    ['Users (preparers / approvers)', d.Users],
    ['Prepayment GL accounts', d.Gls],
    ['Managers', d.Managers],
    ['Vendors', d.Vendors],
    ['Delivery groups', d.Groups],
    ['Purchase orders', d.PurchaseOrders],
    ['PO delivery lines', d.Lines],
    ['Invoices', d.Invoices],
    ['Generated journals', d.Journals],
    ['GL balance records', d.GlBalances],
    ['Reconciliation rows', d.ReconRows]
  ];
  var html = '';
  for(var i=0;i<rows.length;i++){
    html += '<tr><td>' + rows[i][0] + '</td><td class="num"><strong>' + (rows[i][1] != null ? rows[i][1] : 0) + '</strong></td></tr>';
  }
  var body = document.getElementById('import-counts'); if(body) body.innerHTML = html;
  var lbl = document.getElementById('import-file-label'); if(lbl) lbl.textContent = d.FileName || 'workbook';
  var wrap = document.getElementById('import-result'); if(wrap) wrap.style.display = 'block';
  var banner = document.getElementById('import-banner');
  if(banner){ banner.style.display='block'; banner.style.background='var(--suc-bg)'; banner.style.color='var(--success)';
    banner.textContent = '✓ Imported ' + (d.PurchaseOrders||0) + ' POs, ' + (d.Invoices||0) + ' invoices, ' + (d.Journals||0) + ' journals.'; }
}
