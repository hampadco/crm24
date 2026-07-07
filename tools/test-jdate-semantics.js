const fs = require('fs');
const path = require('path');
const vm = require('vm');

const base = path.join(__dirname, '..', 'src', 'Crm.Web', 'wwwroot', 'panel-assets', 'vendor', 'libs');
const sandbox = { console };
sandbox.window = sandbox;
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(path.join(base, 'jdate', 'jdate.js'), 'utf8'), sandbox);

const JDate = sandbox.JDate;

// Today Gregorian 2026-07-07 => Jalali 1405-04-16
const j1 = new JDate(new Date(2026, 6, 7));
console.log('JDate(Date 2026-07-07): getFullYear=', j1.getFullYear(), 'getMonth=', j1.getMonth(), 'getDate=', j1.getDate());

// Constructing with (y, m, d) numbers — Jalali or Gregorian?
const j2 = new JDate(1405, 3, 16);
console.log('JDate(1405,3,16) -> internal gregorian date:', j2.getGregorianDate ? j2.getGregorianDate() : j2._d || '(unknown accessor)');
console.log('keys on JDate proto:', Object.getOwnPropertyNames(Object.getPrototypeOf(j2)).join(', '));

// String parsing
try {
    const j3 = new JDate('2026-07-07');
    console.log("JDate('2026-07-07'): y=", j3.getFullYear(), 'm=', j3.getMonth(), 'd=', j3.getDate());
} catch (e) { console.log("JDate('2026-07-07') threw:", e.message); }
try {
    const j4 = new JDate('1405-04-16');
    console.log("JDate('1405-04-16'): y=", j4.getFullYear(), 'm=', j4.getMonth(), 'd=', j4.getDate());
} catch (e) { console.log("JDate('1405-04-16') threw:", e.message); }
