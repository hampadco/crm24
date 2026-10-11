using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

public class WidgetView
{
    public DashboardWidget Widget { get; set; } = null!;
    public ModuleDef? Module { get; set; }

    /// <summary>counter: مقدار — pie/monthly: برچسب/مقدار سری.</summary>
    public int CounterValue { get; set; }
    public List<(string Label, int Value)> Series { get; set; } = [];

    public int GridX { get; set; } = 1;
    public int GridY { get; set; } = 1;
    public int GridW { get; set; } = 2;
    public int GridH { get; set; } = 1;
}

public class DashboardViewModel
{
    public Tenant Tenant { get; set; } = null!;
    public int UserCount { get; set; }
    public IReadOnlyList<(ModuleDef Module, int RecordCount)> Modules { get; set; } = [];
    public int? TrialDaysLeft { get; set; }
    public string MonthLabel { get; set; } = "";
    public List<MonthModuleStat> MonthModules { get; set; } = [];
    public List<DashEventRow> RecentEvents { get; set; } = [];
    public List<DashGroupVm> Groups { get; set; } = [];
    public List<WidgetView> Widgets { get; set; } = [];
    public List<ModuleDef> AllModules { get; set; } = [];
    public Dictionary<int, List<FieldDef>> PicklistFields { get; set; } = [];
    /// <summary>ویجت‌ها به‌صورت AJAX بعد از رندر اولیه لود می‌شوند.</summary>
    public bool DeferWidgets { get; set; } = true;
}

public class MonthModuleStat
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "bx-cube";
    public int Count { get; set; }
}

public class DashEventRow
{
    public string Who { get; set; } = "";
    public string Initial { get; set; } = "؟";
    public string Sub { get; set; } = "";
    public string Text { get; set; } = "";
    public string When { get; set; } = "";
    public string Href { get; set; } = "";
    public int Tone { get; set; }
}

public class DashGroupVm
{
    public string Title { get; set; } = "";
    public List<DashCellVm> Cells { get; set; } = [];
}

public class DashCellVm
{
    public string Key { get; set; } = "";
    public string Kind { get; set; } = "";
    public int X { get; set; } = 1;
    public int Y { get; set; } = 1;
    public int W { get; set; } = 3;
    public int H { get; set; } = 1;
    public int MinW { get; set; } = 3;
    public int MinH { get; set; } = 1;
    public int MaxH { get; set; } = 3;
    public bool Placed { get; set; }
    public WidgetView? Widget { get; set; }
    public MonthModuleStat? Month { get; set; }
}

public class BoardFile
{
    public int V { get; set; }
    public List<BoardGroupFile> Groups { get; set; } = [];
}

public class BoardGroupFile
{
    public string T { get; set; } = "";
    public List<BoardItemFile> I { get; set; } = [];
}

public class BoardItemFile
{
    public string K { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}

public class DashboardController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly MetadataService _metadata;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DynamicRecordService _records;

    public DashboardController(
        CrmDbContext db,
        ITenantContext tenant,
        MetadataService metadata,
        IServiceScopeFactory scopeFactory,
        DynamicRecordService records)
    {
        _db = db;
        _tenant = tenant;
        _metadata = metadata;
        _scopeFactory = scopeFactory;
        _records = records;
    }

