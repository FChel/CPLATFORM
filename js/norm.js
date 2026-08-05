(function () {
  "use strict";

  var data = window.NORM_DATA || {};
  var statements = data.statements || [];
  var activeCode = statements.length ? statements[0].code : null;
  var lastFocus = null;

  function byId(id) { return document.getElementById(id); }
  function esc(value) {
    return String(value === null || value === undefined ? "" : value)
      .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
  }
  function number(value) {
    if (value === null || value === undefined || value === "") { return "–"; }
    var n = Number(value);
    var text = Math.abs(Math.round(n)).toLocaleString("en-AU");
    return n < 0 ? "(" + text + ")" : text;
  }
  function decimal(value, places) {
    if (value === null || value === undefined) { return "–"; }
    return Number(value).toLocaleString("en-AU", { minimumFractionDigits: places, maximumFractionDigits: places });
  }
  function pad2(value) { return Number(value) < 10 ? "0" + Number(value) : String(value); }
  function statusClass(value) { return String(value || "Mapped").toLowerCase(); }

  function initialise() {
    if (!data.meta || !statements.length) {
      byId("statementDocument").innerHTML = '<div class="norm-empty-state"><span class="norm-kicker">No completed run</span><h1>Import a trial balance to generate statements.</h1><p>The read-only statement view appears after the first successful calculation.</p></div>';
      byId("unmappedButton").hidden = true;
      return;
    }
    byId("metaRelease").textContent = "FY" + data.meta.yearCurrent + " · " + data.meta.release;
    byId("metaRun").textContent = "#" + data.meta.runId;
    byId("metaFingerprint").textContent = "Fingerprint " + String(data.meta.fingerprint || "").slice(0, 12);
    byId("testBanner").hidden = !data.meta.isTestBreak;
    byId("reviewPackLink").href = "NORM_ReviewPack.ashx?run=" + encodeURIComponent(data.meta.runId);
    byId("reviewPackLink").hidden = false;
    byId("wordExportLink").href = "NORM_WordExport.ashx?run=" + encodeURIComponent(data.meta.runId);
    byId("wordExportLink").hidden = false;
    byId("printStatements").addEventListener("click", function () { window.print(); });
    renderProfile();
    renderNavigation();
    renderStatement();
    renderValidations();
    renderReadiness();
    renderUnmapped();
    bindDrawer();
  }

  function renderProfile() {
    var profile = data.profile || {};
    var labels = { NCE: "Non-corporate entity", CCE: "Corporate entity", COMMONWEALTH_COMPANY: "Commonwealth company", GPFS: "General purpose", SPFS: "Special purpose", FULL: "Full disclosure", REDUCED: "Reduced disclosure" };
    var requirements = profile.requirements || [];
    byId("profileSummary").innerHTML = '<div><strong>' + esc(labels[profile.entityType] || profile.entityType || "Not configured") + '</strong>' +
      '<small>' + esc(labels[profile.reportingBasis] || profile.reportingBasis || "Reporting basis") + ' · ' + esc(labels[profile.disclosureTier] || profile.disclosureTier || "Disclosure set") + '</small></div>' +
      '<div class="norm-profile-tags">' + requirements.slice(0, 5).map(function (item) { return '<span>' + esc(item.label) + '</span>'; }).join("") +
      (requirements.length > 5 ? '<span>+' + (requirements.length - 5) + ' more</span>' : '') + '</div>' +
      (data.meta.canPrepare ? '<a href="' + esc(data.meta.reportingUrl) + '">Edit reporting profile →</a>' : '');
  }

  function renderNavigation() {
    byId("statementNav").innerHTML = statements.map(function (statement, index) {
      var selected = statement.code === activeCode;
      return '<button type="button" data-code="' + esc(statement.code) + '" class="' + (selected ? "active" : "") + '" aria-pressed="' + selected + '">' +
        '<span>' + (index + 1) + '</span><strong>' + esc(statement.title) + '</strong></button>';
    }).join("");
    Array.prototype.forEach.call(byId("statementNav").querySelectorAll("button"), function (button) {
      button.addEventListener("click", function () {
        activeCode = button.getAttribute("data-code");
        renderNavigation();
        renderStatement();
      });
    });
  }

  function currentStatement() {
    for (var i = 0; i < statements.length; i++) { if (statements[i].code === activeCode) { return statements[i]; } }
    return statements[0];
  }

  function renderSourceEvidence() {
    var files = data.sourceFiles || [];
    if (!files.length) { return ""; }
    return '<section class="norm-source-evidence" aria-label="Retained source evidence"><div><span>Retained source evidence</span><small>Original files · SHA-256 verified</small></div><div>' +
      files.map(function (file) {
        var periods = file.periodStart === null || file.periodStart === undefined ? "" :
          " · P" + pad2(file.periodStart) + "–" + pad2(file.periodEnd);
        var label = esc(file.type) + periods;
        if (file.downloadUrl) {
          return '<a class="norm-evidence-chip" href="' + esc(file.downloadUrl) + '" title="' + esc(file.file) + ' · ' + esc(file.hash) + '">' +
            '<strong>' + label + '</strong><small>Download original ↗</small></a>';
        }
        return '<span class="norm-evidence-chip norm-evidence-readonly" title="' + esc(file.file) + ' · ' + esc(file.hash) + '">' +
          '<strong>' + label + '</strong><small>Fingerprint retained</small></span>';
      }).join("") + '</div></section>';
  }

  function renderStatement() {
    var statement = currentStatement();
    if (statement.layout === "notes") { renderNotes(statement); return; }
    var currentYear = data.meta.yearCurrent;
    var priorYear = data.meta.yearPrior;
    var rows = (statement.rows || []).map(function (row, index) {
      if (row.type === "section") {
        return '<tr class="norm-section-row"><th colspan="4">' + esc(row.label) + '</th></tr>';
      }
      var sourceCount = (row.sources || []).length;
      var status = statusClass(row.status);
      var clickable = row.clickable && row.resultId;
      var amount = clickable
        ? '<button type="button" class="norm-figure" data-row="' + index + '"><span class="norm-status ' + status + '"></span><span>' + number(row.computed) + '</span></button>'
        : '<span class="norm-figure-static">' + number(row.computed) + '</span>';
      return '<tr class="norm-financial-row ' + esc(row.type) + '"><th scope="row">' + esc(row.label) +
        (sourceCount ? '<small>' + sourceCount + ' source account' + (sourceCount === 1 ? '' : 's') + '</small>' : '') +
        '</th><td class="norm-note">' + esc(row.note || "") + '</td><td class="norm-amount">' + amount +
        '</td><td class="norm-amount norm-prior">' + number(row.prior) + '</td></tr>';
    }).join("");

    byId("statementDocument").innerHTML = '<header class="norm-document-head"><span class="norm-kicker">' + esc(data.meta.entity) + '</span>' +
      '<h1>' + esc(statement.title) + '</h1><p>' + (statement.code === "SOFP" ? 'As at' : 'For the year ended') + ' 30 June ' + currentYear + '</p>' +
      '<div class="norm-document-meta"><span>Source set: ' + esc(data.meta.file) + '</span><span>Configuration: ' + esc(data.meta.release) + '</span><span>Run #' + esc(data.meta.runId) + '</span></div>' +
      renderSourceEvidence() + '</header>' +
      '<div class="norm-table-scroll"><table class="norm-financial-table"><thead><tr><th></th><th>Notes</th><th><b>' + currentYear + '</b>$\'000</th><th><b>' + priorYear + '</b>$\'000</th></tr></thead><tbody>' + rows + '</tbody></table></div>' +
      '<footer class="norm-document-foot">The statement should be read with the accompanying notes. Select any current-year amount to inspect its frozen derivation.</footer>';

    Array.prototype.forEach.call(byId("statementDocument").querySelectorAll("button[data-row]"), function (button) {
      button.addEventListener("click", function () {
        var rowIndex = Number(button.getAttribute("data-row"));
        openTrace(statement.rows[rowIndex], button);
      });
    });
  }

  function renderNotes(statement) {
    var disclosures = statement.disclosures || data.disclosures || [];
    var required = disclosures.filter(function (item) { return item.required && item.note; });
    var sections = [];
    var bySection = {};
    required.forEach(function (item) {
      if (!bySection[item.section]) { bySection[item.section] = []; sections.push(item.section); }
      bySection[item.section].push(item);
    });
    var notes = sections.map(function (section) {
      return '<section class="norm-note-section"><header><span>' + esc(bySection[section][0].sectionCode) + '</span><h2>' + esc(section) + '</h2></header>' +
        bySection[section].map(function (item) {
          var rows = (item.lines || []).map(function (line) {
            return '<tr><th>' + esc(line.label) + '<small>' + Number(line.sourceCount || 0).toLocaleString("en-AU") + ' source rows</small></th><td>' + number(line.amount) + '</td></tr>';
          }).join("");
          var table = rows ? '<table class="norm-note-table"><thead><tr><th>' + esc(item.note || "") + ': ' + esc(item.title) + '</th><th>' + esc(data.meta.yearCurrent) + '<small>$\'000</small></th></tr></thead><tbody>' + rows +
            '<tr class="total"><th>Total ' + esc(item.title.toLowerCase()) + '</th><td>' + number(item.amount) + '</td></tr></tbody></table>' :
            '<div class="norm-note-empty"><span>No mapped balance</span><p>The disclosure remains in the set because the entity profile requires it. Add entity narrative or mapping before sign-off.</p></div>';
          var narrative = item.narrative ? '<div class="norm-accounting-policy"><span>Accounting policy / entity commentary</span><p>' + esc(item.narrative).replace(/\n/g, "<br>") + '</p></div>' : '';
          return '<article class="norm-note-card"><div class="norm-note-card-head"><div><span>Note ' + esc(item.note || "") + '</span><h3>' + esc(item.title) + '</h3></div><em class="' + statusClass(item.status) + '">' + esc(item.status) + '</em></div>' +
            '<p class="norm-note-guidance">' + esc(item.guidance) + '</p>' + table + narrative + '</article>';
        }).join("") + '</section>';
    }).join("");
    var notApplicable = disclosures.filter(function (item) { return !item.required; });
    var na = notApplicable.length ? '<details class="norm-na-register"><summary>' + notApplicable.length + ' disclosures assessed as not applicable</summary><div>' +
      notApplicable.map(function (item) { return '<span><b>' + esc(item.note || item.sectionCode) + '</b>' + esc(item.title) + '<small>Trigger: ' + esc(item.trigger) + '</small></span>'; }).join("") + '</div></details>' : '';
    byId("statementDocument").innerHTML = '<header class="norm-document-head norm-notes-head"><span class="norm-kicker">' + esc(data.meta.entity) + '</span><h1>' + esc(statement.title) + '</h1>' +
      '<p>For the year ended 30 June ' + esc(data.meta.yearCurrent) + '</p><div class="norm-document-meta"><span>PRIMA-aligned conditional set</span><span>' + required.length + ' required note disclosures</span><span>Run #' + esc(data.meta.runId) + '</span></div>' +
      (data.meta.canPrepare ? '<a class="norm-button norm-button-small" href="' + esc(data.meta.reportingUrl) + '#policies">Edit policy wording and workflow</a>' : '') + '</header>' + notes + na +
      '<footer class="norm-document-foot">Note tables are generated from frozen trial-balance lineage. Policy wording is run-specific working content and must be reviewed for the entity before approval.</footer>';
  }

  function renderValidations() {
    var validations = data.validations || [];
    var passed = validations.filter(function (item) { return item.result === "Pass"; }).length;
    byId("validationScore").textContent = passed + " / " + validations.length + " pass";
    byId("validationList").innerHTML = validations.map(function (item) {
      var state = String(item.result || "Warning").toLowerCase();
      var icon = item.result === "Pass" ? "✓" : (item.result === "Fail" ? "!" : "·");
      return '<article class="norm-validation ' + state + '"><span class="norm-validation-icon">' + icon + '</span><div><strong>' +
        esc(item.label) + '</strong><p>' + esc(item.detail) + '</p><small>' + esc(item.severity) + '</small></div></article>';
    }).join("");

    var coverage = 0;
    validations.forEach(function (item) { if (item.code === "MAPPING_VALUE_COVERAGE") { coverage = Number(item.actual || 0); } });
    byId("coverageValue").textContent = decimal(coverage, 1) + "%";
    byId("coverageBar").style.width = Math.max(0, Math.min(100, coverage)) + "%";

    var comparison = { tied: 0, close: 0, variance: 0, total: 0 };
    statements.forEach(function (statement) {
      (statement.rows || []).forEach(function (row) {
        if (row.published === null || row.published === undefined) { return; }
        comparison.total++;
        var state = statusClass(row.status);
        if (state === "tied") { comparison.tied++; }
        else if (state === "close") { comparison.close++; }
        else if (state === "variance") { comparison.variance++; }
      });
    });
    byId("comparisonSummary").hidden = comparison.total === 0;
    byId("comparisonSummary").innerHTML = '<span class="norm-kicker">FY' + esc(data.meta.yearCurrent) + ' audited comparison</span><div>' +
      '<article class="tied"><strong>' + comparison.tied + '</strong><small>Tied</small></article>' +
      '<article class="close"><strong>' + comparison.close + '</strong><small>Within 1%</small></article>' +
      '<article class="variance"><strong>' + comparison.variance + '</strong><small>Review</small></article></div>';

    var disclosures = data.disclosures || [];
    var requiredDisclosures = disclosures.filter(function (item) { return item.required; });
    var completedDisclosures = requiredDisclosures.filter(function (item) { return item.status !== "Needs input"; });
    byId("disclosureProgress").innerHTML = requiredDisclosures.length ? '<span class="norm-kicker">PRIMA disclosure readiness</span><div><strong>' + completedDisclosures.length + ' / ' + requiredDisclosures.length + '</strong><span>generated or drafted</span></div>' +
      '<div class="norm-meter"><i style="width:' + Math.round(100 * completedDisclosures.length / requiredDisclosures.length) + '%"></i></div>' +
      (data.meta.canPrepare ? '<a href="' + esc(data.meta.reportingUrl) + '#disclosure-register">Open disclosure register →</a>' : '') : '';
  }

  function renderReadiness() {
    var validations = data.validations || [];
    var blocking = validations.filter(function (item) { return item.severity === "Blocking" && item.result === "Fail"; });
    var warnings = validations.filter(function (item) { return item.result === "Warning"; });
    var box = byId("runReadiness");
    var state = blocking.length ? "fail" : (warnings.length ? "warning" : "ready");
    var heading = blocking.length ? "Not ready for accounting sign-off" : (warnings.length ? "Ready for accounting review" : "All configured controls passed");
    var detail = blocking.length
      ? blocking.length + " blocking check" + (blocking.length === 1 ? "" : "s") + " must be cleared."
      : (warnings.length ? warnings.length + " review item" + (warnings.length === 1 ? "" : "s") + " remains visible in assurance." : "This run has no failed or warning controls.");
    box.className = "norm-readiness " + state;
    box.innerHTML = '<span class="norm-status ' + state + '"></span><div><strong>' + esc(heading) + '</strong><p>' + esc(detail) + '</p></div>';
    box.hidden = false;
  }

  function renderUnmapped() {
    var sources = (data.unmapped && data.unmapped.sources) || [];
    var button = byId("unmappedButton");
    button.innerHTML = '<span class="norm-status ' + (sources.length ? 'warning' : 'pass') + '"></span><span><strong>' +
      sources.length.toLocaleString("en-AU") + ' unmapped rows</strong><small>Net $\'000 ' + number(data.unmapped ? data.unmapped.amount : 0) + '</small></span>';
    button.addEventListener("click", function () {
      openTrace({ label: "Unmapped source rows", note: "–", computed: data.unmapped.amount,
        published: null, variance: null, status: "Unmapped", sources: sources }, button);
    });
  }

  function noteGroups(sources) {
    var groups = {};
    sources.forEach(function (source) {
      var key = source.note || "Unclassified";
      if (!groups[key]) { groups[key] = { label: key, amount: 0, count: 0 }; }
      groups[key].amount += Number(source.amount || 0);
      groups[key].count++;
    });
    return Object.keys(groups).map(function (key) { return groups[key]; })
      .sort(function (a, b) { return Math.abs(b.amount) - Math.abs(a.amount); });
  }

  function openTrace(row, trigger) {
    lastFocus = trigger || null;
    var sources = row.sources || [];
    var notes = noteGroups(sources);
    var comparison = row.published === null || row.published === undefined ? "" :
      '<div class="norm-trace-comparison"><article><span>Computed</span><strong>' + number(row.computed) + '</strong></article>' +
      '<article><span>Audited FY' + esc(data.meta.yearCurrent) + '</span><strong>' + number(row.published) + '</strong></article>' +
      '<article class="' + statusClass(row.status) + '"><span>Variance</span><strong>' + number(row.variance || 0) + '</strong></article></div>';
    var noteTable = notes.length > 1 ? '<section class="norm-trace-section"><div class="norm-trace-section-head"><h3>Note classifications</h3><span>' + notes.length + ' groups</span></div><table><tbody>' +
      notes.map(function (note) { return '<tr><td>' + esc(note.label) + '<small>' + note.count + ' source rows</small></td><td>' + number(note.amount) + '</td></tr>'; }).join("") +
      '</tbody></table></section>' : "";
    var sourceRows = sources.length ? sources.map(function (source) {
      var search = [source.gl, source.ledger, source.text, source.note, source.cash, source.mapping].join(" ").toLowerCase();
      var account = source.sapUrl
        ? '<a class="norm-sap-link" href="' + esc(source.sapUrl) + '" target="_blank" rel="noopener" title="Open G/L line items in SAP Fiori"><strong>' + esc(source.gl) + '</strong><span>Open in SAP ↗</span></a>'
        : '<strong>' + esc(source.gl) + '</strong>';
      return '<tr data-source-row data-search="' + esc(search) + '" class="' + (source.synthetic ? 'synthetic' : '') + '"><td>' + account + '<small>' + esc(source.ledger) + ' · source row ' + esc(source.row) + '</small></td>' +
        '<td>' + esc(source.text) + (source.cash ? '<span class="norm-tag">' + esc(source.cash) + '</span>' : '') +
        (source.mapping ? '<small class="norm-mapping-rule">' + esc(source.mapping) + '</small>' : '') +
        '</td><td>' + number(source.sourceAmount) + '</td><td>' + number(source.amount) + '</td></tr>';
    }).join("") : '<tr><td colspan="4">No contributing source rows were recorded for this line.</td></tr>';

    var searchBox = sources.length > 8 ? '<label class="norm-trace-search"><span>Find a source account</span><input id="traceSearch" type="search" placeholder="G/L, description, note or cash-flow class" autocomplete="off"><small id="traceSearchCount">' + sources.length + ' rows</small></label>' : '';

    byId("traceDrawer").innerHTML = '<header class="norm-trace-head"><button type="button" id="closeTrace" aria-label="Close derivation">×</button>' +
      '<span class="norm-kicker">Frozen figure derivation · Run #' + esc(data.meta.runId) + '</span><h2>' + esc(row.label) + '</h2><p>Note ' + esc(row.note || '–') + ' · ' + sources.length + ' contributing row' + (sources.length === 1 ? '' : 's') + '</p>' + comparison + '</header>' +
      '<div class="norm-trace-body">' + noteTable + '<section class="norm-trace-section"><div class="norm-trace-section-head"><h3>Trial-balance lineage</h3><span>Presented in $\'000</span></div>' +
      searchBox + '<table class="norm-source-table"><thead><tr><th>Account</th><th>Description and classification</th><th>Source $</th><th>Contribution $\'000</th></tr></thead><tbody>' + sourceRows + '</tbody></table></section>' +
      '<p class="norm-trace-proof">Source-set SHA-256: ' + esc(data.meta.fileHash) + '<br>Input/configuration fingerprint: ' + esc(data.meta.fingerprint) +
      '<br>SAP links open live G/L line-item investigation. The frozen NORM lineage and retained originals remain the run evidence.</p></div>';
    byId("traceScrim").hidden = false;
    byId("traceDrawer").classList.add("open");
    byId("traceDrawer").setAttribute("aria-hidden", "false");
    byId("closeTrace").addEventListener("click", closeTrace);
    bindTraceSearch();
    byId("closeTrace").focus();
  }

  function bindTraceSearch() {
    var input = byId("traceSearch");
    if (!input) { return; }
    input.addEventListener("input", function () {
      var query = input.value.trim().toLowerCase();
      var visible = 0;
      Array.prototype.forEach.call(byId("traceDrawer").querySelectorAll("[data-source-row]"), function (row) {
        var match = !query || String(row.getAttribute("data-search") || "").indexOf(query) >= 0;
        row.hidden = !match;
        if (match) { visible++; }
      });
      byId("traceSearchCount").textContent = visible + " row" + (visible === 1 ? "" : "s");
    });
  }

  function closeTrace() {
    byId("traceDrawer").classList.remove("open");
    byId("traceDrawer").setAttribute("aria-hidden", "true");
    byId("traceScrim").hidden = true;
    if (lastFocus) { lastFocus.focus(); }
  }
  function bindDrawer() {
    byId("traceScrim").addEventListener("click", closeTrace);
    document.addEventListener("keydown", function (event) { if (event.key === "Escape") { closeTrace(); } });
  }

  initialise();
}());
