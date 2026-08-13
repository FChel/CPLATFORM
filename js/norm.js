(function () {
  "use strict";

  var data = window.NORM_DATA || {};
  var statements = data.statements || [];
  var activeCode = statements.length ? statements[0].code : null;
  var lastFocus = null;
  var viewMode = "preparation";

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
  function noteId(value) { return "note-" + String(value || "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, ""); }
  function noteLines(lines) {
    return (lines || []).slice().sort(function (a, b) {
      function other(value) { return /^other\b/i.test(String(value || "").trim()) || /^unclassified$/i.test(String(value || "").trim()); }
      return Number(other(a.label)) - Number(other(b.label));
    });
  }

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
    byId("excelExportLink").href = "NORM_ExcelExport.ashx?run=" + encodeURIComponent(data.meta.runId);
    byId("excelExportLink").hidden = false;
    applyRoute();
    byId("printStatements").addEventListener("click", requestPrint);
    byId("cancelPrint").addEventListener("click", closePrintReview);
    byId("confirmPrint").addEventListener("click", function () { closePrintReview(); buildPrintBook(); window.print(); });
    byId("preparationView").addEventListener("click", function () { setView("preparation"); });
    byId("publicationView").addEventListener("click", function () { setView("publication"); });
    setView("preparation");
    renderProfile();
    renderNavigation();
    renderStatement();
    renderValidations();
    renderReadiness();
    renderUnmapped();
    bindDrawer();
    window.addEventListener("hashchange", function () { applyRoute(); renderNavigation(); renderStatement(); scrollToRoutedNote(); });
    scrollToRoutedNote();
  }

  function setView(mode) {
    viewMode = mode === "publication" ? "publication" : "preparation";
    document.body.classList.toggle("norm-publication-view", viewMode === "publication");
    byId("preparationView").classList.toggle("active", viewMode === "preparation");
    byId("publicationView").classList.toggle("active", viewMode === "publication");
    renderStatement();
  }

  function applyRoute() {
    var hash = String(window.location.hash || "").replace(/^#/, "");
    if (hash.indexOf("note-") === 0) { activeCode = "NOTES"; return; }
    if (hash === "asset-movement") { activeCode = "NOTES"; window.location.hash = noteId("3.2A"); return; }
    for (var i = 0; i < statements.length; i++) {
      if (String(statements[i].code).toLowerCase() === hash.toLowerCase()) { activeCode = statements[i].code; return; }
    }
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
        window.history.replaceState(null, "", "#" + String(activeCode).toLowerCase());
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
    if (statement.layout === "assetMovement") { renderAssetMovement(statement); return; }
    var currentYear = data.meta.yearCurrent;
    var priorYear = data.meta.yearPrior;
    var rows = (statement.rows || []).map(function (row, index) {
      if (row.type === "section" || row.type === "subsection" || row.type === "major") {
        return '<tr class="norm-section-row ' + esc(row.type) + '"><th colspan="5">' + esc(row.label) + '</th></tr>';
      }
      var sourceCount = (row.sources || []).length;
      var status = statusClass(row.status);
      var clickable = row.clickable && row.resultId;
      var amount = clickable
        ? '<button type="button" class="norm-figure" data-row="' + index + '"><span class="norm-status ' + status + '"></span><span>' + number(row.computed) + '</span></button>'
        : '<span class="norm-figure-static">' + number(row.computed) + '</span>';
      var note = row.note ? '<button type="button" class="norm-note-jump" data-note="' + esc(row.note) + '" aria-label="Open note ' + esc(row.note) + '">' + esc(row.note) + '</button>' : '';
      var cashWorking = statement.code === "CASH" && (row.original !== undefined || row.adjustment !== undefined)
        ? '<small class="norm-cash-working">Original ' + number(row.original || 0) + ' · adjustments ' + number(row.adjustment || 0) + '</small>' : '';
      return '<tr class="norm-financial-row ' + esc(row.type) + '"><th scope="row">' + esc(row.label) + cashWorking +
        (sourceCount ? '<small>' + sourceCount + ' source account' + (sourceCount === 1 ? '' : 's') + '</small>' : '') +
        '</th><td class="norm-note">' + note + '</td><td class="norm-amount">' + amount +
        '</td><td class="norm-amount norm-prior">' + number(row.prior) + '</td><td class="norm-amount norm-budget">' + number(row.budget) + '</td></tr>';
    }).join("");

    byId("statementDocument").innerHTML = '<header class="norm-document-head"><span class="norm-kicker">' + esc(data.meta.entity) + '</span>' +
      '<h1>' + esc(statement.title) + '</h1><p>' + (statement.code === "SOFP" ? 'As at' : 'For the year ended') + ' 30 June ' + currentYear + '</p>' +
      '<div class="norm-document-meta"><span>Source set: ' + esc(data.meta.file) + '</span><span>Configuration: ' + esc(data.meta.release) + '</span><span>Run #' + esc(data.meta.runId) + '</span></div>' +
      renderSourceEvidence() + '</header>' +
      '<div class="norm-table-scroll"><table class="norm-financial-table"><thead><tr><th></th><th>Notes</th><th><b>' + currentYear + '</b><small>Current</small>$\'000</th><th><b>' + priorYear + '</b><small>Comparative</small>$\'000</th><th><b>Original Budget</b><small>' + currentYear + '</small>$\'000</th></tr></thead><tbody>' + rows + '</tbody></table></div>' +
      '<footer class="norm-document-foot">The above statement should be read in conjunction with the accompanying notes.' +
      (viewMode === "preparation" ? ' Select any current-year amount to inspect its frozen derivation.' : '') + '</footer>';

    Array.prototype.forEach.call(byId("statementDocument").querySelectorAll("button[data-row]"), function (button) {
      button.addEventListener("click", function () {
        var rowIndex = Number(button.getAttribute("data-row"));
        openTrace(statement.rows[rowIndex], button);
      });
    });
    bindNoteJumps();
  }

  function bindNoteJumps() {
    Array.prototype.forEach.call(byId("statementDocument").querySelectorAll("button[data-note]"), function (button) {
      button.addEventListener("click", function () { navigateToNote(button.getAttribute("data-note")); });
    });
  }

  function navigateToNote(note) {
    activeCode = "NOTES";
    window.history.replaceState(null, "", "#" + noteId(note));
    renderNavigation();
    renderStatement();
    scrollToRoutedNote();
  }

  function scrollToRoutedNote() {
    var hash = String(window.location.hash || "").replace(/^#/, "");
    if (hash.indexOf("note-") !== 0) { return; }
    var target = byId(hash);
    if (target) { window.setTimeout(function () { target.scrollIntoView({ behavior: "smooth", block: "start" }); target.focus({ preventScroll: true }); }, 30); }
  }

  function renderAssetMovement(statement) {
    var rows = (statement.rows || []).map(function (row, index) {
      var closeSources = row.closingSources || [];
      var depSources = row.depreciationSources || [];
      var closing = closeSources.length ? '<button type="button" class="norm-figure" data-asset-row="' + index + '" data-kind="closing"><span class="norm-status mapped"></span><span>' + number(row.closing) + '</span></button>' : number(row.closing);
      var depreciation = depSources.length ? '<button type="button" class="norm-figure" data-asset-row="' + index + '" data-kind="depreciation"><span class="norm-status mapped"></span><span>' + number(row.depreciation) + '</span></button>' : number(row.depreciation);
      return '<tr class="' + (row.total ? 'total' : '') + '"><th>' + esc(row.label) + '</th><td><button type="button" class="norm-note-jump" data-note="' + esc(row.note) + '">' + esc(row.note) + '</button></td>' +
        '<td>' + number(row.opening) + '</td><td>' + number(row.additions) + '</td><td>' + depreciation + '</td><td>' + number(row.revaluations) + '</td><td>' + closing + '</td></tr>';
    }).join("");
    byId("statementDocument").innerHTML = '<header class="norm-document-head"><span class="norm-kicker">' + esc(data.meta.entity) + '</span><h1>' + esc(statement.title) + '</h1>' +
      '<p>Note 3.2A working schedule · for the year ended 30 June ' + esc(data.meta.yearCurrent) + '</p><div class="norm-document-meta"><span>Closing balances and depreciation derive from frozen lineage</span><span>Run #' + esc(data.meta.runId) + '</span></div></header>' +
      '<div class="norm-input-callout"><strong>Movement schedule control</strong><p>Opening balances, additions, disposals and revaluations are controlled schedule inputs. Dashes are intentional until those inputs are loaded and validated; derived closing balances remain drillable.</p></div>' +
      '<div class="norm-table-scroll"><table class="norm-asset-table"><thead><tr><th>Asset class</th><th>Note</th><th>Opening</th><th>Additions / disposals</th><th>Depreciation / amortisation</th><th>Revaluations / other</th><th>Closing</th></tr></thead><tbody>' + rows + '</tbody></table></div>' +
      '<footer class="norm-document-foot">Presented in $\'000. The final movement table must reconcile to the asset register and Statement of Financial Position before publication.</footer>';
    Array.prototype.forEach.call(byId("statementDocument").querySelectorAll("button[data-asset-row]"), function (button) {
      button.addEventListener("click", function () {
        var row = statement.rows[Number(button.getAttribute("data-asset-row"))];
        var isClosing = button.getAttribute("data-kind") === "closing";
        openTrace({ label: row.label + (isClosing ? " closing balance" : " depreciation and amortisation"), note: row.note,
          computed: isClosing ? row.closing : row.depreciation, published: null, variance: null, status: "Mapped",
          sources: isClosing ? row.closingSources : row.depreciationSources }, button);
      });
    });
    bindNoteJumps();
  }

  function assetMovementNoteTable(interactive) {
    var rows = ((data.assetMovement || {}).rows || []).filter(function (row) { return !row.total; });
    var total = ((data.assetMovement || {}).rows || []).filter(function (row) { return row.total; })[0] || {};
    if (!rows.length) { return '<div class="norm-note-empty"><span>Asset movement schedule awaiting mapping</span><p>Complete the asset-class mapping and controlled movement inputs before sign-off.</p></div>'; }
    var movements = [
      { label: "Opening carrying amount", key: "opening" },
      { label: "Additions / disposals", key: "additions" },
      { label: "Depreciation / amortisation", key: "depreciation" },
      { label: "Revaluations / other", key: "revaluations" },
      { label: "Closing carrying amount", key: "closing", total: true }
    ];
    var head = '<tr><th>Movement</th>' + rows.map(function (row) { return '<th>' + esc(row.label) + '</th>'; }).join("") + '<th>Total</th></tr>';
    var body = movements.map(function (movement) {
      var values = rows.map(function (row, index) {
        var value = row[movement.key];
        var drillable = interactive && (movement.key === "closing" ? (row.closingSources || []).length : movement.key === "depreciation" && (row.depreciationSources || []).length);
        return '<td>' + (drillable ? '<button type="button" class="norm-figure" data-note-asset-row="' + index + '" data-kind="' + movement.key + '"><span class="norm-status mapped"></span><span>' + number(value) + '</span></button>' : number(value)) + '</td>';
      }).join("");
      return '<tr class="' + (movement.total ? 'total' : '') + '"><th>' + esc(movement.label) + '</th>' + values + '<td>' + number(total[movement.key]) + '</td></tr>';
    }).join("");
    return '<div class="norm-table-scroll"><table class="norm-asset-table norm-asset-note-table"><thead>' + head + '</thead><tbody>' + body + '</tbody></table></div>' +
      '<p class="norm-print-control">Derived closing and depreciation figures are shown. Opening balances and other movements remain controlled asset-register inputs until validated.</p>';
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
          var lines = noteLines(item.lines);
          var rows = lines.map(function (line) {
            return '<tr><th>' + esc(line.label) + '<small>' + Number(line.sourceCount || 0).toLocaleString("en-AU") + ' source rows</small></th><td>' + number(line.amount) + '</td><td>' + number(line.prior) + '</td></tr>';
          }).join("");
          var priors = lines.filter(function (line) { return line.prior !== null && line.prior !== undefined; });
          var priorTotal = priors.length ? priors.reduce(function (total, line) { return total + Number(line.prior || 0); }, 0) : null;
          var table = item.code === "N3_2A" ? assetMovementNoteTable(true) : (rows ? '<table class="norm-note-table"><thead><tr><th>' + esc(item.note || "") + ': ' + esc(item.title) + '</th><th>' + esc(data.meta.yearCurrent) + '<small>$\'000</small></th><th>' + esc(data.meta.yearPrior) + '<small>$\'000</small></th></tr></thead><tbody>' + rows +
            '<tr class="total"><th>Total ' + esc(item.title.toLowerCase()) + '</th><td>' + number(item.amount) + '</td><td>' + number(priorTotal) + '</td></tr></tbody></table>' :
            '<div class="norm-note-empty"><span>No mapped balance</span><p>The disclosure remains in the set because the entity profile requires it. Add entity narrative or mapping before sign-off.</p></div>');
          var narrative = item.narrative ? '<div class="norm-accounting-policy"><span>Accounting policy / entity commentary</span><p>' + esc(item.narrative).replace(/\n/g, "<br>") + '</p></div>' : '';
          return '<article id="' + noteId(item.note || item.code) + '" class="norm-note-card" tabindex="-1"><div class="norm-note-card-head"><div><span>Note ' + esc(item.note || "") + '</span><h3>' + esc(item.title) + '</h3></div><em class="' + statusClass(item.status) + '">' + esc(item.status) + '</em></div>' +
            table + narrative + '</article>';
        }).join("") + '</section>';
    }).join("");
    var notApplicable = disclosures.filter(function (item) { return !item.required; });
    var na = notApplicable.length ? '<details class="norm-na-register"><summary>' + notApplicable.length + ' disclosures assessed as not applicable</summary><div>' +
      notApplicable.map(function (item) { return '<span><b>' + esc(item.note || item.sectionCode) + '</b>' + esc(item.title) + '<small>Trigger: ' + esc(item.trigger) + '</small></span>'; }).join("") + '</div></details>' : '';
    byId("statementDocument").innerHTML = '<header class="norm-document-head norm-notes-head"><span class="norm-kicker">' + esc(data.meta.entity) + '</span><h1>' + esc(statement.title) + '</h1>' +
      '<p>For the year ended 30 June ' + esc(data.meta.yearCurrent) + '</p><div class="norm-document-meta"><span>PRIMA-aligned conditional set</span><span>' + required.length + ' required note disclosures</span><span>Run #' + esc(data.meta.runId) + '</span></div>' +
      (data.meta.canPrepare ? '<a class="norm-button norm-button-small" href="' + esc(data.meta.reportingUrl) + '#policies">Edit policy wording and workflow</a>' : '') + '</header>' + notes + na +
      '<footer class="norm-document-foot">Note tables are generated from frozen trial-balance lineage. Policy wording is run-specific working content and must be reviewed for the entity before approval.</footer>';
    Array.prototype.forEach.call(byId("statementDocument").querySelectorAll("button[data-note-asset-row]"), function (button) {
      button.addEventListener("click", function () {
        var row = ((data.assetMovement || {}).rows || [])[Number(button.getAttribute("data-note-asset-row"))];
        var closing = button.getAttribute("data-kind") === "closing";
        openTrace({ label: row.label + (closing ? " closing balance" : " depreciation and amortisation"), note: "3.2A",
          computed: closing ? row.closing : row.depreciation, published: null, variance: null, status: "Mapped",
          sources: closing ? row.closingSources : row.depreciationSources }, button);
      });
    });
    scrollToRoutedNote();
  }

  function publicationIssues() {
    return (data.validations || []).filter(function (item) { return item.result !== "Pass"; });
  }

  function requestPrint() {
    var issues = publicationIssues();
    if (!issues.length) { buildPrintBook(); window.print(); return; }
    var blocking = issues.filter(function (item) { return item.severity === "Blocking" || item.result === "Fail"; });
    byId("printReviewSummary").textContent = blocking.length
      ? blocking.length + " blocking or failed control(s) and " + (issues.length - blocking.length) + " review item(s) remain."
      : issues.length + " review item(s) remain. You may print a clearly marked controlled draft for review.";
    byId("printReviewIssues").innerHTML = issues.slice(0, 8).map(function (item) {
      return '<article class="' + statusClass(item.result) + '"><strong>' + esc(item.label) + '</strong><p>' + esc(item.detail) + '</p></article>';
    }).join("") + (issues.length > 8 ? '<p>+' + (issues.length - 8) + ' further items remain in the assurance panel.</p>' : '');
    byId("confirmPrint").textContent = blocking.length ? "Print controlled draft with exceptions" : "Print controlled draft";
    byId("printReview").hidden = false;
    document.body.classList.add("norm-modal-open");
    byId("cancelPrint").focus();
  }

  function closePrintReview() {
    byId("printReview").hidden = true;
    document.body.classList.remove("norm-modal-open");
    byId("printStatements").focus();
  }

  function printHeader(statement) {
    var draft = publicationIssues().length ? '<div class="norm-print-draft">CONTROLLED DRAFT · OUTSTANDING ASSURANCE ITEMS</div>' : '';
    return draft + '<header class="norm-print-head"><span>' + esc(data.meta.entity) + '</span><h1>' + esc(statement.title) + '</h1><p>' +
      (statement.code === "SOFP" ? 'As at' : 'For the year ended') + ' 30 June ' + esc(data.meta.yearCurrent) + '</p></header>';
  }

  function printStandard(statement) {
    var rows = (statement.rows || []).map(function (row) {
      if (row.type === "section" || row.type === "subsection" || row.type === "major")
        return '<tr class="norm-section-row ' + esc(row.type) + '"><th colspan="5">' + esc(row.label) + '</th></tr>';
      return '<tr class="norm-financial-row ' + esc(row.type) + '"><th>' + esc(row.label) + '</th><td>' + esc(row.note || '') + '</td><td>' + number(row.computed) + '</td><td>' + number(row.prior) + '</td><td>' + number(row.budget) + '</td></tr>';
    }).join("");
    return '<section class="norm-print-page">' + printHeader(statement) + '<table class="norm-financial-table"><thead><tr><th></th><th>Notes</th><th>' + esc(data.meta.yearCurrent) + '<small>$\'000</small></th><th>' + esc(data.meta.yearPrior) + '<small>$\'000</small></th><th>Original Budget<small>$\'000</small></th></tr></thead><tbody>' + rows + '</tbody></table></section>';
  }

  function printAsset(statement) {
    var rows = (statement.rows || []).map(function (row) {
      return '<tr class="' + (row.total ? 'total' : '') + '"><th>' + esc(row.label) + '</th><td>' + esc(row.note) + '</td><td>' + number(row.opening) + '</td><td>' + number(row.additions) + '</td><td>' + number(row.depreciation) + '</td><td>' + number(row.revaluations) + '</td><td>' + number(row.closing) + '</td></tr>';
    }).join("");
    return '<section class="norm-print-page norm-print-landscape">' + printHeader(statement) + '<p class="norm-print-control">Derived closing and depreciation columns are shown; controlled movement inputs remain blank until validated.</p><table class="norm-asset-table"><thead><tr><th>Asset class</th><th>Note</th><th>Opening</th><th>Additions / disposals</th><th>Depreciation / amortisation</th><th>Revaluations / other</th><th>Closing</th></tr></thead><tbody>' + rows + '</tbody></table></section>';
  }

  function printNotes(statement) {
    var disclosures = (statement.disclosures || []).filter(function (item) { return item.required && item.note; });
    return disclosures.map(function (item) {
      var lines = noteLines(item.lines);
      var rows = lines.map(function (line) { return '<tr><th>' + esc(line.label) + '</th><td>' + number(line.amount) + '</td><td>' + number(line.prior) + '</td></tr>'; }).join("");
      var priors = lines.filter(function (line) { return line.prior !== null && line.prior !== undefined; });
      var priorTotal = priors.length ? priors.reduce(function (total, line) { return total + Number(line.prior || 0); }, 0) : null;
      return '<section class="norm-print-page norm-print-note">' + printHeader(statement) + '<h2>Note ' + esc(item.note) + ': ' + esc(item.title) + '</h2>' +
        (item.code === "N3_2A" ? assetMovementNoteTable(false) : (rows ? '<table class="norm-note-table"><thead><tr><th>' + esc(item.title) + '</th><th>' + esc(data.meta.yearCurrent) + '<small>$\'000</small></th><th>' + esc(data.meta.yearPrior) + '<small>$\'000</small></th></tr></thead><tbody>' + rows + '<tr class="total"><th>Total</th><td>' + number(item.amount) + '</td><td>' + number(priorTotal) + '</td></tr></tbody></table>' : '<p class="norm-print-control">Required disclosure — controlled input or narrative is outstanding.</p>')) +
        (item.narrative ? '<div class="norm-accounting-policy"><strong>Accounting policy / entity commentary</strong><p>' + esc(item.narrative).replace(/\n/g, '<br>') + '</p></div>' : '') + '</section>';
    }).join("");
  }

  function buildPrintBook() {
    byId("printBook").innerHTML = statements.map(function (statement) {
      if (statement.layout === "notes") { return printNotes(statement); }
      if (statement.layout === "assetMovement") { return printAsset(statement); }
      return printStandard(statement);
    }).join("");
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
