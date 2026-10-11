(function () {
    var palette = ['#2fbe8f', '#4c8dff', '#3dcc7a', '#f5c451', '#7c6cf0', '#38bdf8', '#fb7185', '#94a3b8'];

    function faNum(value) {
        return Number(value || 0).toLocaleString('fa-IR');
    }

    function openRecords(el, point) {
        var moduleName = el.getAttribute('data-module');
        var field = el.getAttribute('data-field');
        if (!moduleName || !field || !point || point.raw === undefined || point.raw === null || point.raw === '')
            return;
        var params = new URLSearchParams();
        params.set('cf_' + field, point.raw);
        params.set('op_' + field, 'equals');
        window.location.assign('/App/m/' + encodeURIComponent(moduleName) + '?' + params.toString());
    }

    function showMessage(el, text) {
        el.innerHTML = '';
        var box = document.createElement('div');
        box.className = 'crm-dash-empty';
        var ring = document.createElement('span');
        ring.className = 'crm-dash-empty-ring';
        var icon = document.createElement('i');
        icon.className = 'bx bx-box';
        var label = document.createElement('p');
        label.textContent = text;
        box.append(ring, icon, label);
        el.appendChild(box);
    }

    function legendList(el, series, meta) {
        var ul = document.createElement('ul');
        ul.className = 'crm-chart-legend';
        series.forEach(function (item, index) {
            var li = document.createElement('li');
            var dot = document.createElement('i');
            dot.style.background = palette[index % palette.length];
            var name = document.createElement('span');
            name.textContent = item.label;
            li.append(dot, name);
            if (meta) {
                var extra = document.createElement('b');
                extra.textContent = meta(item, index);
                li.appendChild(extra);
            }
            if (item.raw) {
                li.classList.add('is-link');
                li.title = 'نمایش رکوردها';
                li.addEventListener('click', function () { openRecords(el, item); });
            }
            ul.appendChild(li);
        });
        return ul;
    }

    function mountPlot(el, split, series, meta) {
        el.innerHTML = '';
        if (!split) {
            var plain = document.createElement('div');
            plain.dir = 'ltr';
            el.appendChild(plain);
            return plain;
        }
        var wrap = document.createElement('div');
        wrap.className = 'crm-chart-split';
        wrap.appendChild(legendList(el, series, meta));
        var plot = document.createElement('div');
        plot.className = 'crm-chart-plot';
        plot.dir = 'ltr';
        wrap.appendChild(plot);
        el.appendChild(wrap);
        return plot;
    }

    function renderFunnel(el, series) {
        var wrap = document.createElement('div');
        wrap.className = 'crm-chart-split';
        wrap.appendChild(legendList(el, series, function (item) { return faNum(item.value); }));
        var plot = document.createElement('div');
        plot.className = 'crm-funnel-plot';
        var n = series.length;
        var width = 320;
        var band = 34;
        var gap = 8;
        var height = n * band + Math.max(0, n - 1) * gap;
        var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
        series.forEach(function (item, index) {
            var y = index * (band + gap);
            var topInset = width * 0.34 * (index / Math.max(n, 1));
            var botInset = width * 0.34 * ((index + 1) / Math.max(n, 1));
            var path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            path.setAttribute('fill', palette[index % palette.length]);
            path.setAttribute('d',
                'M ' + topInset + ' ' + y +
                ' L ' + (width - topInset) + ' ' + y +
                ' L ' + (width - botInset) + ' ' + (y + band) +
                ' L ' + botInset + ' ' + (y + band) + ' Z');
            if (item.raw) {
                path.style.cursor = 'pointer';
                path.addEventListener('click', function () { openRecords(el, item); });
            }
            svg.appendChild(path);
        });
        plot.appendChild(svg);
        wrap.appendChild(plot);
        el.innerHTML = '';
        el.appendChild(wrap);
    }

    function destroyChart(el) {
        if (!el._chart) return;
        try { el._chart.destroy(); } catch (err) { /* already gone */ }
        el._chart = null;
    }

    function chartPixelHeight(el) {
        var height = Math.floor(el.getBoundingClientRect().height);
        return height > 80 ? height : 180;
    }

    function renderChart(el, type, series, months) {
        var height = chartPixelHeight(el);
        destroyChart(el);
        if (!series || !series.length || (type !== 'monthly' && series.every(function (s) { return !s.value; }))) {
            showMessage(el, 'داده‌ای نیست');
            return;
        }

        if (type === 'funnel') {
            renderFunnel(el, series);
            return;
        }

        if (typeof ApexCharts === 'undefined') {
            showMessage(el, 'نمودار در دسترس نیست');
            return;
        }

        var plot;
        var options;

        if (type === 'pie') {
            var total = series.reduce(function (sum, item) { return sum + Number(item.value || 0); }, 0);
            plot = mountPlot(el, true, series, function (item) {
                if (!total) return '۰٪';
                return faNum(Math.round(item.value * 100 / total)) + '٪';
            });
            el._points = series;
            options = {
                chart: { type: 'donut', height: height, fontFamily: 'inherit', toolbar: { show: false }, parentHeightOffset: 0 },
                series: series.map(function (s) { return s.value; }),
                labels: series.map(function (s) { return s.label; }),
                colors: palette,
                legend: { show: false },
                dataLabels: { enabled: false },
                stroke: { width: 4, colors: ['#fff'] },
                plotOptions: {
                    pie: {
                        donut: {
                            size: '74%',
                            labels: {
                                show: true,
                                total: {
                                    show: true,
                                    label: 'مجموع',
                                    color: '#7b8fa3',
                                    fontSize: '13px',
                                    formatter: function (w) {
                                        return w.globals.seriesTotals.reduce(function (a, b) { return a + b; }, 0).toLocaleString('fa-IR');
                                    }
                                },
                                value: {
                                    fontSize: '22px',
                                    fontWeight: 800,
                                    color: '#16324f',
                                    formatter: function (val) { return faNum(val); }
                                }
                            }
                        }
                    }
                },
                tooltip: { y: { formatter: function (val) { return faNum(val); } } }
            };
        } else if (type === 'bar') {
            var ordered = series.map(function (item, index) { return { item: item, index: index }; }).reverse();
            var values = ordered.map(function (row) { return row.item.value; });
            var max = Math.max.apply(null, values.concat([1]));
            plot = mountPlot(el, false);
            el._points = ordered.map(function (row) { return row.item; });
            options = {
                chart: { type: 'bar', height: height, fontFamily: 'inherit', toolbar: { show: false }, parentHeightOffset: 0 },
                series: [{ name: 'تعداد', data: values }],
                xaxis: {
                    categories: ordered.map(function (row) { return row.item.label; }),
                    axisBorder: { show: false },
                    axisTicks: { show: false },
                    labels: { style: { colors: '#7b8fa3', fontSize: '12px', fontFamily: 'inherit' }, trim: true }
                },
                yaxis: { show: false, min: 0, max: Math.ceil(max * 1.28) },
                colors: ordered.map(function (row) { return palette[row.index % palette.length]; }),
                plotOptions: {
                    bar: { borderRadius: 8, columnWidth: '46%', distributed: true, dataLabels: { position: 'top' } }
                },
                legend: { show: false },
                dataLabels: {
                    enabled: height >= 160,
                    offsetY: -12,
                    formatter: function (val) { return faNum(val); },
                    style: { fontSize: '12px', fontWeight: 700, colors: ['#3d5166'] }
                },
                grid: { show: false },
                tooltip: { y: { formatter: function (val) { return faNum(val); } } }
            };
        } else {
            var windowed = series.slice(Math.max(0, series.length - (months || 6))).slice().reverse();
            plot = mountPlot(el, false);
            options = {
                chart: { type: 'area', height: height, fontFamily: 'inherit', toolbar: { show: false }, parentHeightOffset: 0 },
                series: [{ name: 'رکورد', data: windowed.map(function (s) { return s.value; }) }],
                xaxis: {
                    categories: windowed.map(function (s) { return s.label; }),
                    axisBorder: { show: false },
                    axisTicks: { show: false },
                    labels: { style: { colors: '#7b8fa3', fontSize: '12px', fontFamily: 'inherit' } }
                },
                yaxis: { show: false },
                colors: ['#2fbe8f'],
                fill: {
                    type: 'gradient',
                    gradient: { shadeIntensity: 0.4, opacityFrom: 0.35, opacityTo: 0.03, stops: [0, 100] }
                },
                stroke: { width: 3, curve: 'smooth' },
                dataLabels: { enabled: false },
                markers: { size: 0, hover: { size: 5 } },
                grid: {
                    borderColor: '#eef3f8',
                    strokeDashArray: 0,
                    xaxis: { lines: { show: false } }
                },
                tooltip: { y: { formatter: function (val) { return faNum(val); } } }
            };
        }

        if (el.getAttribute('data-field')) {
            options.chart.events = {
                dataPointSelection: function (_event, _ctx, config) {
                    openRecords(el, (el._points || [])[config.dataPointIndex]);
                }
            };
        }

        var chart = new ApexCharts(plot, options);
        el._chart = chart;
        chart.render();
    }

    function redrawCharts() {
        document.querySelectorAll('.widget-chart').forEach(function (el) {
            if (!el._series) return;
            var range = document.querySelector('[data-widget-range="' + el.getAttribute('data-widget-id') + '"]');
            renderChart(el, el.getAttribute('data-type'), el._series, range ? Number(range.value) || 6 : 6);
        });
    }

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-widget-types] button');
        if (!btn) return;
        var group = btn.closest('[data-widget-types]');
        var id = group.getAttribute('data-widget-types');
        var type = btn.getAttribute('data-type');
        var chartEl = document.querySelector('.widget-chart[data-widget-id="' + id + '"]');
        if (!chartEl || !chartEl._series) return;
        group.querySelectorAll('button').forEach(function (item) {
            item.classList.toggle('is-on', item === btn);
        });
        chartEl.setAttribute('data-type', type);
        renderChart(chartEl, type, chartEl._series, 6);
        var tokenInput = document.querySelector('.crm-dash input[name="__RequestVerificationToken"]');
        if (!tokenInput) return;
        fetch('/App/dashboard/widgets/' + id + '/type', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'RequestVerificationToken': tokenInput.value
            },
            body: 'type=' + encodeURIComponent(type)
        });
    });

    document.addEventListener('change', function (e) {
        var sel = e.target.closest('[data-widget-range]');
        if (!sel) return;
        var chartEl = document.querySelector('.widget-chart[data-widget-id="' + sel.getAttribute('data-widget-range') + '"]');
        if (!chartEl || !chartEl._series) return;
        renderChart(chartEl, 'monthly', chartEl._series, Number(sel.value) || 6);
    });

    var resizeTimer;
    window.addEventListener('resize', function () {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(redrawCharts, 180);
    });

    function sortKey(zone) {
        return Array.prototype.map.call(zone.querySelectorAll('[data-sort-id]'), function (el) {
            return el.getAttribute('data-sort-id');
        }).join(',');
    }

    function saveSort(zone) {
        var key = sortKey(zone);
        if (!key || zone._saved === key) return;
        zone._saved = key;
        var tokenInput = document.querySelector('.crm-dash input[name="__RequestVerificationToken"]');
        if (!tokenInput) return;
        var group = zone.getAttribute('data-dash-sort');
        var url = group === 'month' ? '/App/dashboard/month-order' : '/App/dashboard/widgets/reorder';
        var body = group === 'month'
            ? 'names=' + encodeURIComponent(key)
            : 'group=' + encodeURIComponent(group) + '&ids=' + encodeURIComponent(key);
        fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'RequestVerificationToken': tokenInput.value
            },
            body: body
        }).then(function (res) {
            if (!res.ok) zone._saved = '';
        }).catch(function () {
            zone._saved = '';
        });
    }

    function placeCard(zone, dragging, target, event) {
        if (!target || target === dragging || !target.hasAttribute('data-sort-id')) return;
        var rect = target.getBoundingClientRect();
        var dy = event.clientY - (rect.top + rect.height / 2);
        var dx = event.clientX - (rect.left + rect.width / 2);
        var rtl = getComputedStyle(zone).direction === 'rtl';
        var after = Math.abs(dy) > Math.abs(dx) ? dy > 0 : (rtl ? dx < 0 : dx > 0);
        var mark = target.getAttribute('data-sort-id') + (after ? ':a' : ':b');
        if (zone._place === mark) return;
        zone._place = mark;
        zone.insertBefore(dragging, after ? target.nextElementSibling : target);
    }

    function cssNum(el, name, fallback) {
        var n = parseInt(getComputedStyle(el).getPropertyValue(name), 10);
        return n > 0 ? n : fallback;
    }

    function readBox(card) {
        return {
            x: cssNum(card, '--gx', 1),
            y: cssNum(card, '--gy', 1),
            w: cssNum(card, '--gw', 2),
            h: cssNum(card, '--gh', 1)
        };
    }

    function writeBox(card, box) {
        card.style.setProperty('--gx', String(box.x));
        card.style.setProperty('--gy', String(box.y));
        card.style.setProperty('--gw', String(box.w));
        card.style.setProperty('--gh', String(box.h));
    }

    function overlap(a, b) {
        return a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
    }

    function otherBoxes(zone, except) {
        var list = [];
        zone.querySelectorAll('.is-chart[data-sort-id]').forEach(function (el) {
            if (el === except) return;
            var box = readBox(el);
            box.el = el;
            list.push(box);
        });
        return list;
    }

    function canPlace(box, others) {
        if (box.x < 1 || box.y < 1 || box.w < 1 || box.h < 1 || box.h > 3 || box.y > 8) return false;
        if (box.x + box.w - 1 > 4) return false;
        for (var i = 0; i < others.length; i++) {
            if (overlap(box, others[i])) return false;
        }
        return true;
    }

    function gridMetrics(zone) {
        var style = getComputedStyle(zone);
        var rect = zone.getBoundingClientRect();
        var gap = parseFloat(style.columnGap) || 0;
        var col = (rect.width - gap * 3) / 4;
        var row = parseFloat(style.gridAutoRows);
        if (!row || row < 40) row = 304;
        return { rect: rect, stepX: col + gap, stepY: row + gap, rtl: style.direction === 'rtl' };
    }

    function cellAt(zone, clientX, clientY) {
        var m = gridMetrics(zone);
        var localX = m.rtl ? (m.rect.right - clientX) : (clientX - m.rect.left);
        var x = Math.floor(localX / m.stepX) + 1;
        var y = Math.floor((clientY - m.rect.top) / m.stepY) + 1;
        if (x < 1) x = 1;
        if (x > 4) x = 4;
        if (y < 1) y = 1;
        if (y > 8) y = 8;
        return { x: x, y: y };
    }

    function layoutKey(zone) {
        var parts = [];
        zone.querySelectorAll('.is-chart[data-sort-id]').forEach(function (el) {
            var b = readBox(el);
            parts.push(el.getAttribute('data-sort-id') + ':' + b.x + ',' + b.y + ',' + b.w + ',' + b.h);
        });
        return parts.join(';');
    }

    function saveLayout(zone) {
        var key = layoutKey(zone);
        if (!key || zone._saved === key) return;
        zone._saved = key;
        var tokenInput = document.querySelector('.crm-dash input[name="__RequestVerificationToken"]');
        if (!tokenInput) return;
        fetch('/App/dashboard/chart-layout', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'RequestVerificationToken': tokenInput.value
            },
            body: 'layout=' + encodeURIComponent(key)
        }).then(function (res) {
            if (!res.ok) zone._saved = '';
        }).catch(function () {
            zone._saved = '';
        });
    }

    function redrawCard(card) {
        var el = card.querySelector('.widget-chart');
        if (!el || !el._series) return;
        var range = card.querySelector('[data-widget-range]');
        renderChart(el, el.getAttribute('data-type'), el._series, range ? Number(range.value) || 6 : 6);
    }

    function bindChartLayout(zone) {
        zone._saved = layoutKey(zone);
        zone.querySelectorAll('.is-chart[data-sort-id]').forEach(function (card) {
            var handle = card.querySelector('.crm-dash-grip');
            if (handle) {
                handle.addEventListener('pointerdown', function () {
                    card.setAttribute('draggable', 'true');
                });
                handle.addEventListener('pointerup', function () {
                    if (!card.classList.contains('is-dragging'))
                        card.setAttribute('draggable', 'false');
                });
                card.addEventListener('dragstart', function (event) {
                    if (card.getAttribute('draggable') !== 'true') {
                        event.preventDefault();
                        return;
                    }
                    zone._drag = card;
                    card._origin = readBox(card);
                    card.classList.add('is-dragging');
                    event.dataTransfer.effectAllowed = 'move';
                    event.dataTransfer.setData('text/plain', card.getAttribute('data-sort-id') || '');
                });
                card.addEventListener('dragend', function () {
                    card.classList.remove('is-dragging');
                    card.setAttribute('draggable', 'false');
                    zone._drag = null;
                    zone._place = '';
                    saveLayout(zone);
                    requestAnimationFrame(function () { redrawCard(card); });
                });
            }

            var resize = card.querySelector('.crm-dash-resize');
            if (!resize) return;
            resize.addEventListener('pointerdown', function (event) {
                event.preventDefault();
                event.stopPropagation();
                var start = readBox(card);
                var origin = { x: event.clientX, y: event.clientY };
                card.classList.add('is-resizing');
                resize.setPointerCapture(event.pointerId);

                function move(ev) {
                    var metrics = gridMetrics(zone);
                    var dx = metrics.rtl ? (origin.x - ev.clientX) : (ev.clientX - origin.x);
                    var dy = ev.clientY - origin.y;
                    var next = {
                        x: start.x,
                        y: start.y,
                        w: Math.max(1, Math.min(4 - start.x + 1, start.w + Math.round(dx / metrics.stepX))),
                        h: Math.max(1, Math.min(3, start.h + Math.round(dy / metrics.stepY)))
                    };
                    var others = otherBoxes(zone, card);
                    if (!canPlace(next, others)) {
                        var wide = { x: next.x, y: next.y, w: next.w, h: start.h };
                        var tall = { x: next.x, y: next.y, w: start.w, h: next.h };
                        if (canPlace(wide, others)) next = wide;
                        else if (canPlace(tall, others)) next = tall;
                        else return;
                    }
                    writeBox(card, next);
                }

                function up() {
                    resize.removeEventListener('pointermove', move);
                    resize.removeEventListener('pointerup', up);
                    card.classList.remove('is-resizing');
                    saveLayout(zone);
                    requestAnimationFrame(function () { redrawCard(card); });
                }

                resize.addEventListener('pointermove', move);
                resize.addEventListener('pointerup', up);
            });
        });

        zone.addEventListener('dragover', function (event) {
            var dragging = zone._drag;
            if (!dragging) return;
            event.preventDefault();
            var cell = cellAt(zone, event.clientX, event.clientY);
            var size = readBox(dragging);
            var next = { x: cell.x, y: cell.y, w: size.w, h: size.h };
            if (next.x + next.w - 1 > 4) next.x = 5 - next.w;
            var mark = next.x + ',' + next.y;
            if (zone._place === mark) return;
            var others = otherBoxes(zone, dragging);
            if (canPlace(next, others)) {
                zone._place = mark;
                writeBox(dragging, next);
                return;
            }
            var hit = null;
            for (var i = 0; i < others.length; i++) {
                if (overlap(next, others[i])) {
                    hit = others[i];
                    break;
                }
            }
            if (!hit || !dragging._origin) return;
            var moved = { x: next.x, y: next.y, w: size.w, h: size.h };
            var back = { x: dragging._origin.x, y: dragging._origin.y, w: hit.w, h: hit.h };
            var rest = others.filter(function (item) { return item.el !== hit.el; });
            if (!canPlace(moved, rest.concat([back])) || !canPlace(back, rest.concat([moved]))) return;
            zone._place = mark;
            writeBox(hit.el, back);
            writeBox(dragging, moved);
        });
    }

    var boardRoot = document.getElementById('dashGroups');
    var boardCols = 12;
    var boardDrag = null;
    var boardHover = null;
    var boardSaved = '';
    var boardWide = window.matchMedia('(min-width: 992px)').matches;

    function boardAttr(card, name, fallback) {
        var n = parseInt(card.getAttribute(name), 10);
        return n > 0 ? n : fallback;
    }

    function boardOthers(zone, except) {
        var list = [];
        if (!zone) return list;
        zone.querySelectorAll('[data-key]').forEach(function (el) {
            if (el === except) return;
            var box = readBox(el);
            box.el = el;
            list.push(box);
        });
        return list;
    }

    function boardCan(box, others) {
        if (box.x < 1 || box.y < 1 || box.w < 1 || box.h < 1 || box.y > 40 || box.h > 9) return false;
        if (box.x + box.w - 1 > boardCols) return false;
        for (var i = 0; i < others.length; i++) {
            if (overlap(box, others[i])) return false;
        }
        return true;
    }

    function boardMetrics(zone) {
        var style = getComputedStyle(zone);
        var rect = zone.getBoundingClientRect();
        var gap = parseFloat(style.columnGap) || 0;
        var col = (rect.width - gap * (boardCols - 1)) / boardCols;
        var row = parseFloat(style.gridAutoRows);
        if (!row || row < 40) row = 99;
        return { rect: rect, stepX: col + gap, stepY: row + gap, rtl: style.direction === 'rtl' };
    }

    function boardCellAt(zone, clientX, clientY) {
        var m = boardMetrics(zone);
        var localX = m.rtl ? (m.rect.right - clientX) : (clientX - m.rect.left);
        var x = Math.floor(localX / m.stepX) + 1;
        var y = Math.floor((clientY - m.rect.top) / m.stepY) + 1;
        if (x < 1) x = 1;
        if (x > boardCols) x = boardCols;
        if (y < 1) y = 1;
        if (y > 40) y = 40;
        return { x: x, y: y };
    }

    function boardFree(zone, w, h, except) {
        var others = boardOthers(zone, except);
        for (var y = 1; y <= 40; y++) {
            for (var x = 1; x <= boardCols; x++) {
                var box = { x: x, y: y, w: w, h: h };
                if (boardCan(box, others)) return box;
            }
        }
        return { x: 1, y: 1, w: w, h: h };
    }

    function boardSnapshot() {
        var groups = [];
        boardRoot.querySelectorAll('.crm-dash-group').forEach(function (section) {
            var title = section.querySelector('[data-group-title]');
            var name = title ? title.textContent.trim() : '';
            var items = [];
            section.querySelectorAll('[data-key]').forEach(function (el) {
                var b = readBox(el);
                items.push({ k: el.getAttribute('data-key'), x: b.x, y: b.y, w: b.w, h: b.h });
            });
            groups.push({ t: name || 'گروه', i: items });
        });
        return JSON.stringify({ v: 2, groups: groups });
    }

    function saveBoard() {
        if (!boardRoot) return;
        var key = boardSnapshot();
        if (key === boardSaved) return;
        boardSaved = key;
        var tokenInput = document.querySelector('.crm-dash input[name="__RequestVerificationToken"]');
        if (!tokenInput) return;
        fetch('/App/dashboard/board', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'RequestVerificationToken': tokenInput.value
            },
            body: 'layout=' + encodeURIComponent(key)
        }).then(function (res) {
            if (!res.ok) boardSaved = '';
        }).catch(function () {
            boardSaved = '';
        });
    }

    function startResize(handle, event) {
        var card = handle.closest('[data-key]');
        var zone = card && card.parentElement;
        if (!card || !zone) return;
        event.preventDefault();
        event.stopPropagation();
        var start = readBox(card);
        var origin = { x: event.clientX, y: event.clientY };
        var minW = boardAttr(card, 'data-min-w', 3);
        var minH = boardAttr(card, 'data-min-h', 1);
        var maxH = boardAttr(card, 'data-max-h', 3);
        card.classList.add('is-resizing');
        handle.setPointerCapture(event.pointerId);

        function move(ev) {
            var metrics = boardMetrics(zone);
            var dx = metrics.rtl ? (origin.x - ev.clientX) : (ev.clientX - origin.x);
            var dy = ev.clientY - origin.y;
            var next = {
                x: start.x,
                y: start.y,
                w: Math.max(minW, Math.min(boardCols - start.x + 1, start.w + Math.round(dx / metrics.stepX))),
                h: Math.max(minH, Math.min(maxH, start.h + Math.round(dy / metrics.stepY)))
            };
            var others = boardOthers(zone, card);
            if (!boardCan(next, others)) {
                var wide = { x: next.x, y: next.y, w: next.w, h: start.h };
                var tall = { x: next.x, y: next.y, w: start.w, h: next.h };
                if (boardCan(wide, others)) next = wide;
                else if (boardCan(tall, others)) next = tall;
                else return;
            }
            writeBox(card, next);
        }

        function up() {
            handle.removeEventListener('pointermove', move);
            handle.removeEventListener('pointerup', up);
            card.classList.remove('is-resizing');
            saveBoard();
            requestAnimationFrame(function () { redrawCard(card); });
        }

        handle.addEventListener('pointermove', move);
        handle.addEventListener('pointerup', up);
    }

    function startGroupDrag(grip, event) {
        var section = grip.closest('.crm-dash-group');
        if (!section) return;
        event.preventDefault();
        function move(ev) {
            var before = null;
            boardRoot.querySelectorAll('.crm-dash-group').forEach(function (other) {
                if (other === section || before) return;
                var rect = other.getBoundingClientRect();
                if (ev.clientY < rect.top + rect.height / 2) before = other;
            });
            boardRoot.insertBefore(section, before);
        }
        function up() {
            grip.removeEventListener('pointermove', move);
            grip.removeEventListener('pointerup', up);
            saveBoard();
        }
        grip.addEventListener('pointermove', move);
        grip.addEventListener('pointerup', up);
    }

    if (boardRoot) {
        boardSaved = boardSnapshot();
        boardRoot.addEventListener('click', function (event) {
            var remove = event.target.closest('[data-group-remove]');
            if (!remove || !boardRoot.contains(remove)) return;
            var section = remove.closest('.crm-dash-group');
            var groups = boardRoot.querySelectorAll('.crm-dash-group');
            if (!section || groups.length < 2) return;
            var list = Array.prototype.slice.call(groups);
            var index = list.indexOf(section);
            var target = list[index === 0 ? 1 : index - 1];
            var zone = target.querySelector('[data-grid]');
            section.querySelectorAll('[data-key]').forEach(function (card) {
                var size = readBox(card);
                zone.appendChild(card);
                writeBox(card, boardFree(zone, size.w, size.h, card));
            });
            section.remove();
            saveBoard();
        });
        boardRoot.addEventListener('focusout', function (event) {
            if (event.target.matches && event.target.matches('[data-group-title]')) saveBoard();
        });
        boardRoot.addEventListener('keydown', function (event) {
            if (event.target.matches && event.target.matches('[data-group-title]') && event.key === 'Enter') {
                event.preventDefault();
                event.target.blur();
            }
        });
        var addGroup = document.getElementById('dashAddGroup');
        if (addGroup) {
            addGroup.addEventListener('click', function () {
                if (boardRoot.querySelectorAll('.crm-dash-group').length >= 12) return;
                var tpl = document.getElementById('dashGroupTpl');
                if (!tpl) return;
                var node = tpl.content.firstElementChild.cloneNode(true);
                boardRoot.appendChild(node);
                var title = node.querySelector('[data-group-title]');
                if (title) title.focus();
                saveBoard();
            });
        }
    }

    if (boardRoot && boardWide) {
        boardRoot.addEventListener('pointerdown', function (event) {
            var groupGrip = event.target.closest('[data-group-grip]');
            if (groupGrip && boardRoot.contains(groupGrip)) {
                startGroupDrag(groupGrip, event);
                return;
            }
            var resize = event.target.closest('.crm-dash-resize');
            if (resize && boardRoot.contains(resize)) {
                startResize(resize, event);
                return;
            }
            var grip = event.target.closest('.crm-dash-grip');
            var card = grip && grip.closest('[data-key]');
            if (card && boardRoot.contains(card))
                card.setAttribute('draggable', 'true');
        });
        document.addEventListener('pointerup', function () {
            if (boardDrag) return;
            boardRoot.querySelectorAll('[data-key][draggable="true"]').forEach(function (el) {
                if (!el.classList.contains('is-dragging'))
                    el.setAttribute('draggable', 'false');
            });
        });
        boardRoot.addEventListener('dragstart', function (event) {
            var card = event.target.closest('[data-key]');
            if (!card || card.getAttribute('draggable') !== 'true') {
                event.preventDefault();
                return;
            }
            boardDrag = card;
            card.classList.add('is-dragging');
            event.dataTransfer.effectAllowed = 'move';
            event.dataTransfer.setData('text/plain', card.getAttribute('data-key') || '');
        });
        boardRoot.addEventListener('dragover', function (event) {
            if (!boardDrag) return;
            var group = event.target.closest('.crm-dash-group');
            if (!group || !boardRoot.contains(group)) return;
            event.preventDefault();
            var zone = group.querySelector('[data-grid]');
            boardRoot.querySelectorAll('.crm-dash-group.is-drop').forEach(function (el) {
                if (el !== group) el.classList.remove('is-drop');
            });
            var size = readBox(boardDrag);
            var cell = boardCellAt(zone, event.clientX, event.clientY);
            var next = { x: cell.x, y: cell.y, w: size.w, h: size.h };
            if (next.x + next.w - 1 > boardCols) next.x = boardCols - next.w + 1;
            if (zone !== boardDrag.parentElement) {
                group.classList.add('is-drop');
                boardHover = { zone: zone, box: next };
                return;
            }
            boardHover = null;
            var mark = next.x + ',' + next.y;
            if (zone._place === mark) return;
            if (!boardCan(next, boardOthers(zone, boardDrag))) return;
            zone._place = mark;
            writeBox(boardDrag, next);
        });
        boardRoot.addEventListener('drop', function (event) {
            if (!boardDrag || !boardHover) return;
            event.preventDefault();
            var box = boardHover.box;
            if (!boardCan(box, boardOthers(boardHover.zone, boardDrag)))
                box = boardFree(boardHover.zone, box.w, box.h, boardDrag);
            boardHover.zone.appendChild(boardDrag);
            writeBox(boardDrag, box);
            boardHover = null;
        });
        boardRoot.addEventListener('dragend', function () {
            if (!boardDrag) return;
            var card = boardDrag;
            card.classList.remove('is-dragging');
            card.setAttribute('draggable', 'false');
            boardRoot.querySelectorAll('.is-drop').forEach(function (el) { el.classList.remove('is-drop'); });
            boardDrag = null;
            boardHover = null;
            saveBoard();
            requestAnimationFrame(function () { redrawCard(card); });
        });
    }

    if (!document.querySelector('[data-dash-live="1"]')) return;

    fetch('/App/dashboard/widgets-data')
        .then(function (r) { return r.json(); })
        .then(function (items) {
            items.forEach(function (item) {
                if (item.type === 'counter') {
                    var num = document.querySelector('[data-widget-counter="' + item.id + '"]');
                    if (num) num.textContent = faNum(item.counter || 0);
                    return;
                }
                var chartEl = document.querySelector('.widget-chart[data-widget-id="' + item.id + '"]');
                if (!chartEl) return;
                chartEl._series = item.series || [];
                var range = document.querySelector('[data-widget-range="' + item.id + '"]');
                renderChart(chartEl, item.type, chartEl._series, range ? Number(range.value) || 6 : 6);
            });
        })
        .catch(function () {
            document.querySelectorAll('.widget-chart').forEach(function (el) {
                showMessage(el, 'خطا در بارگذاری');
            });
        });
})();
