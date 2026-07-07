/**
 * تقویم شمسی پنل CRM — flatpickr-jdate (مطابق قالب Frest / forms-pickers).
 * روی input های .jalali-date و .jalali-datetime اعمال می‌شود.
 *
 * قرارداد: مقدار input اصلی (که به سرور ارسال می‌شود) همیشه ISO میلادی است
 * (yyyy-MM-dd یا yyyy-MM-ddTHH:mm) و فقط نمایش (altInput) شمسی است.
 */
(function () {
    'use strict';

    var jalaliMonths = [
        'فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور',
        'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند'
    ];

    function two(n) { return (n < 10 ? '0' : '') + n; }

    /** رشته ISO میلادی → Date واقعی میلادی (یا null). */
    function parseGregorianIso(str) {
        var m = /^(\d{4})-(\d{1,2})-(\d{1,2})(?:[T ](\d{1,2}):(\d{1,2}))?/.exec(String(str).trim());
        if (!m) return null;
        var year = +m[1];
        // سال‌های شمسی (۱۳xx–۱۵xx) را میلادی حساب نکن
        if (year < 1700) return null;
        return new Date(year, +m[2] - 1, +m[3], +(m[4] || 0), +(m[5] || 0), 0, 0);
    }

    /** timestamp → رشته ISO میلادی برای ارسال به سرور. */
    function toGregorianIso(ts, withTime) {
        var d = new Date(ts);
        var s = d.getFullYear() + '-' + two(d.getMonth() + 1) + '-' + two(d.getDate());
        if (withTime) s += 'T' + two(d.getHours()) + ':' + two(d.getMinutes());
        return s;
    }

    /** JDate → نمایش شمسی. */
    function toJalaliDisplay(jd, withTime) {
        var s = jd.getFullYear() + '/' + two(jd.getMonth() + 1) + '/' + two(jd.getDate());
        if (withTime) s += ' ' + two(jd.getHours()) + ':' + two(jd.getMinutes());
        return s;
    }

    function makeOptions(withTime, el) {
        var opts = {
            locale: 'fa',
            altInput: true,
            altFormat: withTime ? 'Y/m/d H:i' : 'Y/m/d',
            dateFormat: withTime ? 'Y-m-d\\TH:i' : 'Y-m-d',
            monthSelectorType: 'static',
            disableMobile: true,
            // مقدار ارسال‌شده به سرور میلادی است؛ نمایش شمسی.
            parseDate: function (datestr) {
                if (datestr instanceof Date) return new JDate(datestr);
                var g = parseGregorianIso(datestr);
                if (g) return new JDate(g);
                try { return new JDate(String(datestr)); } catch (e) { return undefined; }
            },
            formatDate: function (date, format) {
                // فرمت‌های حاوی '-' فرمت سیمی (input اصلی) هستند → میلادی
                if (format.indexOf('-') !== -1) return toGregorianIso(date.getTime(), withTime);
                return toJalaliDisplay(date, withTime);
            }
        };
        if (withTime) {
            opts.enableTime = true;
            opts.time_24hr = true;
        }
        if (el.readOnly) opts.clickOpens = false;
        return opts;
    }

    function initOne(el, withTime) {
        if (el._flatpickr || el.classList.contains('form-control-alt-jalali')) return;
        if (el.readOnly && !el.value) return;
        el.dataset.jalaliInit = '1';
        if (el.value) el.value = el.value.trim().replace(' ', 'T');

        var fp = el.flatpickr(makeOptions(withTime, el));
        // altInput کلاس‌های input اصلی (از جمله jalali-*) را به ارث می‌برد؛
        // علامت‌گذاری تا در فراخوانی‌های بعدی دوباره مقداردهی نشود.
        if (fp.altInput) {
            fp.altInput.dataset.jalaliInit = '1';
            fp.altInput.classList.add('form-control-alt-jalali');
        }
    }

    function initJalaliPickers(root) {
        if (typeof flatpickr === 'undefined' || typeof JDate === 'undefined') return;

        var scope = root || document;
        scope.querySelectorAll('.jalali-date:not([data-jalali-init])').forEach(function (el) {
            initOne(el, false);
        });
        scope.querySelectorAll('.jalali-datetime:not([data-jalali-init])').forEach(function (el) {
            initOne(el, true);
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

    document.addEventListener('DOMContentLoaded', function () {
        initJalaliPickers();
    });
})();