    public async Task<IActionResult> Index()
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == _tenant.TenantId);
        if (tenant is null)
            return RedirectToAction("Login", "Account", new { area = "App" });

        // seeder را روی مسیر بحرانی نگه ندار — پس‌زمینه
        ScheduleSeed(tenant.Id);

        var modules = await _metadata.GetActiveModulesAsync();

        var countByModule = await _db.Records.AsNoTracking()
            .GroupBy(r => r.ModuleId)
            .Select(g => new { ModuleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ModuleId, x => x.Count);

        var moduleStats = modules
            .Select(m => (m, countByModule.GetValueOrDefault(m.Id)))
            .ToList();

        var model = new DashboardViewModel
        {
            Tenant = tenant,
            UserCount = await _db.Users.CountAsync(u => u.TenantId == tenant.Id && u.IsActive),
            Modules = moduleStats,
            AllModules = modules.ToList(),
            TrialDaysLeft = tenant.Status == TenantStatus.Trial && tenant.TrialEndsAtUtc is DateTime end
                ? Math.Max(0, (int)Math.Ceiling((end - DateTime.UtcNow).TotalDays))
                : null,
            DeferWidgets = true
        };

        var pc = new PersianCalendar();
        var zone = IranZone();
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var month = pc.GetMonth(nowLocal);
        var year = pc.GetYear(nowLocal);
        var monthStart = pc.ToDateTime(year, month, 1, 0, 0, 0, 0);
        var monthStartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(monthStart, DateTimeKind.Unspecified), zone);
        var createdThisMonth = await _db.Records.AsNoTracking()
            .Where(r => r.CreatedAtUtc >= monthStartUtc)
            .GroupBy(r => r.ModuleId)
            .Select(g => new { ModuleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ModuleId, x => x.Count);
        model.MonthLabel = $"{PersianMonths[month]} {year.ToString(CultureInfo.GetCultureInfo("fa-IR"))}";
        model.MonthModules = modules
            .Where(m => m.ShowInMenu && !m.IsChildModule)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .Select(m => new MonthModuleStat
            {
                Name = m.Name,
                Label = m.PluralLabel,
                Icon = string.IsNullOrWhiteSpace(m.Icon) ? "bx-cube" : m.Icon,
                Count = createdThisMonth.GetValueOrDefault(m.Id)
            })
            .ToList();

        // اسکلت ویجت‌ها بدون تجمیع سنگین — داده از /widgets-data می‌آید
        var widgets = await _db.DashboardWidgets.AsNoTracking()
            .Where(w => w.UserId == _tenant.UserId)
            .OrderBy(w => w.SortOrder)
            .ToListAsync();

        foreach (var widget in widgets)
        {
            if (widget.Type == "board") continue;
            var module = modules.FirstOrDefault(m => m.Id == widget.ModuleId);
            if (module is null) continue;
            model.Widgets.Add(new WidgetView
            {
                Widget = widget,
                Module = module,
                CounterValue = widget.Type == "counter" ? countByModule.GetValueOrDefault(module.Id) : 0
            });
        }

        var board = widgets.FirstOrDefault(w => w.Type == "board");
        var stored = board?.FieldName;
        if (stored is not null && stored.TrimStart().StartsWith('{'))
        {
            model.Groups = BuildBoard(model, stored, null);
        }
        else
        {
            var (months, layout) = SplitBoard(stored);
            model.MonthModules = OrderMonthModules(model.MonthModules, months);
            model.Groups = BuildBoard(model, null, layout);
        }

        model.RecentEvents = await LoadRecentEventsAsync(modules, zone);

        return View(model);
    }

    [HttpGet("/App/dashboard/picklist-fields")]
    public async Task<IActionResult> PicklistFields()
    {
        var modules = await _metadata.GetActiveModulesAsync();
        var fieldMap = await _metadata.GetFieldsForModulesAsync(modules.Select(m => m.Id));
        var payload = fieldMap.ToDictionary(
            kv => kv.Key.ToString(),
            kv => kv.Value
                .Where(f => f.Type == FieldType.Picklist)
                .Select(f => new { name = f.Name, label = f.Label })
                .ToList());
        return Json(payload);
    }

    [HttpGet("/App/dashboard/widgets-data")]
    public async Task<IActionResult> WidgetsData()
    {
        var modules = await _metadata.GetActiveModulesAsync();
        var moduleById = modules.ToDictionary(m => m.Id);

        var countByModule = await _db.Records.AsNoTracking()
            .GroupBy(r => r.ModuleId)
            .Select(g => new { ModuleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ModuleId, x => x.Count);

        var widgets = await _db.DashboardWidgets.AsNoTracking()
            .Where(w => w.UserId == _tenant.UserId && w.Type != "board" && w.ModuleId > 0)
            .OrderBy(w => w.SortOrder)
            .ToListAsync();

        var fieldMap = await _metadata.GetFieldsForModulesAsync(
            widgets.Select(w => w.ModuleId).Distinct());

        var monthlyCache = new Dictionary<int, List<(string Label, int Value)>>();
        var fieldAggCache = new Dictionary<(int ModuleId, string Field), IReadOnlyList<(string Value, int Count)>>();
        var payload = new List<object>();

        foreach (var widget in widgets)
        {
            if (!moduleById.TryGetValue(widget.ModuleId, out var module))
                continue;

            object? series = null;
            var counter = 0;

            switch (widget.Type)
            {
                case "counter":
                    counter = countByModule.GetValueOrDefault(module.Id);
                    break;

                case "pie" or "bar" or "funnel" when widget.FieldName is not null:
                {
                    var cacheKey = (module.Id, widget.FieldName);
                    if (!fieldAggCache.TryGetValue(cacheKey, out var groups))
                    {
                        groups = await _records.AggregateFieldAsync(module.Id, widget.FieldName);
                        fieldAggCache[cacheKey] = groups;
                    }

                    FieldDef? field = null;
                    if (fieldMap.TryGetValue(module.Id, out var fields))
                        field = fields.FirstOrDefault(f => f.Name == widget.FieldName);

                    var rows = groups
                        .Select(g => (Label: ResolvePicklistLabel(field, g.Value), Value: g.Count, Raw: g.Value))
                        .ToList();

                    if (field?.PicklistValues.Count > 0)
                    {
                        var byRaw = rows.ToDictionary(s => s.Raw, s => s, StringComparer.OrdinalIgnoreCase);
                        var known = field.PicklistValues
                            .OrderBy(p => p.SortOrder)
                            .Select(p => byRaw.TryGetValue(p.Value, out var row)
                                ? row
                                : (Label: p.Label, Value: 0, Raw: p.Value))
                            .ToList();
                        var extras = rows.Where(s => !known.Any(k => string.Equals(k.Raw, s.Raw, StringComparison.OrdinalIgnoreCase))).ToList();
                        rows = known.Concat(extras).ToList();
                    }

                    series = rows.Select(s => new { label = s.Label, value = s.Value, raw = s.Raw }).ToList();
                    break;
                }

                case "monthly":
                {
                    if (!monthlyCache.TryGetValue(module.Id, out var monthSeries))
                    {
                        monthSeries = await LoadPersianMonthsAsync(module.Id);
                        monthlyCache[module.Id] = monthSeries;
                    }

                    series = monthSeries.Select(s => new { label = s.Label, value = s.Value }).ToList();
                    break;
                }
            }

            payload.Add(new
            {
                id = widget.Id,
                type = widget.Type,
                title = widget.Title,
                counter,
                series
            });
        }

        return Json(payload);
    }

    [HttpPost("/App/dashboard/widgets/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddWidget(string type, int moduleId, string? fieldName, string? title)
    {
        var module = await _db.Modules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == moduleId);
        if (module is null || _tenant.UserId is not int userId)
            return RedirectToAction(nameof(Index));

        var maxOrder = await _db.DashboardWidgets
            .Where(w => w.UserId == userId)
            .MaxAsync(w => (int?)w.SortOrder) ?? 0;

        _db.DashboardWidgets.Add(new DashboardWidget
        {
            UserId = userId,
            Type = type is "pie" or "monthly" or "bar" or "funnel" ? type : "counter",
            Title = string.IsNullOrWhiteSpace(title) ? module.PluralLabel : title.Trim(),
            ModuleId = moduleId,
            FieldName = fieldName,
            SortOrder = maxOrder + 1
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "ویجت اضافه شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/dashboard/widgets/{id:int}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveWidget(int id)
    {
        var widget = await _db.DashboardWidgets
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == _tenant.UserId);
        if (widget is not null)
        {
            widget.IsDeleted = true;
            widget.DeletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/dashboard/widgets/{id:int}/type")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetWidgetType(int id, string type)
    {
        if (type is not ("pie" or "bar" or "funnel"))
            return BadRequest();

        var widget = await _db.DashboardWidgets
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == _tenant.UserId);
        if (widget is null || string.IsNullOrWhiteSpace(widget.FieldName))
            return NotFound();

        widget.Type = type;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("/App/dashboard/widgets/reorder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReorderWidgets(string group, string? ids)
    {
        if (_tenant.UserId is not int userId || string.IsNullOrWhiteSpace(ids))
            return BadRequest();

        var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .ToList();
        if (idList.Count == 0)
            return BadRequest();

        var rows = await _db.DashboardWidgets
            .Where(w => w.UserId == userId && idList.Contains(w.Id))
            .ToListAsync();
        var allowed = rows.Where(w => group == "charts"
            ? w.Type is "pie" or "bar" or "funnel" or "monthly"
            : w.Type == "counter").ToDictionary(w => w.Id);

        var order = 1;
        foreach (var id in idList)
        {
            if (allowed.TryGetValue(id, out var row))
                row.SortOrder = order++;
        }

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("/App/dashboard/month-order")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMonthOrder(string? names)
    {
        if (_tenant.UserId is not int userId)
            return BadRequest();

        var allowed = await _db.Modules.AsNoTracking()
            .Where(m => m.ShowInMenu && !m.IsChildModule)
            .Select(m => m.Name)
            .ToListAsync();
        var known = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        var list = (names ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(known.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var board = await BoardForAsync(userId);
        var (_, layout) = SplitBoard(board.FieldName);
        board.FieldName = JoinBoard(string.Join(',', list), layout);
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("/App/dashboard/chart-layout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveChartLayout(string? layout)
    {
        if (_tenant.UserId is not int userId)
            return BadRequest();

        var charts = await _db.DashboardWidgets
            .Where(w => w.UserId == userId && (w.Type == "pie" || w.Type == "bar" || w.Type == "funnel" || w.Type == "monthly"))
            .ToListAsync();
        var known = charts.ToDictionary(w => w.Id);
        var boxes = new List<(int Id, int X, int Y, int W, int H)>();
        var placed = new List<(int X, int Y, int W, int H)>();

        foreach (var part in (layout ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split(':');
            if (bits.Length != 2 || !int.TryParse(bits[0], out var id) || !known.ContainsKey(id))
                continue;
            if (!TryBox(bits[1], out var box) || Hits(box, placed))
                continue;
            boxes.Add((id, box.X, box.Y, box.W, box.H));
            placed.Add(box);
        }

        var board = await BoardForAsync(userId);
        var (months, _) = SplitBoard(board.FieldName);
        board.FieldName = JoinBoard(months, string.Join(';', boxes.Select(b => $"{b.Id}:{b.X},{b.Y},{b.W},{b.H}")));

        var order = 1;
        foreach (var box in boxes.OrderBy(b => b.Y).ThenBy(b => b.X))
            known[box.Id].SortOrder = order++;

        await _db.SaveChangesAsync();
        return Ok();
    }

    private async Task<List<DashEventRow>> LoadRecentEventsAsync(IReadOnlyList<ModuleDef> modules, TimeZoneInfo zone)
    {
        var latest = await _db.Records.AsNoTracking()
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(6)
            .Select(r => new { r.Id, r.ModuleId, r.Title, r.CreatedAtUtc, r.CreatedByUserId })
            .ToListAsync();
        if (latest.Count == 0)
            return [];

        var userIds = latest.Where(r => r.CreatedByUserId != null).Select(r => r.CreatedByUserId!.Value).Distinct().ToList();
        var users = new Dictionary<int, (string Name, string Email)>();
        if (userIds.Count > 0)
        {
            var userRows = await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.Email })
                .ToListAsync();
            users = userRows.ToDictionary(
                u => u.Id,
                u => (
                    Name: string.IsNullOrWhiteSpace(u.FullName) ? "کاربر" : u.FullName,
                    Email: u.Email ?? ""));
        }

        var moduleById = modules.ToDictionary(m => m.Id);
        var fa = CultureInfo.GetCultureInfo("fa-IR");
        var pc = new PersianCalendar();
        var rows = new List<DashEventRow>();
        var tone = 0;
        foreach (var row in latest)
        {
            if (!moduleById.TryGetValue(row.ModuleId, out var module))
                continue;
            var who = "سیستم";
            var email = "";
            if (row.CreatedByUserId is int userId && users.TryGetValue(userId, out var user))
            {
                who = user.Name;
                email = user.Email;
            }

            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc), zone);
            var title = string.IsNullOrWhiteSpace(row.Title) ? "بدون عنوان" : row.Title.Trim();
            rows.Add(new DashEventRow
            {
                Who = who,
                Initial = who.Trim()[0].ToString(),
                Sub = string.IsNullOrWhiteSpace(email) ? module.PluralLabel : email,
                Text = $"{title} در {module.PluralLabel} ثبت شد",
                When = $"{pc.GetYear(local).ToString(fa)}/{pc.GetMonth(local).ToString("00", fa)}/{pc.GetDayOfMonth(local).ToString("00", fa)} · {local.Hour.ToString("00", fa)}:{local.Minute.ToString("00", fa)}",
                Href = $"/App/m/{module.Name}/{row.Id}",
                Tone = tone++ % 5
            });
        }

        return rows;
    }

    [HttpPost("/App/dashboard/board")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBoard(string? layout)
    {
        if (_tenant.UserId is not int userId || string.IsNullOrWhiteSpace(layout) || layout.Length > 20000)
            return BadRequest();

        BoardFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<BoardFile>(layout, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return BadRequest();
        }

        if (parsed is null || parsed.Groups.Count is 0 or > 12)
            return BadRequest();

        var widgets = await _db.DashboardWidgets
            .Where(w => w.UserId == userId && w.Type != "board")
            .Select(w => new { w.Id, w.Type })
            .ToListAsync();
        var widgetIds = widgets.Select(w => w.Id).ToHashSet();
        var widgetKind = widgets.ToDictionary(
            w => w.Id,
            w => w.Type is "pie" or "bar" or "funnel" or "monthly" ? "chart" : "counter");
        var moduleNames = await _db.Modules.AsNoTracking()
            .Where(m => m.ShowInMenu && !m.IsChildModule)
            .Select(m => m.Name)
            .ToListAsync();
        var modules = new HashSet<string>(moduleNames, StringComparer.OrdinalIgnoreCase);
        var file = new BoardFile { V = 2 };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in parsed.Groups)
        {
            var title = (group.T ?? "").Trim().Replace('\n', ' ');
            if (title.Length > 40)
                title = title[..40];
            if (title.Length == 0)
                title = "گروه";

            var bucket = new BoardGroupFile { T = title };
            var placed = new List<(int X, int Y, int W, int H)>();
            foreach (var item in group.I ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.K) || !seen.Add(item.K) || !KnownBoardKey(item.K, widgetIds, modules))
                    continue;
                string? kind = null;
                if (item.K.StartsWith("w:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(item.K[2..], out var widgetId)
                    && widgetKind.TryGetValue(widgetId, out var foundKind))
                    kind = foundKind;
                var cell = CellFromKey(item.K, null, null, kind);
                cell.X = item.X;
                cell.Y = item.Y;
                cell.W = item.W;
                cell.H = item.H;
                ClampCell(cell);
                var box = (cell.X, cell.Y, cell.W, cell.H);
                if (Hits(box, placed))
                    continue;
                placed.Add(box);
                bucket.I.Add(new BoardItemFile { K = cell.Key, X = cell.X, Y = cell.Y, W = cell.W, H = cell.H });
            }

            file.Groups.Add(bucket);
        }

        if (file.Groups.Count == 0)
            return BadRequest();

        var board = await BoardForAsync(userId);
        board.FieldName = JsonSerializer.Serialize(file, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await _db.SaveChangesAsync();
        return Ok();
    }

    private async Task<DashboardWidget> BoardForAsync(int userId)
    {
        var board = await _db.DashboardWidgets.FirstOrDefaultAsync(w => w.UserId == userId && w.Type == "board");
        if (board is not null)
            return board;

        board = new DashboardWidget
        {
            UserId = userId,
            Type = "board",
            Title = "چیدمان",
            ModuleId = 0,
            FieldName = "",
            SortOrder = 0
        };
        _db.DashboardWidgets.Add(board);
        return board;
    }

    private static (string Months, string Layout) SplitBoard(string? raw)
    {
        raw ??= "";
        var pipe = raw.IndexOf('|');
        if (pipe < 0)
            return (raw, "");
        return (raw[..pipe], raw[(pipe + 1)..]);
    }

    private static string JoinBoard(string months, string layout)
        => string.IsNullOrEmpty(layout) ? months : months + "|" + layout;

    private static bool TryBox(string raw, out (int X, int Y, int W, int H) box)
    {
        box = default;
        var n = raw.Split(',');
        if (n.Length != 4
            || !int.TryParse(n[0], out var x)
            || !int.TryParse(n[1], out var y)
            || !int.TryParse(n[2], out var w)
            || !int.TryParse(n[3], out var h))
            return false;

        x = Math.Clamp(x, 1, 4);
        y = Math.Clamp(y, 1, 8);
        w = Math.Clamp(w, 1, 4);
        h = Math.Clamp(h, 1, 3);
        if (x + w - 1 > 4)
            w = 5 - x;
        box = (x, y, w, h);
        return true;
    }

    private static bool Hits((int X, int Y, int W, int H) a, List<(int X, int Y, int W, int H)> placed)
    {
        foreach (var b in placed)
        {
            if (a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H)
                return true;
        }
        return false;
    }

    private static List<DashGroupVm> BuildBoard(DashboardViewModel model, string? json, string? legacyLayout)
    {
        var catalog = Catalog(model);
        var byKey = catalog.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
        List<DashGroupVm> groups;

        if (!string.IsNullOrWhiteSpace(json) && TryReadBoard(json, out var saved))
        {
            groups = [];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in saved)
            {
                var bucket = new DashGroupVm { Title = group.T };
                var placed = new List<(int X, int Y, int W, int H)>();
                foreach (var item in group.I ?? [])
                {
                    if (!byKey.TryGetValue(item.K, out var source) || !seen.Add(source.Key))
                        continue;
                    var cell = CloneCell(source);
                    cell.X = item.X;
                    cell.Y = item.Y;
                    cell.W = item.W;
                    cell.H = item.H;
                    ClampCell(cell);
                    var box = (cell.X, cell.Y, cell.W, cell.H);
                    if (Hits(box, placed))
                        continue;
                    cell.Placed = true;
                    placed.Add(box);
                    bucket.Cells.Add(cell);
                }
                groups.Add(bucket);
            }
        }
        else
        {
            groups =
            [
                new DashGroupVm { Title = "نمودارها" },
                new DashGroupVm { Title = "ویجت‌های شما" },
                new DashGroupVm { Title = "ثبت‌شده‌های " + model.MonthLabel },
                new DashGroupVm { Title = "میانبر و رویداد" }
            ];
            var legacy = ParseLegacyCharts(legacyLayout);
            foreach (var source in catalog)
            {
                var cell = CloneCell(source);
                if (cell.Kind == "chart" && legacy.TryGetValue(cell.Key, out var old))
                {
                    cell.X = (old.X - 1) * 3 + 1;
                    cell.Y = (old.Y - 1) * 3 + 1;
                    cell.W = old.W * 3;
                    cell.H = old.H * 3;
                    ClampCell(cell);
                    cell.Placed = true;
                }
                HomeFor(groups, cell.Kind, model.MonthLabel).Cells.Add(cell);
            }
            foreach (var group in groups)
                DropOverlaps(group);
        }

        if (groups.Count == 0)
            groups.Add(new DashGroupVm { Title = "داشبورد" });

        var used = new HashSet<string>(groups.SelectMany(g => g.Cells.Select(c => c.Key)), StringComparer.OrdinalIgnoreCase);
        foreach (var source in catalog)
        {
            if (used.Contains(source.Key))
                continue;
            HomeFor(groups, source.Kind, model.MonthLabel).Cells.Add(CloneCell(source));
        }

        foreach (var group in groups)
            PackUnset(group.Cells);

        return groups;
    }

    private static List<DashCellVm> Catalog(DashboardViewModel model)
    {
        var cells = new List<DashCellVm>();
        foreach (var widget in model.Widgets)
        {
            var chart = widget.Widget.Type is "pie" or "bar" or "funnel" or "monthly";
            cells.Add(CellFromKey("w:" + widget.Widget.Id, widget, null, chart ? "chart" : "counter"));
        }

        cells.Add(CellFromKey("u", null, null));
        foreach (var month in model.MonthModules)
            cells.Add(CellFromKey("m:" + month.Name, null, month));
        cells.Add(CellFromKey("q", null, null));
        cells.Add(CellFromKey("e", null, null));
        return cells;
    }

    private static DashCellVm CellFromKey(string key, WidgetView? widget, MonthModuleStat? month, string? kind = null)
    {
        kind ??= key switch
        {
            "u" => "users",
            "q" => "quick",
            "e" => "events",
            _ when key.StartsWith("m:", StringComparison.OrdinalIgnoreCase) => "month",
            _ => "counter"
        };

        var cell = kind switch
        {
            "chart" => new DashCellVm { W = 6, H = 3, MinW = 4, MinH = 3, MaxH = 9 },
            "counter" => new DashCellVm { W = 6, H = 1, MinW = 3, MinH = 1, MaxH = 3 },
            "quick" => new DashCellVm { W = 4, H = 3, MinW = 3, MinH = 2, MaxH = 9 },
            "events" => new DashCellVm { W = 8, H = 3, MinW = 4, MinH = 3, MaxH = 9 },
            _ => new DashCellVm { W = 3, H = 1, MinW = 3, MinH = 1, MaxH = 3 }
        };
        cell.Key = key;
        cell.Kind = kind;
        cell.Widget = widget;
        cell.Month = month;
        return cell;
    }

    private static DashCellVm CloneCell(DashCellVm source)
        => new()
        {
            Key = source.Key,
            Kind = source.Kind,
            X = source.X,
            Y = source.Y,
            W = source.W,
            H = source.H,
            MinW = source.MinW,
            MinH = source.MinH,
            MaxH = source.MaxH,
            Widget = source.Widget,
            Month = source.Month
        };

    private static bool KnownBoardKey(string key, HashSet<int> widgetIds, HashSet<string> modules)
    {
        if (key is "u" or "q" or "e")
            return true;
        if (key.StartsWith("m:", StringComparison.OrdinalIgnoreCase))
            return modules.Contains(key[2..]);
        if (key.StartsWith("w:", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[2..], out var id))
            return widgetIds.Contains(id);
        return false;
    }

    private static void ClampCell(DashCellVm cell)
    {
        cell.W = Math.Clamp(cell.W, cell.MinW, 12);
        cell.H = Math.Clamp(cell.H, cell.MinH, cell.MaxH);
        cell.X = Math.Clamp(cell.X, 1, 12);
        cell.Y = Math.Clamp(cell.Y, 1, 40);
        if (cell.X + cell.W - 1 > 12)
            cell.X = 13 - cell.W;
    }

    private static string HomeTitle(string kind, string monthLabel) => kind switch
    {
        "chart" => "نمودارها",
        "counter" => "ویجت‌های شما",
        "month" or "users" => "ثبت‌شده‌های " + monthLabel,
        _ => "میانبر و رویداد"
    };

    private static DashGroupVm HomeFor(List<DashGroupVm> groups, string kind, string monthLabel)
    {
        var same = groups.FirstOrDefault(g => g.Cells.Any(c => c.Kind == kind));
        if (same is not null)
            return same;
        var titled = groups.FirstOrDefault(g => g.Title == HomeTitle(kind, monthLabel));
        if (titled is not null)
            return titled;
        if (groups.Count == 0)
        {
            var created = new DashGroupVm { Title = HomeTitle(kind, monthLabel) };
            groups.Add(created);
            return created;
        }
        return groups[0];
    }

    private static bool TryReadBoard(string json, out List<BoardGroupFile> groups)
    {
        groups = [];
        try
        {
            var parsed = JsonSerializer.Deserialize<BoardFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed is null || parsed.V != 2 || parsed.Groups.Count == 0)
                return false;
            groups = parsed.Groups;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, (int X, int Y, int W, int H)> ParseLegacyCharts(string? layout)
    {
        var map = new Dictionary<string, (int X, int Y, int W, int H)>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(layout))
            return map;
        foreach (var part in layout.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split(':');
            if (bits.Length == 2 && int.TryParse(bits[0], out _) && TryBox(bits[1], out var box))
                map["w:" + bits[0]] = box;
        }
        return map;
    }

    private static void DropOverlaps(DashGroupVm group)
    {
        var placed = new List<(int X, int Y, int W, int H)>();
        foreach (var cell in group.Cells)
        {
            if (!cell.Placed)
                continue;
            var box = (cell.X, cell.Y, cell.W, cell.H);
            if (Hits(box, placed))
            {
                cell.Placed = false;
                continue;
            }
            placed.Add(box);
        }
    }

    private static void PackUnset(List<DashCellVm> cells)
    {
        var placed = cells.Where(c => c.Placed).Select(c => (c.X, c.Y, c.W, c.H)).ToList();
        foreach (var cell in cells)
        {
            if (cell.Placed)
                continue;
            var spot = FindSpotWide(placed, cell.W, cell.H) ?? (1, 1, cell.W, cell.H);
            cell.X = spot.Item1;
            cell.Y = spot.Item2;
            cell.W = spot.Item3;
            cell.H = spot.Item4;
            cell.Placed = true;
            placed.Add(spot);
        }
    }

    private static (int, int, int, int)? FindSpotWide(List<(int X, int Y, int W, int H)> placed, int w, int h)
    {
        for (var y = 1; y <= 40; y++)
        {
            for (var x = 1; x <= 12; x++)
            {
                if (x + w - 1 > 12)
                    continue;
                var box = (X: x, Y: y, W: w, H: h);
                if (!Hits(box, placed))
                    return (x, y, w, h);
            }
        }
        return null;
    }

    private static void PlaceChartBoxes(List<WidgetView> widgets, string layout)
    {
        var charts = widgets.Where(w => w.Widget.Type is "pie" or "bar" or "funnel" or "monthly").ToList();
        var saved = new Dictionary<int, (int X, int Y, int W, int H)>();
        foreach (var part in layout.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split(':');
            if (bits.Length == 2 && int.TryParse(bits[0], out var id) && TryBox(bits[1], out var box))
                saved[id] = box;
        }

        var placed = new List<(int X, int Y, int W, int H)>();
        var used = new HashSet<int>();
        foreach (var chart in charts)
        {
            if (!saved.TryGetValue(chart.Widget.Id, out var box) || Hits(box, placed))
                continue;
            chart.GridX = box.X;
            chart.GridY = box.Y;
            chart.GridW = box.W;
            chart.GridH = box.H;
            placed.Add(box);
            used.Add(chart.Widget.Id);
        }

        foreach (var chart in charts)
        {
            if (used.Contains(chart.Widget.Id))
                continue;
            var spot = FindSpot(placed, 2, 1) ?? FindSpot(placed, 1, 1) ?? (1, 1, 2, 1);
            chart.GridX = spot.X;
            chart.GridY = spot.Y;
            chart.GridW = spot.W;
            chart.GridH = spot.H;
            placed.Add(spot);
        }
    }

    private static (int X, int Y, int W, int H)? FindSpot(List<(int X, int Y, int W, int H)> placed, int w, int h)
    {
        for (var y = 1; y <= 8; y++)
        {
            for (var x = 1; x <= 4; x++)
            {
                if (x + w - 1 > 4)
                    continue;
                var box = (x, y, w, h);
                if (!Hits(box, placed))
                    return box;
            }
        }
        return null;
    }

    private static List<MonthModuleStat> OrderMonthModules(List<MonthModuleStat> modules, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return modules;

        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var name in stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rank.ContainsKey(name)) continue;
            rank[name] = index++;
        }

        return modules
            .OrderBy(m => rank.TryGetValue(m.Name, out var place) ? place : int.MaxValue)
            .ThenBy(m => m.Label)
            .ToList();
    }

    private void ScheduleSeed(int tenantId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sales = scope.ServiceProvider.GetRequiredService<SalesModuleSeeder>();
                var business = scope.ServiceProvider.GetRequiredService<BusinessModuleSeeder>();
                await sales.EnsureSeededAsync(tenantId);
                await business.EnsureSeededAsync(tenantId);
            }
            catch
            {
                // seed پس‌زمینه نباید داشبورد را بشکند
            }
        });
    }

    private static string ResolvePicklistLabel(FieldDef? field, string value) =>
        field?.PicklistValues.FirstOrDefault(p => p.Value == value)?.Label ?? value;

    private async Task<List<(string Label, int Value)>> LoadPersianMonthsAsync(int moduleId)
    {
        var pc = new PersianCalendar();
        var zone = IranZone();
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var year = pc.GetYear(nowLocal);
        var month = pc.GetMonth(nowLocal);
        var startYear = year;
        var startMonth = month - 11;
        while (startMonth <= 0)
        {
            startMonth += 12;
            startYear--;
        }

        var startLocal = pc.ToDateTime(startYear, startMonth, 1, 0, 0, 0, 0);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), zone);

        var stamps = await _db.Records.AsNoTracking()
            .Where(r => r.ModuleId == moduleId && r.CreatedAtUtc >= startUtc)
            .Select(r => r.CreatedAtUtc)
            .ToListAsync();

        var buckets = new Dictionary<(int Year, int Month), int>();
        foreach (var stamp in stamps)
        {
            var utc = stamp.Kind == DateTimeKind.Utc ? stamp : DateTime.SpecifyKind(stamp, DateTimeKind.Utc);
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
            var key = (pc.GetYear(local), pc.GetMonth(local));
            buckets[key] = buckets.GetValueOrDefault(key) + 1;
        }

        var points = new List<(string Label, int Value)>(12);
        for (var i = 11; i >= 0; i--)
        {
            var cursor = month - i;
            var cursorYear = year;
            while (cursor <= 0)
            {
                cursor += 12;
                cursorYear--;
            }

            points.Add((PersianMonths[cursor], buckets.GetValueOrDefault((cursorYear, cursor))));
        }

        return points;
    }

    private static TimeZoneInfo IranZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
        }
    }

    private static readonly string[] PersianMonths =
    [
        "", "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد",
        "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    ];
}
