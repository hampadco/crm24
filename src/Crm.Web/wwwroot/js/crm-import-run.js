(function () {
    function closePicks(except) {
        document.querySelectorAll('.crm-imp-pick.is-open').forEach(function (el) {
            if (el === except) return;
            el.classList.remove('is-open');
            var menu = el.querySelector('.crm-imp-pick-menu');
            if (menu) menu.hidden = true;
        });
    }

    function mountPick(select) {
        if (select.dataset.crmPick) return;
        select.dataset.crmPick = '1';
        select.tabIndex = -1;
        select.classList.add('crm-imp-native');

        var wrap = document.createElement('div');
        wrap.className = 'crm-imp-pick';
        select.parentNode.insertBefore(wrap, select);
        wrap.appendChild(select);

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'crm-imp-pick-btn';
        var menu = document.createElement('div');
        menu.className = 'crm-imp-pick-menu';
        menu.hidden = true;
        var list = document.createElement('div');
        list.className = 'crm-imp-pick-list';
        var searchable = select.classList.contains('crm-imp-field') && select.options.length > 6;
        var search = null;
        var empty = null;
        if (searchable) {
            var searchWrap = document.createElement('div');
            searchWrap.className = 'crm-imp-pick-search';
            search = document.createElement('input');
            search.type = 'search';
            search.placeholder = 'جستجوی فیلد';
            search.autocomplete = 'off';
            search.setAttribute('aria-label', 'جستجوی فیلد');
            search.addEventListener('input', function () { applyFilter(search.value); });
            search.addEventListener('keydown', function (event) {
                if (event.key === 'Enter') event.preventDefault();
            });
            searchWrap.appendChild(search);
            menu.appendChild(searchWrap);
            empty = document.createElement('div');
            empty.className = 'crm-imp-pick-empty';
            empty.textContent = 'موردی پیدا نشد';
            empty.hidden = true;
        }
        menu.appendChild(list);
        if (empty) menu.appendChild(empty);

        function paintBtn() {
            var option = select.options[select.selectedIndex];
            btn.textContent = option ? option.textContent : '';
            btn.classList.toggle('is-empty', !select.value);
        }

        function addItem(option) {
            var item = document.createElement('button');
            item.type = 'button';
            item.className = 'crm-imp-pick-item' + (option.selected ? ' is-on' : '');
            item.textContent = option.textContent;
            item.addEventListener('click', function () {
                select.value = option.value;
                select.dispatchEvent(new Event('change', { bubbles: true }));
                close();
            });
            list.appendChild(item);
        }

        function fold(value) {
            return (value || '')
                .replace(/\u200c/g, '')
                .replace(/\s+/g, '')
                .replace(/ي/g, 'ی')
                .replace(/ك/g, 'ک')
                .toLowerCase();
        }

        function applyFilter(query) {
            var needle = fold(query);
            var any = false;
            var group = null;
            Array.prototype.forEach.call(list.children, function (el) {
                if (el.classList.contains('crm-imp-pick-group')) {
                    group = el;
                    el.classList.add('is-hidden');
                    return;
                }
                var hit = !needle || fold(el.textContent).indexOf(needle) >= 0;
                el.classList.toggle('is-hidden', !hit);
                if (hit) {
                    any = true;
                    if (group) group.classList.remove('is-hidden');
                }
            });
            if (empty) empty.hidden = any;
        }

        function build() {
            list.textContent = '';
            Array.prototype.forEach.call(select.children, function (node) {
                if (node.tagName === 'OPTGROUP') {
                    var group = document.createElement('div');
                    group.className = 'crm-imp-pick-group';
                    group.textContent = node.label;
                    list.appendChild(group);
                    Array.prototype.forEach.call(node.children, addItem);
                } else if (node.tagName === 'OPTION') {
                    addItem(node);
                }
            });
            if (search) {
                search.value = '';
                applyFilter('');
            }
        }

        function open() {
            closePicks(wrap);
            build();
            menu.hidden = false;
            menu.classList.remove('is-up');
            wrap.classList.add('is-open');
            var rect = btn.getBoundingClientRect();
            if (window.innerHeight - rect.bottom < 320 && rect.top > 320)
                menu.classList.add('is-up');
            if (search) search.focus();
            else {
                var current = menu.querySelector('.is-on');
                if (current) current.scrollIntoView({ block: 'nearest' });
            }
        }

        function close() {
            menu.hidden = true;
            wrap.classList.remove('is-open');
            paintBtn();
        }

        btn.addEventListener('click', function () {
            if (wrap.classList.contains('is-open')) close();
            else open();
        });
        select.addEventListener('change', paintBtn);
        wrap.appendChild(btn);
        wrap.appendChild(menu);
        paintBtn();
    }

    document.querySelectorAll('.crm-imp-field, .crm-imp-flag').forEach(mountPick);
    document.addEventListener('click', function (event) {
        if (!event.target.closest('.crm-imp-pick')) closePicks();
    });
    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape') closePicks();
    });

    document.querySelectorAll('.crm-imp-row').forEach(function (row) {
        var field = row.querySelector('.crm-imp-field');
        var flag = row.querySelector('.crm-imp-flag');
        if (!field || !flag) return;
        var sync = function () {
            var option = field.options[field.selectedIndex];
            var check = option && option.getAttribute('data-kind') === 'check';
            flag.hidden = !check;
        };
        field.addEventListener('change', sync);
        sync();
    });

    var screen = document.getElementById('crmImpScreen');
    if (!screen) return;

    var form = document.querySelector('[data-crm-import-form]');
    if (form) {
        form.addEventListener('submit', function () {
            screen.hidden = false;
        });
    }

    var pollUrl = screen.getAttribute('data-poll');
    if (!pollUrl) return;

    var listUrl = screen.getAttribute('data-list') || '/';
    var title = document.getElementById('crmImpTitle');
    var detail = document.getElementById('crmImpDetail');
    var bar = document.getElementById('crmImpBar');
    var errors = document.getElementById('crmImpErrors');
    var actions = document.getElementById('crmImpActions');
    var card = screen.querySelector('.crm-imp-card');

    function paint(data) {
        var total = data.total || 0;
        var processed = data.processed || 0;
        var percent = total > 0 ? Math.min(100, Math.round(processed * 100 / total)) : (data.state === 'done' ? 100 : 8);
        if (bar) bar.style.width = percent + '%';

        if (data.state === 'done' || data.state === 'failed') {
            card.classList.add(data.state === 'done' ? 'is-done' : 'is-failed');
            card.classList.remove(data.state === 'done' ? 'is-failed' : 'is-done');
            title.textContent = data.state === 'done' ? 'ورود تمام شد' : 'ورود متوقف شد';
            detail.textContent = data.message || (data.imported + ' رکورد وارد شد');
            if (data.errors && data.errors.length && errors) {
                errors.hidden = false;
                errors.textContent = data.errors.join('\n');
            }
            if (actions) actions.hidden = false;
            var link = document.getElementById('crmImpList');
            if (link) link.href = listUrl;
            return true;
        }

        title.textContent = 'در حال ورود رکوردها';
        detail.textContent = total > 0
            ? processed + ' از ' + total + ' ردیف' + (data.imported ? ' · ' + data.imported + ' ثبت شد' : '')
            : 'آماده‌سازی فایل';
        return false;
    }

    var timer = setInterval(function () {
        fetch(pollUrl, { headers: { 'Accept': 'application/json' } })
            .then(function (response) { return response.ok ? response.json() : null; })
            .then(function (data) {
                if (!data) return;
                if (paint(data)) clearInterval(timer);
            })
            .catch(function () { /* تلاش بعدی */ });
    }, 1000);
})();
