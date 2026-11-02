using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Web.Services;

namespace Crm.Web.Areas.App.Controllers;

public partial class DashboardController
{
    private async Task PrepareStudioWidgetsAsync(IEnumerable<ModuleDef> modules)
    {
        var list = modules.ToList();
        if (_tenant.UserId is int userId)
        {
            var legacy = await _db.DashboardWidgets
                .Where(w => w.UserId == userId && w.Type != "board" && w.Type != "studio")
                .ToListAsync();
            var pairs = new List<(DashboardWidget Widget, WidgetTemplate Template)>();
            foreach (var widget in legacy)
            {
                var spec = LegacySpec(widget, list);
                if (spec is null)
                    continue;
                var template = new WidgetTemplate
                {
                    Title = spec.Value.Title,
                    Skin = spec.Value.Skin,
                    ModuleId = spec.Value.ModuleId,
                    ConfigJson = spec.Value.Config
                };
                _db.WidgetTemplates.Add(template);
                pairs.Add((widget, template));
            }

            if (pairs.Count > 0)
            {
                await _db.SaveChangesAsync();
                foreach (var (widget, template) in pairs)
                {
                    widget.Type = "studio";
                    widget.TemplateId = template.Id;
                    widget.ModuleId = template.ModuleId;
                }

                await _db.SaveChangesAsync();
            }
        }

        await EnsurePresetTemplatesAsync(list);
    }

    private async Task EnsurePresetTemplatesAsync(List<ModuleDef> modules)
    {
        var existing = await _db.WidgetTemplates.AsNoTracking().Select(t => t.ConfigJson).ToListAsync();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in existing)
        {
            var preset = ReadWidgetConfig(json).Preset;
            if (!string.IsNullOrWhiteSpace(preset))
                keys.Add(preset);
        }

        var added = false;
        foreach (var preset in Presets)
        {
            if (keys.Contains(preset.Key))
                continue;
            var moduleId = ModuleId(modules, preset.Module) ?? ModuleId(modules, preset.Fallback);
            if (moduleId is null)
                continue;
            _db.WidgetTemplates.Add(new WidgetTemplate
            {
                Title = preset.Title,
                Skin = preset.Skin,
                ModuleId = moduleId.Value,
                ConfigJson = JsonSerializer.Serialize(new WidgetConfig
                {
                    Measure = preset.Measure,
                    Field = preset.Key == "revenue" && ModuleId(modules, "payments") is null ? "grandTotal" : preset.Field,
                    Grain = preset.Grain,
                    Span = preset.Span,
                    Preset = preset.Key,
                    Columns = preset.Columns
                })
            });
            added = true;
        }

