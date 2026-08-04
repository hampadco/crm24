/**
 * CRM DataTable — Select2, selection bar, floating operator menu (body portal).
 */
(function () {
    'use strict';

    var EMPTY_OPS = { isempty: 1, isnotempty: 1 };
    var activeOpUi = null;

    function submitForm(form) {
        if (!form) return;
        if (form.requestSubmit) form.requestSubmit();
        else form.submit();
    }

    function needsValue(op) {
        return !EMPTY_OPS[String(op || '').toLowerCase()];
    }

    function setFieldValueEnabled(root, field, enabled) {
        var input = root.querySelector('[name="cf_' + field + '"]');
        if (!input) return;
        input.disabled = !enabled;
        if (!enabled) {
            if (input.tagName === 'SELECT') {
                if (window.jQuery) jQuery(input).val(null).trigger('change.select2');
            } else {
                input.value = '';
            }
        }
    }

    function selectedIds(root) {
        return Array.prototype.map.call(
            root.querySelectorAll('.crm-dt-row-check:checked'),
            function (cb) { return cb.value; }
        );
    }

    function updateSelectionUi(root) {
        var ids = selectedIds(root);
        var bar = root.querySelector('[data-crm-dt-selectbar]');
        var countEl = bar && bar.querySelector('[data-count]');
        var n = ids.length;

        root.querySelectorAll('tbody tr[data-record-id]').forEach(function (tr) {
            var cb = tr.querySelector('.crm-dt-row-check');
            tr.classList.toggle('is-selected', !!(cb && cb.checked));
        });

        if (bar && countEl) {
            countEl.textContent = String(n);
            if (n > 0) bar.removeAttribute('hidden');
            else bar.setAttribute('hidden', '');
        }

        var holder = root.querySelector('[data-bulk-ids]');
        if (holder) {
            holder.innerHTML = ids.map(function (id) {
                return '<input type="hidden" name="ids" value="' + id + '" />';
            }).join('');
        }
    }

    function syncCheckAll(root) {
        var all = root.querySelector('.crm-dt-check-all');
        if (!all) return;
        var rows = root.querySelectorAll('.crm-dt-row-check');
        var checked = root.querySelectorAll('.crm-dt-row-check:checked');
        all.checked = rows.length > 0 && checked.length === rows.length;
        all.indeterminate = checked.length > 0 && checked.length < rows.length;
    }

    function closeOpMenu() {
        if (!activeOpUi) return;
        if (activeOpUi.trigger) {
            activeOpUi.trigger.setAttribute('aria-expanded', 'false');
            activeOpUi.trigger.classList.remove('is-open');
        }
        if (activeOpUi.panel && activeOpUi.panel.parentNode) {
            activeOpUi.panel.parentNode.removeChild(activeOpUi.panel);
        }
        activeOpUi = null;
    }

    function positionOpPanel(panel, trigger) {
        var rect = trigger.getBoundingClientRect();
        var panelWidth = 232;
        var left = rect.left + window.scrollX;
        /* RTL: align panel near trigger, keep on-screen */
        if (document.documentElement.dir === 'rtl') {
            left = rect.right + window.scrollX - panelWidth;
        }
        left = Math.max(8 + window.scrollX, Math.min(left, window.scrollX + window.innerWidth - panelWidth - 8));
        var top = rect.bottom + window.scrollY + 4;
        panel.style.left = left + 'px';
        panel.style.top = top + 'px';
        panel.style.width = panelWidth + 'px';
    }

    function openOpMenu(root, form, trigger) {
        closeOpMenu();

        var field = trigger.getAttribute('data-field');
        var currentOp = trigger.getAttribute('data-op') || 'contains';
        var ops = [];
        try {
            ops = JSON.parse(trigger.getAttribute('data-ops') || '[]');
        } catch (e) {
            ops = [];
        }
        if (!ops.length) return;

        var panel = document.createElement('div');
        panel.className = 'crm-dt-op-panel shadow';
        panel.setAttribute('role', 'listbox');
        panel.innerHTML =
            '<div class="crm-dt-op-menu-head">انتخاب عملگر</div>' +
            '<div class="crm-dt-op-menu-search">' +
            '<input type="search" class="form-control form-control-sm crm-dt-op-search" placeholder="انتخاب مقایسه‌کننده" autocomplete="off" />' +
            '</div>' +
            '<div class="crm-dt-op-list"></div>';

        var list = panel.querySelector('.crm-dt-op-list');
        ops.forEach(function (item) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'crm-dt-op-pick' + (item.op === currentOp ? ' active' : '');
            btn.setAttribute('role', 'option');
            btn.setAttribute('data-op', item.op);
            btn.setAttribute('data-label', item.label);
            btn.textContent = item.label;
            list.appendChild(btn);
        });

        document.body.appendChild(panel);
        positionOpPanel(panel, trigger);
        trigger.setAttribute('aria-expanded', 'true');
        trigger.classList.add('is-open');
        activeOpUi = { root: root, form: form, trigger: trigger, panel: panel, field: field };

        var search = panel.querySelector('.crm-dt-op-search');
        if (search) {
            search.focus();
            search.addEventListener('input', function () {
                var q = (search.value || '').trim().toLowerCase();
                panel.querySelectorAll('.crm-dt-op-pick').forEach(function (btn) {
                    var label = (btn.getAttribute('data-label') || btn.textContent || '').toLowerCase();
                    btn.classList.toggle('is-hidden', q !== '' && label.indexOf(q) === -1);
                });
            });
            search.addEventListener('keydown', function (e) {
                e.stopPropagation();
                if (e.key === 'Escape') {
                    e.preventDefault();
                    closeOpMenu();
                }
            });
        }

        panel.addEventListener('click', function (e) {
            var pick = e.target.closest('.crm-dt-op-pick');
            if (!pick) return;
            e.preventDefault();
            e.stopPropagation();

            var op = pick.getAttribute('data-op');
            var label = pick.getAttribute('data-label') || op;
            var opHidden = form.querySelector('.crm-dt-op-hidden[data-field="' + field + '"]');
            if (opHidden) opHidden.value = op;

            trigger.setAttribute('data-op', op);
            trigger.setAttribute('title', label);

            var shell = root.querySelector('.crm-dt-colhead[data-field="' + field + '"] .crm-dt-filter-shell');
            var valueOk = needsValue(op);
            if (shell) shell.classList.toggle('is-novalue', !valueOk);
            setFieldValueEnabled(root, field, valueOk);

            closeOpMenu();

            var input = root.querySelector('[name="cf_' + field + '"]');
            if (!valueOk) {
                submitForm(form);
                return;
            }
            if (input && String(input.value || '').trim()) {
                submitForm(form);
            }
        });
    }

    function initOperatorMenus(root, form) {
        root.querySelectorAll('[data-crm-op-trigger]').forEach(function (trigger) {
            trigger.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                if (activeOpUi && activeOpUi.trigger === trigger) {
                    closeOpMenu();
                    return;
                }
                openOpMenu(root, form, trigger);
            });
        });
    }

    function initSelect2(root) {
        if (typeof window.jQuery === 'undefined' || !jQuery.fn.select2) return;

        root.querySelectorAll('select.crm-dt-filter-select').forEach(function (sel) {
            var $sel = jQuery(sel);
            if ($sel.data('select2')) return;

            var placeholder = sel.getAttribute('data-placeholder') || 'انتخاب';
            $sel.select2({
                width: '100%',
                dir: 'rtl',
                allowClear: true,
                placeholder: placeholder,
                dropdownParent: jQuery(document.body),
                minimumResultsForSearch: 0
            });

            $sel.on('change', function () {
                var field = sel.getAttribute('data-field');
                var form = root.querySelector('.crm-dt-filter-form');
                var opHidden = form && form.querySelector('.crm-dt-op-hidden[data-field="' + field + '"]');
                if (opHidden && !needsValue(opHidden.value)) {
                    opHidden.value = 'equals';
                }
                submitForm(form);
            });
        });
    }

    function initCrmDataTable(root) {
        if (!root || root.dataset.crmDtReady === '1') return;
        root.dataset.crmDtReady = '1';

        var form = root.querySelector('.crm-dt-filter-form');
        if (!form) return;

        initSelect2(root);
        initOperatorMenus(root, form);

        form.addEventListener('submit', function () {
            root.querySelectorAll('.crm-dt-filter-input:disabled, .crm-dt-filter-select:disabled').forEach(function (el) {
                el.disabled = false;
            });
        });

        form.addEventListener('keydown', function (e) {
            if (e.key !== 'Enter') return;
            var tag = (e.target && e.target.tagName) || '';
            if (tag !== 'INPUT') return;
            if (e.target.classList.contains('crm-dt-op-search')) return;
            e.preventDefault();
            submitForm(form);
        });

        root.addEventListener('keydown', function (e) {
            if (e.key !== 'Enter') return;
            if (!e.target || !e.target.classList.contains('crm-dt-filter-input')) return;
            e.preventDefault();
            submitForm(form);
        });

        var checkAll = root.querySelector('.crm-dt-check-all');
        if (checkAll) {
            checkAll.addEventListener('change', function () {
                root.querySelectorAll('.crm-dt-row-check').forEach(function (cb) {
                    cb.checked = checkAll.checked;
                });
                syncCheckAll(root);
                updateSelectionUi(root);
            });
        }

        root.querySelectorAll('.crm-dt-row-check').forEach(function (cb) {
            cb.addEventListener('change', function () {
                syncCheckAll(root);
                updateSelectionUi(root);
            });
        });

        // کلیک روی ردیف → جزئیات (به‌جز چک‌باکس / لینک / دکمه / فرم)
        root.querySelectorAll('tbody tr[data-detail-url]').forEach(function (tr) {
            tr.addEventListener('click', function (e) {
                if (e.defaultPrevented) return;
                if (e.button !== 0) return;
                if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;

                var interactive = e.target.closest(
                    'a, button, input, label, select, textarea, form, .crm-dt-actions, .crm-dt-check'
                );
                if (interactive) return;

                var url = tr.getAttribute('data-detail-url');
                if (url) window.location.href = url;
            });

            tr.addEventListener('keydown', function (e) {
                if (e.key !== 'Enter' && e.key !== ' ') return;
                if (e.target !== tr) return;
                e.preventDefault();
                var url = tr.getAttribute('data-detail-url');
                if (url) window.location.href = url;
            });
        });

        var clearBtn = root.querySelector('[data-crm-dt-clear-selection]');
        if (clearBtn) {
            clearBtn.addEventListener('click', function () {
                root.querySelectorAll('.crm-dt-row-check, .crm-dt-check-all').forEach(function (cb) {
                    cb.checked = false;
                    cb.indeterminate = false;
                });
                updateSelectionUi(root);
            });
        }

        syncCheckAll(root);
        updateSelectionUi(root);
    }

    document.addEventListener('click', function (e) {
        if (!activeOpUi) return;
        if (e.target.closest('.crm-dt-op-panel')) return;
        if (e.target.closest('[data-crm-op-trigger]')) return;
        closeOpMenu();
    });

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') closeOpMenu();
    });

    window.addEventListener('scroll', function () {
        if (activeOpUi) closeOpMenu();
    }, true);

    window.addEventListener('resize', closeOpMenu);

    function boot() {
        document.querySelectorAll('[data-crm-datatable]').forEach(initCrmDataTable);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }

    window.CrmDataTable = { init: initCrmDataTable, closeOpMenu: closeOpMenu };
})();
