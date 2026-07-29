/**
 * Column manager modal — add/remove/reorder list columns.
 */
(function () {
    'use strict';

    function initColumnManager() {
        var form = document.getElementById('columnManagerForm');
        var available = document.getElementById('columnManagerAvailable');
        var selected = document.getElementById('columnManagerSelected');
        var search = document.getElementById('columnManagerSearch');
        var countEl = document.getElementById('columnManagerCount');
        if (!form || !available || !selected) return;

        var maxColumns = parseInt(form.getAttribute('data-max-columns') || '15', 10) || 15;

        function selectedCount() {
            return selected.querySelectorAll('.crm-colmgr-selected-item').length;
        }

        function updateCount() {
            if (countEl) countEl.textContent = '(' + selectedCount() + ' از ' + maxColumns + ')';
        }

        function markAvailableSelected() {
            var ids = {};
            selected.querySelectorAll('.crm-colmgr-selected-item').forEach(function (el) {
                ids[el.getAttribute('data-field-id')] = true;
            });
            available.querySelectorAll('[data-available-item]').forEach(function (btn) {
                var id = btn.getAttribute('data-field-id');
                btn.classList.toggle('is-selected', !!ids[id]);
            });
        }

        function addField(id, label) {
            if (selected.querySelector('.crm-colmgr-selected-item[data-field-id="' + id + '"]')) return;
            if (selectedCount() >= maxColumns) {
                if (window.Swal) {
                    Swal.fire({ icon: 'info', title: 'محدودیت ستون', text: 'حداکثر ' + maxColumns + ' ستون می‌توانید انتخاب کنید.', confirmButtonText: 'باشه' });
                } else {
                    alert('حداکثر ' + maxColumns + ' ستون می‌توانید انتخاب کنید.');
                }
                return;
            }

            var row = document.createElement('div');
            row.className = 'crm-colmgr-selected-item';
            row.setAttribute('data-field-id', id);
            row.innerHTML =
                '<i class="bx bx-menu crm-colmgr-handle"></i>' +
                '<span class="flex-grow-1"></span>' +
                '<button type="button" class="btn btn-sm btn-icon text-danger" data-remove-column title="حذف"><i class="bx bx-x"></i></button>' +
                '<input type="hidden" name="fieldIds" value="' + id + '" />';
            row.querySelector('span').textContent = label;
            selected.appendChild(row);
            markAvailableSelected();
            updateCount();
        }

        function removeField(id) {
            var row = selected.querySelector('.crm-colmgr-selected-item[data-field-id="' + id + '"]');
            if (row) row.remove();
            markAvailableSelected();
            updateCount();
        }

        available.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-available-item]');
            if (!btn || btn.classList.contains('is-selected')) return;
            addField(btn.getAttribute('data-field-id'), btn.getAttribute('data-field-label') || btn.textContent.trim());
        });

        selected.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-remove-column]');
            if (!btn) return;
            var row = btn.closest('.crm-colmgr-selected-item');
            if (!row) return;
            removeField(row.getAttribute('data-field-id'));
        });

        if (search) {
            search.addEventListener('input', function () {
                var q = (search.value || '').trim().toLowerCase();
                available.querySelectorAll('[data-available-item]').forEach(function (btn) {
                    var label = (btn.getAttribute('data-field-label') || '').toLowerCase();
                    btn.classList.toggle('is-hidden', q !== '' && label.indexOf(q) === -1);
                });
            });
        }

        form.addEventListener('submit', function (e) {
            if (selectedCount() === 0) {
                e.preventDefault();
                if (window.Swal) {
                    Swal.fire({ icon: 'warning', title: 'ستون خالی', text: 'حداقل یک ستون انتخاب کنید.', confirmButtonText: 'باشه' });
                } else {
                    alert('حداقل یک ستون انتخاب کنید.');
                }
            }
        });

        if (window.Sortable) {
            Sortable.create(selected, {
                handle: '.crm-colmgr-handle',
                animation: 150,
                ghostClass: 'sortable-ghost'
            });
        }

        markAvailableSelected();
        updateCount();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initColumnManager);
    } else {
        initColumnManager();
    }
})();
