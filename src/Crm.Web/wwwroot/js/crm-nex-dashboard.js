(function () {
    var node = document.getElementById("nexDash");
    if (!node || typeof ApexCharts === "undefined") return;

    var data;
    try { data = JSON.parse(node.textContent || "{}"); }
    catch (e) { return; }

    function fa(n) {
        return String(Math.round(Number(n) || 0)).replace(/\d/g, function (d) { return "۰۱۲۳۴۵۶۷۸۹"[d]; });
    }

    var css = getComputedStyle(document.documentElement);
    function paint(name, fallback) {
        var value = (css.getPropertyValue(name) || "").trim();
        return value || fallback;
    }
    var primary = paint("--bs-primary", "#5955D1");
    var info = paint("--bs-info", "#7008E7");
    var ink = paint("--bs-body-color", "#566a7f");
    var line = paint("--bs-border-color", "#e6e6e6");
    var paper = paint("--bs-body-bg", "#ffffff");
    var primaryRgb = paint("--bs-primary-rgb", "89, 85, 209").replace(/\s+/g, "");
    var infoRgb = paint("--bs-info-rgb", "112, 8, 231").replace(/\s+/g, "");
    function mix(rgb, alpha) { return "rgba(" + rgb + "," + alpha + ")"; }

    var axis = {
        colors: ink,
        fontSize: "13px",
        fontWeight: 500,
        fontFamily: "Vazirmatn, Tahoma, sans-serif"
    };
    var charts = [];

    function mount(root, id, options) {
        var el = root.querySelector(id);
        if (!el) return null;
        var chart = new ApexCharts(el, options);
        chart.render();
        charts.push(chart);
        return chart;
    }

    function ceiling(values) {
        var max = 0;
        (values || []).forEach(function (n) { if (n > max) max = n; });
        if (max <= 0) return 4;
        return Math.ceil(max * 1.25);
    }

    var contacts = data.contacts || { labels: [], data: [] };
    var leads = data.leads || { labels: [], data: [] };
    var tasks = data.tasks || [];
    var revenue = data.revenue || {};
    var sources = data.sources || [];
    var retention = data.retention || { labels: [], won: [], open: [], lost: [] };
    var heat = data.heat || { days: [], series: [] };
    var deals = data.deals || { labels: [], data: [] };

    document.querySelectorAll("[data-nx-slot]").forEach(function (slot) {
        var sid = slot.getAttribute("data-nx-slot");
        function box(name) { return "#c" + sid + "-" + name; }

        mount(slot, box("contacts"), {
            series: [{ name: "مخاطبان", data: contacts.data || [] }],
            chart: { type: "bar", height: 120, width: 150, toolbar: { show: false }, zoom: { enabled: false }, fontFamily: axis.fontFamily },
            plotOptions: { bar: { horizontal: false, columnWidth: "60%", barHeight: "100%", borderRadius: 2 } },
            colors: [primary],
            dataLabels: { enabled: false },
            stroke: { show: true },
            xaxis: { categories: contacts.labels || [], axisBorder: { show: false }, axisTicks: { show: false }, labels: { show: false } },
            yaxis: { labels: { show: false } },
            grid: { borderColor: "transparent", xaxis: { lines: { show: false } }, yaxis: { lines: { show: true } }, padding: { top: 0, bottom: 0, left: 0, right: 0 } },
            fill: {
                type: "gradient",
                gradient: { shade: "light", type: "vertical", shadeIntensity: 0.1, gradientToColors: [info], inverseColors: false, opacityFrom: 1, opacityTo: 0.6, stops: [20, 100] }
            },
            tooltip: { y: { formatter: function (val) { return fa(val); } } },
            legend: { show: false }
        });

        mount(slot, box("leads"), {
            chart: { type: "area", height: 120, toolbar: { show: false }, zoom: { enabled: false }, parentHeightOffset: 0, fontFamily: axis.fontFamily },
            series: [{ name: "سرنخ", data: leads.data || [] }],
            stroke: { curve: "smooth", width: 2, colors: [primary] },
            fill: { type: "solid", colors: [mix(primaryRgb, 0.1)], opacity: 1 },
            dataLabels: { enabled: false },
            markers: { size: 0, colors: ["#FFFFFF"], strokeColors: primary, strokeWidth: 3, hover: { size: 6 } },
            xaxis: { categories: leads.labels || [], labels: { show: false }, axisBorder: { show: false }, axisTicks: { show: false }, crosshairs: { show: false } },
            grid: { show: false, padding: { top: 0, right: 0, bottom: -10, left: 0 } },
            yaxis: { min: 0, max: ceiling(leads.data), show: false, labels: { show: false } },
            tooltip: { theme: "dark", y: { formatter: function (val) { return fa(val); } } }
        });

        mount(slot, box("tasks"), {
            series: tasks.map(function (s) { return s.value || 0; }),
            labels: tasks.map(function (s) { return s.label; }),
            chart: { type: "donut", height: 140, fontFamily: axis.fontFamily, toolbar: { show: false } },
            colors: ["#5955D1", "#ACAAE8", "#DEDDF6"],
            dataLabels: { enabled: false },
            legend: { show: false },
            stroke: { width: 3, colors: [paper] },
            plotOptions: {
                pie: {
                    expandOnClick: false,
                    borderRadius: 3,
                    donut: {
                        size: "70%",
                        labels: {
                            show: true,
                            name: { show: false },
                            total: {
                                show: true,
                                showAlways: true,
                                label: " ",
                                color: ink,
                                fontSize: "20px",
                                fontWeight: 700,
                                formatter: function (w) {
                                    return fa(w.globals.seriesTotals.reduce(function (a, b) { return a + b; }, 0));
                                }
                            },
                            value: { show: false }
                        }
                    }
                }
            },
            tooltip: { enabled: false }
        });

        function revenueOptions(key) {
            var series = revenue[key] || { labels: [], data: [] };
            return {
                series: [{ name: "درآمد", data: series.data || [] }],
                chart: { type: "bar", height: 280, toolbar: { show: false }, fontFamily: axis.fontFamily },
                plotOptions: { bar: { horizontal: false, columnWidth: "70%", barHeight: "100%", borderRadius: 4 } },
                colors: [primary],
                dataLabels: { enabled: false },
                stroke: { show: true },
                xaxis: {
                    categories: series.labels || [],
                    axisBorder: { color: line },
                    axisTicks: { show: false },
                    labels: { style: axis }
                },
                yaxis: {
                    min: 0,
                    tickAmount: 4,
                    labels: { formatter: function (val) { return fa(val); }, style: axis }
                },
                grid: { borderColor: line, strokeDashArray: 5, xaxis: { lines: { show: false } }, yaxis: { lines: { show: true } } },
                fill: {
                    type: "gradient",
                    gradient: { shade: "light", type: "vertical", shadeIntensity: 0.1, gradientToColors: [info], inverseColors: false, opacityFrom: 1, opacityTo: 0.6, stops: [20, 100] }
                },
                tooltip: { y: { formatter: function (val) { return fa(val); } } },
                legend: { show: false }
            };
        }
        var revenueChart = mount(slot, box("revenue"), revenueOptions("month"));
        slot.querySelectorAll("[data-nx-revenue]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                slot.querySelectorAll("[data-nx-revenue]").forEach(function (other) { other.classList.remove("active"); });
                btn.classList.add("active");
                if (!revenueChart) return;
                var next = revenue[btn.getAttribute("data-nx-revenue")] || { labels: [], data: [] };
                revenueChart.updateOptions({
                    xaxis: { categories: next.labels || [] },
                    series: [{ name: "درآمد", data: next.data || [] }]
                }, false, false);
            });
        });

        mount(slot, box("sources"), {
            series: sources.map(function (s) { return { name: s.label, data: [s.percent || 0] }; }),
            chart: { type: "bar", height: 95, stacked: true, stackType: "100%", toolbar: { show: false }, animations: { enabled: true }, fontFamily: axis.fontFamily },
            plotOptions: { bar: { horizontal: true, barHeight: "100%", borderRadius: 0 } },
            dataLabels: { enabled: false },
            stroke: { width: 1, colors: ["#ffffff"] },
            xaxis: { labels: { show: false }, axisBorder: { show: false }, axisTicks: { show: false } },
            yaxis: { labels: { show: false } },
            grid: { show: false, padding: { top: -15, bottom: -15, left: -15, right: 0 } },
            legend: { show: false },
            fill: {
                opacity: 1,
                colors: [mix(primaryRgb, 0.1), mix(primaryRgb, 0.25), mix(primaryRgb, 0.5), mix(primaryRgb, 0.75), mix(primaryRgb, 1)]
            },
            tooltip: { y: { formatter: function (val) { return fa(val) + "٪"; } } }
        });

        mount(slot, box("retention"), {
            series: [
                { name: "برده", data: retention.won || [] },
                { name: "باز", data: retention.open || [] },
                { name: "باخته", data: retention.lost || [] }
            ],
            chart: { type: "bar", height: 295, stacked: true, toolbar: { show: false }, zoom: { enabled: false }, fontFamily: axis.fontFamily },
            colors: [primary, mix(primaryRgb, 0.45), mix(primaryRgb, 0.22)],
            plotOptions: { bar: { horizontal: false, colors: { backgroundBarColors: [document.documentElement.getAttribute("data-bs-theme") === "dark" ? "#2a2a2a" : mix(primaryRgb, 0.06)], backgroundBarOpacity: 1 } } },
            yaxis: { show: false },
            states: { normal: { filter: { type: "none" } }, hover: { filter: { type: "none" } }, active: { filter: { type: "none" } } },
            xaxis: { categories: retention.labels || [], axisTicks: { show: false }, axisBorder: { show: false }, labels: { style: axis } },
            legend: { position: "bottom", offsetY: 0, labels: { colors: ink }, markers: { fillColors: [primary, mix(primaryRgb, 0.45), mix(primaryRgb, 0.22)], strokeWidth: 0 } },
            grid: { borderColor: "transparent", xaxis: { lines: { show: false } }, yaxis: { lines: { show: true } } },
            fill: { opacity: 1 },
            dataLabels: { enabled: false },
            tooltip: { y: { formatter: function (val) { return fa(val); } } }
        });

        mount(slot, box("gauge"), {
            series: [data.gauge || 0],
            chart: { type: "radialBar", offsetY: 0, height: 350, sparkline: { enabled: true }, fontFamily: axis.fontFamily },
            plotOptions: {
                radialBar: {
                    startAngle: -95,
                    endAngle: 95,
                    track: { background: "rgba(255,255,255,0.3)", strokeWidth: "100%", margin: 25 },
                    dataLabels: {
                        name: { show: false },
                        value: {
                            show: true,
                            offsetY: -35,
                            fontSize: "28px",
                            fontWeight: 600,
                            color: "#fff",
                            formatter: function () { return data.gaugeLabel || "۰"; }
                        }
                    }
                }
            },
            grid: { padding: { top: 0, bottom: 0, left: 0, right: 0 } },
            fill: { colors: ["#ffffff"] }
        });

        var heatMax = 0;
        (heat.series || []).forEach(function (row) {
            (row.data || []).forEach(function (n) { if (n > heatMax) heatMax = n; });
        });
        var mid = Math.max(1, Math.ceil(heatMax * 0.45));
        mount(slot, box("heat"), {
            series: heat.series || [],
            chart: { height: 250, type: "heatmap", toolbar: { show: false }, fontFamily: axis.fontFamily },
            stroke: { width: 2, colors: [paper] },
            dataLabels: { enabled: false },
            plotOptions: {
                heatmap: {
                    shadeIntensity: 0.95,
                    radius: 6,
                    distributed: false,
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
            xaxis: { categories: heat.days || [], axisBorder: { show: false }, axisTicks: { show: false }, labels: { style: axis } },
            legend: { show: false },
            tooltip: { y: { formatter: function (val) { return fa(val); } } }
        });

        mount(slot, box("deals"), {
            chart: { type: "area", height: 225, toolbar: { show: false }, zoom: { enabled: false }, parentHeightOffset: 0, fontFamily: axis.fontFamily },
            series: [{ name: "معامله", data: deals.data || [] }],
            stroke: { curve: "smooth", width: 2, colors: [info] },
            fill: { type: "solid", colors: [mix(infoRgb, 0.1)], opacity: 1 },
            dataLabels: { enabled: false },
            markers: { size: 0, colors: ["#FFFFFF"], strokeColors: info, strokeWidth: 3, hover: { size: 6 } },
            xaxis: { categories: deals.labels || [], labels: { show: false }, axisBorder: { show: false }, axisTicks: { show: false }, crosshairs: { show: false } },
            grid: { show: false, padding: { top: 0, right: 0, bottom: -10, left: 0 } },
            yaxis: { min: 0, max: ceiling(deals.data), show: false, labels: { show: false } },
            tooltip: { theme: "dark", y: { formatter: function (val) { return fa(val); } } }
        });

        var search = slot.querySelector("[data-nx-search]");
        var table = slot.querySelector("[data-nx-customers]");
        if (search && table) {
            search.addEventListener("input", function () {
                var q = search.value.trim();
                table.querySelectorAll("tbody tr").forEach(function (row) {
                    row.hidden = q.length > 0 && row.textContent.indexOf(q) < 0;
                });
            });
        }
    });
})();
