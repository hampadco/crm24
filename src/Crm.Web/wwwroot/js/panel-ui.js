/**
 * UI مشترک پنل: Toast/Alert با SweetAlert2 + تأیید حذف به‌جای confirm() بومی.
 */
(function () {
    'use strict';

    var rtl = { confirmButtonText: 'تأیید', cancelButtonText: 'انصراف' };

    function ensureSwal() {
        return typeof Swal !== 'undefined';
    }

    function toast(icon, title) {
        if (!ensureSwal()) {
            window.alert(title);
            return;
        }
        Swal.fire({
            toast: true,
            position: 'top-end',
            icon: icon,
            title: title,
            showConfirmButton: false,
            timer: 4200,
            timerProgressBar: true,
            reverseButtons: true,
            customClass: { popup: 'crm-swal-toast' }
        });
    }

    function showFlashAlerts() {
        document.querySelectorAll('[data-crm-flash]').forEach(function (el) {
            var type = el.getAttribute('data-crm-flash') || 'info';
            var text = (el.textContent || '').trim();
            if (!text) return;
            el.remove();
            if (!ensureSwal()) {
                window.alert(text);
                return;
            }
            var icon = type === 'success' ? 'success' : type === 'error' ? 'error' : 'info';
            Swal.fire({
                icon: icon,
                title: type === 'success' ? 'انجام شد' : type === 'error' ? 'خطا' : 'اطلاع',
                text: text,
                confirmButtonText: rtl.confirmButtonText,
                reverseButtons: true,
                customClass: { confirmButton: 'btn btn-primary' },
                buttonsStyling: false
            });
        });
    }

    window.crmConfirm = function (message, options) {
        options = options || {};
        if (!ensureSwal()) {
            return Promise.resolve(window.confirm(message));
        }
        return Swal.fire({
            icon: options.icon || 'warning',
            title: options.title || 'آیا مطمئن هستید؟',
            text: message,
            showCancelButton: true,
            confirmButtonText: options.confirmText || rtl.confirmButtonText,
            cancelButtonText: options.cancelText || rtl.cancelButtonText,
            reverseButtons: true,
            focusCancel: true,
            customClass: {
                confirmButton: 'btn btn-danger me-2',
                cancelButton: 'btn btn-outline-secondary'
            },
            buttonsStyling: false
        }).then(function (result) {
            return !!result.isConfirmed;
        });
    };

    function bindConfirmForms() {
        document.addEventListener('submit', function (e) {
            var form = e.target;
            if (!(form instanceof HTMLFormElement)) return;
            var msg = form.getAttribute('data-confirm');
            if (!msg) return;
            if (form.dataset.crmConfirmed === '1') {
                delete form.dataset.crmConfirmed;
                return;
            }
            e.preventDefault();
            e.stopPropagation();
            window.crmConfirm(msg, {
                confirmText: form.getAttribute('data-confirm-ok') || 'بله، ادامه',
                icon: form.getAttribute('data-confirm-icon') || 'warning'
            }).then(function (ok) {
                if (!ok) return;
                form.dataset.crmConfirmed = '1';
                if (typeof form.requestSubmit === 'function') form.requestSubmit();
                else form.submit();
            });
        }, true);
    }

    function initSelect2(root) {
        if (typeof jQuery === 'undefined' || !jQuery.fn.select2) return;
        var $ = jQuery;
        $(root || document).find('select.crm-select2').each(function () {
            var $el = $(this);
            if ($el.hasClass('select2-hidden-accessible')) return;
            $el.wrap('<div class="position-relative"></div>').select2({
                dir: 'rtl',
                width: '100%',
                placeholder: $el.data('placeholder') || 'جستجو و انتخاب…',
                allowClear: !!$el.data('allow-clear'),
                dropdownParent: $el.parent()
            });
        });
    }

    window.initCrmSelect2 = initSelect2;

    document.addEventListener('DOMContentLoaded', function () {
        showFlashAlerts();
        bindConfirmForms();
        initSelect2(document);
    });
})();
