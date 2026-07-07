/**
 * تقویم شمسی پنل CRM — flatpickr-jdate (مطابق قالب Frest / forms-pickers).
 * روی input های .jalali-date و .jalali-datetime اعمال می‌شود.
 *
 * قرارداد داده:
 *  - مقدار input اصلی (که به سرور ارسال می‌شود) همیشه ISO میلادی است: 2026-07-07 یا 2026-07-07T14:30
 *  - نمایش برای کاربر (altInput) همیشه شمسی است: 1405/04/16 یا 1405/04/16 14:30
 */
(function () {
    'use strict';

    var jalaliMonths = [
        'فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور',
        'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند'
    ];

    function pad(n) { return (n < 10 ? '0' : '') + n; }

    function toPersianDigits(s) {
        var fa = '۰۱۲۳۴۵۶۷۸۹';
        return String(s).replace(/\d/g, function (d) { return fa[+d]; });
    }

    function fromPersianDigits(s) {
        return String(s)
            .replace(/[۰-۹]/g, function (d) { return String('۰۱۲۳۴۵۶۷۸۹'.indexOf(d)); })
            .replace(/[٠-٩]/g, function (d) { return String('٠١٢٣٤٥٦٧٨٩'.indexOf(d)); });
    }

    /**
     * پارس رشته تاریخ. رشته‌های سرور میلادی ISO هستند (سال >= 1700)
     * و رشته‌های شمسی سال < 1700 دارند.
     * خروجی: JDate (که فورک jdate ای flatpickr داخلی استفاده می‌کند).
     */
    function parseAny(datestr) {
        if (datestr && typeof datestr.getTime === 'function') return new JDate(datestr.getTime());
        if (typeof datestr === 'number') return new JDate(datestr);
        var s = fromPersianDigits(String(datestr)).trim();
        var m = s.match(/^(\d{4})[-\/](\d{1,2})[-\/](\d{1,2})(?:[T\s](\d{1,2}):(\d{1,2}))?/);
        if (!m) {
            if (s.toLowerCase() === 'today') return new JDate();
            return undefined;
        }
        var y = +m[1], mo = +m[2] - 1, d = +m[3], h = +(m[4] || 0), mi = +(m[5] || 0);
        if (y >= 1700) {
            // میلادی → به Date واقعی و سپس JDate
            return new JDate(new Date(y, mo, d, h, mi, 0, 0).getTime());
        }
        // شمسی
        return new JDate(y, mo, d, h, mi, 0, 0);
    }

    /**
     * فرمت خروجی. اگر فرمت شامل '-' باشد یعنی مقدار مخفی (سرور) → ISO میلادی؛
     * در غیر این صورت نمایش شمسی برای کاربر.
     */
    function formatAny(jd, format) {
        var withTime = format.indexOf('H') !== -1;
        if (format.indexOf('-') !== -1) {
            var g = new Date(jd.getTime());
            var iso = g.getFullYear() + '-' + pad(g.getMonth() + 1) + '-' + pad(g.getDate());
            if (withTime) iso += 'T' + pad(g.getHours()) + ':' + pad(g.getMinutes());
            return iso;
        }
        var out = jd.getFullYear() + '/' + pad(jd.getMonth() + 1) + '/' + pad(jd.getDate());
        if (withTime) out += ' ' + pad(jd.getHours()) + ':' + pad(jd.getMinutes());
        return out;
    }

    function normalizeInitialValue(el) {
        if (!el.value) return;
        el.value = el.value.trim().replace(' ', 'T');
    }

    function buildOptions(el, withTime) {
        var opts = {
            locale: 'fa',
            altInput: true,
            altFormat: withTime ? 'Y/m/d H:i' : 'Y/m/d',
            dateFormat: withTime ? 'Y-m-d\\TH:i' : 'Y-m-d',
            monthSelectorType: 'static',
            disableMobile: true,
            parseDate: parseAny,
            formatDate: formatAny
        };
        if (withTime) {
            opts.enableTime = true;
            opts.time_24hr = true;
        }
        if (el.readOnly) opts.clickOpens = false;
        return opts;
    }

    function initJalaliPickers(root) {
        if (typeof flatpickr === 'undefined' || typeof JDate === 'undefined') return;

        var scope = root || document;

        scope.querySelectorAll('.jalali-date:not([data-jalali-init])').forEach(function (el) {
            el.dataset.jalaliInit = '1';
            normalizeInitialValue(el);
            el.flatpickr(buildOptions(el, false));
        });

        scope.querySelectorAll('.jalali-datetime:not([data-jalali-init])').forEach(function (el) {
            el.dataset.jalaliInit = '1';
            normalizeInitialValue(el);
            el.flatpickr(buildOptions(el, true));
        });
    }

    /** عنوان شمسی برای FullCalendar */
    function jalaliTitle(date) {
        var jd = new JDate(date);
        return jalaliMonths[jd.getMonth()] + ' ' + jd.getFullYear();
    }

    window.initJalaliPickers = initJalaliPickers;
    window.jalaliCalendarTitle = jalaliTitle;
    window.jalaliDayNumber = function (date) {
        return new JDate(date).getDate();
    };
    // برای تست/استفاده مجدد
    window.jalaliParseAny = parseAny;
    window.jalaliFormatAny = formatAny;
    window.toPersianDigits = window.toPersianDigits || toPersianDigits;

    document.addEventListener('DOMContentLoaded', function () {
        initJalaliPickers();
    });
})();
