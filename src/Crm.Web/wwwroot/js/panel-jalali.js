/**
 * تقویم شمسی پنل CRM — flatpickr-jdate (مطابق قالب Frest / forms-pickers).
 * روی input های .jalali-date و .jalali-datetime اعمال می‌شود.
 */
(function () {
    'use strict';

    var jalaliMonths = [
        'فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور',
        'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند'
    ];

    function normalizeInitialValue(el, withTime) {
        if (!el.value) return;
        var v = el.value.trim().replace(' ', 'T');
        if (withTime && v.indexOf('T') === -1 && v.indexOf(' ') > -1) {
            v = v.replace(' ', 'T');
        }
        el.value = v;
    }

    function initJalaliPickers(root) {
        if (typeof flatpickr === 'undefined') return;

        var scope = root || document;

        scope.querySelectorAll('.jalali-date:not([data-jalali-init])').forEach(function (el) {
            if (el.readOnly && !el.value) return;
            el.dataset.jalaliInit = '1';
            normalizeInitialValue(el, false);

            var opts = {
                locale: 'fa',
                altInput: true,
                altFormat: 'Y/m/d',
                dateFormat: 'Y-m-d',
                monthSelectorType: 'static',
                disableMobile: true
            };
            if (el.readOnly) opts.clickOpens = false;
            var fp = el.flatpickr(opts);
            if (el.value) fp.setDate(el.value, false);
        });

        scope.querySelectorAll('.jalali-datetime:not([data-jalali-init])').forEach(function (el) {
            if (el.readOnly && !el.value) return;
            el.dataset.jalaliInit = '1';
            normalizeInitialValue(el, true);

            var opts = {
                enableTime: true,
                locale: 'fa',
                altInput: true,
                altFormat: 'Y/m/d H:i',
                dateFormat: 'Y-m-d\\TH:i',
                monthSelectorType: 'static',
                disableMobile: true,
                time_24hr: true
            };
            if (el.readOnly) opts.clickOpens = false;
            var fp = el.flatpickr(opts);
            if (el.value) fp.setDate(el.value.replace('T', ' '), false);
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
