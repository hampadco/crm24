/* طراح قالب چاپ وردمانند: سرفصل/بدنه/پاورقی روی یک بوم A4 + پیش‌نمایش فقط‌خواندنی */
(function () {
    'use strict';

    var root = document.getElementById('ptDesigner');
    if (!root) return;

    var catalog = {};
    try {
        catalog = JSON.parse(document.getElementById('ptCatalog').textContent || '{}');
    } catch (e) {
        catalog = {};
    }

    var PARTS = ['header', 'body', 'footer'];
    var PART_LABELS = { header: 'سرفصل', body: 'بدنه', footer: 'پاورقی' };
    var activePart = 'body';
    var editors = {};
    var MM_TO_PX = 96 / 25.4;
    var syncTimer = null;
    var guideTimer = null;

    function band(part) { return root.querySelector('.pt-band[data-part="' + part + '"]'); }
    function textarea(part) { return root.querySelector('textarea[data-part="' + part + '"]'); }
    function preview(part) { return root.querySelector('.pt-band-preview[data-part="' + part + '"]'); }

    /**
     * float را برمی‌دارد؛ عرض px را به 100% محدود می‌کند؛ colgroup خراب را پاک می‌کند.
     * جدول نیم‌عرض → margin-inline-end:auto تا در RTL به راست بچسبد.
     */
    function sanitizePrintHtml(html) {
        if (!html) return html || '';
        var doc = new DOMParser().parseFromString('<div id="pt-sanitize-root">' + html + '</div>', 'text/html');
        var wrap = doc.getElementById('pt-sanitize-root');
        if (!wrap) return html;

        wrap.querySelectorAll('colgroup').forEach(function (cg) { cg.remove(); });
        wrap.querySelectorAll('table.ck-table-resized').forEach(function (t) {
            t.classList.remove('ck-table-resized');
            if (!t.className.trim()) t.removeAttribute('class');
        });

        wrap.querySelectorAll('figure.table, figure.image, table').forEach(function (el) {
            var style = el.getAttribute('style') || '';
            if (!style) {
                if (el.tagName === 'TABLE') el.setAttribute('width', '100%');
                return;
            }

            var floated = /float\s*:\s*(left|right)/i.test(style);
            var widthMatch = style.match(/width\s*:\s*([^;]+)/i);
            var hadStartAuto = /margin-inline-start\s*:\s*auto/i.test(style);
            var keepPartial = false;

            style = style
                .replace(/float\s*:\s*[^;]+;?/gi, '')
                .replace(/margin-inline-start\s*:\s*auto;?/gi, '')
                .replace(/\s*;\s*;+/g, ';')
                .replace(/^\s*;\s*|\s*;\s*$/g, '')
                .trim();

            if (widthMatch) {
                var w = String(widthMatch[1]).trim();
                var px = /^(\d+(?:\.\d+)?)px$/i.exec(w);
                var pct = /^(\d+(?:\.\d+)?)%$/.exec(w);
                if (px || (pct && parseFloat(pct[1]) > 100)) {
                    style = style.replace(/width\s*:\s*[^;]+;?/gi, 'width:100%;');
                    w = '100%';
                } else if (pct && parseFloat(pct[1]) < 100) {
                    keepPartial = true;
                }
            }

            if ((floated || hadStartAuto || keepPartial) && keepPartial
                && !/margin-inline-end\s*:\s*auto/i.test(style)) {
                style = (style ? style.replace(/;?\s*$/, ';') : '') + 'margin-inline-end:auto';
            }

            style = style.replace(/\s*;\s*;+/g, ';').replace(/^\s*;\s*|\s*;\s*$/g, '').trim();
            if (style) el.setAttribute('style', style);
            else el.removeAttribute('style');

            if (el.tagName === 'TABLE' && !keepPartial) {
                el.setAttribute('width', '100%');
            }
        });

        return wrap.innerHTML;
    }

    function applySanitizedData(ed) {
        if (!ed) return;
        try {
            var raw = ed.getData();
            var cleaned = sanitizePrintHtml(raw);
            if (cleaned && cleaned !== raw) {
                ed.setData(cleaned);
            }
        } catch (e) { /* ignore */ }
    }

    function partHtml(part) {
        var ed = editors[part];
        if (ed) {
            try { return sanitizePrintHtml(ed.getData()); }
            catch (e) { /* fall through */ }
        }
        var ta = textarea(part);
        return sanitizePrintHtml(ta ? ta.value : '');
    }

    function isBlankHtml(html) {
        if (!html) return true;
        var text = html
            .replace(/<br\s*\/?>/gi, '')
            .replace(/&nbsp;/gi, ' ')
            .replace(/<[^>]+>/g, '')
            .replace(/\s+/g, '')
            .trim();
        return !text;
    }

    // ── راه‌اندازی ویرایشگرها ───────────────────────────────────
    var PREMIUM = [
        'AIAssistant', 'MultiLevelList',
        'RealTimeCollaborativeComments', 'RealTimeCollaborativeTrackChanges',
        'RealTimeCollaborativeRevisionHistory', 'PresenceList', 'Comments',
        'TrackChanges', 'TrackChangesData', 'RevisionHistory', 'Pagination',
        'WProofreader', 'MathType', 'SlashCommand', 'Template', 'DocumentOutline',
        'FormatPainter', 'TableOfContents', 'PasteFromOfficeEnhanced', 'CaseChange',
        'TableColumnResize'
    ];

    var TOOLBAR = [
        'undo', 'redo', '|',
        'sourceEditing', 'findAndReplace', '|',
        'heading', 'fontSize', 'fontColor', 'fontBackgroundColor', '|',
        'bold', 'italic', 'underline', 'strikethrough', 'subscript', 'superscript', 'removeFormat', '|',
        'alignment:right', 'alignment:center', 'alignment:left', 'alignment:justify', '|',
        'bulletedList', 'numberedList', 'outdent', 'indent', '|',
        'insertTable', 'insertImage', 'link', 'horizontalLine', 'pageBreak', 'specialCharacters', '|',
        'htmlEmbed'
    ];

    function buildConfig() {
        var rtl = root.getAttribute('data-dir') !== 'ltr';
        return {
            licenseKey: '',
            removePlugins: PREMIUM,
            language: { ui: 'fa', content: rtl ? 'fa' : 'en' },
            toolbar: { items: TOOLBAR, shouldNotGroupWhenFull: true },
            htmlSupport: {
                allow: [{ name: /.*/, attributes: true, classes: true, styles: true }]
            },
            htmlEmbed: { showPreviews: true },
            alignment: {
                options: ['right', 'center', 'left', 'justify']
            },
            heading: {
                options: [
                    { model: 'paragraph', title: 'پاراگراف', class: 'ck-heading_paragraph' },
                    { model: 'heading1', view: 'h1', title: 'تیتر ۱', class: 'ck-heading_heading1' },
                    { model: 'heading2', view: 'h2', title: 'تیتر ۲', class: 'ck-heading_heading2' },
                    { model: 'heading3', view: 'h3', title: 'تیتر ۳', class: 'ck-heading_heading3' },
                    { model: 'heading4', view: 'h4', title: 'تیتر ۴', class: 'ck-heading_heading4' }
                ]
            },
            fontSize: {
                options: [8, 9, 10, 11, 12, 14, 16, 18, 22, 26, 32, 40],
                supportAllValues: true
            },
            fontColor: { columns: 6 },
            fontBackgroundColor: { columns: 6 },
            table: {
                contentToolbar: [
                    'tableColumn', 'tableRow', 'mergeTableCells',
                    'tableProperties', 'tableCellProperties'
                ],
                tableProperties: {
                    defaultProperties: {
                        borderStyle: 'solid',
                        borderColor: '#111827',
                        borderWidth: '1px'
                    }
                }
            },
            image: {
                toolbar: ['imageTextAlternative', 'imageStyle:inline', 'imageStyle:block', 'imageStyle:side']
            },
            link: { addTargetToExternalLinks: true }
        };
    }

    var toolbarHost = document.getElementById('ptCkToolbarHost');
    var stickySpacer = null;
    if (toolbarHost && toolbarHost.parentNode) {
        stickySpacer = document.createElement('div');
        stickySpacer.className = 'pt-ck-toolbar-spacer';
        stickySpacer.setAttribute('aria-hidden', 'true');
        toolbarHost.parentNode.insertBefore(stickySpacer, toolbarHost);
    }

    function navbarBottom() {
        var nav = document.getElementById('layout-navbar');
        if (!nav) return 64;
        var rect = nav.getBoundingClientRect();
        return Math.max(Math.ceil(rect.bottom > 0 ? rect.bottom : rect.height), 64);
    }

    function syncStickyOffset() {
        var top = Math.max(0, navbarBottom() - 1);
        root.style.setProperty('--pt-sticky-top', top + 'px');
        document.documentElement.style.setProperty('--pt-sticky-top', top + 'px');
    }

    function pinDesignerChrome() {
        if (!toolbarHost || !stickySpacer) return;
        if (toolbarHost.classList.contains('is-empty')) {
            stickySpacer.classList.remove('is-active');
            stickySpacer.style.height = '';
            toolbarHost.classList.remove('is-fixed');
            return;
        }

        syncStickyOffset();
        var top = Math.max(0, navbarBottom() - 1);
        var pinAt = stickySpacer.classList.contains('is-active')
            ? stickySpacer.getBoundingClientRect().top + window.scrollY
            : toolbarHost.getBoundingClientRect().top + window.scrollY;

        var shouldFix = window.scrollY + top >= pinAt;

        if (shouldFix) {
            var widthHost = root.getBoundingClientRect();
            stickySpacer.style.height = toolbarHost.offsetHeight + 'px';
            stickySpacer.classList.add('is-active');
            toolbarHost.classList.add('is-fixed');
            root.style.setProperty('--pt-sticky-left', Math.round(widthHost.left) + 'px');
            root.style.setProperty('--pt-sticky-width', Math.round(widthHost.width) + 'px');
            document.documentElement.style.setProperty('--pt-sticky-top', top + 'px');
            root.style.setProperty('--pt-sticky-top', top + 'px');
        } else {
            stickySpacer.classList.remove('is-active');
            stickySpacer.style.height = '';
            toolbarHost.classList.remove('is-fixed');
            root.style.removeProperty('--pt-sticky-left');
            root.style.removeProperty('--pt-sticky-width');
        }
    }

    syncStickyOffset();
    pinDesignerChrome();
    window.addEventListener('resize', function () {
        pinDesignerChrome();
        scheduleGuides();
    });
    window.addEventListener('scroll', pinDesignerChrome, { passive: true });
    window.addEventListener('load', function () {
        pinDesignerChrome();
        scheduleGuides();
    });

    function mountToolbar(part) {
        if (!toolbarHost) return;

        var prevPart = toolbarHost.getAttribute('data-part');
        var hosted = toolbarHost.querySelector('.ck-editor__top');

        if (hosted && prevPart && editors[prevPart] && editors[prevPart].ui.view.element) {
            var prevRoot = editors[prevPart].ui.view.element;
            if (!prevRoot.querySelector('.ck-editor__top')) {
                prevRoot.insertBefore(hosted, prevRoot.firstChild);
            }
        }

        var active = editors[part];
        if (!active || !active.ui || !active.ui.view || !active.ui.view.element) {
            toolbarHost.classList.add('is-empty');
            toolbarHost.removeAttribute('data-part');
            return;
        }

        var activeTop = active.ui.view.element.querySelector('.ck-editor__top');
        if (!activeTop) {
            toolbarHost.classList.add('is-empty');
            return;
        }

        toolbarHost.appendChild(activeTop);
        toolbarHost.setAttribute('data-part', part);
        toolbarHost.classList.remove('is-empty');
        pinDesignerChrome();
    }

    function pageHeightPx() {
        var raw = getComputedStyle(root).getPropertyValue('--pt-page-height').trim();
        var n = parseFloat(raw);
        return n > 0 ? n : Math.round(297 * MM_TO_PX);
    }

    function marginPx() {
        return {
            top: num('ptMarginTop', 12) * MM_TO_PX,
            right: num('ptMarginRight', 12) * MM_TO_PX,
            bottom: num('ptMarginBottom', 12) * MM_TO_PX,
            left: num('ptMarginLeft', 12) * MM_TO_PX
        };
    }

    function unpinFooterForMeasure(footerBand) {
        if (!footerBand) return;
        footerBand.classList.remove('is-pinned');
        footerBand.style.position = '';
        footerBand.style.top = '';
        footerBand.style.left = '';
        footerBand.style.right = '';
        footerBand.style.width = '';
    }

    function updatePageGuides() {
        var doc = document.getElementById('ptDocument');
        var guides = document.getElementById('ptPageGuides');
        var printable = document.getElementById('ptPrintable');
        var headerBand = band('header');
        var bodyBand = band('body');
        var footerBand = band('footer');
        if (!doc || !guides || !printable || !bodyBand) return;

        var pageH = pageHeightPx();
        var m = marginPx();

        // پاورقی را موقتاً به جریان برگردان تا ارتفاع واقعی‌اش را بگیریم
        unpinFooterForMeasure(footerBand);
        if (bodyBand) bodyBand.style.paddingBottom = '';

        var headerH = headerBand ? headerBand.offsetHeight : 0;
        var footerH = footerBand ? Math.max(footerBand.offsetHeight, 28) : 28;
        var bodyH = bodyBand.offsetHeight;

        // ارتفاع قابل استفادهٔ هر صفحه داخل حاشیه، با رزرو نوار پاورقی
        var innerPageH = Math.max(120, pageH - m.top - m.bottom);
        var page1Flow = Math.max(80, innerPageH - footerH - 8);
        var otherFlow = Math.max(80, innerPageH - headerH - footerH - 8);

        var remaining = Math.max(0, headerH + bodyH);
        var pages = 1;
        if (remaining > page1Flow) {
            remaining -= page1Flow;
            pages += Math.ceil(remaining / otherFlow);
        }
        pages = Math.max(1, pages);

        var totalH = pages * pageH;
        doc.style.minHeight = totalH + 'px';
        printable.style.minHeight = totalH + 'px';

        // پاورقی واقعی فقط پایین آخرین صفحه — دیگر وسط صفحه ۲ نمی‌افتد
        if (footerBand) {
            footerBand.classList.add('is-pinned');
            footerBand.style.top = (totalH - m.bottom - footerH) + 'px';
            bodyBand.style.paddingBottom = (footerH + 16) + 'px';
        }

        guides.innerHTML = '';
        for (var i = 0; i < pages; i++) {
            var label = document.createElement('div');
            label.className = 'pt-page-label';
            label.style.top = (i * pageH + 6) + 'px';
            label.textContent = 'صفحه ' + (i + 1);
            guides.appendChild(label);

            if (i > 0) {
                var line = document.createElement('div');
                line.className = 'pt-page-break-line';
                line.style.top = (i * pageH) + 'px';
                line.innerHTML = '<span>صفحه ' + (i + 1) + '</span>';
                guides.appendChild(line);
            }
        }

        updateGhosts(pages, pageH, footerH);
    }

    function scheduleGuides() {
        clearTimeout(guideTimer);
        guideTimer = setTimeout(updatePageGuides, 60);
    }

    function updateGhosts(pages, pageH, footerH) {
        var ghosts = document.getElementById('ptGhosts');
        if (!ghosts) return;
        ghosts.innerHTML = '';
        if (pages < 2) return;

        var headerHtml = partHtml('header');
        var footerHtml = partHtml('footer');
        var headerBlank = isBlankHtml(headerHtml);
        var footerBlank = isBlankHtml(footerHtml);
        if (headerBlank && footerBlank) return;

        var m = marginPx();
        var fh = Math.max(footerH || 28, 28);

        for (var i = 0; i < pages; i++) {
            var pageTop = i * pageH;

            // سرفصل شبح فقط از صفحه ۲ به بعد (پایین خط شکستن، بالای ناحیهٔ محتوا)
            if (i > 0 && !headerBlank) {
                var gh = document.createElement('div');
                gh.className = 'pt-ghost-band is-header ck-content pt-chrome-preview';
                gh.style.top = (pageTop + m.top) + 'px';
                gh.innerHTML = '<div class="pt-ghost-tag">' + PART_LABELS.header + ' (تکرار)</div>' + headerHtml;
                ghosts.appendChild(gh);
            }

            // پاورقی شبح روی صفحات قبل از آخر — دقیقاً پایین همان صفحه
            if (i < pages - 1 && !footerBlank) {
                var gf = document.createElement('div');
                gf.className = 'pt-ghost-band is-footer ck-content pt-chrome-preview';
                gf.style.top = (pageTop + pageH - m.bottom - fh) + 'px';
                gf.style.maxHeight = fh + 'px';
                gf.innerHTML = '<div class="pt-ghost-tag">' + PART_LABELS.footer + ' (تکرار)</div>' + footerHtml;
                ghosts.appendChild(gf);
            }
        }
    }

    function syncChromePreviews() {
        PARTS.forEach(function (p) {
            var el = preview(p);
            if (!el) return;
            if (p === activePart) {
                el.innerHTML = '';
                return;
            }
            var html = partHtml(p);
            el.innerHTML = isBlankHtml(html)
                ? '<span class="pt-chrome-empty">خالی — برای ویرایش کلیک کنید</span>'
                : html;
        });
        scheduleGuides();
    }

    function scheduleSync() {
        clearTimeout(syncTimer);
        syncTimer = setTimeout(syncChromePreviews, 120);
    }

    function bindEditorEvents(part, ed) {
        ed.model.document.on('change:data', scheduleSync);
        ed.ui.focusTracker.on('change:isFocused', function () {
            if (ed.ui.focusTracker.isFocused && activePart !== part) {
                showPart(part);
            }
        });
        // بعد از هر رندر ویو، رنگ سلول را از مدل دوباره روی DOM بکش
        // (وگرنه با عوض شدن فوکوس، استایل موقت DOM پاک می‌شود)
        try {
            var paintTimer = null;
            ed.editing.view.on('render', function () {
                clearTimeout(paintTimer);
                paintTimer = setTimeout(function () { syncAllCellBackgroundPaints(ed); }, 0);
            });
        } catch (e0) { /* ignore */ }
        try {
            var editable = ed.ui.getEditableElement();
            if (editable && window.ResizeObserver) {
                var ro = new ResizeObserver(scheduleGuides);
                ro.observe(editable);
            }
            if (editable) {
                editable.addEventListener('contextmenu', function (ev) {
                    if (part !== activePart) showPart(part);
                    openContextMenu(ev, ed);
                });
            }
        } catch (e) { /* ignore */ }
    }

    // ── منوی راست‌کلیک شبیه ورد ─────────────────────────────────
    var ctxMenu = null;
    var ctxHideBound = false;
    var ctxSnapshot = null; // { ed, cells, ranges }

    function ensureContextMenu() {
        if (ctxMenu) return ctxMenu;
        ctxMenu = document.createElement('div');
        ctxMenu.className = 'pt-ctx-menu';
        ctxMenu.id = 'ptCtxMenu';
        ctxMenu.setAttribute('role', 'menu');
        ctxMenu.hidden = true;
        // preventDefault حیاتی است — وگرنه فوکوس ادیتور و انتخاب سلول از بین می‌رود
        ctxMenu.addEventListener('mousedown', function (e) {
            e.preventDefault();
            e.stopPropagation();
        });
        document.body.appendChild(ctxMenu);
        return ctxMenu;
    }

    function hideContextMenu() {
        if (!ctxMenu) return;
        ctxMenu.hidden = true;
        ctxMenu.innerHTML = '';
    }

    function bindContextMenuDismiss() {
        if (ctxHideBound) return;
        ctxHideBound = true;
        document.addEventListener('mousedown', function (e) {
            if (!ctxMenu || ctxMenu.hidden) return;
            if (ctxMenu.contains(e.target)) return;
            hideContextMenu();
            ctxSnapshot = null;
        });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                hideContextMenu();
                ctxSnapshot = null;
            }
        });
        window.addEventListener('scroll', function () {
            hideContextMenu();
            ctxSnapshot = null;
        }, true);
        window.addEventListener('resize', function () {
            hideContextMenu();
            ctxSnapshot = null;
        });
    }

    function isInTableDom(target) {
        var el = target;
        while (el && el.nodeType === 1) {
            if (el.matches && (el.matches('td,th,table,figure.table') || el.classList.contains('table')))
                return true;
            if (el.classList && el.classList.contains('ck-editor__editable')) break;
            el = el.parentElement;
        }
        return false;
    }

    function commandEnabled(ed, name) {
        try {
            var cmd = ed.commands.get(name);
            return !!(cmd && cmd.isEnabled);
        } catch (e) {
            return false;
        }
    }

    function captureCtxSnapshot(ed) {
        var cells = getSelectedTableCells(ed);
        var ranges = [];
        try {
            ranges = Array.from(ed.model.document.selection.getRanges()).map(function (r) {
                return r.clone();
            });
        } catch (e) { /* ignore */ }
        ctxSnapshot = { ed: ed, cells: cells.slice(), ranges: ranges };
        return ctxSnapshot;
    }

    function liveSnapshotCells(ed) {
        var cells = (ctxSnapshot && ctxSnapshot.cells) ? ctxSnapshot.cells.slice() : [];
        cells = cells.filter(function (c) {
            try { return c && c.root && c.root.document === ed.model.document; }
            catch (e) { return false; }
        });
        if (cells.length) return cells;
        return getSelectedTableCells(ed);
    }

    function restoreCtxSelection(ed) {
        if (!ed || !ctxSnapshot || !ctxSnapshot.ranges || !ctxSnapshot.ranges.length) return false;
        try {
            ed.model.change(function (writer) {
                writer.setSelection(ctxSnapshot.ranges);
            });
            ed.editing.view.focus();
            return true;
        } catch (e) {
            return false;
        }
    }

    function runCommand(ed, name, value) {
        if (!ed) return;
        leaveSourceMode(ed);
        try {
            restoreCtxSelection(ed);
            ed.editing.view.focus();
            if (typeof value === 'undefined') ed.execute(name);
            else ed.execute(name, value);
            // به‌روزرسانی اسنپ‌شات بعد از دستورات انتخاب ردیف/ستون
            if (name === 'selectTableRow' || name === 'selectTableColumn') {
                captureCtxSnapshot(ed);
            }
        } catch (err) {
            console.warn('command failed', name, err);
        }
        scheduleSync();
    }

    function openCkUi(ed, componentName) {
        if (!ed || !ed.ui || !ed.ui.componentFactory || !ed.ui.componentFactory.has(componentName)) {
            return false;
        }
        try {
            leaveSourceMode(ed);
            restoreCtxSelection(ed);
            ed.editing.view.focus();
            var view = ed.ui.componentFactory.create(componentName);
            if (view && typeof view.fire === 'function') {
                view.fire('execute');
                if (view.element && !view.element.isConnected) {
                    document.body.appendChild(view.element);
                    view.fire('execute');
                    setTimeout(function () {
                        if (view.element && view.element.parentNode === document.body) {
                            view.element.remove();
                        }
                    }, 0);
                }
                return true;
            }
        } catch (err) {
            console.warn('openCkUi failed', componentName, err);
        }
        return false;
    }

    function getSelectedTableCells(ed) {
        if (!ed) return [];
        try {
            var plugin = ed.plugins.get('TableSelection');
            if (plugin && typeof plugin.getSelectedTableCells === 'function') {
                var selected = plugin.getSelectedTableCells();
                if (selected && selected.length) return Array.from(selected);
            }
        } catch (e) { /* ignore */ }

        var unique = [];
        try {
            var selection = ed.model.document.selection;
            var mark = typeof WeakSet !== 'undefined' ? new WeakSet() : null;
            Array.from(selection.getRanges()).forEach(function (range) {
                Array.from(range.getItems({ shallow: false })).forEach(function (item) {
                    if (!item || !item.is || !item.is('element', 'tableCell')) return;
                    if (mark) {
                        if (mark.has(item)) return;
                        mark.add(item);
                    } else if (unique.indexOf(item) >= 0) {
                        return;
                    }
                    unique.push(item);
                });
            });
            if (unique.length) return unique;

            var pos = selection.getFirstPosition();
            if (pos) {
                var node = pos.parent;
                while (node && node.name !== '$root') {
                    if (node.name === 'tableCell') {
                        unique.push(node);
                        break;
                    }
                    node = node.parent;
                }
            }
        } catch (e2) { /* ignore */ }
        return unique;
    }

    function paintDomCellBackground(ed, cell, color) {
        try {
            var mapper = ed.editing.mapper;
            var converter = ed.editing.view.domConverter;
            var viewEl = mapper.toViewElement(cell);
            if (!viewEl) return null;
            var dom = converter.mapViewToDom(viewEl);
            if (!dom) return null;
            var td = dom;
            if (td.nodeType === 1) {
                if (!(td.matches && td.matches('td,th'))) {
                    td = td.closest ? td.closest('td,th') : null;
                }
            } else {
                td = null;
            }
            if (!td) return null;

            if (color) {
                td.style.setProperty('background-color', color, 'important');
                td.setAttribute('data-pt-cell-bg', '1');
                td.style.setProperty('--pt-cell-bg', color);
            } else if (td.getAttribute('data-pt-cell-bg')) {
                // فقط رنگ‌هایی که خودمان زدیم را پاک کن — استایل قالب را دست نزن
                td.style.removeProperty('background-color');
                td.style.removeProperty('--pt-cell-bg');
                td.removeAttribute('data-pt-cell-bg');
            }
            return td;
        } catch (e) {
            return null;
        }
    }

    function cellBackgroundFromModel(cell) {
        if (!cell) return null;
        var color = cell.getAttribute('tableCellBackgroundColor');
        if (color) return color;
        var htmlAttrs = cell.getAttribute('htmlTdAttributes') || cell.getAttribute('htmlThAttributes');
        if (htmlAttrs && htmlAttrs.styles) {
            return htmlAttrs.styles['background-color']
                || htmlAttrs.styles.background
                || null;
        }
        return null;
    }

    function forEachTableCell(ed, fn) {
        try {
            var root = ed.model.document.getRoot();
            Array.from(ed.model.createRangeIn(root).getItems()).forEach(function (item) {
                if (item && item.is && item.is('element', 'tableCell')) fn(item);
            });
        } catch (e) { /* ignore */ }
    }

    function syncAllCellBackgroundPaints(ed) {
        if (!ed) return;
        forEachTableCell(ed, function (cell) {
            var color = cellBackgroundFromModel(cell);
            if (color) paintDomCellBackground(ed, cell, color);
        });
    }

    function setCellHtmlBgAttribute(writer, cell, color, attrName) {
        var old = cell.getAttribute(attrName);
        var base = old || { styles: {}, classes: [], attributes: {} };
        var styles = Object.assign({}, base.styles || {});
        if (color) styles['background-color'] = color;
        else delete styles['background-color'];

        var next = {
            styles: styles,
            classes: base.classes ? Array.from(base.classes) : [],
            attributes: Object.assign({}, base.attributes || {})
        };
        var empty = !Object.keys(styles).length
            && !(next.classes && next.classes.length)
            && !Object.keys(next.attributes).length;
        if (empty) {
            if (old) writer.removeAttribute(attrName, cell);
        } else {
            writer.setAttribute(attrName, next, cell);
        }
    }

    function getCellLocation(ed, cell) {
        try {
            var row = cell.parent;
            if (!row || row.name !== 'tableRow') return null;
            var table = row.parent;
            while (table && table.name !== 'table') table = table.parent;
            if (!table) return null;

            var tables = [];
            var root = ed.model.document.getRoot();
            Array.from(ed.model.createRangeIn(root).getItems()).forEach(function (item) {
                if (item && item.is && item.is('element', 'table')) tables.push(item);
            });
            var tableIndex = tables.indexOf(table);
            if (tableIndex < 0) return null;

            var rows = Array.from(table.getChildren());
            var rowIndex = rows.indexOf(row);
            var cellIndex = Array.from(row.getChildren()).indexOf(cell);
            if (rowIndex < 0 || cellIndex < 0) return null;
            return { tableIndex: tableIndex, rowIndex: rowIndex, cellIndex: cellIndex };
        } catch (e) {
            return null;
        }
    }

    function setTdBackgroundStyle(td, color) {
        var st = td.getAttribute('style') || '';
        st = st.replace(/background-color\s*:\s*[^;]+;?/gi, '').replace(/;;+/g, ';').trim();
        if (!color) {
            if (st) td.setAttribute('style', st);
            else td.removeAttribute('style');
            return;
        }
        if (st && st.slice(-1) !== ';') st += ';';
        st += 'background-color:' + color;
        td.setAttribute('style', st);
    }

    function injectBackgroundByLocations(html, locations, color) {
        var doc = new DOMParser().parseFromString('<div id="pt-bg-root">' + html + '</div>', 'text/html');
        var root = doc.getElementById('pt-bg-root');
        if (!root) return html;
        var tables = root.querySelectorAll('table');
        locations.forEach(function (loc) {
            if (!loc || loc.tableIndex < 0 || loc.tableIndex >= tables.length) return;
            var table = tables[loc.tableIndex];
            var rows = table.rows || table.querySelectorAll('tr');
            var tr = rows[loc.rowIndex];
            if (!tr) return;
            var cells = tr.cells || tr.querySelectorAll('th,td');
            var td = cells[loc.cellIndex];
            if (!td) return;
            setTdBackgroundStyle(td, color);
        });
        return root.innerHTML;
    }

    function cellHasBackgroundInHtml(html, loc, color) {
        if (!loc || !color) return false;
        try {
            var doc = new DOMParser().parseFromString('<div id="pt-bg-check">' + html + '</div>', 'text/html');
            var root = doc.getElementById('pt-bg-check');
            var tables = root.querySelectorAll('table');
            if (loc.tableIndex >= tables.length) return false;
            var rows = tables[loc.tableIndex].rows || tables[loc.tableIndex].querySelectorAll('tr');
            var tr = rows[loc.rowIndex];
            if (!tr) return false;
            var cells = tr.cells || tr.querySelectorAll('th,td');
            var td = cells[loc.cellIndex];
            if (!td) return false;
            var st = (td.getAttribute('style') || '').toLowerCase();
            return st.indexOf('background-color') >= 0 && st.indexOf(String(color).toLowerCase()) >= 0;
        } catch (e) {
            return false;
        }
    }

    function applyCellBackground(ed, color) {
        if (!ed) return;
        leaveSourceMode(ed);
        var cells = liveSnapshotCells(ed);
        if (!cells.length) {
            restoreCtxSelection(ed);
            cells = getSelectedTableCells(ed);
        }
        if (!cells.length) {
            console.warn('applyCellBackground: no cells');
            return;
        }

        var locations = cells.map(function (c) { return getCellLocation(ed, c); }).filter(Boolean);
        var clear = (color === null || color === '');

        // انتخاب رسمی سلول‌ها
        try {
            var tableSelection = ed.plugins.get('TableSelection');
            if (tableSelection && typeof tableSelection.setCellSelection === 'function') {
                tableSelection.setCellSelection(cells[0], cells[cells.length - 1]);
            }
        } catch (e) { /* ignore */ }

        // ۱) مدل رسمی + GHS
        try {
            ed.model.change(function (writer) {
                cells.forEach(function (cell) {
                    if (clear) writer.removeAttribute('tableCellBackgroundColor', cell);
                    else writer.setAttribute('tableCellBackgroundColor', color, cell);
                    setCellHtmlBgAttribute(writer, cell, clear ? null : color, 'htmlTdAttributes');
                    setCellHtmlBgAttribute(writer, cell, clear ? null : color, 'htmlThAttributes');
                });
            });
        } catch (err) {
            console.warn('applyCellBackground model failed', err);
        }

        // ۲) دستور رسمی CKEditor
        try {
            if (ed.commands.get('tableCellBackgroundColor')) {
                ed.execute('tableCellBackgroundColor', { value: clear ? null : color });
            }
        } catch (err3) { /* ignore */ }

        // ۳) اجبار ماندگاری از طریق HTML (همان مسیری که رنگ‌های قالب می‌مانند)
        //    رنگ فقط روی DOM با فوکوس دیده می‌شد؛ با setData در مدل/داده می‌نشیند.
        try {
            var html = ed.getData() || '';
            var needsPatch = clear || locations.some(function (loc) {
                return !cellHasBackgroundInHtml(html, loc, color);
            });
            if (needsPatch && locations.length) {
                var patched = injectBackgroundByLocations(html, locations, clear ? null : color);
                if (patched && patched !== html) {
                    ed.setData(sanitizePrintHtml(patched));
                }
            }
        } catch (err4) {
            console.warn('applyCellBackground data patch failed', err4);
        }

        // ۴) رنگ بصری پایدار (با !important) — بعد از setData روی سلول‌های جدید
        setTimeout(function () {
            syncAllCellBackgroundPaints(ed);
            // اگر clear بود، سلول‌های انتخاب‌شده را صریح پاک کن
            if (clear) {
                getSelectedTableCells(ed).forEach(function (cell) {
                    paintDomCellBackground(ed, cell, null);
                });
            }
            scheduleSync();
            captureCtxSnapshot(ed);
        }, 20);

        try { ed.editing.view.focus(); } catch (e) { /* ignore */ }
    }

    function applyFontStyle(ed, commandName, color) {
        if (!ed) return;
        leaveSourceMode(ed);
        restoreCtxSelection(ed);
        try {
            ed.editing.view.focus();
            if (color === null || color === '') {
                ed.execute(commandName, { value: null });
            } else {
                ed.execute(commandName, { value: color });
            }
        } catch (err) {
            console.warn(commandName + ' failed', err);
        }
        scheduleSync();
    }

    function applyCellStyle(ed, styles) {
        if (!ed) return;
        if (Object.prototype.hasOwnProperty.call(styles, 'tableCellBackgroundColor')
            && Object.keys(styles).length === 1) {
            applyCellBackground(ed, styles.tableCellBackgroundColor);
            return;
        }

        leaveSourceMode(ed);
        var cells = liveSnapshotCells(ed);
        if (!cells.length) return;
        try {
            ed.model.change(function (writer) {
                cells.forEach(function (cell) {
                    Object.keys(styles).forEach(function (key) {
                        var val = styles[key];
                        if (val === null || val === '') writer.removeAttribute(key, cell);
                        else writer.setAttribute(key, val, cell);
                    });
                });
            });
            ed.editing.view.focus();
        } catch (err) {
            console.warn('applyCellStyle failed', err);
        }
        scheduleSync();
    }

    function applyTableBorderNone(ed) {
        if (!ed) return;
        leaveSourceMode(ed);
        restoreCtxSelection(ed);
        var cells = liveSnapshotCells(ed);
        try {
            if (cells.length) {
                // فقط روی جدولِ سلول‌های انتخاب‌شده
                var table = cells[0];
                while (table && table.name !== 'table' && table.parent) table = table.parent;
                ed.model.change(function (writer) {
                    cells.forEach(function (cell) {
                        writer.setAttribute('tableCellBorderStyle', 'none', cell);
                        writer.setAttribute('tableCellBorderWidth', '0', cell);
                    });
                });
            }

            var editable = ed.ui.getEditableElement();
            var firstView = null;
            try {
                if (cells[0] && ed.editing.mapper) {
                    firstView = ed.editing.mapper.toViewElement(cells[0]);
                }
            } catch (e) { /* ignore */ }

            var domTd = firstView ? ed.editing.view.domConverter.mapViewToDom(firstView) : null;
            var tableEl = domTd ? domTd.closest('table') : null;
            if (!tableEl && editable) {
                var sel = editable.ownerDocument.getSelection();
                var node = sel && sel.anchorNode ? sel.anchorNode : null;
                var el = node && node.nodeType === 1 ? node : (node ? node.parentElement : null);
                while (el && el !== editable) {
                    if (el.tagName === 'TABLE') { tableEl = el; break; }
                    el = el.parentElement;
                }
            }
            if (tableEl) {
                tableEl.setAttribute('style', ((tableEl.getAttribute('style') || '') + ';border:none').replace(/^;/, ''));
                tableEl.querySelectorAll('td,th').forEach(function (cell) {
                    var st = cell.getAttribute('style') || '';
                    if (!/border\s*:/i.test(st)) cell.setAttribute('style', (st ? st + ';' : '') + 'border:none');
                    else cell.setAttribute('style', st.replace(/border\s*:[^;]+;?/gi, 'border:none;'));
                });
                ed.setData(sanitizePrintHtml(ed.getData()));
            }
            ed.editing.view.focus();
        } catch (err) {
            console.warn('applyTableBorderNone failed', err);
        }
        scheduleSync();
    }

    function ctxItem(label, icon, action, opts) {
        opts = opts || {};
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'pt-ctx-item' + (opts.accent ? ' is-accent' : '');
        btn.setAttribute('role', 'menuitem');
        btn.disabled = !!opts.disabled;
        btn.innerHTML = (icon ? '<i class="bx ' + icon + '"></i>' : '') + '<span>' + label + '</span>';
        if (!opts.disabled && action) {
            btn.addEventListener('mousedown', function (e) {
                e.preventDefault();
                e.stopPropagation();
            });
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                hideContextMenu();
                action();
            });
        }
        return btn;
    }

    function ctxSep() {
        var hr = document.createElement('div');
        hr.className = 'pt-ctx-sep';
        hr.setAttribute('role', 'separator');
        return hr;
    }

    function ctxLabel(text) {
        var el = document.createElement('div');
        el.className = 'pt-ctx-label';
        el.textContent = text;
        return el;
    }

    function appendSwatches(menu, colors, onPick) {
        var sw = document.createElement('div');
        sw.className = 'pt-ctx-swatches';
        colors.forEach(function (item) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'pt-ctx-swatch' + (item.clear ? ' is-clear' : '');
            b.title = item.title || (item.clear ? 'حذف' : item.c);
            if (item.c) b.style.background = item.c;
            b.addEventListener('mousedown', function (e) {
                e.preventDefault();
                e.stopPropagation();
            });
            b.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                hideContextMenu();
                onPick(item.c);
            });
            sw.appendChild(b);
        });
        menu.appendChild(sw);
    }

    function openInsertModal(id) {
        var el = document.getElementById(id);
        if (!el || !window.bootstrap) return;
        var inst = bootstrap.Modal.getInstance(el) || new bootstrap.Modal(el);
        inst.show();
    }

    var CELL_BG_COLORS = [
        { c: null, clear: true, title: 'شفاف' },
        { c: '#ffffff' },
        { c: '#f3f4f6' },
        { c: '#e5e7eb' },
        { c: '#dbeafe' },
        { c: '#dcfce7' },
        { c: '#fef3c7' },
        { c: '#fee2e2' },
        { c: '#6b7280' }
    ];
    var TEXT_COLORS = [
        { c: null, clear: true, title: 'پیش‌فرض' },
        { c: '#111827' },
        { c: '#374151' },
        { c: '#dc2626' },
        { c: '#2563eb' },
        { c: '#059669' },
        { c: '#d97706' },
        { c: '#ffffff' }
    ];
    var TEXT_BG_COLORS = [
        { c: null, clear: true, title: 'بدون پس‌زمینه' },
        { c: '#fef3c7' },
        { c: '#dbeafe' },
        { c: '#dcfce7' },
        { c: '#fee2e2' },
        { c: '#f3f4f6' },
        { c: '#e5e7eb' }
    ];

    function appendIconRow(menu, items) {
        var row = document.createElement('div');
        row.className = 'pt-ctx-icon-row';
        items.forEach(function (it) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'pt-ctx-icon-btn';
            b.title = it.title;
            b.setAttribute('aria-label', it.title);
            b.disabled = !!it.disabled;
            b.innerHTML = '<i class="bx ' + it.icon + '"></i>';
            if (!it.disabled && it.action) {
                b.addEventListener('mousedown', function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                });
                b.addEventListener('click', function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    hideContextMenu();
                    it.action();
                });
            }
            row.appendChild(b);
        });
        menu.appendChild(row);
    }

    function appendTextFormatSection(menu, ed) {
        menu.appendChild(ctxLabel('متن'));
        appendIconRow(menu, [
            { title: 'ضخیم', icon: 'bx-bold', action: function () { runCommand(ed, 'bold'); }, disabled: !commandEnabled(ed, 'bold') },
            { title: 'ایتالیک', icon: 'bx-italic', action: function () { runCommand(ed, 'italic'); }, disabled: !commandEnabled(ed, 'italic') },
            { title: 'زیرخط', icon: 'bx-underline', action: function () { runCommand(ed, 'underline'); }, disabled: !commandEnabled(ed, 'underline') },
            { title: 'پاک کردن قالب', icon: 'bx-eraser', action: function () { runCommand(ed, 'removeFormat'); }, disabled: !commandEnabled(ed, 'removeFormat') }
        ]);
        menu.appendChild(ctxLabel('تراز'));
        appendIconRow(menu, [
            { title: 'راست', icon: 'bx-align-right', action: function () { runCommand(ed, 'alignment', { value: 'right' }); } },
            { title: 'وسط', icon: 'bx-align-middle', action: function () { runCommand(ed, 'alignment', { value: 'center' }); } },
            { title: 'چپ', icon: 'bx-align-left', action: function () { runCommand(ed, 'alignment', { value: 'left' }); } },
            { title: 'هم‌تراز', icon: 'bx-align-justify', action: function () { runCommand(ed, 'alignment', { value: 'justify' }); } }
        ]);
        menu.appendChild(ctxLabel('رنگ متن'));
        appendSwatches(menu, TEXT_COLORS, function (c) { applyFontStyle(ed, 'fontColor', c); });
        menu.appendChild(ctxLabel('پس‌زمینه متن'));
        appendSwatches(menu, TEXT_BG_COLORS, function (c) { applyFontStyle(ed, 'fontBackgroundColor', c); });
    }

    function buildContextMenuItems(ed, inTable) {
        var menu = ensureContextMenu();
        menu.innerHTML = '';
        var snap = captureCtxSnapshot(ed);
        var selectedCount = snap.cells.length;

        if (inTable) {
            menu.appendChild(ctxLabel(selectedCount > 1
                ? ('جدول — ' + selectedCount + ' سلول')
                : 'جدول'));

            appendIconRow(menu, [
                { title: 'انتخاب ردیف', icon: 'bx-menu', action: function () { runCommand(ed, 'selectTableRow'); }, disabled: !commandEnabled(ed, 'selectTableRow') },
                { title: 'انتخاب ستون', icon: 'bx-columns', action: function () { runCommand(ed, 'selectTableColumn'); }, disabled: !commandEnabled(ed, 'selectTableColumn') },
                { title: 'سطر بالا', icon: 'bx-plus', action: function () { runCommand(ed, 'insertTableRowAbove'); }, disabled: !commandEnabled(ed, 'insertTableRowAbove') },
                { title: 'سطر پایین', icon: 'bx-subdirectory-left', action: function () { runCommand(ed, 'insertTableRowBelow'); }, disabled: !commandEnabled(ed, 'insertTableRowBelow') },
                { title: 'ستون راست', icon: 'bx-dock-right', action: function () { runCommand(ed, 'insertTableColumnRight'); }, disabled: !commandEnabled(ed, 'insertTableColumnRight') },
                { title: 'ستون چپ', icon: 'bx-dock-left', action: function () { runCommand(ed, 'insertTableColumnLeft'); }, disabled: !commandEnabled(ed, 'insertTableColumnLeft') },
                { title: 'حذف سطر', icon: 'bx-trash', action: function () { runCommand(ed, 'removeTableRow'); }, disabled: !commandEnabled(ed, 'removeTableRow') },
                { title: 'حذف ستون', icon: 'bx-x', action: function () { runCommand(ed, 'removeTableColumn'); }, disabled: !commandEnabled(ed, 'removeTableColumn') }
            ]);

            var mergeIcons = [];
            if (commandEnabled(ed, 'mergeTableCells')) {
                mergeIcons.push({ title: 'ادغام', icon: 'bx-git-merge', action: function () { runCommand(ed, 'mergeTableCells'); } });
            }
            if (commandEnabled(ed, 'splitTableCellVertically')) {
                mergeIcons.push({ title: 'جدا عمودی', icon: 'bx-git-repo-forked', action: function () { runCommand(ed, 'splitTableCellVertically'); } });
            }
            if (commandEnabled(ed, 'splitTableCellHorizontally')) {
                mergeIcons.push({ title: 'جدا افقی', icon: 'bx-git-branch', action: function () { runCommand(ed, 'splitTableCellHorizontally'); } });
            }
            if (mergeIcons.length) appendIconRow(menu, mergeIcons);

            appendIconRow(menu, [
                { title: 'ویژگی سلول', icon: 'bx-grid-alt', action: function () {
                    if (!openCkUi(ed, 'tableCellProperties')) runCommand(ed, 'tableCellProperties');
                } },
                { title: 'ویژگی جدول', icon: 'bx-table', action: function () {
                    if (!openCkUi(ed, 'tableProperties')) runCommand(ed, 'tableProperties');
                } },
                { title: 'پس‌زمینه شفاف', icon: 'bx-color-fill', action: function () { applyCellBackground(ed, null); } },
                { title: 'بدون حاشیه', icon: 'bx-square', action: function () { applyTableBorderNone(ed); } }
            ]);

            menu.appendChild(ctxLabel(selectedCount > 1 ? 'پس‌زمینه سلول‌ها' : 'پس‌زمینه سلول'));
            appendSwatches(menu, CELL_BG_COLORS, function (c) { applyCellBackground(ed, c); });

            menu.appendChild(ctxSep());
            appendTextFormatSection(menu, ed);
        } else {
            appendTextFormatSection(menu, ed);
        }

        var footer = document.createElement('div');
        footer.className = 'pt-ctx-footer';
        footer.appendChild(ctxSep());
        footer.appendChild(ctxItem('افزودن بلاک', 'bx-layer-plus', function () {
            openInsertModal('ptBlockModal');
        }, { accent: true }));
        footer.appendChild(ctxItem('افزودن فیلد / تابع', 'bx-purchase-tag-alt', function () {
            openInsertModal('ptFieldModal');
        }, { accent: true }));
        menu.appendChild(footer);
    }

    function placeContextMenu(x, y) {
        var menu = ensureContextMenu();
        menu.hidden = false;
        menu.style.left = '0px';
        menu.style.top = '0px';
        var rect = menu.getBoundingClientRect();
        var left = x;
        var top = y;
        var pad = 8;
        if (left + rect.width > window.innerWidth - pad) left = window.innerWidth - rect.width - pad;
        if (top + rect.height > window.innerHeight - pad) top = window.innerHeight - rect.height - pad;
        if (left < pad) left = pad;
        if (top < pad) top = pad;
        menu.style.left = Math.round(left) + 'px';
        menu.style.top = Math.round(top) + 'px';
    }

    function openContextMenu(ev, ed) {
        if (!ed) return;
        ev.preventDefault();
        ev.stopPropagation();
        bindContextMenuDismiss();
        var inTable = isInTableDom(ev.target);
        buildContextMenuItems(ed, inTable);
        placeContextMenu(ev.clientX, ev.clientY);
    }

    function createEditors() {
        var Classic = window.CKEDITOR && window.CKEDITOR.ClassicEditor;
        if (!Classic) {
            root.classList.add('pt-plain');
            showPart('body');
            syncChromePreviews();
            return;
        }

        pinDesignerChrome();

        var config = buildConfig();
        var pending = PARTS.length;
        PARTS.forEach(function (part) {
            Classic.create(textarea(part), config)
                .then(function (ed) {
                    editors[part] = ed;
                    try {
                        ed.editing.view.change(function (writer) {
                            writer.setAttribute('dir', root.getAttribute('data-dir') || 'rtl', ed.editing.view.document.getRoot());
                        });
                    } catch (e) { /* ignore */ }

                    bindEditorEvents(part, ed);

                    pending -= 1;
                    if (pending === 0) {
                        PARTS.forEach(function (p) { applySanitizedData(editors[p]); });
                        showPart('body');
                        applyPageChrome();
                        syncChromePreviews();
                    }
                })
                .catch(function (err) {
                    console.error('CKEditor init failed for ' + part, err);
                    pending -= 1;
                    if (pending === 0) {
                        root.classList.add('pt-plain');
                        showPart('body');
                        syncChromePreviews();
                    }
                });
        });
    }

    function activeEditor() { return editors[activePart] || null; }

    function showPart(part) {
        if (PARTS.indexOf(part) < 0) return;
        activePart = part;
        hideContextMenu();

        PARTS.forEach(function (p) {
            var el = band(p);
            if (!el) return;
            var isActive = p === part;
            el.classList.toggle('is-active', isActive);
            el.classList.toggle('is-readonly', !isActive);

            var editorWrap = el.querySelector('.pt-band-editor');
            var previewEl = el.querySelector('.pt-band-preview');
            if (editorWrap) editorWrap.classList.toggle('d-none', !isActive);
            if (previewEl) previewEl.classList.toggle('d-none', isActive);
        });

        root.querySelectorAll('.pt-part-tab').forEach(function (tab) {
            tab.classList.toggle('active', tab.getAttribute('data-part') === part);
        });

        mountToolbar(part);
        syncChromePreviews();

        var ed = editors[part];
        if (ed) {
            try { ed.editing.view.focus(); } catch (e) { /* ignore */ }
        }
    }

    root.querySelectorAll('.pt-part-tab').forEach(function (tab) {
        tab.addEventListener('click', function () { showPart(tab.getAttribute('data-part')); });
    });

    root.querySelectorAll('.pt-band').forEach(function (el) {
        el.addEventListener('click', function (ev) {
            if (!el.classList.contains('is-readonly')) return;
            if (ev.target.closest('a,button,input,textarea,select')) return;
            showPart(el.getAttribute('data-part'));
        });
    });

    // ── دکمه‌های تراز سریع ──────────────────────────────────────
    root.querySelectorAll('[data-align]').forEach(function (btn) {
        btn.addEventListener('mousedown', function (e) { e.preventDefault(); });
        btn.addEventListener('click', function () {
            var ed = activeEditor();
            if (!ed) return;
            leaveSourceMode(ed);
            ed.editing.view.focus();
            var value = btn.getAttribute('data-align');
            var cmd = ed.commands.get('alignment');
            if (cmd) {
                try { ed.execute('alignment', { value: value }); }
                catch (err) { console.warn('alignment failed', err); }
            }
        });
    });

    // ── درج محتوا ───────────────────────────────────────────────
    function insertHtml(html, asBlock) {
        var ed = activeEditor();
        if (!ed) {
            var ta = textarea(activePart);
            var start = ta.selectionStart || 0;
            var end = ta.selectionEnd || start;
            ta.value = ta.value.slice(0, start) + html + ta.value.slice(end);
            ta.focus();
            ta.selectionStart = ta.selectionEnd = start + html.length;
            scheduleSync();
            return;
        }

        leaveSourceMode(ed);
        ed.editing.view.focus();

        ed.model.change(function (writer) {
            var fragment = ed.data.toModel(ed.data.processor.toView(html));
            if (asBlock) {
                ed.model.insertContent(fragment, rootInsertPosition(ed, writer));
                return;
            }
            if (!ed.model.document.selection.getFirstPosition()) {
                writer.setSelection(writer.createPositionAt(ed.model.document.getRoot(), 'end'));
            }
            ed.model.insertContent(fragment);
        });
        scheduleSync();
    }

    function rootInsertPosition(ed, writer) {
        var docRoot = ed.model.document.getRoot();
        var position = ed.model.document.selection.getFirstPosition();
        if (!position) return writer.createPositionAt(docRoot, 'end');

        var node = position.parent;
        while (node && node.parent && node.parent !== docRoot) node = node.parent;
        return node && node.parent === docRoot
            ? writer.createPositionAfter(node)
            : writer.createPositionAt(docRoot, 'end');
    }

    function leaveSourceMode(ed) {
        if (!ed.plugins.has('SourceEditing')) return;
        var plugin = ed.plugins.get('SourceEditing');
        if (plugin.isSourceEditingMode) plugin.isSourceEditingMode = false;
    }

    // ── اندازه کاغذ + حاشیه + جهت (لایو) ─────────────────────────
    var pageSizes = {};
    try {
        pageSizes = JSON.parse(root.getAttribute('data-page-sizes') || '{}');
    } catch (e) {
        pageSizes = { A4: [210, 297] };
    }

    function num(id, fallback) {
        var el = document.getElementById(id);
        if (!el) return fallback;
        var v = parseInt(el.value, 10);
        return isNaN(v) ? fallback : Math.max(0, Math.min(60, v));
    }

    function applyPageChrome() {
        var sizeName = (root.getAttribute('data-page-size') || 'A4').toUpperCase();
        var dims = pageSizes[sizeName] || pageSizes.A4 || [210, 297];
        var landscapeEl = document.getElementById('ptDesignLandscape');
        var landscape = landscapeEl ? landscapeEl.value === 'true' : root.getAttribute('data-landscape') === '1';
        var wMm = landscape ? dims[1] : dims[0];
        var hMm = landscape ? dims[0] : dims[1];

        var mt = num('ptMarginTop', 12);
        var mr = num('ptMarginRight', 12);
        var mb = num('ptMarginBottom', 12);
        var ml = num('ptMarginLeft', 12);

        var dirEl = document.getElementById('ptDesignTextDirection');
        var dir = dirEl ? dirEl.value : (root.getAttribute('data-dir') || 'rtl');
        root.setAttribute('data-dir', dir);

        root.style.setProperty('--pt-page-width', Math.round(wMm * MM_TO_PX) + 'px');
        root.style.setProperty('--pt-page-height', Math.round(hMm * MM_TO_PX) + 'px');
        root.style.setProperty('--pt-margin-top', mt + 'mm');
        root.style.setProperty('--pt-margin-right', mr + 'mm');
        root.style.setProperty('--pt-margin-bottom', mb + 'mm');
        root.style.setProperty('--pt-margin-left', ml + 'mm');

        var label = document.getElementById('ptPageSizeLabel');
        if (label) {
            label.textContent = sizeName + ' · ' + (landscape ? 'افقی' : 'عمودی') + ' · ' + wMm + '×' + hMm + 'mm';
        }

        Object.keys(editors).forEach(function (key) {
            var ed = editors[key];
            if (!ed) return;
            try {
                ed.editing.view.change(function (writer) {
                    writer.setAttribute('dir', dir, ed.editing.view.document.getRoot());
                });
            } catch (e) { /* ignore */ }
        });

        document.querySelectorAll('.pt-band .ck-editor__editable, .pt-chrome-preview').forEach(function (el) {
            el.setAttribute('dir', dir);
        });

        scheduleGuides();
    }

    ['ptMarginTop', 'ptMarginRight', 'ptMarginBottom', 'ptMarginLeft', 'ptDesignTextDirection', 'ptDesignLandscape']
        .forEach(function (id) {
            var el = document.getElementById(id);
            if (!el) return;
            el.addEventListener('input', applyPageChrome);
            el.addEventListener('change', applyPageChrome);
        });

    applyPageChrome();
    var wrap = root.querySelector('.pt-editor-wrap');
    if (wrap) wrap.classList.add('page-width');

    var printableEl = document.getElementById('ptPrintable');
    if (printableEl && window.ResizeObserver) {
        new ResizeObserver(scheduleGuides).observe(printableEl);
    }

    // ── مودال افزودن فیلد / تابع ────────────────────────────────
    var fieldModalEl = document.getElementById('ptFieldModal');
    var fieldNav = fieldModalEl.querySelector('.pt-picker-nav');
    var fieldBody = fieldModalEl.querySelector('.pt-picker-body');
    var fieldSearch = document.getElementById('ptFieldSearch');
    var selectedToken = null;

    var GROUPS = [
        { key: 'company', label: 'فیلدهای شرکت' },
        { key: 'record', label: 'فیلدهای رکورد' },
        { key: 'related', label: 'فیلدهای بلاک‌های مرتبط' },
        { key: 'custom', label: 'فیلدهای سفارشی' },
        { key: 'functions', label: 'توابع سفارشی' },
        { key: 'inventory', label: 'فیلدهای موجودی' }
    ];
    var activeGroup = 'record';
    var activeRelated = null;

    function tokensFor(group) {
        if (group === 'related') {
            var groups = catalog.related || [];
            if (!groups.length) return [];
            var picked = groups.filter(function (g) { return !activeRelated || g.key === activeRelated; });
            return (picked[0] || groups[0]).tokens || [];
        }
        return catalog[group] || [];
    }

    function escapeHtml(text) {
        return String(text == null ? '' : text)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function renderFieldNav() {
        fieldNav.innerHTML = '';
        GROUPS.forEach(function (g) {
            var count = g.key === 'related'
                ? (catalog.related || []).length
                : (catalog[g.key] || []).length;
            if (!count) return;
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.textContent = g.label;
            btn.className = g.key === activeGroup ? 'active' : '';
            btn.addEventListener('click', function () {
                activeGroup = g.key;
                renderFieldNav();
                renderFieldBody();
            });
            fieldNav.appendChild(btn);
        });
    }

    function renderFieldBody() {
        var term = (fieldSearch.value || '').trim().toLowerCase();
        fieldBody.innerHTML = '';

        if (activeGroup === 'related' && (catalog.related || []).length) {
            var picker = document.createElement('select');
            picker.className = 'form-select form-select-sm mb-2';
            (catalog.related || []).forEach(function (g) {
                var opt = document.createElement('option');
                opt.value = g.key;
                opt.textContent = g.label;
                if (g.key === activeRelated) opt.selected = true;
                picker.appendChild(opt);
            });
            picker.addEventListener('change', function () {
                activeRelated = picker.value;
                renderFieldBody();
            });
            var hint = document.createElement('div');
            hint.className = 'pt-hint mb-1';
            hint.textContent = 'انتخاب فیلدهای ماژول';
            fieldBody.appendChild(hint);
            fieldBody.appendChild(picker);
            if (!activeRelated) activeRelated = catalog.related[0].key;
        }

        var list = document.createElement('div');
        list.className = 'pt-token-list';
        var rows = tokensFor(activeGroup).filter(function (t) {
            return !term || t.label.toLowerCase().indexOf(term) >= 0 || t.token.toLowerCase().indexOf(term) >= 0;
        });

        if (!rows.length) {
            list.innerHTML = '<div class="text-muted small py-3 text-center">موردی یافت نشد.</div>';
        }

        rows.forEach(function (t) {
            var row = document.createElement('div');
            row.className = 'pt-token-row';
            row.innerHTML = '<span>' + escapeHtml(t.label) + '</span><code>' + escapeHtml(t.token) + '</code>';
            row.addEventListener('click', function () {
                list.querySelectorAll('.pt-token-row').forEach(function (r) { r.classList.remove('selected'); });
                row.classList.add('selected');
                selectedToken = t.token;
            });
            row.addEventListener('dblclick', function () {
                selectedToken = t.token;
                insertToken();
            });
            list.appendChild(row);
        });

        fieldBody.appendChild(list);
    }

    fieldSearch.addEventListener('input', renderFieldBody);

    function insertToken() {
        if (!selectedToken) return;
        insertHtml(selectedToken);
        bootstrap.Modal.getInstance(fieldModalEl).hide();
    }

    document.getElementById('ptInsertField').addEventListener('click', insertToken);

    document.getElementById('ptCopyField').addEventListener('click', function () {
        if (!selectedToken) return;
        navigator.clipboard.writeText(selectedToken).then(function () {
            var btn = document.getElementById('ptCopyField');
            var old = btn.innerHTML;
            btn.innerHTML = '<i class="bx bx-check me-1"></i> کپی شد';
            setTimeout(function () { btn.innerHTML = old; }, 1500);
        });
    });

    fieldModalEl.addEventListener('show.bs.modal', function () {
        selectedToken = null;
        renderFieldNav();
        renderFieldBody();
    });

    // ── مودال افزودن بلاک ───────────────────────────────────────
    var blockModalEl = document.getElementById('ptBlockModal');
    var blockNav = blockModalEl.querySelector('.pt-picker-nav');
    var blockBody = blockModalEl.querySelector('.pt-picker-body');
    var activeBlockGroup = 'blocks';
    var selectedBlock = null;

    function renderBlockNav() {
        blockNav.innerHTML = '';
        [
            { key: 'blocks', label: 'بلاک‌های سفارشی' },
            { key: 'relatedBlocks', label: 'بلاک‌های مرتبط' }
        ].forEach(function (g) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.textContent = g.label;
            btn.className = g.key === activeBlockGroup ? 'active' : '';
            btn.addEventListener('click', function () {
                activeBlockGroup = g.key;
                selectedBlock = null;
                renderBlockNav();
                renderBlockBody();
            });
            blockNav.appendChild(btn);
        });
    }

    function renderBlockBody() {
        blockBody.innerHTML = '';
        var grid = document.createElement('div');
        grid.className = 'pt-block-grid';
        var previewBox = document.createElement('div');
        previewBox.className = 'pt-block-preview';
        previewBox.innerHTML = '<span class="text-muted">یک بلاک را انتخاب کنید تا پیش‌نمایش آن نمایش داده شود.</span>';

        (catalog[activeBlockGroup] || []).forEach(function (b) {
            var card = document.createElement('div');
            card.className = 'pt-block-card';
            card.innerHTML = '<strong>' + escapeHtml(b.label) + '</strong><small>' + escapeHtml(b.description) + '</small>';
            card.addEventListener('click', function () {
                grid.querySelectorAll('.pt-block-card').forEach(function (c) { c.classList.remove('selected'); });
                card.classList.add('selected');
                selectedBlock = b.html;
                previewBox.innerHTML = b.html;
            });
            card.addEventListener('dblclick', function () {
                selectedBlock = b.html;
                insertBlock();
            });
            grid.appendChild(card);
        });

        blockBody.appendChild(grid);
        blockBody.appendChild(previewBox);
    }

    function insertBlock() {
        if (!selectedBlock) return;
        insertHtml(selectedBlock, true);
        bootstrap.Modal.getInstance(blockModalEl).hide();
    }

    document.getElementById('ptInsertBlock').addEventListener('click', insertBlock);

    blockModalEl.addEventListener('show.bs.modal', function () {
        selectedBlock = null;
        renderBlockNav();
        renderBlockBody();
    });

    // ── ذخیره ───────────────────────────────────────────────────
    document.getElementById('ptDesignForm').addEventListener('submit', function () {
        PARTS.forEach(function (p) {
            var ed = editors[p];
            if (!ed) return;
            leaveSourceMode(ed);
            textarea(p).value = sanitizePrintHtml(ed.getData());
        });
    });

    createEditors();
})();
