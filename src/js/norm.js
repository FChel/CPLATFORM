/* Server emits window.NORM_DATA in Statements.aspx; fall back to {} for design preview. */
const NORM = (window.NORM_DATA||{});

const $ = s => document.querySelector(s);
const fmt = n => (n===null||n===undefined) ? "–" : (n<0?"(":"")+Math.abs(n).toLocaleString("en-AU")+(n<0?")":"");
const STATEMENTS = [
  {id:"soci", ix:"1", name:"Statement of Comprehensive Income", org:"Department of Defence",
   sub:"For the year ended 30 June 2024", rows:NORM.soci},
  {id:"sofp", ix:"2", name:"Statement of Financial Position", org:"Department of Defence",
   sub:"As at 30 June 2024", rows:NORM.sofp},
  {id:"soce", ix:"3", name:"Statement of Changes in Equity", disabled:true},
  {id:"cash", ix:"4", name:"Cash Flow Statement", disabled:true},
];
let current = "soci";
let injected = false;

function renderNav(){
  $("#nav").innerHTML = STATEMENTS.map(s =>
    `<li data-id="${s.id}" class="${s.disabled?'disabled':''}" ${s.id===current?'aria-current="true"':''}>
       <span class="ix">${s.ix}</span>${s.name}</li>`).join("");
  $("#nav").querySelectorAll("li[data-id]").forEach(li=>{
    if(li.classList.contains("disabled")) return;
    li.onclick = ()=>{ current = li.dataset.id; renderNav(); renderDoc(); };
  });
}

function renderDoc(){
  const s = STATEMENTS.find(x=>x.id===current);
  let html = `<div class="doc-head"><div class="org">${s.org}</div><h1>${s.name}</h1><div class="sub">${s.sub}</div></div>
    <table class="stmt"><thead><tr>
      <th class="lbl"></th><th>Notes</th>
      <th><span class="yr num">2024</span>$'000</th><th><span class="yr num">2023</span>$'000</th>
    </tr></thead><tbody>`;
  s.rows.forEach((r,i)=>{
    if(r.type==="section"){ html += `<tr class="sec"><td colspan="4">${r.label}</td></tr>`; return; }
    const cls = r.type==="total" ? "total" : "row";
    const stt = r.status||"unmapped";
    html += `<tr class="${cls} ${stt}">
      <td class="lbl">${r.label}</td>
      <td class="note">${r.note||""}</td>
      <td class="fig"><a class="v" href="#" data-i="${i}" tabindex="0">
          <span class="dot ${stt}"></span><span class="num">${fmt(r.computed)}</span></a></td>
      <td class="fig prior num">${fmt(r.pub23)}</td></tr>`;
  });
  html += `</tbody></table>`;
  $("#doc").innerHTML = html;
  $("#doc").querySelectorAll("a.v").forEach(a=>{
    a.onclick = e=>{ e.preventDefault(); openTrace(s.rows[+a.dataset.i]); };
    a.onkeydown = e=>{ if(e.key==="Enter"||e.key===" "){e.preventDefault();a.click();} };
  });
}

function renderValidations(){
  const items = NORM.validations.map((v,idx)=>{
    let cls="pending", ic="·";
    let pass = v.pass;
    if(idx===0 && injected){ pass=false; }            // demo break targets debits=credits
    if(pass===true){ cls="pass"; ic="✓"; }
    else if(pass===false){ cls="fail"; ic="!"; }
    let detail = v.detail;
    if(idx===0){
      detail = injected
        ? `Out of balance by <span class="n">${fmt(48250)}</span> — test entry not posted to a contra account`
        : `${v.detail}: <span class="n">${fmt(v.val)}</span>`;
    }
    return `<li class="vitem ${cls}"><div class="ic">${ic}</div>
      <div><div class="k">${v.k}</div><div class="d">${detail}</div></div></li>`;
  }).join("");
  $("#vlist").innerHTML = items;
}

