/* کاریز: انتخاب چندتایی، Sortable، اکاردئون، مودال سریع */
(function () {
    'use strict';

    var root = document.getElementById('crmKanban');
    if (!root) return;

    var moduleName = root.dataset.module || '';
    var fieldName = root.dataset.field || '';
    var canEdit = root.dataset.canEdit === '1';
    var tokenInput = document.querySelector('#afToken input[name="__RequestVerificationToken"]');
    var token = tokenInput ? tokenInput.value : '';
    var bulkBar = document.getElementById('crmKanbanBulk');
    var bulkCount = document.getElementById('crmKanbanBulkCount');
    var bulkIdsBoxes = document.querySelectorAll('[data-bulk-ids]');
    var quickModalEl = document.getElementById('crmKanbanQuickModal');
    var quickBody = document.getElementById('crmKanbanQuickBody');
    var quickModal = quickModalEl && window.bootstrap
        ? bootstrap.Modal.getOrCreateInstance(quickModalEl)
        : null;

    function selectedChecks() {
        return Array.prototype.slice.call(root.querySelectorAll('.crm-kanban-card-check:checked'));
    }

    function selectedIds() {
        return selectedChecks().map(function (c) { return c.value; });
    }

    function updateBulkUi() {
        var ids = selectedIds();
        var n = ids.length;
        if (bulkBar) bulkBar.classList.toggle('is-visible', n > 0);
        if (bulkCount) bulkCount.textContent = String(n);
        bulkIdsBoxes.forEach(function (box) {
            box.innerHTML = '';
            ids.forEach(function (id) {
                var input = document.createElement('input');
                input.type = 'hidden';
                input.name = 'ids';
                input.value = id;
                box.appendChild(input);
            });
        });
        root.querySelectorAll('.crm-kanban-card').forEach(function (card) {
            var check = card.querySelector('.crm-kanban-card-check');
            card.classList.toggle('is-selected', !!(check && check.checked));
        });
        root.querySelectorAll('[data-col-check]').forEach(function (colCheck) {
            var col = colCheck.closest('.crm-kanban-col');
            if (!col) return;
            var checks = col.querySelectorAll('.crm-kanban-card-check');
            var checked = col.querySelectorAll('.crm-kanban-card-check:checked');
            colCheck.checked = checks.length > 0 && checked.length === checks.length;
            colCheck.indeterminate = checked.length > 0 && checked.length < checks.length;
        });
    }

    root.addEventListener('change', function (e) {
        var t = e.target;
        if (t && t.classList && t.classList.contains('crm-kanban-card-check')) {
            updateBulkUi();
            return;
        }
        if (t && t.matches && t.matches('[data-col-check]')) {
            var col = t.closest('.crm-kanban-col');
            if (!col) return;
            col.querySelectorAll('.crm-kanban-card-check').forEach(function (c) {
                c.checked = t.checked;
            });
            updateBulkUi();
        }
    });

    var clearBtn = document.getElementById('crmKanbanClearSel');
    if (clearBtn) {
        clearBtn.addEventListener('click', function () {
            root.querySelectorAll('.crm-kanban-card-check').forEach(function (c) { c.checked = false; });
            updateBulkUi();
        });
    }

    // اکاردئون
    root.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-kanban-expand]');
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation();
        var card = btn.closest('.crm-kanban-card');
        if (card) card.classList.toggle('is-open');
    });

    // مودال سریع
    async function openQuick(id) {
        if (!quickBody || !quickModal) {
            window.location.href = '/App/m/' + encodeURIComponent(moduleName) + '/' + id;
            return;
        }
        quickBody.innerHTML = '<div class="text-center text-muted py-5"><div class="spinner-border spinner-border-sm"></div></div>';
        quickModal.show();
        try {
            var resp = await fetch('/App/kanban/' + encodeURIComponent(moduleName) + '/card/' + id, {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!resp.ok) throw new Error('fail');
            quickBody.innerHTML = await resp.text();
        } catch (err) {
            quickBody.innerHTML = '<div class="alert alert-danger mb-0">بارگذاری جزئیات ناموفق بود.</div>';
        }
    }

    root.addEventListener('click', function (e) {
        var title = e.target.closest('[data-kanban-quick]');
        if (!title) return;
        e.preventDefault();
        e.stopPropagation();
        var id = title.getAttribute('data-kanban-quick');
        if (id) openQuick(id);
    });

    // جلوگیری از شروع درگ وقتی روی چک‌باکس/دکمه هستیم
    if (window.Sortable) {
        root.querySelectorAll('.crm-kanban-col-body').forEach(function (col) {
            new Sortable(col, {
                group: 'crm-kanban',
                animation: 150,
                draggable: '.crm-kanban-card',
                ghostClass: 'crm-kanban-sortable-ghost',
                disabled: !canEdit,
                filter: 'a,button,input,label,form',
                preventOnFilter: false,
                onMove: function () { return canEdit; },
                onEnd: async function (evt) {
                    if (!canEdit || evt.from === evt.to) {
                        refreshCounts();
                        return;
                    }
                    var recordId = evt.item && evt.item.dataset ? evt.item.dataset.id : null;
                    var value = evt.to ? evt.to.dataset.value : '';
                    if (!recordId) return;

                    var body = new URLSearchParams({
                        recordId: recordId,
                        field: fieldName,
                        value: value || '',
                        __RequestVerificationToken: token
                    });
                    try {
                        var resp = await fetch('/App/kanban/' + encodeURIComponent(moduleName) + '/move', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                            body: body
                        });
                        if (!resp.ok) {
                            evt.from.appendChild(evt.item);
                        }
                    } catch (err) {
                        evt.from.appendChild(evt.item);
                    }
                    refreshCounts();
                }
            });
        });
    }

    function refreshCounts() {
        root.querySelectorAll('.crm-kanban-col').forEach(function (col) {
            var body = col.querySelector('.crm-kanban-col-body');
            var badge = col.querySelector('[data-col-count]');
            if (body && badge)
                badge.textContent = String(body.querySelectorAll('.crm-kanban-card').length);
        });
    }

    // فیلتر ستون‌ها
    var allCb = document.getElementById('kanbanColAll');
    var applyCols = document.getElementById('kanbanApplyColumns');
    if (allCb) {
        allCb.addEventListener('change', function () {
            document.querySelectorAll('.kanban-col-check').forEach(function (c) {
                c.checked = allCb.checked;
            });
        });
    }
    if (applyCols) {
        applyCols.addEventListener('click', function () {
            var selected = Array.prototype.slice.call(document.querySelectorAll('.kanban-col-check'))
                .filter(function (c) { return c.checked; })
                .map(function (c) { return c.value; });
            var url = new URL(window.location.href);
            if (selected.length === 0 || selected.length >= document.querySelectorAll('.kanban-col-check').length) {
                url.searchParams.delete('columns');
            } else {
                url.searchParams.set('columns', selected.join(','));
            }
            window.location.href = url.pathname + '?' + url.searchParams.toString();
        });
    }

    // فیلتر برچسب
    var applyTags = document.getElementById('kanbanApplyTags');
    if (applyTags) {
        applyTags.addEventListener('click', function () {
            var selected = Array.prototype.slice.call(document.querySelectorAll('.kanban-tag-check'))
                .filter(function (c) { return c.checked; })
                .map(function (c) { return c.value; });
            var url = new URL(window.location.href);
            if (selected.length === 0) url.searchParams.delete('tags');
            else url.searchParams.set('tags', selected.join(','));
            window.location.href = url.pathname + '?' + url.searchParams.toString();
        });
    }

    updateBulkUi();
})();
