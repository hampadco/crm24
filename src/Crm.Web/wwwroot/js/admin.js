(function () {
    'use strict';

    var uploadUrl = '/Admin/Media/Upload';
    var uploadAudioUrl = '/Admin/Media/UploadAudio';

    function getAntiForgeryToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function toPersianDigits(value) {
        return String(value).replace(/\d/g, function (d) {
            return '۰۱۲۳۴۵۶۷۸۹'[Number(d)];
        });
    }

    function formatPriceNumber(value) {
        var digits = String(value).replace(/[^\d]/g, '');
        if (!digits) return '';
        return toPersianDigits(Number(digits).toLocaleString('en-US'));
    }

    function parsePriceNumber(value) {
        var digits = String(value).replace(/[^\d]/g, '');
        return digits ? digits : '0';
    }

    function initPriceFields(root) {
        (root || document).querySelectorAll('.admin-price-wrap').forEach(function (wrap) {
            var display = wrap.querySelector('.admin-price-display');
            var hidden = wrap.querySelector('.admin-price-value');
            if (!display || !hidden) return;

            display.addEventListener('input', function () {
                var raw = parsePriceNumber(display.value);
                hidden.value = raw;
                display.value = formatPriceNumber(raw);
            });

            display.addEventListener('blur', function () {
                if (!display.value.trim()) {
                    hidden.value = '0';
                    display.value = formatPriceNumber('0');
                }
            });
        });
    }

    function updateImagePreview(field) {
        var urlInput = field.querySelector('.admin-image-url');
        var preview = field.querySelector('.admin-image-preview');
        var img = preview ? preview.querySelector('img') : null;
        if (!urlInput || !preview || !img) return;

        var url = (urlInput.value || '').trim();
        if (url) {
            img.src = url;
            preview.classList.remove('is-empty');
        } else {
            img.removeAttribute('src');
            preview.classList.add('is-empty');
        }
    }

    function initImageFields(root) {
        (root || document).querySelectorAll('.admin-image-field').forEach(function (field) {
            var urlInput = field.querySelector('.admin-image-url');
            var fileInput = field.querySelector('.admin-image-file');
            var uploadBtn = field.querySelector('.admin-image-upload-btn');
            var clearBtn = field.querySelector('.admin-image-clear-btn');
            var status = field.querySelector('.admin-image-status');

            updateImagePreview(field);

            if (urlInput) {
                urlInput.addEventListener('input', function () {
                    updateImagePreview(field);
                    if (status) {
                        status.textContent = '';
                        status.className = 'admin-image-status';
                    }
                });
            }

            if (uploadBtn && fileInput) {
                uploadBtn.addEventListener('click', function () {
                    fileInput.click();
                });

                fileInput.addEventListener('change', function () {
                    var file = fileInput.files && fileInput.files[0];
                    if (!file) return;

                    if (status) {
                        status.textContent = 'در حال آپلود…';
                        status.className = 'admin-image-status';
                    }

                    uploadBtn.disabled = true;
                    var formData = new FormData();
                    formData.append('file', file);
                    var token = getAntiForgeryToken();
                    if (token) {
                        formData.append('__RequestVerificationToken', token);
                    }

                    fetch(uploadUrl, {
                        method: 'POST',
                        body: formData
                    })
                        .then(function (res) { return res.json(); })
                        .then(function (data) {
                            if (data.success && data.url) {
                                urlInput.value = data.url;
                                updateImagePreview(field);
                                if (status) {
                                    status.textContent = 'آپلود موفق';
                                    status.className = 'admin-image-status is-success';
                                }
                            } else {
                                if (status) {
                                    status.textContent = data.message || 'خطا در آپلود';
                                    status.className = 'admin-image-status is-error';
                                }
                            }
                        })
                        .catch(function () {
                            if (status) {
                                status.textContent = 'خطا در ارتباط با سرور';
                                status.className = 'admin-image-status is-error';
                            }
                        })
                        .finally(function () {
                            uploadBtn.disabled = false;
                            fileInput.value = '';
                        });
                });
            }

            if (clearBtn) {
                clearBtn.addEventListener('click', function () {
                    urlInput.value = '';
                    updateImagePreview(field);
                    if (status) {
                        status.textContent = '';
                        status.className = 'admin-image-status';
                    }
                });
            }
        });
    }

    function toHiddenDateValue(unixMs) {
        var d = new Date(unixMs);
        var pad = function (n) { return String(n).padStart(2, '0'); };
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) +
            'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }

    function initAudioFields(root) {
        (root || document).querySelectorAll('.admin-audio-field').forEach(function (field) {
            var urlInput = field.querySelector('.admin-audio-url');
            var fileInput = field.querySelector('.admin-audio-file');
            var uploadBtn = field.querySelector('.admin-audio-upload-btn');
            var clearBtn = field.querySelector('.admin-audio-clear-btn');
            var preview = field.querySelector('.admin-audio-preview');
            var audio = preview ? preview.querySelector('audio') : null;
            var status = field.querySelector('.admin-audio-status');

            function updatePreview() {
                if (!urlInput || !preview || !audio) return;
                var url = (urlInput.value || '').trim();
                if (url) {
                    audio.src = url;
                    preview.classList.remove('is-empty');
                } else {
                    audio.removeAttribute('src');
                    preview.classList.add('is-empty');
                }
            }

            updatePreview();

            if (urlInput) {
                urlInput.addEventListener('input', function () {
                    updatePreview();
                    if (status) {
                        status.textContent = '';
                        status.className = 'admin-audio-status';
                    }
                });
            }

            if (uploadBtn && fileInput) {
                uploadBtn.addEventListener('click', function () {
                    fileInput.click();
                });

                fileInput.addEventListener('change', function () {
                    var file = fileInput.files && fileInput.files[0];
                    if (!file) return;

                    if (status) {
                        status.textContent = 'در حال آپلود…';
                        status.className = 'admin-audio-status';
                    }

                    uploadBtn.disabled = true;
                    var formData = new FormData();
                    formData.append('file', file);
                    var token = getAntiForgeryToken();
                    if (token) {
                        formData.append('__RequestVerificationToken', token);
                    }

                    fetch(uploadAudioUrl, {
                        method: 'POST',
                        body: formData
                    })
                        .then(function (res) { return res.json(); })
                        .then(function (data) {
                            if (data.success && data.url) {
                                urlInput.value = data.url;
                                updatePreview();
                                if (status) {
                                    status.textContent = 'آپلود موفق';
                                    status.className = 'admin-audio-status is-success';
                                }
                            } else if (status) {
                                status.textContent = data.message || 'خطا در آپلود';
                                status.className = 'admin-audio-status is-error';
                            }
                        })
                        .catch(function () {
                            if (status) {
                                status.textContent = 'خطا در ارتباط با سرور';
                                status.className = 'admin-audio-status is-error';
                            }
                        })
                        .finally(function () {
                            uploadBtn.disabled = false;
                            fileInput.value = '';
                        });
                });
            }

            if (clearBtn) {
                clearBtn.addEventListener('click', function () {
                    urlInput.value = '';
                    updatePreview();
                    if (status) {
                        status.textContent = '';
                        status.className = 'admin-audio-status';
                    }
                });
            }
        });
    }

    function createDatepickerPositioner(input) {
        var bound = false;

        function getContainer() {
            return document.querySelector('.datepicker-container');
        }

        function positionCalendar() {
            var container = getContainer();
            if (!container) return;

            container.style.position = 'fixed';
            container.style.zIndex = '2600';
            container.style.margin = '0';

            var rect = input.getBoundingClientRect();
            var margin = 8;
            var gap = 6;
            var vw = window.innerWidth;
            var vh = window.innerHeight;
            var cw = container.offsetWidth || 300;
            var ch = container.offsetHeight || 340;

            var top = rect.bottom + gap;
            if (top + ch > vh - margin) {
                top = rect.top - ch - gap;
            }
            if (top < margin) {
                top = Math.max(margin, vh - ch - margin);
            }

            var left = rect.right - cw;
            if (left + cw > vw - margin) {
                left = vw - cw - margin;
            }
            if (left < margin) {
                left = margin;
            }

            container.style.top = Math.round(top) + 'px';
            container.style.left = Math.round(left) + 'px';
            container.style.right = 'auto';
            container.style.bottom = 'auto';
        }

        function onScrollOrResize() {
            window.requestAnimationFrame(positionCalendar);
        }

        function bind() {
            if (bound) return;
            bound = true;
            window.addEventListener('scroll', onScrollOrResize, true);
            window.addEventListener('resize', onScrollOrResize);
        }

        function unbind() {
            if (!bound) return;
            bound = false;
            window.removeEventListener('scroll', onScrollOrResize, true);
            window.removeEventListener('resize', onScrollOrResize);
        }

        return {
            show: function () {
                window.requestAnimationFrame(function () {
                    positionCalendar();
                    bind();
                    window.requestAnimationFrame(positionCalendar);
                });
                setTimeout(positionCalendar, 0);
                setTimeout(positionCalendar, 40);
            },
            hide: function () {
                unbind();
            }
        };
    }

    function initJalaliFields(root) {
        if (typeof $ === 'undefined' || !$.fn.persianDatepicker) return;

        var scope = root || document;
        scope.querySelectorAll('.admin-jalali-input').forEach(function (input) {
            if (input.dataset.jalaliInit === '1') return;
            input.dataset.jalaliInit = '1';

            var targetSelector = input.getAttribute('data-target');
            var target = targetSelector ? document.querySelector(targetSelector) : null;
            if (!target) return;

            var wrap = input.closest('.admin-jalali-input-wrap');
            var trigger = wrap ? wrap.querySelector('.admin-jalali-trigger') : null;
            var positioner = createDatepickerPositioner(input);

            $(input).persianDatepicker({
                format: 'YYYY/MM/DD HH:mm',
                autoClose: true,
                zIndex: 2600,
                calendar: {
                    persian: { locale: 'fa' }
                },
                timePicker: {
                    enabled: true,
                    meridiem: { enabled: false }
                },
                toolbox: {
                    calendarSwitch: { enabled: false },
                    todayButton: { enabled: true },
                    submitButton: { enabled: true }
                },
                onSelect: function (unix) {
                    target.value = toHiddenDateValue(unix);
                },
                onShow: function () {
                    positioner.show();
                },
                onHide: function () {
                    positioner.hide();
                }
            });

            if (target.value) {
                var initial = new Date(target.value);
                if (!isNaN(initial.getTime())) {
                    $(input).persianDatepicker('setDate', initial.getTime());
                }
            }

            if (trigger) {
                trigger.addEventListener('click', function () {
                    input.focus();
                    $(input).persianDatepicker('show');
                });
            }

            input.addEventListener('click', function () {
                $(input).persianDatepicker('show');
            });
        });
    }

    function splitTagNames(value) {
        return String(value || '')
            .split(/[\n,،;|]+/)
            .map(function (s) { return s.trim(); })
            .filter(function (s) { return s.length > 0; });
    }

    function initTagFields(root) {
        (root || document).querySelectorAll('.admin-tags-field').forEach(function (field) {
            if (field.dataset.tagsInit === '1') return;
            field.dataset.tagsInit = '1';

            var hidden = field.querySelector('#tagNames') || field.querySelector('input[name="tagNames"]');
            var box = field.querySelector('.admin-tags-box');
            var list = field.querySelector('.admin-tags-list');
            var input = field.querySelector('.admin-tags-input');
            if (!hidden || !box || !list || !input) return;

            var tags = [];

            function syncHidden() {
                hidden.value = tags.join('\n');
            }

            function renderTags() {
                list.innerHTML = '';
                tags.forEach(function (tag, index) {
                    var chip = document.createElement('span');
                    chip.className = 'admin-tag-chip';
                    chip.textContent = tag;

                    var removeBtn = document.createElement('button');
                    removeBtn.type = 'button';
                    removeBtn.className = 'admin-tag-chip-remove';
                    removeBtn.innerHTML = '&times;';
                    removeBtn.setAttribute('aria-label', 'حذف ' + tag);
                    removeBtn.addEventListener('click', function () {
                        tags.splice(index, 1);
                        renderTags();
                        syncHidden();
                        input.focus();
                    });

                    chip.appendChild(removeBtn);
                    list.appendChild(chip);
                });
            }

            function addTag(raw) {
                var value = String(raw || '').trim();
                if (!value) return false;
                if (tags.some(function (t) { return t.toLowerCase() === value.toLowerCase(); })) {
                    return false;
                }
                tags.push(value);
                renderTags();
                syncHidden();
                return true;
            }

            splitTagNames(hidden.value).forEach(function (tag) {
                addTag(tag);
            });
            hidden.value = tags.join('\n');

            box.addEventListener('click', function () {
                input.focus();
            });

            input.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    if (addTag(input.value)) {
                        input.value = '';
                    }
                    return;
                }

                if (e.key === 'Backspace' && !input.value && tags.length) {
                    tags.pop();
                    renderTags();
                    syncHidden();
                }
            });

            input.addEventListener('blur', function () {
                if (input.value.trim()) {
                    if (addTag(input.value)) {
                        input.value = '';
                    }
                }
            });
        });
    }

    function initCategoryPickers(root) {
        (root || document).querySelectorAll('.admin-category-picker').forEach(function (field) {
            if (field.dataset.categoryInit === '1') return;
            field.dataset.categoryInit = '1';

            var type = field.getAttribute('data-category-type');
            var hidden = field.querySelector('#CategoryId') || field.querySelector('input[name="CategoryId"]');
            var searchInput = field.querySelector('.admin-category-search');
            var dropdown = field.querySelector('.admin-category-dropdown');
            var optionsList = field.querySelector('.admin-category-options');
            var selectedBar = field.querySelector('.admin-category-selected-bar');
            var selectedName = field.querySelector('.admin-category-selected-name');
            var clearBtn = field.querySelector('.admin-category-clear');
            var status = field.querySelector('.admin-category-status');
            if (!hidden || !searchInput || !dropdown || !optionsList) return;

            var categories = [];
            try {
                categories = JSON.parse(field.getAttribute('data-categories') || '[]');
            } catch (err) {
                categories = [];
            }

            var searchTimer = null;
            var activeIndex = -1;

            function setStatus(message, kind) {
                if (!status) return;
                status.textContent = message || '';
                status.className = 'admin-category-status' + (kind ? ' is-' + kind : '');
            }

            function hideDropdown() {
                dropdown.hidden = true;
                activeIndex = -1;
            }

            function positionDropdown() {
                var rect = searchInput.getBoundingClientRect();
                var width = Math.max(rect.width, 240);
                dropdown.style.width = width + 'px';
                dropdown.style.left = Math.round(rect.left) + 'px';
                dropdown.style.right = 'auto';
                dropdown.style.top = Math.round(rect.bottom + 6) + 'px';

                dropdown.hidden = false;
                var dropdownHeight = dropdown.offsetHeight || 260;
                if (rect.bottom + dropdownHeight + 12 > window.innerHeight) {
                    dropdown.style.top = Math.max(8, Math.round(rect.top - dropdownHeight - 6)) + 'px';
                }
            }

            function showDropdown() {
                positionDropdown();
            }

            function findLocal(query) {
                var term = String(query || '').trim().toLowerCase();
                if (!term) return categories.slice(0, 20);
                return categories.filter(function (c) {
                    return c.name.toLowerCase().indexOf(term) !== -1 ||
                        (c.slug && c.slug.toLowerCase().indexOf(term) !== -1);
                }).slice(0, 20);
            }

            function exactMatch(query) {
                var term = String(query || '').trim().toLowerCase();
                if (!term) return null;
                return categories.find(function (c) {
                    return c.name.toLowerCase() === term;
                }) || null;
            }

            function renderOptions(items, query) {
                optionsList.innerHTML = '';
                activeIndex = -1;

                if (!items.length && !query) {
                    var empty = document.createElement('li');
                    empty.className = 'admin-category-option admin-category-option--empty';
                    empty.textContent = 'دسته‌ای یافت نشد';
                    optionsList.appendChild(empty);
                    return;
                }

                items.forEach(function (item, index) {
                    var btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'admin-category-option';
                    btn.textContent = item.name;
                    btn.dataset.id = String(item.id);
                    btn.dataset.index = String(index);
                    btn.addEventListener('mousedown', function (e) {
                        e.preventDefault();
                        selectCategory(item);
                    });
                    optionsList.appendChild(btn);
                });

                var trimmed = String(query || '').trim();
                if (trimmed && !exactMatch(trimmed)) {
                    var createBtn = document.createElement('button');
                    createBtn.type = 'button';
                    createBtn.className = 'admin-category-option admin-category-option--create';
                    createBtn.textContent = 'ایجاد دسته «' + trimmed + '»';
                    createBtn.addEventListener('mousedown', function (e) {
                        e.preventDefault();
                        createCategory(trimmed);
                    });
                    optionsList.appendChild(createBtn);
                } else if (!items.length) {
                    var none = document.createElement('li');
                    none.className = 'admin-category-option admin-category-option--empty';
                    none.textContent = 'موردی یافت نشد — Enter برای ایجاد';
                    optionsList.appendChild(none);
                }
            }

            function selectCategory(item) {
                hidden.value = String(item.id);
                if (selectedBar && selectedName) {
                    selectedName.textContent = item.name;
                    selectedBar.hidden = false;
                }
                searchInput.value = '';
                hideDropdown();
                setStatus('');
            }

            function clearSelection() {
                hidden.value = '';
                if (selectedBar) selectedBar.hidden = true;
                searchInput.value = '';
                setStatus('');
                searchInput.focus();
            }

            function loadRemote(query) {
                var url = '/Admin/Categories/Search?type=' + encodeURIComponent(type) +
                    '&q=' + encodeURIComponent(query || '');
                fetch(url)
                    .then(function (res) { return res.json(); })
                    .then(function (items) {
                        categories = items || [];
                        field.setAttribute('data-categories', JSON.stringify(categories));
                        renderOptions(categories, query);
                        showDropdown();
                    })
                    .catch(function () {
                        renderOptions(findLocal(query), query);
                        showDropdown();
                    });
            }

            function createCategory(name) {
                setStatus('در حال ایجاد دسته…');
                var formData = new FormData();
                formData.append('type', type);
                formData.append('name', name);
                var token = getAntiForgeryToken();
                if (token) formData.append('__RequestVerificationToken', token);

                fetch('/Admin/Categories/QuickCreate', {
                    method: 'POST',
                    body: formData
                })
                    .then(function (res) {
                        return res.json().then(function (data) {
                            if (!res.ok) {
                                throw data;
                            }
                            return data;
                        });
                    })
                    .then(function (data) {
                        if (!data.success) {
                            setStatus(data.message || 'خطا در ایجاد دسته', 'error');
                            return;
                        }

                        var exists = categories.some(function (c) { return c.id === data.id; });
                        if (!exists) {
                            categories.push({ id: data.id, name: data.name, slug: data.slug });
                            field.setAttribute('data-categories', JSON.stringify(categories));
                        }

                        selectCategory({ id: data.id, name: data.name });
                        setStatus(data.existed ? 'دسته موجود انتخاب شد' : 'دسته جدید ایجاد شد', 'success');
                    })
                    .catch(function (err) {
                        setStatus((err && err.message) || 'خطا در ارتباط با سرور', 'error');
                    });
            }

            searchInput.addEventListener('focus', function () {
                loadRemote(searchInput.value.trim());
            });

            searchInput.addEventListener('input', function () {
                clearTimeout(searchTimer);
                var query = searchInput.value.trim();
                searchTimer = setTimeout(function () {
                    loadRemote(query);
                }, 250);
            });

            searchInput.addEventListener('keydown', function (e) {
                var optionButtons = optionsList.querySelectorAll('.admin-category-option:not(.admin-category-option--empty)');

                if (e.key === 'ArrowDown') {
                    e.preventDefault();
                    if (dropdown.hidden) showDropdown();
                    activeIndex = Math.min(activeIndex + 1, optionButtons.length - 1);
                } else if (e.key === 'ArrowUp') {
                    e.preventDefault();
                    activeIndex = Math.max(activeIndex - 1, 0);
                } else if (e.key === 'Enter') {
                    e.preventDefault();
                    var query = searchInput.value.trim();
                    if (activeIndex >= 0 && optionButtons[activeIndex]) {
                        optionButtons[activeIndex].dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
                        return;
                    }
                    var match = exactMatch(query);
                    if (match) {
                        selectCategory(match);
                        return;
                    }
                    if (query) createCategory(query);
                    return;
                } else if (e.key === 'Escape') {
                    hideDropdown();
                    return;
                } else {
                    return;
                }

                optionButtons.forEach(function (btn, idx) {
                    btn.classList.toggle('is-active', idx === activeIndex);
                });
                if (optionButtons[activeIndex]) {
                    optionButtons[activeIndex].scrollIntoView({ block: 'nearest' });
                }
            });

            searchInput.addEventListener('blur', function () {
                setTimeout(hideDropdown, 150);
            });

            window.addEventListener('resize', function () {
                if (!dropdown.hidden) positionDropdown();
            });

            var scrollHost = document.querySelector('.admin-main') || window;
            scrollHost.addEventListener('scroll', function () {
                if (!dropdown.hidden) positionDropdown();
            }, { passive: true });

            if (clearBtn) {
                clearBtn.addEventListener('click', clearSelection);
            }

            var selectedId = field.getAttribute('data-selected-id') || hidden.value;
            if (selectedId) {
                var selected = categories.find(function (c) { return String(c.id) === String(selectedId); });
                if (selected) {
                    selectCategory(selected);
                }
            }
        });
    }

    function initSidebar() {
        var groups = document.querySelectorAll('.admin-nav-group[data-nav-group]');
        if (!groups.length) return;

        groups.forEach(function (group) {
            var key = 'tabenAdminNav_' + group.getAttribute('data-nav-group');
            var toggle = group.querySelector('.admin-nav-group-toggle');
            if (!toggle) return;

            var saved = localStorage.getItem(key);
            if (saved === 'open') {
                group.classList.add('is-open');
                toggle.setAttribute('aria-expanded', 'true');
            } else if (saved === 'closed') {
                group.classList.remove('is-open');
                toggle.setAttribute('aria-expanded', 'false');
            }

            toggle.addEventListener('click', function () {
                var isOpen = group.classList.toggle('is-open');
                toggle.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
                localStorage.setItem(key, isOpen ? 'open' : 'closed');
            });
        });
    }

    function initElementorFormSubmit(root) {
        (root || document).querySelectorAll('form[data-elementor-form]').forEach(function (form) {
            if (form.dataset.elementorSubmitInit === '1') return;
            form.dataset.elementorSubmitInit = '1';

            form.addEventListener('submit', function (e) {
                if (!window.elementorBuilder || form.dataset.contentSaved === '1') return;
                e.preventDefault();
                elementorBuilder.save();
                form.dataset.contentSaved = '1';
                if (typeof form.requestSubmit === 'function') {
                    form.requestSubmit();
                } else {
                    form.submit();
                }
            });
        });
    }

    function initAdminForms() {
        initPriceFields();
        initImageFields();
        initAudioFields();
        initJalaliFields();
        initTagFields();
        initCategoryPickers();
        initEditorTabFullscreen();
        initElementorFormSubmit();
    }

    function initEditorTabFullscreen() {
        document.querySelectorAll('[data-bs-toggle="tab"][data-bs-target="#tab-basic"]').forEach(function (tab) {
            tab.addEventListener('shown.bs.tab', function () {
                if (window.elementorBuilder && typeof window.elementorBuilder.exitFullscreen === 'function') {
                    window.elementorBuilder.exitFullscreen();
                }
            });
        });
    }

    function initAdmin() {
        initSidebar();
        initAdminForms();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAdmin);
    } else {
        initAdmin();
    }

    window.tabenAdmin = {
        refresh: initAdminForms
    };
})();
