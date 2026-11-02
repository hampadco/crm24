using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Web.Services;

namespace Crm.Web.Areas.App.Controllers;

public static class WidgetSkins
{
    public sealed record Skin(string Id, string Title, string Family, int W, int H, int MinW, int MaxW, int MinH, int MaxH);

    public static readonly Skin[] All =
    [
        new("spark", "میلهٔ کوچک گرادیانی", "series", 4, 3, 4, 12, 3, 6),
        new("area", "ناحیهٔ بنفش", "series", 4, 3, 4, 12, 3, 6),
        new("area-info", "ناحیهٔ بنفش روشن", "series", 6, 4, 4, 12, 3, 6),
        new("bar", "میلهٔ گرادیانی با تب زمان", "series", 8, 4, 6, 12, 2, 8),
        new("line", "خط دو سری", "series", 8, 4, 6, 12, 2, 8),
        new("heat", "نقشهٔ حرارتی", "series", 4, 5, 4, 12, 3, 8),
        new("share", "نوار سهم افقی", "group", 4, 4, 4, 12, 3, 8),
        new("stack", "ستون روی‌هم", "group", 6, 3, 6, 12, 2, 8),
        new("doughnut", "دونات", "group", 4, 3, 4, 12, 2, 8),
        new("funnel", "قیف", "group", 6, 4, 6, 12, 3, 8),
        new("pipeline", "نوار پیشرفت", "group", 6, 4, 6, 12, 3, 8),
        new("status", "وضعیت روی کارت بنفش", "group", 4, 4, 4, 12, 3, 8),
        new("radial", "گیج سفید", "ratio", 4, 5, 4, 12, 3, 8),
        new("target", "گیج هدف", "ratio", 4, 5, 4, 12, 3, 8),
        new("kpi", "عدد بزرگ", "kpi", 4, 3, 3, 6, 2, 4),
        new("list", "فهرست", "rows", 4, 5, 4, 12, 4, 8),
        new("table", "جدول", "rows", 8, 4, 6, 12, 4, 8),
        new("check", "چک‌لیست", "rows", 4, 5, 4, 12, 4, 8)
    ];

    public static Skin? Find(string? id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
}

public class WidgetConfig
{
    public string Measure { get; set; } = "count";
    public string? Field { get; set; }
    public string? DateField { get; set; }
    public string? Field2 { get; set; }
    public string? PartValue { get; set; }
    public string Ratio { get; set; } = "monthYear";
    public string? DoneValue { get; set; }
    public List<string> Columns { get; set; } = [];
    public string Grain { get; set; } = "month";
    public int Span { get; set; } = 6;
    public string? Preset { get; set; }
    public List<WidgetFilter> Filters { get; set; } = [];
}

public class WidgetFilter
{
    public string? Field { get; set; }
    public string? Op { get; set; }
    public string? Value { get; set; }
}

public class StudioPaint
{
    public int WidgetId { get; set; }
    public string Skin { get; set; } = "";
    public string Title { get; set; } = "";
    public string Module { get; set; } = "";
    public string Kpi { get; set; } = "۰";
    public string Delta { get; set; } = "۰٪";
    public bool Up { get; set; } = true;
    public string Hint { get; set; } = "";
    public string[] Labels { get; set; } = [];
    public int[] Data { get; set; } = [];
    public List<StudioSeries> Series { get; set; } = [];
    public List<StudioSlice> Slices { get; set; } = [];
    public int Gauge { get; set; }
    public string GaugeLabel { get; set; } = "۰";
    public string GaugeSub { get; set; } = "";
    public string[] HeatDays { get; set; } = [];
    public List<StudioSeries> Heat { get; set; } = [];
    public List<string> Heads { get; set; } = [];
    public List<StudioRow> Rows { get; set; } = [];
    public StudioTabs? Tabs { get; set; }
}

public class StudioSeries
{
    public string Name { get; set; } = "";
    public int[] Data { get; set; } = [];
}

public class StudioSlice
{
    public string Label { get; set; } = "";
    public int Value { get; set; }
    public int Percent { get; set; }
}

public class StudioRow
{
    public string Title { get; set; } = "";
    public string When { get; set; } = "";
    public string Href { get; set; } = "";
    public bool Done { get; set; }
    public List<string> Cells { get; set; } = [];
}

public class StudioTabs
{
    public string[] MonthLabels { get; set; } = [];
    public int[] Month { get; set; } = [];
    public string[] WeekLabels { get; set; } = [];
    public int[] Week { get; set; } = [];
    public string[] TodayLabels { get; set; } = [];
    public int[] Today { get; set; } = [];
}

public class StudioPageVm
{
    public List<WidgetTemplate> Templates { get; set; } = [];
    public List<ModuleDef> Modules { get; set; } = [];
    public string FieldsJson { get; set; } = "{}";
    public WidgetTemplate? Editing { get; set; }
    public WidgetConfig Config { get; set; } = new();
}

public partial class DashboardController
{
    [HttpGet("/App/dashboard/studio")]
    public async Task<IActionResult> Studio(int? id)
    {
        var modules = (await _metadata.GetActiveModulesAsync()).ToList();
        await PrepareStudioWidgetsAsync(modules);
        var fieldMap = await _metadata.GetFieldsForModulesAsync(modules.Select(m => m.Id));
        var fields = fieldMap.ToDictionary(
            kv => kv.Key.ToString(),
            kv => kv.Value.Select(f => new { name = f.Name, label = f.Label, type = f.Type.ToString(), values = f.PicklistValues.OrderBy(p => p.SortOrder).Select(p => new { p.Value, p.Label }) }));

        WidgetTemplate? editing = null;
        var config = new WidgetConfig();
        if (id is int templateId)
        {
            editing = await _db.WidgetTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId);
            if (editing is not null)
                config = ReadWidgetConfig(editing.ConfigJson);
        }

