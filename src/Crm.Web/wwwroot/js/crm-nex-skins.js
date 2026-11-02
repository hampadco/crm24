(function () {
    function fa(n) {
        return String(Math.round(Number(n) || 0)).replace(/\d/g, function (d) { return "۰۱۲۳۴۵۶۷۸۹"[d]; });
    }

    var css = getComputedStyle(document.documentElement);
    function paintVar(name, fallback) {
        var value = (css.getPropertyValue(name) || "").trim();
        return value || fallback;
    }
    var primary = paintVar("--bs-primary", "#5955D1");
    var info = paintVar("--bs-info", "#7008E7");
    var secondary = paintVar("--bs-secondary", "#8592a3");
    var ink = paintVar("--bs-body-color", "#566a7f");
    var line = paintVar("--bs-border-color", "#e6e6e6");
    var paper = paintVar("--bs-body-bg", "#ffffff");
    var primaryRgb = paintVar("--bs-primary-rgb", "89, 85, 209").replace(/\s+/g, "");
    var infoRgb = paintVar("--bs-info-rgb", "112, 8, 231").replace(/\s+/g, "");
    function mix(rgb, alpha) { return "rgba(" + rgb + "," + alpha + ")"; }

    var axis = { colors: ink, fontSize: "13px", fontWeight: 500, fontFamily: "Vazirmatn, Tahoma, sans-serif" };
    var font = "Vazirmatn, Tahoma, sans-serif";

    function ceiling(values) {
        var max = 0;
        (values || []).forEach(function (n) { if (n > max) max = n; });
        return max <= 0 ? 4 : Math.ceil(max * 1.25);
    }

    function card(host, inner) {
        host.innerHTML = "";
        var wrap = document.createElement("div");
        wrap.className = "card h-100";
        wrap.innerHTML = inner;
        host.appendChild(wrap);
        return wrap;
    }

    function delta(up, text) {
        return '<span class="badge badge-sm ' + (up ? "bg-success-subtle text-success" : "bg-danger-subtle text-danger") + '">' + (text || "۰٪") + "</span>";
    }

    function boxExtra(node) {
        var style = getComputedStyle(node);
        return (parseFloat(style.marginTop) || 0) + (parseFloat(style.marginBottom) || 0);
    }

    function plotNode(host, el) {
        if (el && el.nodeType === 1) {
            if (el.hasAttribute && el.hasAttribute("data-plot")) return el;
            if (el.closest) return el.closest("[data-plot]") || host.querySelector("[data-plot]");
        }
        return host.querySelector("[data-plot]");
    }

    function plotRoom(host, el) {
        var plot = plotNode(host, el);
        var frame = host.closest(".crm-dash-card") || host.closest("#studioPreview") || host.closest(".nx-body") || host;
        if (!plot || !frame.clientHeight) return 0;
        var room = frame.clientHeight - (plot.getBoundingClientRect().top - frame.getBoundingClientRect().top);
        var parent = plot.parentElement;
        var after = false;
        if (parent) {
            Array.prototype.forEach.call(parent.children, function (child) {
                if (child === plot) { after = true; return; }
                if (after) room -= child.offsetHeight + boxExtra(child);
            });
        }
        var pad = getComputedStyle(frame);
        room -= (parseFloat(pad.paddingBottom) || 0) + 8;
        return Math.floor(Math.min(room, frame.clientHeight - 8));
    }

    function plotHeight(host, fallback, el) {
        var room = plotRoom(host, el);
        var studio = !!host.closest("#studioPreview");
        if (room < 64) return studio ? Math.max(fallback || 160, 280) : Math.max(fallback || 140, 120);
        return studio ? Math.max(160, room) : Math.max(96, room);
    }

    function mount(host, el, options) {
        if (!el || typeof ApexCharts === "undefined") return;
        options.chart = options.chart || {};
        var grow = options.chart.nxFit !== false && !options.chart.width;
        delete options.chart.nxFit;
        if (grow) {
            options.chart.height = plotHeight(host, options.chart.height || 220, el);
            options.chart.parentHeightOffset = 0;
        }
        var chart = new ApexCharts(el, options);
        chart._crmFit = grow;
        chart.render();
        host._charts.push(chart);
    }

    function fit(host) {
        if (!host || host._crmFitting) return;
        host._crmFitting = true;
        try {
            (host._charts || []).forEach(function (chart) {
                if (!chart) return;
                if (chart.canvas && typeof chart.resize === "function") {
                    var box = chart.canvas.closest(".nx-plot") || chart.canvas.parentElement;
                    var side = plotHeight(host, 140, box || chart.canvas);
                    if (Math.abs((chart._crmH || 0) - side) < 4) return;
                    chart._crmH = side;
                    if (box) {
                        box.style.flex = "0 0 " + side + "px";
                        box.style.width = side + "px";
                        box.style.height = side + "px";
                    }
                    chart.resize();
                    return;
                }
                if (!chart._crmFit) return;
                var height = plotHeight(host, 160, chart.el);
                if (Math.abs((chart._crmH || 0) - height) < 4) return;
                chart._crmH = height;
                var plot = plotNode(host, chart.el);
                if (plot) {
                    plot.style.flex = "0 0 " + height + "px";
                    plot.style.height = height + "px";
                    plot.style.minHeight = "0px";
                }
                if (!(chart.w && chart.w.config && chart.w.config.chart)) return;
                chart.w.config.chart.height = height;
                chart.w.config.chart.parentHeightOffset = 0;
                chart.w.globals.resized = true;
                chart.w.globals.dataChanged = false;
                chart.w.globals.shouldAnimate = false;
                try {
                    if (typeof chart.update === "function") chart.update();
                } catch (e) { /* اندازهٔ بعدی دوباره حساب می‌شود */ }
            });
        } finally {
            host._crmFitting = false;
        }
    }

    function watch(host) {
        if (!host || host._crmWatch || typeof ResizeObserver === "undefined") return;
        var frame = host.closest(".crm-dash-card") || host.closest("#studioPreview") || host;
        host._crmWatch = new ResizeObserver(function () {
            if (host._crmFrame) cancelAnimationFrame(host._crmFrame);
            host._crmFrame = requestAnimationFrame(function () { fit(host); });
        });
        host._crmWatch.observe(frame);
    }

    function purpleSteps() {
        return [primary, mix(primaryRgb, 0.45), mix(primaryRgb, 0.22)];
    }

    function themeBar() {
        return document.documentElement.getAttribute("data-bs-theme") === "dark" ? "#2a2a2a" : mix(primaryRgb, 0.06);
    }

    function gradientFill() {
        return {
            type: "gradient",
            gradient: { shade: "light", type: "vertical", shadeIntensity: 0.1, gradientToColors: [info], inverseColors: false, opacityFrom: 1, opacityTo: 0.6, stops: [20, 100] }
        };
    }

    var skins = {
        spark: function (host, item) {
            var wrap = card(host,
                '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + "</h6></div>" +
                '<div class="card-body pt-2 pb-0 d-flex flex-column">' +
                '<div class="d-flex align-items-center gap-2"><h2 class="mb-0">' + item.kpi + "</h2>" + delta(item.up, item.delta) + "</div>" +
                '<div data-plot class="nx-plot"></div></div>' +
                '<div class="card-footer border-0 pt-1"><p class="mb-0">' + (item.hint || "") + "</p></div>");
            mount(host, wrap.querySelector("[data-plot]"), {
                series: [{ name: item.title, data: item.data || [] }],
                chart: { type: "bar", height: 140, toolbar: { show: false }, zoom: { enabled: false }, fontFamily: font },
                plotOptions: { bar: { horizontal: false, columnWidth: "60%", barHeight: "100%", borderRadius: 2 } },
                colors: [primary],
                dataLabels: { enabled: false },
                stroke: { show: true },
                xaxis: { categories: item.labels || [], axisBorder: { show: false }, axisTicks: { show: false }, labels: { show: false } },
                yaxis: { labels: { show: false } },
                grid: { borderColor: "transparent", xaxis: { lines: { show: false } }, yaxis: { lines: { show: true } }, padding: { top: 0, right: 0, bottom: 0, left: 0 } },
                fill: gradientFill(),
                tooltip: { y: { formatter: fa } },
                legend: { show: false }
            });
        },
        area: function (host, item) { areaSkin(host, item, primary, mix(primaryRgb, 0.1), 120); },
        "area-info": function (host, item) { areaSkin(host, item, info, mix(infoRgb, 0.1), 225); },
        bar: function (host, item) {
            var tabs = item.tabs || {};
            var wrap = card(host,
                '<div class="card-header d-flex flex-wrap gap-2 align-items-center justify-content-between border-0 pb-2">' +
                '<h6 class="card-title mb-0">' + item.title + "</h6>" +
                '<ul class="nav nav-pills nav-pills-custom p-1 bg-light rounded-5">' +
                '<li class="nav-item"><button type="button" class="nav-link rounded-5" data-tab="today">امروز</button></li>' +
                '<li class="nav-item"><button type="button" class="nav-link rounded-5" data-tab="week">هفته</button></li>' +
                '<li class="nav-item"><button type="button" class="nav-link rounded-5 active" data-tab="month">ماه</button></li>' +
                "</ul></div>" +
                '<div class="card-body pt-0"><h2 class="mb-2">' + item.kpi + '</h2><div data-plot class="revenue-chart"></div></div>');
            var sets = {
                month: { labels: tabs.monthLabels || item.labels || [], data: tabs.month || item.data || [] },
                week: { labels: tabs.weekLabels || [], data: tabs.week || [] },
                today: { labels: tabs.todayLabels || [], data: tabs.today || [] }
            };
            function options(key) {
                var set = sets[key];
                return {
                    series: [{ name: item.title, data: set.data }],
                    chart: { type: "bar", height: 280, toolbar: { show: false }, fontFamily: font },
                    plotOptions: { bar: { horizontal: false, columnWidth: "70%", barHeight: "100%", borderRadius: 4 } },
                    colors: [primary],
                    dataLabels: { enabled: false },
                    stroke: { show: true },
                    xaxis: { categories: set.labels, axisBorder: { color: line }, axisTicks: { show: false }, labels: { rotate: 0, rotateAlways: false, hideOverlappingLabels: true, trim: false, style: axis } },
                    yaxis: { min: 0, tickAmount: 4, labels: { formatter: fa, style: axis } },
                    grid: { borderColor: line, strokeDashArray: 5, xaxis: { lines: { show: false } }, yaxis: { lines: { show: true } } },
                    fill: gradientFill(),
                    tooltip: { y: { formatter: fa } },
                    legend: { show: false }
                };
            }
            var plot = wrap.querySelector("[data-plot]");
            if (plot) plot.classList.add("nx-plot");
            var chart = null;
            if (plot && typeof ApexCharts !== "undefined") {
                var first = options("month");
                first.chart.height = plotHeight(host, 220, plot);
                first.chart.parentHeightOffset = 0;
                chart = new ApexCharts(plot, first);
                chart._crmFit = true;
                chart.render();
                host._charts.push(chart);
            }
            wrap.querySelectorAll("[data-tab]").forEach(function (btn) {
                btn.addEventListener("click", function () {
                    wrap.querySelectorAll("[data-tab]").forEach(function (other) { other.classList.remove("active"); });
                    btn.classList.add("active");
                    if (!chart) return;
                    var next = sets[btn.getAttribute("data-tab")];
                    chart.updateOptions({ xaxis: { categories: next.labels }, series: [{ name: item.title, data: next.data }] }, false, false);
                });
            });
        },
        line: function (host, item) {
            var wrap = card(host, '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + '</h6></div><div class="card-body d-flex flex-column"><div data-plot class="nx-plot"></div></div>');
            var series = (item.series || []).map(function (s) { return { name: s.name, data: s.data || [] }; });
            if (!series.length) series = [{ name: item.title, data: item.data || [] }];
            mount(host, wrap.querySelector("[data-plot]"), {
                series: series,
                chart: { height: 300, type: "line", zoom: { enabled: false }, toolbar: { show: false }, fontFamily: font },
                colors: [secondary, primary],
                dataLabels: { enabled: false },
                stroke: { width: [2, 2], curve: "smooth", dashArray: [8, 0] },
                markers: { size: 0, hover: { sizeOffset: 6 } },
                xaxis: { categories: item.labels || [], axisTicks: { show: false }, labels: { rotate: 0, hideOverlappingLabels: true, trim: false, style: axis } },
                yaxis: { min: 0, labels: { formatter: fa, style: axis } },
                grid: { borderColor: line, strokeDashArray: 5 },
                legend: { position: "bottom", labels: { colors: ink } },
                tooltip: { y: { formatter: fa } }
            });
        },
        heat: function (host, item) {
            var wrap = card(host, '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + '</h6></div><div class="card-body p-0 d-flex flex-column"><div data-plot class="nx-plot"></div></div>');
            var max = 0;
            (item.heat || []).forEach(function (row) { (row.data || []).forEach(function (n) { if (n > max) max = n; }); });
            var mid = Math.max(1, Math.ceil(max * 0.45));
            mount(host, wrap.querySelector("[data-plot]"), {
                series: item.heat || [],
                chart: { height: 250, type: "heatmap", toolbar: { show: false }, fontFamily: font },
                stroke: { width: 2, colors: [paper] },
                dataLabels: { enabled: false },
                plotOptions: {
                    heatmap: {
                        shadeIntensity: 0.95,
                        radius: 6,
                        colorScale: {
                            ranges: [
                                { from: 0, to: 0, color: "#E0E7FF" },
                                { from: 1, to: mid, color: "#A5B4FC" },
                                { from: mid + 1, to: 100000, color: "#6366F1" }
                            ]
                        }
                    }
                },
                grid: { show: false },
                yaxis: { labels: { style: axis } },
                xaxis: { categories: item.heatDays || [], axisBorder: { show: false }, axisTicks: { show: false }, labels: { style: axis } },
                legend: { show: false },
                tooltip: { y: { formatter: fa } }
            });
        },
        share: function (host, item) {
            var slices = item.slices || [];
            var rows = slices.map(function (s, i) {
                var opacity = slices.length <= 1 ? 1 : 0.15 + (i * 0.8 / Math.max(1, slices.length - 1));
                return '<div class="nx-legend-row"><span class="d-inline-block rounded-1" style="width:.7rem;height:.7rem;background:var(--bs-primary);opacity:' + opacity + '"></span><span class="nx-legend-name">' + s.label + '</span><strong>' + fa(s.percent) + "٪</strong></div>";
            }).join("");
            var wrap = card(host,
                '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + "</h6></div>" +
                '<div class="card-body pt-0 d-flex flex-column"><div data-plot class="chart-rounded"></div><div class="nx-legend">' + rows + "</div></div>");
            mount(host, wrap.querySelector("[data-plot]"), {
                series: slices.map(function (s) { return { name: s.label, data: [s.percent || 0] }; }),
                chart: { type: "bar", height: 95, nxFit: false, stacked: true, stackType: "100%", toolbar: { show: false }, fontFamily: font },
                plotOptions: { bar: { horizontal: true, barHeight: "100%", borderRadius: 0 } },
                dataLabels: { enabled: false },
                stroke: { width: 1, colors: ["#ffffff"] },
                xaxis: { labels: { show: false }, axisBorder: { show: false }, axisTicks: { show: false } },
                yaxis: { labels: { show: false } },
                grid: { show: false, padding: { top: -15, bottom: -15, left: -15, right: 0 } },
                legend: { show: false },
                fill: { opacity: 1, colors: [mix(primaryRgb, 0.1), mix(primaryRgb, 0.25), mix(primaryRgb, 0.5), mix(primaryRgb, 0.75), mix(primaryRgb, 1)] },
                tooltip: { y: { formatter: function (val) { return fa(val) + "٪"; } } }
            });
        },
        stack: function (host, item) {
            var steps = purpleSteps();
            var wrap = card(host, '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + '</h6></div><div class="card-body d-flex flex-column"><div data-plot class="nx-plot retention-chart"></div></div>');
            mount(host, wrap.querySelector("[data-plot]"), {
                series: (item.series || []).map(function (s) { return { name: s.name, data: s.data || [] }; }),
                chart: { type: "bar", height: 295, stacked: true, toolbar: { show: false }, zoom: { enabled: false }, fontFamily: font },
                colors: steps,
                plotOptions: { bar: { horizontal: false, colors: { backgroundBarColors: [themeBar()], backgroundBarOpacity: 1 } } },
                yaxis: { show: false },
                xaxis: { categories: item.labels || [], axisTicks: { show: false }, axisBorder: { show: false }, labels: { rotate: 0, hideOverlappingLabels: true, trim: false, style: axis } },
                legend: { position: "bottom", labels: { colors: ink }, markers: { fillColors: steps, strokeWidth: 0 } },
                grid: { borderColor: "transparent", xaxis: { lines: { show: false } }, yaxis: { lines: { show: true } } },
                fill: { opacity: 1 },
                dataLabels: { enabled: false },
                tooltip: { y: { formatter: fa } }
            });
        },
        doughnut: function (host, item) {
            var slices = item.slices || [];
            var legend = slices.map(function (s, i) {
                var colors = ["#5955D1", "#ACAAE8", "#DEDDF6", "#8b88e0", "#cfcdec"];
                return '<div class="d-flex gap-2 align-items-center py-1"><span class="d-inline-block rounded-1" style="width:.7rem;height:.7rem;background:' + colors[i % colors.length] + '"></span>' + s.label + '<strong class="ms-auto">' + fa(s.value) + "</strong></div>";
            }).join("");
            var wrap = card(host, '<div class="card-body d-flex flex-column"><h6 class="mb-2">' + item.title + '</h6><div class="nx-doughnut"><div class="nx-legend">' + legend + '</div><div class="nx-plot nx-doughnut-plot"><canvas data-plot></canvas></div></div></div>');
            var canvas = wrap.querySelector("[data-plot]");
            if (!canvas || typeof Chart === "undefined") return;
            var chart = new Chart(canvas, {
                type: "doughnut",
                data: {
                    labels: slices.map(function (s) { return s.label; }),
                    datasets: [{
                        data: slices.map(function (s) { return s.value; }),
                        backgroundColor: ["#5955D1", "#ACAAE8", "#DEDDF6"],
                        borderRadius: 3,
                        hoverOffset: 5,
                        borderWidth: 3,
                        borderColor: paper
                    }]
                },
                options: { responsive: true, maintainAspectRatio: false, cutout: "70%", plugins: { legend: { display: false }, tooltip: { enabled: false } } },
                plugins: [{
                    id: "centerText",
                    afterDraw: function (c) {
                        var area = c.chartArea;
                        var ctx = c.ctx;
                        var total = (c.data.datasets[0].data || []).reduce(function (a, b) { return a + b; }, 0);
                        ctx.save();
                        ctx.textAlign = "center";
                        ctx.textBaseline = "middle";
                        ctx.font = "bold 20px Vazirmatn, Tahoma, sans-serif";
                        ctx.fillStyle = ink;
                        ctx.fillText(fa(total), (area.left + area.right) / 2, (area.top + area.bottom) / 2);
                        ctx.restore();
                    }
                }]
            });
            host._charts.push(chart);
        },
        funnel: function (host, item) {
            var slices = item.slices || [];
            if (!slices.length) slices = [{ label: "—", value: 0 }];
            var max = 1;
            slices.forEach(function (s) { if ((s.value || 0) > max) max = s.value; });
            var steps = slices.map(function (s, i) {
                var top = Math.max(34, ((s.value || 0) / max) * 100);
                var next = slices[i + 1] ? Math.max(26, ((slices[i + 1].value || 0) / max) * 100) : Math.max(18, top * 0.62);
                var scale = ["#5955D1", "#6e6ad6", "#8b88e0", "#a9a6ea", "#c8c6f3", "#DEDDF6"];
                var color = scale[Math.min(i, scale.length - 1)];
                var inkOn = i >= scale.length - 2 ? "#3d3a8f" : "#fff";
                var insetTop = (100 - top) / 2;
                var insetBot = (100 - next) / 2;
                return '<div class="crm-funnel-step" style="background:' + color + ';color:' + inkOn + ';clip-path:polygon(' + insetTop + '% 0,' + (100 - insetTop) + '% 0,' + (100 - insetBot) + '% 100%,' + insetBot + '% 100%)"><span>' + s.label + '</span><strong>' + fa(s.value) + '</strong></div>';
            }).join("");
            card(host, '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + '</h6></div><div class="card-body"><div class="crm-funnel">' + steps + '</div></div>');
        },
        pipeline: function (host, item) {
            var tones = ["progress-primary", "progress-primary", "progress-primary", "progress-primary"];
            var bars = (item.slices || []).map(function (s, i) {
                var width = Math.max(s.percent || 0, s.value > 0 ? 8 : 0);
                var opacity = Math.max(0.25, 1 - i * 0.15);
                return '<div class="progress progress-overlap mb-1 ' + tones[i % tones.length] + '" style="opacity:' + opacity + '"><div class="progress-label">' + s.label + '</div><div class="progress-value">' + fa(s.value) + '</div><div class="progress-bar" style="width:' + width + '%"></div></div>';
            }).join("");
            card(host, '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + '</h6></div><div class="card-body pt-2"><div class="d-flex gap-2 align-items-center mb-4"><h2 class="mb-0">' + item.kpi + "</h2></div>" + bars + "</div>");
        },
        status: function (host, item) {
            var slices = item.slices || [];
            var bars = slices.slice(0, 3).map(function (s, i) {
                var opacity = i === 0 ? "1" : i === 1 ? ".5" : ".25";
                return '<div class="progress bg-transparent" style="width:' + (s.percent || 0) + '%"><div class="progress-bar bg-white" style="opacity:' + opacity + '"></div></div>';
            }).join("");
            var lines = slices.map(function (s) {
                return '<div class="d-flex justify-content-between mb-2"><span>' + s.label + "</span><strong>" + fa(s.percent) + "٪</strong></div>";
            }).join("");
            card(host, '<div class="card bg-primary border-0 text-white h-100"><div class="card-header border-0"><h6 class="mb-0 text-white">' + item.title + '</h6></div><div class="card-body"><div class="progress-stacked bg-transparent mb-3">' + bars + "</div>" + lines + "</div></div>");
        },
        radial: function (host, item) { radialSkin(host, item, true); },
        target: function (host, item) { radialSkin(host, item, false); },
        kpi: function (host, item) {
            card(host, '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + '</h6></div><div class="card-body"><div class="d-flex align-items-center gap-2"><h2 class="mb-0">' + item.kpi + "</h2>" + delta(item.up, item.delta) + '</div></div><div class="card-footer border-0 pt-0"><p class="mb-0">' + (item.hint || "") + "</p></div>");
        },
        list: function (host, item) {
            var rows = (item.rows || []).map(function (row) {
                return '<a class="d-block p-3 bg-light bg-opacity-50 mb-2 rounded text-body" href="' + row.href + '"><h6 class="mb-1">' + row.title + '</h6><div class="text-primary small">' + row.when + "</div></a>";
            }).join("") || '<p class="text-body-secondary mb-0">رکوردی نیست.</p>';
            card(host, '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + '</h6></div><div class="card-body">' + rows + "</div>");
        },
        table: function (host, item) {
            var heads = (item.heads || []).map(function (h) { return "<th>" + h + "</th>"; }).join("");
            var rows = (item.rows || []).map(function (row) {
                var cells = (row.cells || []).map(function (c) { return "<td>" + c + "</td>"; }).join("");
                return "<tr><td><a href=\"" + row.href + "\">" + row.title + "</a></td>" + cells + "<td>" + row.when + "</td></tr>";
            }).join("") || '<tr><td class="text-body-secondary">رکوردی نیست.</td></tr>';
            card(host, '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + '</h6></div><div class="card-body px-2"><table class="table table-sm align-middle mb-0"><thead class="table-light"><tr><th>عنوان</th>' + heads + "<th>تاریخ</th></tr></thead><tbody>" + rows + "</tbody></table></div>");
        },
        check: function (host, item) {
            var rows = (item.rows || []).map(function (row) {
                return '<li class="list-group-item d-flex gap-2 align-items-center"><input class="form-check-input" type="checkbox" disabled ' + (row.done ? "checked" : "") + '><a class="' + (row.done ? "text-decoration-line-through text-body-secondary" : "") + '" href="' + row.href + '">' + row.title + '</a><span class="text-body-secondary small ms-auto">' + row.when + "</span></li>";
            }).join("") || '<p class="text-body-secondary mb-0">رکوردی نیست.</p>';
            card(host, '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + '</h6></div><div class="card-body px-2"><ul class="list-group list-group-flush">' + rows + "</ul></div>");
        }
    };

    function areaSkin(host, item, stroke, fill, height) {
        var wrap = card(host,
            '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + "</h6></div>" +
            '<div class="card-body pt-2 pb-0 d-flex flex-column"><div class="d-flex align-items-center gap-2"><h2 class="mb-0">' + item.kpi + "</h2>" + delta(item.up, item.delta) + '</div><div data-plot class="nx-plot"></div></div>');
        mount(host, wrap.querySelector("[data-plot]"), {
            chart: { type: "area", height: height, toolbar: { show: false }, zoom: { enabled: false }, parentHeightOffset: 0, fontFamily: font },
            series: [{ name: item.title, data: item.data || [] }],
            stroke: { curve: "smooth", width: 2, colors: [stroke] },
            fill: { type: "solid", colors: [fill], opacity: 1 },
            dataLabels: { enabled: false },
            markers: { size: 0, colors: ["#FFFFFF"], strokeColors: stroke, strokeWidth: 3, hover: { size: 6 } },
            xaxis: { categories: item.labels || [], labels: { show: false }, axisBorder: { show: false }, axisTicks: { show: false }, crosshairs: { show: false } },
            grid: { show: false, padding: { top: 8, right: 0, bottom: 0, left: 0 } },
            yaxis: { min: 0, max: ceiling(item.data), show: false, labels: { show: false } },
            tooltip: { theme: "dark", y: { formatter: fa } }
        });
    }

    function radialSkin(host, item, onPrimary) {
        var shell = onPrimary
            ? '<div class="card bg-primary border-0 text-white h-100"><div class="card-header border-0"><h6 class="mb-0 text-white">' + item.title + '</h6></div><div class="card-body pt-0 d-flex flex-column"><div data-plot class="nx-plot"></div><div class="text-white text-center">' + (item.gaugeSub || "") + "</div></div></div>"
            : '<div class="card-header border-0 pb-0"><h6 class="mb-0">' + item.title + '</h6></div><div class="card-body pt-0 d-flex flex-column"><div data-plot class="nx-plot"></div><p class="text-center text-body-secondary mb-0">' + (item.gaugeSub || "") + "</p></div>";
        var wrap = card(host, shell);
        if (onPrimary) wrap.classList.add("border-0", "bg-transparent");
        var track = onPrimary ? "rgba(255,255,255,0.3)" : mix(primaryRgb, 0.6);
        var fill = onPrimary ? "#ffffff" : primary;
        var color = onPrimary ? "#ffffff" : ink;
        mount(host, wrap.querySelector("[data-plot]"), {
            series: [item.gauge || 0],
            chart: { type: "radialBar", height: 280, sparkline: { enabled: true }, fontFamily: font },
            plotOptions: {
                radialBar: {
                    startAngle: -95,
                    endAngle: 95,
                    track: { background: track, strokeWidth: "100%", margin: 25 },
                    dataLabels: {
                        name: { show: false },
                        value: { show: true, offsetY: -30, fontSize: "28px", fontWeight: 600, color: color, formatter: function () { return item.gaugeLabel || "۰"; } }
                    }
                }
            },
            fill: { colors: [fill] }
        });
    }

    function paint(host, item) {
        if (!host || !item) return;
        (host._charts || []).forEach(function (chart) { try { chart.destroy(); } catch (e) { /* gone */ } });
        host._charts = [];
        var draw = skins[item.skin] || skins.kpi;
        draw(host, item);
        watch(host);
        requestAnimationFrame(function () { fit(host); });
    }

    window.CrmNexSkins = { paint: paint, fit: fit };

    var slots = document.querySelectorAll("[data-studio-widget]");
    if (!slots.length) return;
    fetch("/App/dashboard/studio-data")
        .then(function (r) { return r.json(); })
        .then(function (items) {
            var byId = {};
            (items || []).forEach(function (item) { byId[item.widgetId] = item; });
            slots.forEach(function (slot) {
                var host = slot.querySelector("[data-skin-host]");
                paint(host, byId[slot.getAttribute("data-studio-widget")]);
            });
        })
        .catch(function () { /* دادهٔ ویجت نرسید */ });
})();