function openTrace(r){
  const matched = r.status==="tied";
  const isVar = r.status==="variance"||r.status==="close";
  const rows = r.rows||[];
  let tie = "";
  if(r.pub24!==undefined){
    const cls = matched?"match":(isVar?"var":"");
    const diff = r.diff||0;
    tie = `<div class="tie">
      <div class="box"><div class="l">Computed $'000</div><div class="v">${fmt(r.computed)}</div></div>
      <div class="box ${cls}"><div class="l">Published 2024</div><div class="v">${fmt(r.pub24)}</div></div>
      <div class="box ${cls}"><div class="l">${matched?"Reconciled":"Variance"}</div>
        <div class="v">${matched?"✓ $0":fmt(diff)}</div></div></div>`;
  }
  const rule = r.rule || "No mapping rule assigned yet";
  const ledger = rows.length ? rows.map(x=>
     `<tr><td class="src">${x.cc}</td><td class="gl">${x.gl}</td>
          <td class="desc">${x.text}</td><td class="amt num">${fmt(x.amt)}</td></tr>`).join("")
     : `<tr><td colspan="4" class="desc">No trial-balance accounts are mapped to this line yet. Assign a mapping rule to populate it.</td></tr>`;
  $("#drawer").innerHTML = `
    <div class="dh">
      <button class="x" id="dx" aria-label="Close">×</button>
      <div class="eyebrow"><span class="dot ${r.status||'unmapped'}"></span> ${current==="soci"?"Comprehensive income":"Financial position"} · Note ${r.note||"—"}</div>
      <h2>${r.label}</h2>
      <div class="rule-chip">
        <svg width="14" height="14" viewBox="0 0 14 14" fill="none"><path d="M2 7h10M7 2v10" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>
        Mapping rule · FY2024 v0.1 · ${rule}</div>
      ${tie}
    </div>
    <div class="db">
      <div class="led-head"><span class="t">Contributing trial-balance accounts</span>
        <span class="c">${rows.length} account${rows.length===1?"":"s"}</span></div>
      <table class="led"><tbody>${ledger}</tbody></table>
      <div class="note-line">Source: ${NORM.meta.source} · ${NORM.meta.basis}. Ledger column shows the originating ROMAN company code.</div>
    </div>`;
  $("#dx").onclick = closeTrace;
  $("#scrim").classList.add("show");
  $("#drawer").classList.add("show");
  $("#drawer").setAttribute("aria-hidden","false");
}
function openPool(){
  openTrace({label:"Unmapped accounts", note:"—", status:"unmapped",
    rule:"Awaiting mapping rule — these accounts are not yet classified to a statement line",
    rows:NORM.unmapped.rows});
}
function closeTrace(){
  $("#scrim").classList.remove("show");
  $("#drawer").classList.remove("show");
  $("#drawer").setAttribute("aria-hidden","true");
}

function init(){
  $("#ctxEntity").textContent = "Departmental";
  $("#ctxVer").textContent = "map "+NORM.meta.mapVersion.split(" ")[0]+" "+NORM.meta.mapVersion.split(" ")[1];
  $("#covPct").textContent = NORM.meta.coverage+"%";
  $("#covBar").style.width = NORM.meta.coverage+"%";
  $("#poolBtn").innerHTML = `<b>${NORM.unmapped.n} unmapped accounts</b>Net $'000 ${fmt(NORM.unmapped.net)} · awaiting mapping rules — click to review`;
  $("#poolBtn").onclick = openPool;
  $("#scrim").onclick = closeTrace;
  document.addEventListener("keydown", e=>{ if(e.key==="Escape") closeTrace(); });
  $("#breakBtn").onclick = ()=>{
    injected = !injected;
    $("#breakBtn").textContent = injected ? "Clear the test break" : "Inject a test break";
    $("#breakBtn").classList.toggle("armed", injected);
    renderValidations();
  };
  renderNav(); renderDoc(); renderValidations();
}
init();
