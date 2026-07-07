// Simulates browser load order of the Jalali picker stack to catch load-time errors.
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const base = path.join(__dirname, '..', 'src', 'Crm.Web', 'wwwroot', 'panel-assets', 'vendor', 'libs');

const sandbox = {
    console,
    navigator: { userAgent: 'node' },
    document: {
        createElement: () => ({ style: {}, classList: { add() {}, remove() {} }, setAttribute() {}, appendChild() {} }),
        createTextNode: () => ({}),
        addEventListener() {},
        querySelectorAll: () => [],
        documentElement: { style: {} },
        body: {}
    }
};
sandbox.window = sandbox;
sandbox.self = sandbox;
sandbox.globalThis = sandbox;
vm.createContext(sandbox);

const scripts = [
    path.join(base, 'jdate', 'jdate.js'),
    path.join(base, 'flatpickr', 'flatpickr-jdate.js'),
    path.join(base, 'flatpickr', 'l10n', 'fa-jdate.js')
];

for (const s of scripts) {
    try {
        vm.runInContext(fs.readFileSync(s, 'utf8'), sandbox, { filename: path.basename(s) });
        console.log('OK loaded:', path.basename(s));
    } catch (e) {
        console.log('FAIL loading', path.basename(s), '->', e.constructor.name + ':', e.message);
    }
}

console.log('typeof window.JDate =', typeof sandbox.JDate);
console.log('typeof window.flatpickr =', typeof sandbox.flatpickr);
if (sandbox.flatpickr) {
    console.log('flatpickr keys:', Object.keys(sandbox.flatpickr).slice(0, 15).join(', '));
    console.log('has l10ns:', !!sandbox.flatpickr.l10ns, '| l10ns.fa registered:', !!(sandbox.flatpickr.l10ns && sandbox.flatpickr.l10ns.fa));
}
console.log('HTMLElement.prototype patched: n/a in node (check flatpickr fn typeof above)');
