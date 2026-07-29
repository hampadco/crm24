using System.Globalization;
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
}

public class DashboardViewModel
{
    public Tenant Tenant { get; set; } = null!;
    public int UserCount { get; set; }
    public IReadOnlyList<(ModuleDef Module, int RecordCount)> Modules { get; set; } = [];
    public int? TrialDaysLeft { get; set; }
    public List<WidgetView> Widgets { get; set; } = [];
    public List<ModuleDef> AllModules { get; set; } = [];
    public Dictionary<int, List<FieldDef>> PicklistFields { get; set; } = [];
    /// <summary>ویجت‌ها به‌صورت AJAX بعد از رندر اولیه لود می‌شوند.</summary>
    public bool DeferWidgets { get; set; } = true;
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
            UserCount = await _db.Users.CountAsync(u => u.TenantId == tenant.Id),
            Modules = moduleStats,
            AllModules = modules.ToList(),
            TrialDaysLeft = tenant.Status == TenantStatus.Trial && tenant.TrialEndsAtUtc is DateTime end
                ? Math.Max(0, (int)Math.Ceiling((end - DateTime.UtcNow).TotalDays))
                : null,
            DeferWidgets = true
        };

        // اسکلت ویجت‌ها بدون تجمیع سنگین — داده از /widgets-data می‌آید
        var widgets = await _db.DashboardWidgets.AsNoTracking()
            .Where(w => w.UserId == _tenant.UserId)
            .OrderBy(w => w.SortOrder)
            .ToListAsync();

        foreach (var widget in widgets)
        {
            var module = modules.FirstOrDefault(m => m.Id == widget.ModuleId);
            if (module is null) continue;
            model.Widgets.Add(new WidgetView
            {
                Widget = widget,
                Module = module,
                CounterValue = widget.Type == "counter" ? countByModule.GetValueOrDefault(module.Id) : 0
            });
        }

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
            .Where(w => w.UserId == _tenant.UserId)
            .OrderBy(w => w.SortOrder)
            .ToListAsync();

        var fieldMap = await _metadata.GetFieldsForModulesAsync(
            widgets.Select(w => w.ModuleId).Distinct());

        var monthlyCache = new Dictionary<int, List<(int Year, int Month, int Count)>>();
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

                    if (widget.Type == "funnel" && field?.PicklistValues.Count > 0)
                    {
                        var orderMap = field.PicklistValues
                            .OrderBy(p => p.SortOrder)
                            .Select((p, i) => (p.Value, Index: i))
                            .ToDictionary(x => x.Value, x => x.Index, StringComparer.OrdinalIgnoreCase);

                        rows = rows
                            .OrderBy(s => orderMap.TryGetValue(s.Raw, out var idx) ? idx : int.MaxValue)
                            .ThenByDescending(s => s.Value)
                            .ToList();
                    }

                    series = rows.Select(s => new { label = s.Label, value = s.Value }).ToList();
                    break;
                }

                case "monthly":
                {
                    if (!monthlyCache.TryGetValue(module.Id, out var counts))
                    {
                        var since = DateTime.UtcNow.AddMonths(-5);
                        var firstOfWindow = new DateTime(since.Year, since.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                        var raw = await _db.Records.AsNoTracking()
                            .Where(r => r.ModuleId == module.Id && r.CreatedAtUtc >= firstOfWindow)
                            .GroupBy(r => new { r.CreatedAtUtc.Year, r.CreatedAtUtc.Month })
                            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                            .ToListAsync();
                        counts = raw.Select(c => (c.Year, c.Month, c.Count)).ToList();
                        monthlyCache[module.Id] = counts;
                    }

                    var pc = new PersianCalendar();
                    var monthSeries = new List<object>();
                    for (var i = 5; i >= 0; i--)
                    {
                        var month = DateTime.UtcNow.AddMonths(-i);
                        var count = counts.FirstOrDefault(c => c.Year == month.Year && c.Month == month.Month).Count;
                        monthSeries.Add(new { label = $"{pc.GetYear(month)}/{pc.GetMonth(month):00}", value = count });
                    }
                    series = monthSeries;
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
}