        if (added)
            await _db.SaveChangesAsync();
    }

    private static (string Title, string Skin, int ModuleId, string Config)? LegacySpec(DashboardWidget widget, List<ModuleDef> modules)
    {
        var title = string.IsNullOrWhiteSpace(widget.Title) ? null : widget.Title.Trim();
        WidgetConfig Config(string measure, string? field, string grain, int span, List<string>? columns = null) => new()
        {
            Measure = measure,
            Field = field,
            Grain = grain,
            Span = span,
            Columns = columns ?? []
        };

        (string Skin, int ModuleId, WidgetConfig Config, string Title) Bound(string skin, string moduleName, WidgetConfig config, string fallbackTitle)
        {
            var moduleId = widget.ModuleId > 0 && (moduleName.Length == 0 || modules.Any(m => m.Id == widget.ModuleId))
                ? widget.ModuleId
                : ModuleId(modules, moduleName) ?? 0;
            return (skin, moduleId, config, title ?? fallbackTitle);
        }

        var mapped = widget.Type switch
        {
            "counter" => Bound("kpi", "", Config("count", null, "month", 6), "شمارنده"),
            "pie" => Bound("doughnut", "", Config("count", widget.FieldName, "month", 6), "دونات"),
            "bar" => Bound("share", "", Config("count", widget.FieldName, "month", 6), "نوار سهم"),
            "funnel" => Bound("funnel", "", Config("count", widget.FieldName, "month", 6), "قیف"),
            "monthly" => Bound("bar", "", Config("count", null, "month", 12), "روند ماهانه"),
            "nx-contacts" => Bound("spark", "contacts", Config("count", null, "month", 6), "مخاطبان"),
            "nx-leads" => Bound("area", "leads", Config("count", null, "month", 6), "سرنخ‌ها"),
            "nx-tasks" => Bound("doughnut", "tasks", Config("count", "status", "month", 6), "وظایف"),
            "nx-deals" => Bound("kpi", "opportunities", Config("count", null, "month", 6), "معاملات"),
            "nx-revenue" => Bound("bar", ModuleId(modules, "payments") is null ? "invoices" : "payments", Config("sum", ModuleId(modules, "payments") is null ? "grandTotal" : "amount", "month", 12), "درآمد"),
            "nx-sources" => Bound("share", "leads", Config("count", "source", "month", 6), "منابع"),
            "nx-retention" => Bound("stack", "opportunities", Config("count", "stage", "month", 6), "نرخ برد"),
            "nx-earning" => Bound("radial", "invoices", Config("sum", "grandTotal", "month", 6), "درآمد کل"),
            "nx-orders" => Bound("status", "invoices", Config("count", "status", "month", 6), "وضعیت سفارش"),
            "nx-heatmap" => Bound("heat", "invoices", Config("count", null, "hour", 24), "نقشهٔ زمانی"),
            "nx-meetings" => Bound("list", "events", Config("count", null, "day", 14), "جلسه‌ها"),
            "nx-dealview" => Bound("area-info", "opportunities", Config("sum", "amount", "month", 6), "نمای معاملات"),
            "nx-funnel" => Bound("funnel", "opportunities", Config("count", "stage", "month", 6), "قیف"),
            "nx-customers" => Bound("table", "contacts", Config("count", null, "month", 6, ["name", "mobile", "email"]), "مشتریان"),
            "nx-tasklist" => Bound("check", "tasks", Config("count", "status", "month", 6), "وظایف"),
            _ => ((string Skin, int ModuleId, WidgetConfig Config, string Title)?)null
        };

        if (mapped is null || mapped.Value.ModuleId <= 0)
            return null;
        if (widget.Type == "nx-tasklist")
            mapped.Value.Config.DoneValue = "done";
        return (mapped.Value.Title, mapped.Value.Skin, mapped.Value.ModuleId, JsonSerializer.Serialize(mapped.Value.Config));
    }

    private static int? ModuleId(List<ModuleDef> modules, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return modules.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static readonly PresetSpec[] Presets =
    [
        new("contacts", "مخاطبان", "spark", "contacts", null, "count", null, "month", 6, []),
        new("leads", "سرنخ‌ها", "area", "leads", null, "count", null, "month", 6, []),
        new("tasks", "وظایف", "doughnut", "tasks", null, "count", "status", "month", 6, []),
        new("deals", "معاملات", "kpi", "opportunities", null, "count", null, "month", 6, []),
        new("revenue", "درآمد", "bar", "payments", "invoices", "sum", "amount", "month", 12, []),
        new("sources", "منابع", "share", "leads", null, "count", "source", "month", 6, []),
        new("winrate", "نرخ برد", "stack", "opportunities", null, "count", "stage", "month", 6, []),
        new("funnel", "قیف", "funnel", "opportunities", null, "count", "stage", "month", 6, []),
        new("meetings", "جلسه‌ها", "list", "events", null, "count", null, "day", 14, []),
        new("customers", "مشتریان", "table", "contacts", null, "count", null, "month", 6, ["name", "mobile", "email"])
    ];

    private readonly record struct PresetSpec(string Key, string Title, string Skin, string Module, string? Fallback, string Measure, string? Field, string Grain, int Span, List<string> Columns);

    private async Task<StudioPaint> PaintTemplateAsync(WidgetTemplate template)
    {
        var skin = WidgetSkins.Find(template.Skin)?.Id ?? template.Skin;
        var config = ReadWidgetConfig(template.ConfigJson);
        config.Grain = NormalizeGrain(config.Grain);
        if (config.Span <= 0)
            config.Span = DefaultSpan(config.Grain);
        var modules = await _metadata.GetActiveModulesAsync();
        var module = modules.FirstOrDefault(m => m.Id == template.ModuleId);
        var paint = new StudioPaint
        {
            Skin = skin,
            Title = string.IsNullOrWhiteSpace(template.Title) ? "ویجت" : template.Title,
            Module = module?.Name ?? ""
        };
        if (module is null)
            return paint;

        var fieldMap = await _metadata.GetFieldsForModulesAsync([module.Id]);
        fieldMap.TryGetValue(module.Id, out var fields);
        fields ??= [];
        var family = WidgetSkins.Find(skin)?.Family ?? "kpi";
        var sum = config.Measure == "sum";

        string LabelOf(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "نامشخص";
            var field = fields.FirstOrDefault(f => f.Name == config.Field);
            return field?.PicklistValues.FirstOrDefault(p => string.Equals(p.Value, raw, StringComparison.OrdinalIgnoreCase))?.Label ?? raw;
        }

        if (family is "series" or "kpi" or "ratio")
        {
            var points = await DailyAsync(module.Id, config, sum ? config.Field : null, false);
            var seriesPoints = config.Grain == "hour"
                ? await DailyAsync(module.Id, config, sum ? config.Field : null, true)
                : points;
            var rolled = Roll(seriesPoints, config.Grain, config.Span, sum);
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IranZone());
            var pc = new PersianCalendar();
            var year = pc.GetYear(now);
            var month = pc.GetMonth(now);
            var prev = month == 1 ? (year - 1, 12) : (year, month - 1);
            decimal Take(AggPoint point) => sum ? point.Total : point.Count;
            var allValue = points.Sum(Take);
            var thisValue = points.Where(p => InPersianMonth(pc, p.Local, year, month)).Sum(Take);
            var prevValue = points.Where(p => InPersianMonth(pc, p.Local, prev.Item1, prev.Item2)).Sum(Take);
            var yearValue = points.Where(p => pc.GetYear(p.Local) == year).Sum(Take);
            (paint.Delta, paint.Up) = DeltaAmount(thisValue, prevValue);
            paint.Kpi = sum ? allValue.ToFa() : ((int)allValue).ToFa();
            paint.Hint = "ماه قبل: " + (sum ? prevValue.ToFa() : ((int)prevValue).ToFa());
            paint.Labels = rolled.Labels;
            paint.Data = rolled.Data;

            if (skin == "bar")
            {
                var months = Roll(points, "month", Math.Max(config.Span, 6), sum);
                var days = Roll(points, "day", 7, sum);
                paint.Tabs = new StudioTabs
                {
                    MonthLabels = months.Labels,
                    Month = months.Data,
                    WeekLabels = days.Labels,
                    Week = days.Data,
                    TodayLabels = ["۰–۴", "۴–۸", "۸–۱۲", "۱۲–۱۶", "۱۶–۲۰", "۲۰–۲۴"],
                    Today = await HourBandsAsync(module.Id, config)
                };
                paint.Labels = months.Labels;
                paint.Data = months.Data;
            }

            if (skin == "line")
            {
                var second = string.IsNullOrWhiteSpace(config.Field2)
                    ? new int[rolled.Data.Length]
                    : Roll(await DailyAsync(module.Id, config, config.Field2, config.Grain == "hour"), config.Grain, config.Span, true).Data;
                paint.Series =
                [
                    new StudioSeries { Name = paint.Title, Data = rolled.Data },
                    new StudioSeries { Name = "سری دوم", Data = Fit(second, rolled.Data.Length) }
                ];
            }

            if (skin is "radial" or "target")
            {
                decimal part;
                decimal whole;
                if (config.Ratio == "picklist" && !string.IsNullOrWhiteSpace(config.Field))
                {
                    var groups = await GroupAsync(module.Id, config);
                    whole = Math.Max(1, groups.Sum(g => g.Count));
                    part = groups.Where(g => string.Equals(g.Value, config.PartValue, StringComparison.OrdinalIgnoreCase)).Sum(g => g.Count);
                    paint.GaugeLabel = skin == "target"
                        ? ((int)Math.Round(part * 100m / whole)).ToFa() + "٪"
                        : ((int)part).ToFa();
                }
                else
                {
                    whole = yearValue <= 0 ? 1 : yearValue;
                    part = thisValue;
                    paint.GaugeLabel = skin == "target"
                        ? ((int)Math.Min(100, Math.Round(part * 100m / whole))).ToFa() + "٪"
                        : (sum ? yearValue.ToFa() : ((int)yearValue).ToFa());
                }

                paint.Gauge = (int)Math.Min(100, Math.Round(part * 100m / whole));
                paint.GaugeSub = "این ماه: " + (sum ? thisValue.ToFa() : ((int)thisValue).ToFa());
                paint.Kpi = paint.GaugeLabel;
            }

            if (skin == "heat")
                (paint.HeatDays, paint.Heat) = await HeatAsync(module.Id, config);
        }
        else if (family == "group")
        {
            var groups = await GroupAsync(module.Id, config);
            var field = fields.FirstOrDefault(f => f.Name == config.Field);
            if (field?.PicklistValues.Count > 0)
            {
                var known = field.PicklistValues.OrderBy(p => p.SortOrder).Select(p => p.Value).ToList();
                groups = known
                    .Select(v => new GroupAggRow { Value = v, Count = groups.FirstOrDefault(g => string.Equals(g.Value, v, StringComparison.OrdinalIgnoreCase))?.Count ?? 0 })
                    .Concat(groups.Where(g => known.All(v => !string.Equals(v, g.Value, StringComparison.OrdinalIgnoreCase))))
                    .ToList();
            }

            var total = Math.Max(1, groups.Sum(g => g.Count));
            var max = Math.Max(1, groups.Select(g => g.Count).DefaultIfEmpty(0).Max());
            paint.Slices = groups.Select(g => new StudioSlice
            {
                Label = LabelOf(g.Value),
                Value = g.Count,
                Percent = (int)Math.Round(g.Count * 100.0 / (skin is "funnel" or "pipeline" ? max : total))
            }).ToList();
            paint.Kpi = groups.Sum(g => g.Count).ToFa();

            if (skin == "stack")
            {
                var top = groups.OrderByDescending(g => g.Count).Take(3).ToList();
                var split = await SplitAsync(module.Id, config);
                var months = Roll(split.Select(s => new AggPoint(s.Local, s.Count, 0)).ToList(), "month", 6, false);
                var series = new List<StudioSeries>();
                foreach (var item in top)
                {
                    var only = split.Where(s => string.Equals(s.Value, item.Value, StringComparison.OrdinalIgnoreCase))
                        .Select(s => new AggPoint(s.Local, s.Count, 0)).ToList();
                    series.Add(new StudioSeries
                    {
                        Name = LabelOf(item.Value),
                        Data = Fit(Roll(only, "month", 6, false).Data, months.Labels.Length)
                    });
                }

                paint.Labels = months.Labels;
                paint.Series = series;
            }
        }
        else
        {
            var heads = new List<string>();
            foreach (var column in config.Columns.Take(3))
            {
                var field = fields.FirstOrDefault(f => f.Name == column);
                heads.Add(field?.Label ?? column);
            }

            paint.Heads = heads;
            var latest = await LatestAsync(module.Id, config);
            paint.Rows = latest.Select(row =>
            {
                var status = string.IsNullOrWhiteSpace(config.Field) ? null : Field(row.CustomData ?? "", config.Field);
                return new StudioRow
                {
                    Title = string.IsNullOrWhiteSpace(row.Title) ? "رکورد" : row.Title,
                    When = PersianDateHelper.ToJalaliDate(row.CreatedAtUtc),
                    Href = $"/App/m/{module.Name}/{row.Id}",
                    Done = !string.IsNullOrWhiteSpace(config.DoneValue)
                        && string.Equals(status, config.DoneValue, StringComparison.OrdinalIgnoreCase),
                    Cells = config.Columns.Take(3).Select(c => Field(row.CustomData ?? "", c) ?? "—").ToList()
                };
            }).ToList();
        }

        return paint;
    }

    private async Task<List<AggPoint>> DailyAsync(int moduleId, WidgetConfig config, string? sumField, bool hourly)
    {
        var dateField = SafeName(config.DateField) ? config.DateField! : "";
        var field = SafeName(sumField) ? sumField! : "";
        var since = DateTime.UtcNow.AddHours(-(Math.Clamp(config.Span, 1, 48) + 2));
        List<DayAggRow> rows;
        if (hourly)
        {
            rows = await QueryAsync<DayAggRow>("""
                SELECT to_char(date_trunc('hour', "CreatedAtUtc" AT TIME ZONE 'Asia/Tehran'), 'YYYY-MM-DD HH24') AS "Day",
                       COUNT(*)::int AS "Count",
                       COALESCE(SUM(CASE WHEN {0} = '' THEN 0 ELSE COALESCE(NULLIF(regexp_replace(translate(COALESCE("CustomData" ->> {0}, ''), '۰۱۲۳۴۵۶۷۸۹٬،,', '0123456789'), '[^0-9.\-]', '', 'g'), '')::numeric, 0) END), 0) AS "Total"
                FROM "Records"
                """, [field], moduleId, config, since, "GROUP BY 1");
        }
        else
        {
            rows = await QueryAsync<DayAggRow>("""
                SELECT (CASE WHEN {0} = '' THEN to_char("CreatedAtUtc" AT TIME ZONE 'Asia/Tehran', 'YYYY-MM-DD')
                             ELSE COALESCE(NULLIF(LEFT("CustomData" ->> {0}, 10), ''), to_char("CreatedAtUtc" AT TIME ZONE 'Asia/Tehran', 'YYYY-MM-DD')) END) AS "Day",
                       COUNT(*)::int AS "Count",
                       COALESCE(SUM(CASE WHEN {1} = '' THEN 0 ELSE COALESCE(NULLIF(regexp_replace(translate(COALESCE("CustomData" ->> {1}, ''), '۰۱۲۳۴۵۶۷۸۹٬،,', '0123456789'), '[^0-9.\-]', '', 'g'), '')::numeric, 0) END), 0) AS "Total"
                FROM "Records"
                """, [dateField, field], moduleId, config, null, "GROUP BY 1");
        }

        var points = new List<AggPoint>();
        foreach (var row in rows)
        {
            if (TryParseBucket(row.Day, out var local))
                points.Add(new AggPoint(local, row.Count, row.Total));
        }

        return points;
    }

    private async Task<List<GroupAggRow>> GroupAsync(int moduleId, WidgetConfig config)
    {
        var field = SafeName(config.Field) ? config.Field! : "";
        return await QueryAsync<GroupAggRow>("""
            SELECT COALESCE("CustomData" ->> {0}, '') AS "Value", COUNT(*)::int AS "Count"
            FROM "Records"
            """, [field], moduleId, config, null, "GROUP BY 1 ORDER BY 2 DESC");
    }

    private async Task<List<SplitPoint>> SplitAsync(int moduleId, WidgetConfig config)
    {
        var field = SafeName(config.Field) ? config.Field! : "";
        var rows = await QueryAsync<SplitAggRow>("""
            SELECT to_char("CreatedAtUtc" AT TIME ZONE 'Asia/Tehran', 'YYYY-MM-DD') AS "Day",
                   COALESCE("CustomData" ->> {0}, '') AS "Value",
                   COUNT(*)::int AS "Count"
            FROM "Records"
            """, [field], moduleId, config, null, "GROUP BY 1, 2");
        var list = new List<SplitPoint>();
        foreach (var row in rows)
        {
            if (TryParseBucket(row.Day, out var local))
                list.Add(new SplitPoint(local, row.Value ?? "", row.Count));
        }

        return list;
    }

    private async Task<(string[] Days, List<StudioSeries> Series)> HeatAsync(int moduleId, WidgetConfig config)
    {
        var rows = await QueryAsync<HeatAggRow>("""
            SELECT EXTRACT(DOW FROM "CreatedAtUtc" AT TIME ZONE 'Asia/Tehran')::int AS "Dow",
                   EXTRACT(HOUR FROM "CreatedAtUtc" AT TIME ZONE 'Asia/Tehran')::int AS "Hour",
                   COUNT(*)::int AS "Count"
            FROM "Records"
            """, [], moduleId, config, null, "GROUP BY 1, 2");
        var bands = new[] { 8, 10, 12, 14, 16 };
        var grid = bands.Select(_ => new int[7]).ToArray();
        foreach (var row in rows)
        {
            var day = (row.Dow + 1) % 7;
            var band = row.Hour < 9 ? 0 : row.Hour < 11 ? 1 : row.Hour < 13 ? 2 : row.Hour < 15 ? 3 : 4;
            if (day is >= 0 and < 7)
                grid[band][day] += row.Count;
        }

        return (
            ["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه"],
            bands.Select((hour, i) => new StudioSeries { Name = hour.ToFa(), Data = grid[i] }).ToList());
    }

    private async Task<int[]> HourBandsAsync(int moduleId, WidgetConfig config)
    {
        var zone = IranZone();
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
        var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(today, DateTimeKind.Unspecified), zone);
        var rows = await QueryAsync<HeatAggRow>("""
            SELECT 0 AS "Dow",
                   EXTRACT(HOUR FROM "CreatedAtUtc" AT TIME ZONE 'Asia/Tehran')::int AS "Hour",
                   COUNT(*)::int AS "Count"
            FROM "Records"
            """, [], moduleId, config, start, "GROUP BY 2");
        var data = new int[6];
        foreach (var row in rows)
            data[Math.Clamp(row.Hour / 4, 0, 5)] += row.Count;
        return data;
    }

    private async Task<List<LatestAggRow>> LatestAsync(int moduleId, WidgetConfig config)
    {
        return await QueryAsync<LatestAggRow>("""
            SELECT "Id" AS "Id",
                   COALESCE("Title", '') AS "Title",
                   "CreatedAtUtc" AS "CreatedAtUtc",
                   COALESCE("CustomData"::text, '') AS "CustomData"
            FROM "Records"
            """, [], moduleId, config, null, """ORDER BY "CreatedAtUtc" DESC LIMIT 8""");
    }

    private static (string[] Labels, int[] Data) Roll(List<AggPoint> points, string grain, int span, bool sum)
    {
        span = Math.Clamp(span, 1, 36);
        var pc = new PersianCalendar();
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IranZone());
        decimal Take(AggPoint point) => sum ? point.Total : point.Count;
        int SumWhere(Func<AggPoint, bool> pred) => (int)Math.Round(points.Where(pred).Sum(Take));

        if (grain == "hour")
        {
            var labels = new List<string>();
            var data = new List<int>();
            var cursor = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
            for (var i = span - 1; i >= 0; i--)
            {
                var hour = cursor.AddHours(-i);
                labels.Add(hour.Hour.ToFa());
                data.Add(SumWhere(p => p.Local.Date == hour.Date && p.Local.Hour == hour.Hour));
            }

            return (labels.ToArray(), data.ToArray());
        }

        if (grain == "weekday")
        {
            var since = now.Date.AddDays(-Math.Max(span, 7));
            var data = new int[7];
            foreach (var point in points.Where(p => p.Local.Date >= since))
                data[((int)point.Local.DayOfWeek + 1) % 7] += (int)Math.Round(Take(point));
            return (["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه"], data);
        }

        if (grain == "day")
        {
            var labels = new List<string>();
            var data = new List<int>();
            for (var i = span - 1; i >= 0; i--)
            {
                var day = now.Date.AddDays(-i);
                labels.Add(PersianDateHelper.ToJalaliDate(day));
                data.Add(SumWhere(p => p.Local.Date == day));
            }

            return (labels.ToArray(), data.ToArray());
        }

        if (grain == "week")
        {
            var labels = new List<string>();
            var data = new List<int>();
            var saturday = now.Date.AddDays(-(((int)now.DayOfWeek + 1) % 7));
            for (var i = span - 1; i >= 0; i--)
            {
                var start = saturday.AddDays(-7 * i);
                var end = start.AddDays(7);
                labels.Add(PersianDateHelper.ToJalaliDate(start));
                data.Add(SumWhere(p => p.Local.Date >= start && p.Local.Date < end));
            }

            return (labels.ToArray(), data.ToArray());
        }

        if (grain == "year")
        {
            var labels = new List<string>();
            var data = new List<int>();
            var year = pc.GetYear(now);
            for (var i = span - 1; i >= 0; i--)
            {
                var cursor = year - i;
                labels.Add(cursor.ToFa());
                data.Add(SumWhere(p => pc.GetYear(p.Local) == cursor));
            }

            return (labels.ToArray(), data.ToArray());
        }

        var monthLabels = new List<string>();
        var monthData = new List<int>();
        var thisYear = pc.GetYear(now);
        var thisMonth = pc.GetMonth(now);
        for (var i = span - 1; i >= 0; i--)
        {
            var cursor = thisMonth - i;
            var cursorYear = thisYear;
            while (cursor <= 0)
            {
                cursor += 12;
                cursorYear--;
            }

            monthLabels.Add(PersianMonths[cursor]);
            var y = cursorYear;
            var m = cursor;
            monthData.Add(SumWhere(p => InPersianMonth(pc, p.Local, y, m)));
        }

        return (monthLabels.ToArray(), monthData.ToArray());
    }

    private static bool InPersianMonth(PersianCalendar pc, DateTime local, int year, int month)
    {
        try
        {
            return pc.GetYear(local) == year && pc.GetMonth(local) == month;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static int[] Fit(int[] data, int length)
    {
        if (data.Length == length)
            return data;
        var next = new int[length];
        Array.Copy(data, next, Math.Min(data.Length, length));
        return next;
    }

    private static bool TryParseBucket(string? day, out DateTime local)
    {
        local = default;
        if (string.IsNullOrWhiteSpace(day))
            return false;
        var text = PersianFormattingHelper.FromPersianDigits(day).Trim();
        var head = text.Split(' ', 'T')[0];
        var hour = 0;
        if (text.Length > head.Length)
            int.TryParse(text[(head.Length + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out hour);
        var parts = head.Split('/', '-');
        if (parts.Length == 3 && int.TryParse(parts[0], out var year) && year is >= 1300 and <= 1500)
        {
            try
            {
                local = PersianDateHelper.FromJalaliDateTime(head).AddHours(hour);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        if (!DateTime.TryParse(head, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        local = parsed.Date.AddHours(hour);
        return true;
    }

    private static (string Text, bool Up) DeltaAmount(decimal current, decimal previous)
    {
        if (previous <= 0)
            return current <= 0 ? ("۰٪", true) : ("+۱۰۰٪", true);
        var pct = (int)Math.Round((current - previous) * 100m / previous);
        var up = pct >= 0;
        return ((up ? "+" : "−") + Math.Abs(pct).ToFa() + "٪", up);
    }

    private static WidgetConfig ReadWidgetConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new WidgetConfig();
        try
        {
            return JsonSerializer.Deserialize<WidgetConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new WidgetConfig();
        }
        catch (JsonException)
        {
            return new WidgetConfig();
        }
    }

    private static string WriteWidgetConfig(string? measure, string? field, string? dateField, string? field2, string? partValue, string? ratio, string? doneValue, string? columns, string? grain, int span, string? filtersJson, string? preset)
    {
        var filters = new List<WidgetFilter>();
        if (!string.IsNullOrWhiteSpace(filtersJson))
        {
            try
            {
                filters = JsonSerializer.Deserialize<List<WidgetFilter>>(filtersJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            catch (JsonException)
            {
                filters = [];
            }
        }

        var grainName = NormalizeGrain(grain);
        var config = new WidgetConfig
        {
            Measure = measure == "sum" ? "sum" : "count",
            Field = NullIfEmpty(field),
            DateField = NullIfEmpty(dateField),
            Field2 = NullIfEmpty(field2),
            PartValue = NullIfEmpty(partValue),
            Ratio = ratio == "picklist" ? "picklist" : "monthYear",
            DoneValue = NullIfEmpty(doneValue),
            Columns = (columns ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(SafeName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList(),
            Grain = grainName,
            Span = Math.Clamp(span <= 0 ? DefaultSpan(grainName) : span, 1, 36),
            Preset = NullIfEmpty(preset),
            Filters = filters
                .Where(f => SafeName(f.Field) && (Valueless(NormalizeOp(f.Op)) || !string.IsNullOrWhiteSpace(f.Value)))
                .Take(12)
                .Select(f => new WidgetFilter
                {
                    Field = f.Field!.Trim(),
                    Op = NormalizeOp(f.Op),
                    Value = Valueless(NormalizeOp(f.Op)) ? null : f.Value!.Trim()
                })
                .ToList()
        };
        return JsonSerializer.Serialize(config);
    }

    private static string NormalizeGrain(string? grain) => grain switch
    {
        "hour" or "weekday" or "day" or "week" or "month" or "year" => grain,
        _ => "month"
    };

    private static int DefaultSpan(string grain) => grain switch
    {
        "hour" => 24,
        "weekday" => 28,
        "day" => 14,
        "week" => 8,
        "year" => 3,
        _ => 6
    };

    private async Task<List<T>> QueryAsync<T>(string selectFrom, object[] selectArgs, int moduleId, WidgetConfig config, DateTime? since, string tail)
        where T : class
    {
        var args = new List<object>(selectArgs);
        var sql = new StringBuilder(selectFrom);
        var moduleSlot = args.Count;
        args.Add(moduleId);
        var tenantSlot = args.Count;
        args.Add(_tenant.TenantId ?? 0);
        sql.Append(" WHERE \"ModuleId\" = {").Append(moduleSlot).Append("} AND \"TenantId\" = {").Append(tenantSlot).Append("} AND \"IsDeleted\" = FALSE");
        if (since is DateTime from)
        {
            var sinceSlot = args.Count;
            args.Add(from);
            sql.Append(" AND \"CreatedAtUtc\" >= {").Append(sinceSlot).Append('}');
        }

        AppendFilters(sql, args, config);
        sql.Append(' ').Append(tail);
        return await _db.Database.SqlQueryRaw<T>(sql.ToString(), args.ToArray()).ToListAsync();
    }

    private static void AppendFilters(StringBuilder sql, List<object> args, WidgetConfig config)
    {
        foreach (var filter in (config.Filters ?? []).Where(f => SafeName(f.Field)).Take(12))
        {
            var op = NormalizeOp(filter.Op);
            var fieldSlot = args.Count;
            args.Add(filter.Field!.Trim());
            var raw = "COALESCE(\"CustomData\" ->> {" + fieldSlot + "}, '')";
            var digits = "translate(COALESCE(\"CustomData\" ->> {" + fieldSlot + "}, ''), '۰۱۲۳۴۵۶۷۸۹', '0123456789')";
            var number = "NULLIF(regexp_replace(translate(COALESCE(\"CustomData\" ->> {" + fieldSlot + "}, ''), '۰۱۲۳۴۵۶۷۸۹٬،,', '0123456789'), '[^0-9.\\-]', '', 'g'), '')::numeric";
            var dateKey = $"""LEFT(regexp_replace({digits}, '[^0-9]', '', 'g'), 8)""";
            sql.Append(" AND ");
            switch (op)
            {
                case "empty":
                    sql.Append(raw).Append(" = ''");
                    break;
                case "notEmpty":
                    sql.Append(raw).Append(" <> ''");
                    break;
                case "neq":
                    args.Add(filter.Value ?? "");
                    sql.Append(raw).Append(" <> {").Append(args.Count - 1).Append('}');
                    break;
                case "contains":
                    args.Add(Like(filter.Value));
                    sql.Append(raw).Append(" ILIKE {").Append(args.Count - 1).Append("} ESCAPE '\\'");
                    break;
                case "notContains":
                    args.Add(Like(filter.Value));
                    sql.Append(raw).Append(" NOT ILIKE {").Append(args.Count - 1).Append("} ESCAPE '\\'");
                    break;
                case "numEq":
                case "numNeq":
                case "gt":
                case "lt":
                case "gte":
                case "lte":
                    if (!TryAmount(filter.Value, out var amount))
                    {
                        sql.Append("TRUE");
                        break;
                    }

                    args.Add(amount);
                    var sign = op switch
                    {
                        "numNeq" => "<>",
                        "gt" => ">",
                        "lt" => "<",
                        "gte" => ">=",
                        "lte" => "<=",
                        _ => "="
                    };
                    sql.Append(number).Append(' ').Append(sign).Append(" {").Append(args.Count - 1).Append('}');
                    break;
                case "dateEq":
                case "dateGt":
                case "dateLt":
                    args.Add(DateKey(filter.Value));
                    var dateSign = op == "dateGt" ? ">" : op == "dateLt" ? "<" : "=";
                    sql.Append(dateKey).Append(' ').Append(dateSign).Append(" {").Append(args.Count - 1).Append('}');
                    break;
                case "has":
                case "notHas":
                    args.Add("," + (filter.Value ?? "").Trim() + ",");
                    sql.Append(op == "notHas" ? "position(" : "position(");
                    sql.Append('{').Append(args.Count - 1).Append("} in ',' || replace(replace(replace(replace(")
                        .Append(raw).Append(", ' ', ''), '\"', ''), '[', ''), ']', '') || ',')")
                        .Append(op == "notHas" ? " = 0" : " > 0");
                    break;
                case "check":
                    sql.Append("LOWER(").Append(raw).Append(filter.Value == "false"
                        ? ") IN ('false', '0', 'no', 'خیر')"
                        : ") IN ('true', '1', 'yes', 'بله')");
                    break;
                default:
                    args.Add(filter.Value ?? "");
                    sql.Append(raw).Append(" = {").Append(args.Count - 1).Append('}');
                    break;
            }
        }
    }

    private static string Like(string? value)
    {
        var text = (value ?? "").Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return "%" + text + "%";
    }

    private static bool TryAmount(string? value, out decimal amount)
    {
        var text = PersianFormattingHelper.FromPersianDigits(value ?? "")
            .Replace(",", "", StringComparison.Ordinal)
            .Replace("٬", "", StringComparison.Ordinal)
            .Replace("،", "", StringComparison.Ordinal)
            .Trim();
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static string DateKey(string? value)
    {
        var digits = new string(PersianFormattingHelper.FromPersianDigits(value ?? "").Where(char.IsDigit).ToArray());
        return digits.Length >= 8 ? digits[..8] : digits;
    }

    private static string NormalizeOp(string? op) => op switch
    {
        "neq" or "contains" or "notContains" or "empty" or "notEmpty" or "numEq" or "numNeq"
            or "gt" or "lt" or "gte" or "lte" or "dateEq" or "dateGt" or "dateLt"
            or "has" or "notHas" or "check" => op,
        _ => "eq"
    };

    private static bool Valueless(string op) => op is "empty" or "notEmpty";

    private static bool SafeName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class DayAggRow
    {
        public string Day { get; set; } = "";
        public int Count { get; set; }
        public decimal Total { get; set; }
    }

    private sealed class GroupAggRow
    {
        public string? Value { get; set; }
        public int Count { get; set; }
    }

    private sealed class SplitAggRow
    {
        public string Day { get; set; } = "";
        public string? Value { get; set; }
        public int Count { get; set; }
    }

    private sealed class HeatAggRow
    {
        public int Dow { get; set; }
        public int Hour { get; set; }
        public int Count { get; set; }
    }

    private sealed class LatestAggRow
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? CustomData { get; set; }
    }

    private readonly record struct AggPoint(DateTime Local, int Count, decimal Total);
    private readonly record struct SplitPoint(DateTime Local, string Value, int Count);
}
