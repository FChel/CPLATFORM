"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { spawn } = require("node:child_process");
const readline = require("node:readline");

const [, , executable, hostPath, playwrightPath, evidence, timestamp] = process.argv;
const { chromium } = require(playwrightPath);
const root = path.resolve(__dirname, "../../..");
const edge = path.join(process.env["ProgramFiles(x86)"], "Microsoft/Edge/Application/msedge.exe");
const origin = "http://norm-manual-fixture.test";

async function run() {
  const host = spawn(executable, [hostPath], { windowsHide: true });
  host.stderr.pipe(process.stderr);
  const replies = readline.createInterface({ input: host.stdout });
  const waiting = replies[Symbol.asyncIterator]();
  let dispatch = Promise.resolve();
  let browser;
  try {
    browser = await chromium.launch({ executablePath: edge, headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1050 } });
    let identity = "Manual tester";
    await context.route("**/*", async route => {
      const request = route.request();
      const url = new URL(request.url());
      if (url.origin !== origin) return route.abort();
      if (/^\/(css|js)\/[a-z0-9.-]+$/i.test(url.pathname)) {
        return route.fulfill({ path: path.join(root, url.pathname.slice(1)) });
      }
      if (!/^\/NORM_(ManualEntry|Statements)\.aspx$/i.test(url.pathname)) return route.fulfill({ status: 404 });
      const headers = await request.allHeaders();
      const call = {
        Page: url.pathname.slice(1), Query: url.search.slice(1), Method: request.method(),
        Cookie: headers.cookie || "", Form: request.postData() || "", Identity: identity
      };
      if (call.Method === "POST") assert.match(new URLSearchParams(call.Form).get("manualContext") || "", /^[a-f0-9]{64}$/);
      const response = dispatch.then(async () => {
        host.stdin.write(JSON.stringify(call) + "\n");
        const result = await waiting.next();
        if (result.done) throw new Error("Web Forms fixture host exited.");
        const data = JSON.parse(result.value);
        if (data.Error) throw new Error(data.Error);
        return data;
      });
      dispatch = response.catch(() => {});
      try {
        const data = await response;
        const outgoing = Object.fromEntries(Object.entries(data.Headers).filter(([key]) =>
          ["set-cookie", "location", "cache-control"].includes(key.toLowerCase())));
        outgoing["content-type"] = "text/html; charset=utf-8";
        await route.fulfill({ status: data.Status, headers: outgoing, body: data.Body || "" });
      } catch (error) {
        await route.fulfill({ status: 500, body: String(error) });
      }
    });

    const page = await context.newPage();
    page.setDefaultTimeout(12000);
    const pagePath = (runId, schedule) => "/NORM_ManualEntry.aspx?run=" + runId + "&schedule=" + schedule;
    const amount = (key, prior = false) => page.locator(`input[name="amount_${prior ? "prior" : "current"}_${key}"]`);
    const source = (key, prior = false) => page.locator(`input[name="source_${prior ? "prior" : "current"}_${key}"]`);
    const noteRow = title => page.locator("table.norm-note-table tbody tr").filter({ has: page.getByRole("rowheader", { name: title, exact: false }) });
    const status = () => page.locator("p[role=status]").innerText();
    async function open(runId, schedule, extra = "") {
      const response = await page.goto(origin + pagePath(runId, schedule) + extra);
      assert.equal(response.status(), 200, await page.locator("body").innerText());
      await page.getByRole("heading", { name: "Manual Entry", exact: true }).waitFor();
    }
    async function fill(values, prior = false) {
      for (const [key, value] of Object.entries(values)) {
        await amount(key, prior).fill(String(value));
        await source(key, prior).fill("NIHRACS reference model fixture");
      }
    }
    async function save(expected = /Schedule saved/) {
      assert.match(await page.locator("input[name=manualContext]").inputValue(), /^[a-f0-9]{64}$/);
      const submitted = page.waitForResponse(response => response.request().method() === "POST");
      await page.getByRole("button", { name: "Save schedule and record evidence" }).click();
      const response = await submitted;
      assert.ok([200, 302].includes(response.status()), await page.locator("body").innerText());
      if (response.status() === 302) {
        const target = new URL((await response.allHeaders()).location, response.url()).href;
        await page.waitForURL(target, { waitUntil: "domcontentloaded" }).catch(async error => {
          throw new Error("Redirect from " + response.url() + " to " + target + " reached " + page.url() + ": " +
            await page.locator("body").innerText(), { cause: error });
        });
      }
      try {
        await page.locator("p[role=status]").filter({ hasText: expected }).waitFor();
      } catch (error) {
        throw new Error("Save notice at " + page.url() + " after HTTP " + response.status() + ": " +
          await page.locator("body").innerText(), { cause: error });
      }
    }
    async function rowValues(title) {
      return (await noteRow(title).locator("td").allInnerTexts()).map(value => value.replace(/,/g, "").trim());
    }
    async function capture(name, locator) {
      await locator.screenshot({ path: path.join(evidence, `NORM_Stage3_I4_${name}_${timestamp}.png`) });
    }
    function pass(message) { console.log("PASS " + message); }

    await open(1, "KMP");
    await fill({ STB: 240, PEB: 27, OTH: 8, NMB: 11 });
    await save();
    await page.getByLabel("Schedule", { exact: true }).selectOption({ value: pagePath(1, "MATURITY").slice(1) });
    await page.waitForURL("**/*schedule=MATURITY");
    await fill({ PRV_NON: 41, LEA_NON: 70 });
    await save();
    await open(2, "KMP");
    assert.equal(await amount("STB").inputValue(), "");
    assert.equal(await amount("STB", true).inputValue(), "240");
    assert.match(await page.locator("body").innerText(), /FY2026.*Run #2/);
    assert.match(await page.locator("body").innerText(), /A\$'000 · Whole numbers/);
    await amount("STB").fill("268");
    await save(/Supply a source reference/);
    await amount("STB").fill("268.5");
    await save(/whole number/);
    assert.equal(await amount("STB").inputValue(), "268.5");
    await fill({ STB: 268, PEB: 28, OTH: 12, NMB: 5 });
    await save();
    const kmpEvidence = (await status()).match(/#(\d+)/)[1];
    assert.deepEqual(await rowValues("Total KMP remuneration"), ["313", "286"]);
    assert.equal(await amount("TOTAL").count(), 0);
    await page.reload();
    assert.equal(await amount("STB").inputValue(), "268");
    await capture("KMP_Preparation", page.locator("main"));
    pass("KMP uses annual values in configured whole A$'000, requires sources and keeps calculated totals read-only");

    await open(2, "MATURITY");
    assert.equal(await amount("PRV_TOTAL").count(), 0);
    assert.equal(await amount("PRV_CUR").count(), 0);
    assert.match(await page.locator("body").innerText(), /FY2026 incomplete/);
    await fill({ PRV_NON: 0 });
    await save();
    await page.reload();
    assert.equal(await amount("PRV_NON").inputValue(), "0");
    assert.equal(await amount("LEA_NON").inputValue(), "");
    await fill({ PRV_NON: 53, LEA_NON: 34 });
    await page.locator("#manualReason").fill("Replace zero test value with fixture input");
    await save();
    assert.deepEqual(await rowValues("Employee provisions — current"), ["1345", "1194"]);
    assert.deepEqual(await rowValues("Lease liabilities — current"), ["36", "35"]);
    assert.match(await page.locator("body").innerText(), /Exact balance 1398\.426\. Presented at 1398/);
    assert.match(await page.locator("body").innerText(), /FY2026 reconciled · FY2025 reconciled/);
    await capture("Classification_Preparation", page.locator("main"));
    pass("liability splits reproduce both periods, preserve zero and blank, and reconcile to the presented Approach A totals");

    const draft = page.waitForResponse(response => response.url().includes("/NORM_Statements.aspx"));
    await page.getByRole("link", { name: "Draft financial statements" }).click();
    assert.equal((await draft).status(), 200, await page.locator("body").innerText());
    await page.locator("#note-5-1").waitFor();
    assert.equal(await page.locator("#note-5-1 table").count(), 1);
    assert.match(await page.locator("#note-5-1").innerText(), /1,345/);
    assert.equal(await page.locator("#note-3-2 table").count(), 1);
    assert.match(await page.locator("#note-3-2").innerText(), /313/);
    assert.equal(await page.evaluate(() => window.NORM_DATA.validations.some(item => item.result === "Fail" &&
      item.code === "FIXTURE_OPEN")), true);
    assert.equal(await page.locator("#excelExportLink").isVisible(), false);
    await capture("Classification_Draft_Note", page.locator("#note-5-1"));
    await capture("KMP_Draft_Note", page.locator("#note-3-2"));
    pass("the hosted draft Notes show both destinations once and preserve the independent failed assurance result");

    await open(4, "MATURITY");
    assert.equal(await amount("PRV_NON").inputValue(), "53");
    assert.equal(await amount("LEA_NON").inputValue(), "34");
    assert.match(await page.locator("body").innerText(), /1327\.508\. Presented at 1328/);
    assert.match(await page.locator("body").innerText(), /Exact balance 33\.4\. Presented at 34/);
    assert.deepEqual(await rowValues("Lease liabilities — current"), ["0", "35"]);
    assert.match(await page.locator("body").innerText(), /FY2026 reconciled/);
    pass("lease split 34 non-current, 0 current, Pass; replacement preserves inputs and shows the exact-to-presented difference");
    for (const [entered, current, result] of [
      ["35", -1, "failed"], ["33", 1, "reconciled"], ["0", 34, "reconciled"],
      ["-1", 35, "failed"], ["", null, "incomplete"]
    ]) {
      await amount("LEA_NON").fill(entered);
      await page.locator("#manualReason").fill("Check the presented lease split");
      await save();
      const saved = await amount("LEA_NON").inputValue();
      assert.equal(saved, entered);
      const [total] = await rowValues("Lease liabilities — linked total");
      assert.match(total, /^34(?:\s|$)/);
      assert.equal(saved === "" ? null : Number(total.split(/\s/)[0]) - Number(saved), current);
      const [displayed] = await rowValues("Lease liabilities — current");
      if (result === "reconciled") assert.equal(displayed, String(current));
      else assert.match(displayed, result === "failed" ? /^—\s*Invalid classification/ : /^—\s*Calculation awaits its inputs/);
      assert.match(await page.locator("body").innerText(), new RegExp("FY2026 " + result));
      console.log("Checked lease split " + (entered || "blank") + " non-current, " +
        (current === null ? "incomplete" : current) + " current, " + result);
    }
    await open(5, "MATURITY");
    assert.equal(await amount("PRV_NON").inputValue(), "");
    assert.equal(await amount("PRV_NON", true).inputValue(), "53");
    assert.match(await page.locator("body").innerText(), /Linked reporting balance is missing/);
    pass("rollover inherits prior values with blank current inputs and identifies a missing linked result");

    await open(2, "KMP");
    const token = page.locator("input[name=manualContext]");
    const stale = await token.inputValue();
    await fill({ STB: 241 }, true);
    await save(/Record a change reason/);
    await page.locator("#manualReason").fill("Prior payroll correction");
    await save();
    assert.deepEqual(await rowValues("Total KMP remuneration"), ["313", "287"]);
    assert.match(await page.locator("body").innerText(), /Original FY2025 revision/);
    await token.evaluate((input, priorToken) => input.setAttribute("value", priorToken), stale);
    assert.equal(await token.inputValue(), stale);
    await save(/schedule or its source changed/);
    await open(2, "KMP", "&evidence=" + kmpEvidence);
    assert.deepEqual(await rowValues("Total KMP remuneration"), ["313", "286"]);
    assert.equal(await page.getByRole("button", { name: "Save schedule and record evidence" }).count(), 0);
    await open(1, "KMP");
    assert.equal(await amount("STB").inputValue(), "240");
    await open(5, "KMP");
    assert.equal(await amount("STB", true).inputValue(), "268");
    pass("comparative reasons and immutable evidence preserve the original inputs and reject a stale form");

    await open(2, "KMP");
    await fill({ STB: 1234 });
    await page.locator("#manualReason").fill("Denied fixture");
    identity = "Outside tester";
    const denied = page.waitForResponse(response => response.request().method() === "POST");
    await page.getByRole("button", { name: "Save schedule and record evidence" }).click();
    assert.equal((await denied).status(), 403);
    const deniedRead = await page.goto(origin + pagePath(2, "KMP"));
    assert.equal(deniedRead.status(), 403);
    identity = "Unregistered tester";
    assert.equal((await page.goto(origin + pagePath(2, "KMP"))).status(), 403);
    identity = "Manual tester";
    await open(2, "KMP");
    assert.equal(await amount("STB").inputValue(), "268");
    pass("unauthorised GET and a real cross-entity form POST cannot read or change annual data");
  } finally {
    if (browser) await browser.close();
    host.stdin.end();
    host.kill();
  }
}

run().catch(error => { console.error(error); process.exitCode = 1; });
