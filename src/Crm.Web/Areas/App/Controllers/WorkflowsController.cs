using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

public class WorkflowActionInput
{
    public WorkflowActionType Type { get; set; }
    public string ConfigJson { get; set; } = "{}";
}

public class WorkflowFormModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ModuleId { get; set; }
    public WorkflowTrigger Trigger { get; set; }
    public WorkflowSchedule? Schedule { get; set; }
    public string ConditionsJson { get; set; } = """{"logic":"and","items":[]}""";
    public List<WorkflowActionInput> Actions { get; set; } = [];

    public List<ModuleDef> Modules { get; set; } = [];
    public Dictionary<int, List<object>> ModuleFields { get; set; } = [];
}

/// <summary>موتور گردش‌کار: مدیریت قوانین، اکشن‌ها و لاگ اجرا.</summary>
public class WorkflowsController : AppControllerBase
{
    public static string ActionLabel(WorkflowActionType type) => type switch
    {
        WorkflowActionType.SendEmail => "ارسال ایمیل",
        WorkflowActionType.SendSms => "ارسال پیامک/پیام‌رسان",
        WorkflowActionType.CreateTask => "ایجاد وظیفه",
        WorkflowActionType.CreateEvent => "ایجاد رویداد",
        WorkflowActionType.UpdateField => "بروزرسانی فیلد",
        WorkflowActionType.CreateRecord => "ایجاد رکورد",
        WorkflowActionType.Notify => "اعلان درون‌سیستمی",
        WorkflowActionType.ToggleTag => "افزودن/حذف برچسب",
        WorkflowActionType.SendToAccounting => "ارسال به حسابداری",
        WorkflowActionType.CallWebhook => "فراخوانی وب‌هوک",
        _ => "اجرای تابع سفارشی"
    };

    private readonly CrmDbContext _db;
    private readonly MetadataService _metadata;

    public WorkflowsController(CrmDbContext db, MetadataService metadata)
    {
        _db = db;
        _metadata = metadata;
    }

    [HttpGet("/App/workflows")]
    public async Task<IActionResult> Index()
    {
        var rules = await _db.WorkflowRules.AsNoTracking()
            .Include(r => r.Module)
            .Include(r => r.Actions)
            .OrderByDescending(r => r.Id)
            .ToListAsync();

        var logCounts = await _db.WorkflowLogs.AsNoTracking()
            .GroupBy(l => l.RuleId)
            .Select(g => new { RuleId = g.Key, Count = g.Count(), Failed = g.Count(l => !l.Success) })
            .ToDictionaryAsync(x => x.RuleId, x => (x.Count, x.Failed));

        ViewData["Title"] = "گردش‌کار";
        ViewBag.LogCounts = logCounts;
        return View(rules);
    }

    [HttpGet("/App/workflows/create")]
    public async Task<IActionResult> Create()
    {
        var model = new WorkflowFormModel();
        await FillListsAsync(model);
        ViewData["Title"] = "قانون گردش‌کار جدید";
        return View("Form", model);
    }

    [HttpGet("/App/workflows/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var rule = await _db.WorkflowRules.AsNoTracking()
            .Include(r => r.Actions)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (rule is null)
            return NotFound();

        var model = new WorkflowFormModel
        {
            Id = rule.Id,
            Name = rule.Name,
            ModuleId = rule.ModuleId,
            Trigger = rule.Trigger,
            Schedule = rule.Schedule,
            ConditionsJson = rule.ConditionsJson,
            Actions = rule.Actions.OrderBy(a => a.SortOrder)
                .Select(a => new WorkflowActionInput { Type = a.Type, ConfigJson = a.ConfigJson })
                .ToList()
        };
        await FillListsAsync(model);
        ViewData["Title"] = $"ویرایش {rule.Name}";
        return View("Form", model);
    }

    [HttpPost("/App/workflows/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(WorkflowFormModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name) || model.ModuleId == 0 || model.Actions.Count == 0)
        {
            TempData["Error"] = "نام، ماژول و حداقل یک اکشن الزامی است.";
            await FillListsAsync(model);
            return View("Form", model);
        }

        // اعتبارسنجی JSON ها قبل از ذخیره
        try
        {
            WorkflowEngine.ParseConditions(model.ConditionsJson);
            foreach (var action in model.Actions)
                JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ConfigJson) ? "{}" : action.ConfigJson);
        }
        catch (JsonException)
        {
            TempData["Error"] = "قالب شرط‌ها یا پیکربندی اکشن معتبر نیست.";
            await FillListsAsync(model);
            return View("Form", model);
        }

        WorkflowRule rule;
        if (model.Id == 0)
        {
            rule = new WorkflowRule();
            _db.WorkflowRules.Add(rule);
        }
        else
        {
            rule = await _db.WorkflowRules.Include(r => r.Actions).FirstAsync(r => r.Id == model.Id);
            _db.WorkflowActions.RemoveRange(rule.Actions);
            rule.Actions.Clear();
        }

        rule.Name = model.Name.Trim();
        rule.ModuleId = model.ModuleId;
        rule.Trigger = model.Trigger;
        rule.Schedule = model.Trigger == WorkflowTrigger.Scheduled ? model.Schedule ?? WorkflowSchedule.Daily : null;
        rule.ConditionsJson = model.ConditionsJson;

        var order = 0;
        foreach (var action in model.Actions)
        {
            rule.Actions.Add(new WorkflowAction
            {
                Type = action.Type,
                ConfigJson = string.IsNullOrWhiteSpace(action.ConfigJson) ? "{}" : action.ConfigJson,
                SortOrder = ++order
            });
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "قانون گردش‌کار ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/workflows/{id:int}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var rule = await _db.WorkflowRules.FindAsync(id);
        if (rule is not null)
        {
            rule.IsActive = !rule.IsActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = rule.IsActive ? "قانون فعال شد." : "قانون غیرفعال شد.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/workflows/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var rule = await _db.WorkflowRules.FindAsync(id);
        if (rule is not null)
        {
            rule.IsDeleted = true;
            rule.DeletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "قانون حذف شد.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/App/workflows/{id:int}/logs")]
    public async Task<IActionResult> Logs(int id)
    {
        var rule = await _db.WorkflowRules.AsNoTracking()
            .Include(r => r.Module)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (rule is null)
            return NotFound();

        var logs = await _db.WorkflowLogs.AsNoTracking()
            .Where(l => l.RuleId == id)
            .OrderByDescending(l => l.Id)
            .Take(200)
            .ToListAsync();

        ViewData["Title"] = $"لاگ اجرای {rule.Name}";
        ViewBag.Rule = rule;
        return View(logs);
    }

    private async Task FillListsAsync(WorkflowFormModel model)
    {
        model.Modules = (await _metadata.GetActiveModulesAsync()).ToList();
        foreach (var module in model.Modules)
        {
            var fields = await _metadata.GetFieldsAsync(module.Id);
            model.ModuleFields[module.Id] = fields
                .Select(f => (object)new { name = f.Name, label = f.Label })
                .ToList();
        }
    }
}
