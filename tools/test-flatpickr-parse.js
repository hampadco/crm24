const fs = require('fs');
const path = require('path');
const vm = require('vm');

const base = path.join(__dirname, '..', 'src', 'Crm.Web', 'wwwroot', 'panel-assets', 'vendor', 'libs');
const sandbox = {
    console,
    navigator: { userAgent: 'node' },
    document: {
        createElement: () => ({ style: {}, classList: { add() {}, remove() {} }, setAttribute() {}, appendChild() {} }),
        addEventListener() {}, querySelectorAll: () => [], documentElement: { style: {} }, body: {}
    }
};
sandbox.window = sandbox; sandbox.self = sandbox; sandbox.globalThis = sandbox;
vm.createContext(sandbox);

for (const s of ['jdate/jdate.js', 'flatpickr/flatpickr-jdate.js', 'flatpickr/l10n/fa-jdate.js'])
    vm.runInContext(fs.readFileSync(path.join(base, s), 'utf8'), sandbox, { filename: s });

// Also load our panel-jalali.js to grab parseAny/formatAny
const pj = path.join(__dirname, '..', 'src', 'Crm.Web', 'wwwroot', 'js', 'panel-jalali.js');
vm.runInContext(fs.readFileSync(pj, 'utf8'), sandbox, { filename: 'panel-jalali.js' });

const fp = sandbox.flatpickr;
const JDate = sandbox.JDate;

console.log('--- default fork behavior ---');
const p1 = fp.parseDate('2026-07-07', 'Y-m-d');
console.log("fork parseDate('2026-07-07','Y-m-d') => jalali y/m/d:", p1.getFullYear(), p1.getMonth() + 1, p1.getDate(), '| gregorian:', new Date(p1.getTime()).toISOString().slice(0, 10));
console.log("fork formatDate(that, 'Y-m-d'):", fp.formatDate(p1, 'Y-m-d'));

console.log('--- our bridge ---');
const p2 = sandbox.jalaliParseAny('2026-07-07');
console.log("parseAny('2026-07-07') => jalali:", p2.getFullYear() + '/' + (p2.getMonth() + 1) + '/' + p2.getDate(), '| gregorian:', new Date(p2.getTime()).toISOString().slice(0, 10));
console.log("formatAny(p2, 'Y-m-d') =>", sandbox.jalaliFormatAny(p2, 'Y-m-d'));
console.log("formatAny(p2, 'Y/m/d') =>", sandbox.jalaliFormatAny(p2, 'Y/m/d'));

const p3 = sandbox.jalaliParseAny('1405/04/16');
console.log("parseAny('1405/04/16') => gregorian:", sandbox.jalaliFormatAny(p3, 'Y-m-d'));

const p4 = sandbox.jalaliParseAny('2026-07-07T14:30');
console.log("parseAny('2026-07-07T14:30') => iso:", sandbox.jalaliFormatAny(p4, 'Y-m-d\\TH:i'), '| display:', sandbox.jalaliFormatAny(p4, 'Y/m/d H:i'));

// JDate instance passthrough (flatpickr may re-parse selected dates)
const p5 = sandbox.jalaliParseAny(p4);
console.log('parseAny(JDate instance) ok:', p5 ? sandbox.jalaliFormatAny(p5, 'Y-m-d\\TH:i') : 'FAILED');
