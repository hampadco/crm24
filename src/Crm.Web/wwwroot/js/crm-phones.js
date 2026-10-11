(function () {
    document.querySelectorAll('[data-crm-phones]').forEach(function (box) {
        var hidden = box.querySelector('input[type="hidden"]');
        if (!hidden) return;

        function sync() {
            var parts = [];
            box.querySelectorAll('.crm-phone-part').forEach(function (input) {
                var value = input.value.trim();
                if (value) parts.push(value);
            });
            hidden.value = parts.join('\n');
        }

        function row() {
            var sample = box.querySelector('.crm-phone-part');
            var wrap = document.createElement('div');
            wrap.className = 'crm-phone-row input-group';
            var input = document.createElement('input');
            input.type = 'text';
            input.dir = 'ltr';
            input.className = sample ? sample.className : 'form-control crm-phone-part';
            input.autocomplete = 'off';
            input.classList.add('crm-phone-part');
            var button = document.createElement('button');
            button.type = 'button';
            button.className = 'btn btn-outline-secondary';
            button.setAttribute('data-crm-phone-remove', '');
            button.title = 'حذف این شماره';
            button.innerHTML = '<i class="bx bx-x"></i>';
            wrap.appendChild(input);
            wrap.appendChild(button);
            return wrap;
        }

        box.addEventListener('input', sync);
        box.addEventListener('click', function (event) {
            var add = event.target.closest('[data-crm-phone-add]');
            if (add) {
                var anchor = box.querySelector('[data-crm-phone-add]');
                box.insertBefore(row(), anchor);
                var added = anchor.previousElementSibling && anchor.previousElementSibling.querySelector('input');
                if (added) added.focus();
                sync();
                return;
            }
            var remove = event.target.closest('[data-crm-phone-remove]');
            if (!remove) return;
            var rows = box.querySelectorAll('.crm-phone-row');
            if (rows.length > 1)
                remove.closest('.crm-phone-row').remove();
            else
                remove.closest('.crm-phone-row').querySelector('input').value = '';
            sync();
        });

        var form = box.closest('form');
        if (form) form.addEventListener('submit', sync);
    });
})();
