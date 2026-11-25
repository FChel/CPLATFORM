"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const { spawn } = require("node:child_process");
const os = require("node:os");

const [, , evidenceFile, output, timestamp] = process.argv;
const root = path.resolve(__dirname, "../../..");
const read = name => fs.readFileSync(path.join(root, name), "utf8");
const local = name => pathToFileURL(path.join(root, name)).href;
const fixture = JSON.parse(fs.readFileSync(evidenceFile, "utf8"));
const dataJson = value => JSON.stringify(value).replace(/</g, "\\u003c");
const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "norm-narrative-browser-"));

const report = read("NORM/NORM_Reporting.aspx");
const start = report.indexOf('<section class="norm-reporting-panel norm-narrative-panel">');
const end = report.indexOf('<section class="norm-reporting-hero">', start);
assert.ok(start >= 0 && end > start, "Production editor markup could not be located");
const editor = report.slice(start, end)
  .replace(/<p>Narrative blocks are unavailable[^<]*<\/p>/, "")
  .replace(/<p>This approved configuration is read-only\.<\/p>/, "")
  .replace("<%= Server.HtmlEncode(RunLabel) %>", "NIHRACS · FY2027 · Local fixture")
  .replace("<%= NarrativeEditorData %>", dataJson(fixture))
  .replace('<%= NarrativeCanEdit ? "true" : "false" %>', "true")
  .replace(/<asp:Button\b[^>]*?Text="([^"]+)"\s+CssClass="([^"]+)"[^>]*?\/>/gs,
    (_, label, style) => '<button type="button" class="' + style + '">' + label + "</button>")
  .replace(/<%[\s\S]*?%>/g, "")
  .replace("../js/norm-narrative.js", local("js/norm-narrative.js"));
fs.writeFileSync(path.join(scratch, "editor.html"),
  '<!doctype html><html lang="en-AU"><meta charset="utf-8"><title>NORM reporting fixture</title><link rel="stylesheet" href="' +
  local("css/norm.css") + '"><body class="norm-page norm-reporting-page"><header class="norm-topbar"><div class="norm-brand">' +
  '<span class="norm-brand-mark">N</span><span><strong>NORM</strong><small>Reporting</small></span></div></header>' +
  '<main class="norm-reporting-shell"><form>' + editor + "</form></main></body></html>");

const note = {
  code: "NI_IS_EXP_SUP", sectionCode: "1", section: "Financial Performance", note: "1.1B", title: "Supplier expenses",
  required: true, status: "Needs input", completenessDetail: "Supplier expense supporting evidence remains outstanding.",
  amount: 170, priorAmount: 162, narrative: "Legacy wording is retained as plain text.",
  lines: [{ label: "Medical supplies", amount: 125, prior: 120 }, { label: "Contracted services", amount: 45, prior: 42 }]
};
const lease = {
  code: "NI_BS_LIA_IBL_LEA", sectionCode: "2", section: "Financial Position", note: "2.4A", title: "Leases", required: true,
  status: "Needs input", amount: 40, priorAmount: 42, lines: [{ label: "Lease liabilities", amount: 40, prior: 42 }]
};
const statements = {
  meta: { entity: "NIHRACS · Local fixture", yearCurrent: 2027, yearPrior: 2026, release: "v1", runId: 21, draftReview: true },
  statements: [
    { code: "SOCI", title: "Statement of Comprehensive Income", rows: [{ label: "Suppliers", type: "detail", computed: 170, prior: 162 }] },
    { code: "NOTES", title: "Notes to the financial statements", layout: "notes", disclosures: [note, lease] }
  ],
  disclosures: [note, lease], narrativeBlocks: fixture.blocks,
  validations: [{ code: "DISCLOSURE_SUPPLIERS", label: "Supplier expenses", detail: "Not Run. Verify the supporting schedule.", severity: "Disclosure review", result: "Warning" }]
};
let page = read("NORM/NORM_Statements.aspx")
  .replace("<%= NormDataJson %>", dataJson(statements))
  .replace("NORM_Reporting.aspx?run=<%= selectedRunId %>", "#")
  .replace("NORM_Mapping.aspx?release=<%= selectedReleaseId %>", "#")
  .replace("<%= Server.HtmlEncode(CurrentEnvironment) %>", "Local fixture")
  .replace(/<%[\s\S]*?%>/g, "");
page = page.replace(/\.\.\/(css|js)\/([a-z0-9.-]+)(?:\?v=[^"]*)?/gi, (_, dir, file) => local(dir + "/" + file));
fs.writeFileSync(path.join(scratch, "statements.html"), page);

const edge = path.join(process.env["ProgramFiles(x86)"], "Microsoft/Edge/Application/msedge.exe");
const profile = path.join(scratch, "profile");
const browser = spawn(edge, [
  "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
  "--remote-debugging-port=0", "--user-data-dir=" + profile, "--window-size=1400,1080", "about:blank"
], { windowsHide: true, stdio: "ignore" });
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

async function run() {
  let ports;
  for (let attempt = 0; attempt < 60; attempt++) {
    const portFile = path.join(profile, "DevToolsActivePort");
    if (fs.existsSync(portFile)) { ports = fs.readFileSync(portFile, "utf8").trim().split(/\r?\n/); break; }
    await delay(250);
  }
  assert.ok(ports, "Edge did not expose the local debugging endpoint");
  const targets = await (await fetch("http://127.0.0.1:" + ports[0] + "/json/list")).json();
  const socket = new WebSocket(targets.find(item => item.type === "page").webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.addEventListener("open", resolve); socket.addEventListener("error", reject); });
  const pending = new Map();
  let id = 0;
  socket.addEventListener("message", message => {
    const response = JSON.parse(message.data);
    const entry = pending.get(response.id);
    if (!entry) return;
    pending.delete(response.id);
    if (response.error) entry.reject(new Error(response.error.message));
    else entry.resolve(response.result);
  });
  function cdp(method, params = {}) {
    const sendId = ++id;
    return new Promise((resolve, reject) => {
      pending.set(sendId, { resolve, reject });
      socket.send(JSON.stringify({ id: sendId, method, params }));
    });
  }
  async function evaluate(expression) {
    const response = await cdp("Runtime.evaluate", { expression, returnByValue: true });
    assert.ok(!response.exceptionDetails, JSON.stringify(response.exceptionDetails));
    return response.result.value;
  }
  async function navigate(name, fragment, readiness) {
    await cdp("Page.navigate", { url: pathToFileURL(path.join(scratch, name)).href + fragment });
    for (let attempt = 0; attempt < 50; attempt++) {
      if (await evaluate(readiness)) return;
      await delay(100);
    }
    assert.fail("Browser fixture did not initialise: " + name);
  }
  async function capture(name) {
    const shot = await cdp("Page.captureScreenshot", { format: "png", captureBeyondViewport: false });
    fs.writeFileSync(path.join(output, "NORM_Stage3_I3_" + name + "_" + timestamp + ".png"), Buffer.from(shot.data, "base64"));
  }
  try {
    await navigate("editor.html", "", "typeof window.normSubmitNarrative === 'function'");
    assert.ok(await evaluate(`(() => {
      const items = Array.from(document.querySelectorAll('#narrativeList button'));
      items.find(item => item.textContent.includes('Accounting policy')).click();
      return document.querySelector('#blockEditor ul li').textContent === 'Medical supplies' &&
        document.querySelector('#blockEditor sup').textContent === '2' &&
        document.querySelector('#blockEditor sub').textContent === '2' &&
        document.getElementById('blockReview').textContent.includes('annual review required') &&
        document.getElementById('blockPlacement').value === 'After';
    })()`), "Existing formatting, placement and rollover status did not render");
    await capture("Narrative_Editor");
    assert.ok(await evaluate(`(() => {
      document.getElementById('newNarrative').click();
      const editor = document.getElementById('blockEditor');
      editor.textContent = 'Browser formatting check';
      const range = document.createRange();
      range.selectNodeContents(editor);
      const selection = window.getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
      document.dispatchEvent(new Event('selectionchange'));
      document.querySelector('[data-format=bold]').click();
      const submitted = window.normSubmitNarrative() && /<(strong|b)>Browser formatting check/.test(document.getElementById('blockHtml').value);
      document.getElementById('cancelNarrative').click();
      return submitted && editor.textContent.trim() === '';
    })()`), "Editor toolbar, submission or cancel did not behave as expected");
    await navigate("statements.html", "#notes", "!!document.querySelector('.norm-note-card')");
    assert.ok(await evaluate(`(() => {
      const note = document.getElementById('note-1-1b');
      const blocks = note.querySelectorAll('.norm-narrative-block');
      const table = note.querySelector('.norm-note-table');
      const lease = document.getElementById('note-2-4a');
      return blocks.length === 3 && !!(blocks[0].compareDocumentPosition(table) & Node.DOCUMENT_POSITION_FOLLOWING) &&
        !!(table.compareDocumentPosition(blocks[1]) & Node.DOCUMENT_POSITION_FOLLOWING) &&
        !!blocks[1].querySelector('ol') && blocks[2].textContent.includes('comparative working schedule') && note.textContent.includes('Legacy wording') &&
        note.textContent.includes('Needs input') && lease.textContent.includes('maturity profile') &&
        document.getElementById('publicationView').disabled && document.getElementById('reviewPackLink').hidden;
    })()`), "Note placement, legacy content, readiness or draft restrictions changed");
    await evaluate("document.getElementById('note-1-1b').scrollIntoView({block:'center'}); true");
    await capture("Formatted_Statement");
    console.log("PASS Edge browser editor, toolbar, cancel, roll-forward marker, and formatted before/after Notes rendering");
  } finally { socket.close(); }
}

run().catch(error => { console.error(error); process.exitCode = 1; })
  .finally(() => { browser.kill(); });
