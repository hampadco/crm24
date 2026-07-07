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
}

public class DashboardController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly MetadataService _metadata;
    private readonly SalesModuleSeeder _salesSeeder;

    public DashboardController(CrmDbContext db, ITenantContext tenant, MetadataService metadata, SalesModuleSeeder salesSeeder)
    {
        _db = db;
        _tenant = tenant;
        _metadata = metadata;
        _salesSeeder = salesSeeder;
    }

    public async Task<IActionResult> Index()
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == _tenant.TenantId);
        if (tenant is null)
            return RedirectToAction("Login", "Account", new { area = "App" });

        // ارتقای Tenant های قدیمی: ماژول‌های فروش اگر نبودند ساخته می‌شوند
        await _salesSeeder.EnsureSeededAsync(tenant.Id);

        var modules = await _metadata.GetActiveModulesAsync();
        var moduleStats = new List<(ModuleDef, int)>();
        foreach (var module in modules)
        {
            var count = await _db.Records.CountAsync(r => r.ModuleId == module.Id);
            moduleStats.Add((module, count));
        }

        var model = new DashboardViewModel
        {
            Tenant = tenant,
            UserCount = await _db.Users.CountAsync(u => u.TenantId == tenant.Id),
            Modules = moduleStats,
            AllModules = modules.ToList(),
            TrialDaysLeft = tenant.Status == TenantStatus.Trial && tenant.TrialEndsAtUtc is DateTime end
                ? Math.Max(0, (int)Math.Ceiling((end - DateTime.UtcNow).TotalDays))
                : null
        };

        foreach (var module in modules)
        {
            var fields = await _metadata.GetFieldsAsync(module.Id);
            model.PicklistFields[module.Id] = fields.Where(f => f.Type == FieldType.Picklist).ToList();
        }

        // ویجت‌های شخصی کاربر
        var widgets = await _db.DashboardWidgets.AsNoTracking()
            .Where(w => w.UserId == _tenant.UserId)
            .OrderBy(w => w.SortOrder)
            .ToListAsync();

        foreach (var widget in widgets)
        {
            var module = modules.FirstOrDefault(m => m.Id == widget.ModuleId);
            if (module is null)
                continue;

            var view = new WidgetView { Widget = widget, Module = module };

            switch (widget.Type)
            {
                case "counter":
                    view.CounterValue = await _db.Records.CountAsync(r => r.ModuleId == module.Id);
                    break;

                case "pie" when widget.FieldName is not null:
                {
                    var fields = await _metadata.GetFieldsAsync(module.Id);
                    var field = fields.FirstOrDefault(f => f.Name == widget.FieldName);
                    var records = await _db.Records.AsNoTracking()
                        .Where(r => r.ModuleId == module.Id).Take(5000).ToListAsync();
                    var groups = records
                        .Select(r => DynamicRecordService.ParseData(r).GetValueOrDefault(widget.FieldName))
                        .GroupBy(v => v ?? "(خالی)")
                        .Select(g => (Label: ResolvePicklistLabel(field, g.Key), Value: g.Count()))
                        .OrderByDescending(g => g.Value)
                        .ToList();
                    view.Series = groups;
                    break;
                }

                case "monthly":
                {
                    var since = DateTime.UtcNow.AddMonths(-5);
                    var firstOfWindow = new DateTime(since.Year, since.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    var counts = await _db.Records.AsNoTracking()
                        .Where(r => r.ModuleId == module.Id && r.CreatedAtUtc >= firstOfWindow)
                        .GroupBy(r => new { r.CreatedAtUtc.Year, r.CreatedAtUtc.Month })
                        .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                        .ToListAsync();

                    var pc = new PersianCalendar();
                    for (var i = 5; i >= 0; i--)
                    {
                        var month = DateTime.UtcNow.AddMonths(-i);
                        var count = counts.FirstOrDefault(c => c.Year == month.Year && c.Month == month.Month)?.Count ?? 0;
                        view.Series.Add(($"{pc.GetYear(month)}/{pc.GetMonth(month):00}", count));
                    }
                    break;
                }
            }

            model.Widgets.Add(view);
        }

        return View(model);
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
            Type = type is "pie" or "monthly" ? type : "counter",
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

    private static string ResolvePicklistLabel(FieldDef? field, string value) =>
        field?.PicklistValues.FirstOrDefault(p => p.Value == value)?.Label ?? value;
}
