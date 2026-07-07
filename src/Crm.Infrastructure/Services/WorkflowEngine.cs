using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

public class WorkflowCondition
{
    public string Field { get; set; } = string.Empty;
    public string Op { get; set; } = "equals";
    public string? Value { get; set; }
}

public class WorkflowConditionGroup
{
    public string Logic { get; set; } = "and";
    public List<WorkflowCondition> Items { get; set; } = [];
}

/// <summary>
/// موتور گردش‌کار: ارزیابی شرط‌های و/یا روی فیلدهای رکورد (شامل سفارشی)
/// و اجرای زنجیره ۱۱ اکشن. اجرای async از طریق Hangfire.
/// </summary>
public class WorkflowEngine
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly CrmDbContext _db;
    private readonly TenantContext _tenantContext;
    private readonly IEmailSender _email;
    private readonly ISmsSender _sms;
    private readonly IAccountingGateway _accounting;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WorkflowEngine> _logger;

    public WorkflowEngine(
        CrmDbContext db, TenantContext tenantContext,
        IEmailSender email, ISmsSender sms, IAccountingGateway accounting,
        IHttpClientFactory httpFactory, ILogger<WorkflowEngine> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _email = email;
        _sms = sms;
        _accounting = accounting;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>نقطه ورود جاب Hangfire برای رویدادهای رکورد (ایجاد/ویرایش).</summary>
    public async Task ExecuteForRecordAsync(int tenantId, int moduleId, int recordId, WorkflowTrigger trigger)
    {
        _tenantContext.TenantId = tenantId;

        var rules = await _db.WorkflowRules
            .Include(r => r.Actions)
            .Include(r => r.Module)
            .Where(r => r.IsActive && r.ModuleId == moduleId && r.Trigger == trigger)
            .ToListAsync();
        if (rules.Count == 0)
            return;

        var record = await _db.Records.FirstOrDefaultAsync(r => r.Id == recordId && r.ModuleId == moduleId);
        if (record is null)
            return;

        foreach (var rule in rules)
            await RunRuleOnRecordAsync(rule, record);

        await _db.SaveChangesAsync();
    }

    /// <summary>جاب ساعتی: قوانین زمان‌بندی‌شده روی همه رکوردهای ماژول اجرا می‌شوند.</summary>
    public async Task RunScheduledRulesAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var rules = await _db.WorkflowRules
            .IgnoreQueryFilters()
            .Include(r => r.Actions)
            .Include(r => r.Module)
            .Where(r => r.IsActive && !r.IsDeleted && r.Trigger == WorkflowTrigger.Scheduled)
            .ToListAsync();

        foreach (var rule in rules)
        {
            var shouldRun = rule.Schedule switch
            {
                WorkflowSchedule.Hourly => true,
                WorkflowSchedule.Daily => nowUtc.Hour == 5,
                WorkflowSchedule.Monthly => nowUtc.Day == 1 && nowUtc.Hour == 5,
                _ => false
            };
            if (!shouldRun)
                continue;

            _tenantContext.TenantId = rule.TenantId;
            var records = await _db.Records
                .IgnoreQueryFilters()
                .Where(r => r.TenantId == rule.TenantId && r.ModuleId == rule.ModuleId && !r.IsDeleted)
                .OrderByDescending(r => r.Id)
                .Take(2000)
                .ToListAsync();

            foreach (var record in records)
                await RunRuleOnRecordAsync(rule, record);
        }

        await _db.SaveChangesAsync();
    }

    private async Task RunRuleOnRecordAsync(WorkflowRule rule, DynamicRecord record)
    {
        var data = DynamicRecordService.ParseData(record);
        data["__title"] = record.Title;

        if (!Evaluate(ParseConditions(rule.ConditionsJson), data))
            return;

        foreach (var action in rule.Actions.OrderBy(a => a.SortOrder))
        {
            try
            {
                var message = await ExecuteActionAsync(action, rule, record, data);
                _db.WorkflowLogs.Add(new WorkflowLog
                {
                    TenantId = rule.TenantId,
                    RuleId = rule.Id,
                    RecordId = record.Id,
                    Success = true,
                    Message = message
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workflow action {Type} failed for rule {Rule}", action.Type, rule.Id);
                _db.WorkflowLogs.Add(new WorkflowLog
                {
                    TenantId = rule.TenantId,
                    RuleId = rule.Id,
                    RecordId = record.Id,
                    Success = false,
                    Message = $"{action.Type}: {ex.Message}"
                });
            }
        }
    }

    public static WorkflowConditionGroup ParseConditions(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<WorkflowConditionGroup>(json, JsonOpts) ?? new();
        }
        catch
        {
            return new WorkflowConditionGroup();
        }
    }

    /// <summary>ارزیابی گروه شرط با منطق و/یا روی داده رکورد.</summary>
    public static bool Evaluate(WorkflowConditionGroup group, Dictionary<string, string?> data)
    {
        if (group.Items.Count == 0)
            return true;

        var results = group.Items.Select(condition =>
        {
            var actual = data.GetValueOrDefault(condition.Field);
            return condition.Op switch
            {
                "equals" => string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),
                "notEquals" => !string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),
                "contains" => actual is not null && condition.Value is not null &&
                              actual.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
                "greaterThan" => decimal.TryParse(actual, out var a) && decimal.TryParse(condition.Value, out var b) && a > b,
                "lessThan" => decimal.TryParse(actual, out var a2) && decimal.TryParse(condition.Value, out var b2) && a2 < b2,
                "isEmpty" => string.IsNullOrWhiteSpace(actual),
                "isNotEmpty" => !string.IsNullOrWhiteSpace(actual),
                _ => false
            };
        });

        return group.Logic.Equals("or", StringComparison.OrdinalIgnoreCase)
            ? results.Any(r => r)
            : results.All(r => r);
    }

    /// <summary>جایگذاری {field} در قالب‌های متنی اکشن.</summary>
    public static string Interpolate(string? template, Dictionary<string, string?> data)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        var result = new StringBuilder(template);
        foreach (var (key, value) in data)
            result.Replace("{" + key + "}", value ?? "");
        return result.ToString();
    }

    private async Task<string> ExecuteActionAsync(
        WorkflowAction action, WorkflowRule rule, DynamicRecord record, Dictionary<string, string?> data)
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, string?>>(action.ConfigJson, JsonOpts) ?? new();
        string Cfg(string key) => Interpolate(config.GetValueOrDefault(key), data);

        switch (action.Type)
        {
            case WorkflowActionType.SendEmail:
            {
                var to = Cfg("to");
                await _email.SendAsync(to, Cfg("subject"), Cfg("body"));
                return $"ایمیل به {to} ارسال شد.";
            }

            case WorkflowActionType.SendSms:
            {
                var to = Cfg("to");
                await _sms.SendAsync(to, Cfg("text"));
                return $"پیامک به {to} ارسال شد.";
            }

            case WorkflowActionType.CreateTask:
                return await CreateChildRecordAsync("tasks", new Dictionary<string, string?>
                {
                    ["name"] = Cfg("name"),
                    ["dueDate"] = DateTime.UtcNow.AddDays(
                        int.TryParse(config.GetValueOrDefault("dueInDays"), out var days) ? days : 1).ToString("yyyy-MM-ddTHH:mm"),
                    ["priority"] = config.GetValueOrDefault("priority") ?? "normal",
                    ["status"] = "todo"
                }, record);

            case WorkflowActionType.CreateEvent:
                return await CreateChildRecordAsync("events", new Dictionary<string, string?>
                {
                    ["name"] = Cfg("name"),
                    ["startAt"] = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm"),
                    ["endAt"] = DateTime.UtcNow.AddDays(1).AddHours(1).ToString("yyyy-MM-ddTHH:mm")
                }, record);

            case WorkflowActionType.UpdateField:
            {
                var fieldName = config.GetValueOrDefault("field") ?? throw new InvalidOperationException("فیلد هدف تعیین نشده.");
                data[fieldName] = Cfg("value");
                data.Remove("__title");
                record.CustomData = JsonSerializer.Serialize(data);
                return $"فیلد {fieldName} بروزرسانی شد.";
            }

            case WorkflowActionType.CreateRecord:
            {
                var moduleName = config.GetValueOrDefault("module") ?? throw new InvalidOperationException("ماژول هدف تعیین نشده.");
                return await CreateChildRecordAsync(moduleName, new Dictionary<string, string?>
                {
                    ["name"] = Cfg("name")
                }, record);
            }

            case WorkflowActionType.Notify:
            {
                var userId = record.OwnerUserId ?? record.CreatedByUserId;
                if (userId is null)
                    return "کاربری برای اعلان یافت نشد.";
                _db.Notifications.Add(new Notification
                {
                    TenantId = rule.TenantId,
                    UserId = userId.Value,
                    Title = Cfg("title"),
                    Body = Cfg("body"),
                    LinkUrl = $"/App/m/{rule.Module?.Name}"
                });
                return "اعلان درون‌سیستمی ثبت شد.";
            }

            case WorkflowActionType.ToggleTag:
            {
                var tagName = Cfg("tag");
                var remove = config.GetValueOrDefault("mode") == "remove";
                return await ToggleTagAsync(rule.TenantId, record, tagName, remove);
            }

            case WorkflowActionType.SendToAccounting:
                await _accounting.PushAsync("record", record.Id, record.CustomData);
                return "به حسابداری ارسال شد.";

            case WorkflowActionType.CallWebhook:
            {
                var url = config.GetValueOrDefault("url") ?? throw new InvalidOperationException("آدرس وب‌هوک تعیین نشده.");
                var client = _httpFactory.CreateClient("workflow-webhook");
                client.Timeout = TimeSpan.FromSeconds(10);
                var payload = JsonSerializer.Serialize(new { recordId = record.Id, title = record.Title, data });
                var response = await client.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"));
                return $"وب‌هوک {url} — پاسخ {(int)response.StatusCode}";
            }

            case WorkflowActionType.RunCustomFunction:
                _logger.LogInformation("Custom function requested for record {Id}: {Config}", record.Id, action.ConfigJson);
                return "تابع سفارشی اجرا شد (stub).";

            default:
                throw new InvalidOperationException($"اکشن ناشناخته: {action.Type}");
        }
    }

    private async Task<string> CreateChildRecordAsync(
        string moduleName, Dictionary<string, string?> values, DynamicRecord source)
    {
        var module = await _db.Modules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == source.TenantId && m.Name == moduleName && !m.IsDeleted)
            ?? throw new InvalidOperationException($"ماژول {moduleName} یافت نشد.");

        var child = new DynamicRecord
        {
            TenantId = source.TenantId,
            ModuleId = module.Id,
            Title = values.GetValueOrDefault("name") ?? "(بدون عنوان)",
            OwnerUserId = source.OwnerUserId,
            CustomData = JsonSerializer.Serialize(values)
        };
        _db.Records.Add(child);
        return $"رکورد در {module.PluralLabel} ساخته شد: {child.Title}";
    }

    private async Task<string> ToggleTagAsync(int tenantId, DynamicRecord record, string tagName, bool remove)
    {
        var moduleName = await _db.Modules.IgnoreQueryFilters()
            .Where(m => m.Id == record.ModuleId).Select(m => m.Name).FirstAsync();

        if (string.IsNullOrWhiteSpace(tagName))
            throw new InvalidOperationException("نام برچسب تعیین نشده.");

        var tag = await _db.Tags.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Name == tagName && !t.IsDeleted);

        if (remove)
        {
            if (tag is null)
                return "برچسب وجود ندارد.";
            var link = await _db.TagLinks.IgnoreQueryFilters()
                .FirstOrDefaultAsync(l => l.TagId == tag.Id && l.RecordId == record.Id && !l.IsDeleted);
            if (link is not null)
                _db.TagLinks.Remove(link);
            return $"برچسب «{tagName}» حذف شد.";
        }

        tag ??= _db.Tags.Add(new Tag { TenantId = tenantId, Name = tagName }).Entity;
        var exists = tag.Id != 0 && await _db.TagLinks.IgnoreQueryFilters()
            .AnyAsync(l => l.TagId == tag.Id && l.RecordId == record.Id && !l.IsDeleted);
        if (!exists)
            _db.TagLinks.Add(new TagLink { TenantId = tenantId, Tag = tag, ModuleName = moduleName, RecordId = record.Id });
        return $"برچسب «{tagName}» افزوده شد.";
    }
}
