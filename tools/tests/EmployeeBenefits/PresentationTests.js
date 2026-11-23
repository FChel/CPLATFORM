"use strict";

const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const assert = require("node:assert/strict");
const [, , sourcePath, output, total = "4", leave = "1"] = process.argv;
const nihracs = total === "--nihracs";
const data = JSON.parse(fs.readFileSync(path.join(output, "presentation.json"), "utf8"));
const elements = new Map();
const document = {
  getElementById(id) {
    if (!elements.has(id)) {
      elements.set(id, {
        innerHTML: "",
        classList: { toggle() {} },
        querySelectorAll() { return []; }
      });
    }
    return elements.get(id);
  }
};
const window = { NORM_DATA: data, location: { hash: "" } };
let source = fs.readFileSync(sourcePath, "utf8");
const entry = "  initialise();";
assert.ok(source.includes(entry), "Statement script entry point missing");
source = source.replace(entry, `
  renderStatement();
  window.income = byId("statementDocument").innerHTML;
  renderNotes(statements[${nihracs ? 2 : 1}]);
  window.notes = byId("statementDocument").innerHTML;
  buildPrintBook();
  window.printBook = byId("printBook").innerHTML;
`);
vm.runInNewContext(source, { window, document });

function displayedAmount(html, label, column) {
  const rows = [...html.matchAll(/<tr\b[^>]*>(.*?)<\/tr>/gs)];
  const row = rows.find(match => new RegExp('<th(?:\\s[^>]*)?>' + label + '<\\/th>').test(match[1]));
  assert.ok(row, "No rendered row for " + label);
  const cells = [...row[1].matchAll(/<td\b[^>]*>(.*?)<\/td>/gs)];
  return cells[column][1].replace(/<[^>]+>/g, "").trim();
}

if (nihracs) {
  assert.equal(displayedAmount(window.income, "Employee benefits", 1), "8,291");
  assert.equal(displayedAmount(window.income, "Total expenses", 1), "28,489");
  assert.equal(displayedAmount(window.income, "Net cost of services", 1), "(24,817)");
  assert.equal(displayedAmount(window.notes, "Total employee benefits", 0), "8,291");
  assert.equal(displayedAmount(window.printBook, "Employee benefits", 1), "8,291");
  assert.equal(displayedAmount(window.printBook, "Total employee benefits", 0), "8,291");
  assert.equal(displayedAmount(window.printBook, "Term deposits", 0), "407");
  assert.equal(displayedAmount(window.printBook, "Net assets", 1), "100");
} else {
  assert.equal(displayedAmount(window.income, "Employee benefits", 1), total);
  assert.equal(displayedAmount(window.income, "Total expenses", 1), total);
  assert.equal(displayedAmount(window.income, "Net cost of services", 1), total);
  assert.equal(displayedAmount(window.notes, "Total employee benefits", 0), total);
  assert.equal(displayedAmount(window.printBook, "Employee benefits", 1), total);
  assert.equal(displayedAmount(window.printBook, "Total employee benefits", 0), total);
  for (const label of ["Wages and salaries", "Defined contribution plans", "Defined benefit plans"]) {
    assert.equal(displayedAmount(window.notes, label, 0), "1");
    assert.equal(displayedAmount(window.printBook, label, 0), "1");
  }
  assert.equal(displayedAmount(window.notes, "Leave and other entitlements", 0), leave);
  assert.equal(displayedAmount(window.printBook, "Leave and other entitlements", 0), leave);
}
const styles = fs.readFileSync(path.resolve(path.dirname(sourcePath), "../css/norm.css"), "utf8");
fs.writeFileSync(path.join(output, "print.html"),
  '<!doctype html><meta charset="utf-8"><title>Employee Benefits pilot</title><style>' + styles +
  '</style><body class="norm-page norm-statements-page"><div class="norm-print-book">' + window.printBook + '</div></body>');
console.log("PASS production JavaScript renders the same Employee benefits total and detail in web and browser print");
