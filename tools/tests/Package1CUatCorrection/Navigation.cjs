// Execute the actual Import page navigation script in a bounded DOM substitute.
const fs = require('fs'), vm = require('vm'), assert = require('assert');
const page = fs.readFileSync('NORM/NORM_Import.aspx', 'utf8');
const script = page.match(/<script>([\s\S]*?)<\/script>/)[1].replace('<%= ReleaseList.ClientID %>', 'ReleaseList');
const releaseEvents = {}, linkEvents = {};
const release = { value: '8', addEventListener: (name, fn) => releaseEvents[name] = fn };
const history = { href: 'https://cplatform.beeqira.com.au/NORM/NORM_SourceHistory.aspx?release=&viewStream=Administered', addEventListener: (name, fn) => linkEvents[name] = fn };
vm.runInNewContext(script, { URL, document: { getElementById: id => id === 'ReleaseList' ? release : history }, window: { location: { href: 'https://cplatform.beeqira.com.au/NORM/NORM_Import.aspx' } } });
function check(value) { const url = new URL(history.href); assert.equal(url.searchParams.get('release'), value); assert.equal(url.searchParams.get('viewStream'), 'Administered'); assert.equal(url.pathname, '/NORM/NORM_SourceHistory.aspx'); }
check('8'); console.log('PASS navigation initial current selection and stream');
release.value = '9'; releaseEvents.change(); check('9'); console.log('PASS navigation selection change without postback');
release.value = '7'; linkEvents.click(); check('7'); console.log('PASS navigation click uses latest selection');
release.value = ''; releaseEvents.change(); check(''); console.log('PASS navigation empty selection does not invent a newest release');
const css = fs.readFileSync('css/norm-import.css', 'utf8');
assert(css.includes('.norm-button:disabled:hover')); assert(css.includes('cursor: not-allowed')); assert(css.includes('box-shadow: none'));
console.log('PASS targeted disabled style covers hover/cursor/shadow');
console.log('TOTAL PASS 5 (actual navigation script; DOM substitute, no authenticated browser claim)');
