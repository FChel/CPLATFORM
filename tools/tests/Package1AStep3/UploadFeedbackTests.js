// Execute the production Year Setup script with a deterministic DOM/clock/network boundary.
// No browser, SQL Server, network connection or third-party JavaScript dependency is used.
'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const page = fs.readFileSync(path.join(__dirname, '../../../NORM/NORM_YearSetup.aspx'), 'utf8');
const script = page.match(/<script>\s*([\s\S]*?)<\/script>/)[1]
    .replace(/<%=\s*(\w+)\.ClientID\s*%>/g, '$1');
let passed = 0;
function test(name, body) { body(); passed++; console.log('PASS ' + name); }
function setup() {
    const elements = {};
    function element(id) {
        const events = {};
        const classes = new Set();
        return elements[id] = {
            id, name: id, value: '', textContent: '', hidden: false, disabled: false,
            style: {}, files: [], events,
            classList: { add: x => classes.add(x), remove: x => classes.delete(x), contains: x => classes.has(x) },
            addEventListener: (name, callback) => { events[name] = callback; },
            setAttribute: () => {},
            querySelectorAll: () => [element(id + '-stage0'), element(id + '-stage1'), element(id + '-stage2')]
        };
    }
    for (const id of ['CurrentFinancialYear', 'priorYearPreview', 'form1', 'UploadPriorButton', 'UploadBudgetButton',
        'PriorYearFile', 'BudgetFile', 'PriorYearStartPage', 'BudgetStartPage', 'normUploadProgress',
        'normUploadProgressTitle', 'normUploadProgressPhase', 'normUploadProgressPercent',
        'normUploadProgressDetail', 'normUploadProgressTrack', 'normUploadProgressBar', 'normUploadProgressClose']) element(id);
    elements.CurrentFinancialYear.value = '2025'; // Unsaved edit must not cause a postback on validation failure.
    elements.normUploadProgress.hidden = elements.normUploadProgressClose.hidden = true;
    const body = element('body');
    let now = 0, next = 0, formDataCount = 0, rendered = '';
    const timers = new Map(), requests = [];
    function schedule(fn, delay, interval) { const id = ++next; timers.set(id, { fn, at: now + delay, interval }); return id; }
    function tick(ms) {
        const end = now + ms;
        for (;;) {
            let pending;
            for (const [id, timer] of timers) if (timer.at <= end && (!pending || timer.at < pending[1].at)) pending = [id, timer];
            if (!pending) break;
            const [id, timer] = pending; now = timer.at;
            if (timer.interval) timer.at += timer.interval; else timers.delete(id);
            timer.fn();
        }
        now = end;
    }
    function FormData() { formDataCount++; this.append = () => {}; }
    function XMLHttpRequest() {
        this.upload = {}; this.open = () => {}; this.send = () => { this.sent = true; };
        requests.push(this);
    }
    const document = { body, getElementById: id => elements[id], open: () => {}, write: html => { rendered = html; }, close: () => {} };
    const window = { FormData, XMLHttpRequest, location: { href: 'https://example.invalid/NORM/NORM_YearSetup.aspx' },
        setTimeout: (fn, delay) => schedule(fn, delay, 0), setInterval: (fn, delay) => schedule(fn, delay, delay), clearInterval: id => timers.delete(id) };
    vm.runInNewContext(script, { document, window, FormData, XMLHttpRequest });
    return { elements, body, requests, tick, rendered: () => rendered,
        submit(kind, withSubmitter = true) {
            const button = elements[kind === 'prior' ? 'UploadPriorButton' : 'UploadBudgetButton'];
            if (!withSubmitter) button.events.click();
            let prevented = false;
            elements.form1.events.submit({ submitter: withSubmitter ? button : undefined, preventDefault: () => { prevented = true; } });
            return prevented;
        },
        assertNoPost() { assert.equal(formDataCount, 0); assert.equal(requests.length, 0); assert.equal(elements.CurrentFinancialYear.value, '2025'); }
    };
}
function assertReadable(h, text) {
    assert.equal(h.elements.normUploadProgress.hidden, false);
    assert.equal(h.elements.normUploadProgressDetail.textContent, text);
    assert.equal(h.elements.normUploadProgressClose.hidden, false);
    assert.equal(h.body.classList.contains('norm-upload-busy'), true);
    h.tick(10000); // Explicitly pass the old 100-ms expiry and normal success/response delays.
    assert.equal(h.elements.normUploadProgress.hidden, false);
    assert.equal(h.elements.normUploadProgressDetail.textContent, text);
    assert.equal(h.rendered(), '');
}
for (const [kind, label, file, input] of [
    ['prior', 'Prior Year Financial Statements', 'PriorYearFile', 'PriorYearStartPage'],
    ['budget', 'Portfolio Budget Statements', 'BudgetFile', 'BudgetStartPage']
]) {
    test(kind + ' no-file error remains readable without posting unsaved year', () => {
        const h = setup(); assert.equal(h.submit(kind), true);
        assertReadable(h, 'Choose the ' + label + ' document.'); h.assertNoPost();
    });
    test(kind + ' missing PDF page remains readable without posting', () => {
        const h = setup(); h.elements[file].files = [{ name: 'approved.pdf' }]; h.submit(kind);
        assertReadable(h, 'Enter the PDF page where the financial-statement tables commence.'); h.assertNoPost();
    });
    test(kind + ' invalid PDF pages remain readable without posting', () => {
        for (const value of ['0', '-1', '10000', 'abc']) {
            const h = setup(); h.elements[file].files = [{ name: 'approved.pdf' }]; h.elements[input].value = value; h.submit(kind);
            assertReadable(h, 'Enter the PDF page where the financial-statement tables commence.'); h.assertNoPost();
        }
    });
    test(kind + ' existing Close dismisses validation and corrected upload proceeds', () => {
        const h = setup(); h.submit(kind); h.elements.normUploadProgressClose.events.click();
        assert.equal(h.elements.normUploadProgress.hidden, true);
        assert.equal(h.body.classList.contains('norm-upload-busy'), false);
        h.elements[file].files = [{ name: 'approved.pdf' }]; h.elements[input].value = '1'; h.submit(kind);
        assert.equal(h.requests.length, 1); assert.equal(h.requests[0].sent, true);
        assert.equal(h.elements.normUploadProgress.classList.contains('failed'), false);
        assert.equal(h.elements.normUploadProgressClose.hidden, true);
        assert.equal(h.elements.UploadPriorButton.disabled, true);
        assert.equal(h.elements.UploadBudgetButton.disabled, true);
    });
}
test('valid workbook retains optional-page upload and successful response behaviour', () => {
    const h = setup(); h.elements.PriorYearFile.files = [{ name: 'approved.xlsx' }]; h.submit('prior');
    const xhr = h.requests[0]; assert.equal(xhr.sent, true);
    xhr.upload.onprogress({ lengthComputable: true, loaded: 1, total: 2 });
    assert.match(h.elements.normUploadProgressPhase.textContent, /50%/);
    xhr.status = 200; xhr.responseText = '<html>existing success results</html>'; xhr.onload();
    h.tick(499); assert.equal(h.rendered(), ''); h.tick(1); assert.equal(h.rendered(), xhr.responseText);
});
test('existing network-failure notification remains readable and dismissible', () => {
    const h = setup(); h.elements.PriorYearFile.files = [{ name: 'approved.xlsx' }]; h.submit('prior');
    h.requests[0].onerror(); assertReadable(h, 'The browser lost contact with NORM. Your selected file has not been confirmed as loaded.');
    h.elements.normUploadProgressClose.events.click(); assert.equal(h.elements.normUploadProgress.hidden, true);
});
test('non-upload actions retain normal postback behaviour', () => {
    const h = setup(); let prevented = false;
    h.elements.form1.events.submit({ submitter: { name: 'SaveYearButton' }, preventDefault: () => { prevented = true; } });
    assert.equal(prevented, false); h.assertNoPost();
});
test('older submitter fallback still displays a persistent validation error', () => {
    const h = setup(); h.submit('prior', false);
    assertReadable(h, 'Choose the Prior Year Financial Statements document.'); h.assertNoPost();
});
console.log('TOTAL PASS ' + passed + ' (production JavaScript; substituted DOM/clock/network, no SQL access)');