        return View(new StudioPageVm
        {
            Templates = await _db.WidgetTemplates.AsNoTracking().OrderByDescending(t => t.Id).ToListAsync(),
            Modules = modules,
            FieldsJson = JsonSerializer.Serialize(fields, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            Editing = editing,
            Config = config
        });
    }

    [HttpPost("/App/dashboard/studio/preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StudioPreview(string skin, int moduleId, string? title, string? measure, string? field, string? dateField, string? field2, string? partValue, string? ratio, string? doneValue, string? columns, string? grain, int span, string? filtersJson)
    {
        var paint = await PaintTemplateAsync(new WidgetTemplate
        {
            Title = string.IsNullOrWhiteSpace(title) ? "پیش‌نمایش" : title.Trim(),
            Skin = skin,
            ModuleId = moduleId,
            ConfigJson = WriteWidgetConfig(measure, field, dateField, field2, partValue, ratio, doneValue, columns, grain, span, filtersJson, null)
        });
        return Json(paint);
    }

    [HttpPost("/App/dashboard/studio/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStudio(int? id, string skin, int moduleId, string? title, string? measure, string? field, string? dateField, string? field2, string? partValue, string? ratio, string? doneValue, string? columns, string? grain, int span, string? filtersJson)
    {
        var known = WidgetSkins.Find(skin);
        var module = await _db.Modules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == moduleId);
        if (known is null || module is null)
        {
            TempData["Error"] = "پوست یا ماژول انتخاب نشده است.";
            return RedirectToAction(nameof(Studio));
        }

        var name = string.IsNullOrWhiteSpace(title) ? known.Title : title.Trim();
        WidgetTemplate row;
        if (id is int templateId)
        {
            row = await _db.WidgetTemplates.FirstOrDefaultAsync(t => t.Id == templateId) ?? new WidgetTemplate();
            if (row.Id == 0)
                _db.WidgetTemplates.Add(row);
        }
        else
        {
            row = new WidgetTemplate();
            _db.WidgetTemplates.Add(row);
        }

        var previous = ReadWidgetConfig(row.ConfigJson);
        var config = WriteWidgetConfig(measure, field, dateField, field2, partValue, ratio, doneValue, columns, grain, span, filtersJson, previous.Preset);
        row.Title = name;
        row.Skin = known.Id;
        row.ModuleId = module.Id;
        row.ConfigJson = config;
        await _db.SaveChangesAsync();
        TempData["Success"] = "قالب ویجت ذخیره شد.";
        return RedirectToAction(nameof(Studio), new { id = row.Id });
    }

    [HttpPost("/App/dashboard/studio/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStudio(int id)
    {
        var row = await _db.WidgetTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (row is not null)
            _db.WidgetTemplates.Remove(row);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Studio));
    }

    [HttpPost("/App/dashboard/studio/place")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceStudio(int templateId)
    {
        if (_tenant.UserId is not int userId)
            return RedirectToAction(nameof(Index));

        var template = await _db.WidgetTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId);
        if (template is null)
            return RedirectToAction(nameof(Index));

        var maxOrder = await _db.DashboardWidgets
            .Where(w => w.UserId == userId)
            .MaxAsync(w => (int?)w.SortOrder) ?? 0;

        _db.DashboardWidgets.Add(new DashboardWidget
        {
            UserId = userId,
            Type = "studio",
            Title = template.Title,
            ModuleId = template.ModuleId,
            TemplateId = template.Id,
            SortOrder = maxOrder + 1
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "ویجت روی داشبورد قرار گرفت.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/App/dashboard/studio-data")]
    public async Task<IActionResult> StudioData()
    {
        if (_tenant.UserId is not int userId)
            return Json(Array.Empty<StudioPaint>());

        var widgets = await _db.DashboardWidgets.AsNoTracking()
            .Where(w => w.UserId == userId && w.Type == "studio" && w.TemplateId != null)
            .ToListAsync();
        var templateIds = widgets.Select(w => w.TemplateId!.Value).Distinct().ToList();
        var templates = await _db.WidgetTemplates.AsNoTracking()
            .Where(t => templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id);

        var paints = new List<StudioPaint>();
        foreach (var widget in widgets)
        {
            if (widget.TemplateId is not int id || !templates.TryGetValue(id, out var template))
                continue;
            var paint = await PaintTemplateAsync(template);
            paint.WidgetId = widget.Id;
            paints.Add(paint);
        }

        return Json(paints);
    }

    [HttpPost("/App/dashboard/reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetDashboard()
    {
        if (_tenant.UserId is not int userId)
            return RedirectToAction(nameof(Index));

        var modules = (await _metadata.GetActiveModulesAsync()).ToList();
        await PrepareStudioWidgetsAsync(modules);
        var existing = await _db.DashboardWidgets.Where(w => w.UserId == userId).ToListAsync();
        _db.DashboardWidgets.RemoveRange(existing);
        await _db.SaveChangesAsync();

        var templates = await _db.WidgetTemplates.AsNoTracking().ToListAsync();
        var plan = new (string Group, (string Key, string Title, int X, int Y, int W, int H)[] Items)[]
        {
            ("نمای کلی", [("contacts", "مخاطبان", 1, 1, 4, 3), ("leads", "سرنخ‌ها", 5, 1, 4, 3), ("deals", "معاملات", 9, 1, 4, 3)]),
            ("فروش", [("funnel", "قیف فروش", 1, 1, 6, 4), ("revenue", "درآمد", 7, 1, 6, 4), ("sources", "منابع", 1, 5, 6, 3), ("winrate", "نرخ برد", 7, 5, 6, 3)]),
            ("پیگیری", [("tasks", "وظایف", 1, 1, 6, 3), ("meetings", "جلسه‌ها", 7, 1, 6, 3), ("customers", "مشتریان", 1, 4, 12, 3)])
        };

        var placed = new List<(string Group, DashboardWidget Widget, int X, int Y, int W, int H)>();
        var order = 1;
        foreach (var group in plan)
        {
            foreach (var item in group.Items)
            {
                var template = templates.FirstOrDefault(t => ReadWidgetConfig(t.ConfigJson).Preset == item.Key);
                if (template is null)
                    continue;
                var widget = new DashboardWidget
                {
                    UserId = userId,
                    Type = "studio",
                    Title = item.Title,
                    ModuleId = template.ModuleId,
                    TemplateId = template.Id,
                    SortOrder = order++
                };
                _db.DashboardWidgets.Add(widget);
                placed.Add((group.Group, widget, item.X, item.Y, item.W, item.H));
            }
        }

        await _db.SaveChangesAsync();
        var board = new BoardFile { V = 2 };
        foreach (var groupName in plan.Select(p => p.Group))
        {
            var items = placed.Where(p => p.Group == groupName).ToList();
            if (items.Count == 0)
                continue;
            board.Groups.Add(new BoardGroupFile
            {
                T = groupName,
                I = items.Select(p => new BoardItemFile
                {
                    K = "w:" + p.Widget.Id,
                    X = p.X,
                    Y = p.Y,
                    W = p.W,
                    H = p.H
                }).ToList()
            });
        }

        _db.DashboardWidgets.Add(new DashboardWidget
        {
            UserId = userId,
            Type = "board",
            Title = "چیدمان",
            FieldName = JsonSerializer.Serialize(board),
            SortOrder = 0
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "داشبورد پیش‌فرض قرار گرفت.";
        return RedirectToAction(nameof(Index));
    }
}
