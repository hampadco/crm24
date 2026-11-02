using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Services;
using Crm.Web.Services;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>تنظیمات یکپارچگی‌ها (پیامک/ایمیل/بله/حسابداری/VoIP) + مدیریت کلیدهای API.</summary>
public class IntegrationsController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TenantIntegrationService _integrations;
    private readonly TelefonchyClient _telefonchy;
    private readonly SmsService _sms;
    private readonly MetadataService _metadata;
    private readonly ApiWebhookJob _webhooks;
    private readonly TenantOpenApiBuilder _openApi;

    public IntegrationsController(
        CrmDbContext db, ITenantContext tenant, TenantIntegrationService integrations,
        TelefonchyClient telefonchy, SmsService sms, MetadataService metadata, ApiWebhookJob webhooks,
        TenantOpenApiBuilder openApi)
    {
        _db = db;
        _tenant = tenant;
        _integrations = integrations;
        _telefonchy = telefonchy;
        _sms = sms;
        _metadata = metadata;
        _webhooks = webhooks;
        _openApi = openApi;
    }

    [HttpGet("/App/integrations")]
    public async Task<IActionResult> Index()
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var config = await _integrations.GetAsync();
        if (string.IsNullOrWhiteSpace(config.TelefonchyHookSecret))
        {
            config.TelefonchyHookSecret = TenantIntegrationService.NewHookSecret();
            await _integrations.SaveAsync(config);
        }

        ViewBag.Config = config;
        ViewBag.ApiKeys = await _db.ApiKeys.AsNoTracking()
            .OrderByDescending(k => k.Id).ToListAsync();
        ViewBag.ApiWebhooks = await _db.ApiWebhooks.AsNoTracking()
            .OrderByDescending(h => h.Id).ToListAsync();
        ViewBag.ApiModules = (await _metadata.GetActiveModulesAsync())
            .Where(m => !m.IsChildModule)
            .OrderBy(m => m.PluralLabel)
            .Select(m => new { m.Name, m.PluralLabel })
            .ToList();
        ViewBag.ApiUsers = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == _tenant.TenantId && u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Email })
            .ToListAsync();
        ViewBag.TelefonchyOrigin = TelefonchyPublicOrigin();
        ViewData["Title"] = "یکپارچگی‌ها و API";
        return View();
    }

    [HttpPost("/App/integrations/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(IntegrationConfig config)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var existing = await _integrations.GetAsync();
        if (string.IsNullOrWhiteSpace(config.SmtpPassword))
            config.SmtpPassword = existing.SmtpPassword;
        if (string.IsNullOrWhiteSpace(config.SmsApiKey))
            config.SmsApiKey = existing.SmsApiKey;
        if (config.IsIpPanel && string.IsNullOrWhiteSpace(config.SmsApiUrl))
            config.SmsApiUrl = "https://edge.ippanel.com/v1";
        if (string.IsNullOrWhiteSpace(config.TelefonchyToken))
            config.TelefonchyToken = existing.TelefonchyToken;
        if (string.IsNullOrWhiteSpace(config.TelefonchyHookSecret))
            config.TelefonchyHookSecret = existing.TelefonchyHookSecret;
        if (string.IsNullOrWhiteSpace(config.TelefonchyHookSecret))
            config.TelefonchyHookSecret = TenantIntegrationService.NewHookSecret();
        config.TelefonchyLastSyncUtc = existing.TelefonchyLastSyncUtc;

        await _integrations.SaveAsync(config);
        TempData["Success"] = "تنظیمات یکپارچگی ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/integrations/sms/test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestSms()
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var credit = await _sms.GetCreditAsync();
        if (!credit.Ok)
            TempData["Error"] = credit.Error ?? "اتصال به آی‌پی‌پنل برقرار نشد.";
        else
            TempData["Success"] = credit.Credit is decimal amount
                ? $"اتصال آی‌پی‌پنل برقرار است. موجودی: {amount:N0} ریال"
                : "اتصال آی‌پی‌پنل برقرار است.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/integrations/telefonchy/test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestTelefonchy()
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var page = await _telefonchy.GetCallsAsync(page: 1);
        if (page is null)
        {
            TempData["Error"] = "اتصال به تلفنچی برقرار نشد. توکن و شناسه سرویس را بررسی کنید.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"اتصال موفق بود. {page.TotalItems} تماس در گزارش تلفنچی دیده شد.";
        TempData["TelefonchyExtens"] = await BuildExtensReportAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/integrations/telefonchy/extens")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TelefonchyExtens()
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var report = await BuildExtensReportAsync();
        if (report is null)
            TempData["Error"] = "لیست داخلی‌ها از تلفنچی دریافت نشد. توکن وب‌سرویس را بررسی کنید.";
        else
            TempData["TelefonchyExtens"] = report;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>داخلی‌های واقعی سرویس؛ مقدار «داخلی تلفنچی» کاربران باید از این لیست باشد.</summary>
    private async Task<string?> BuildExtensReportAsync()
    {
        var services = await _telefonchy.GetServicesAsync();
        if (services is null || services.Count == 0)
            return null;

        var lines = new List<string>();
        foreach (var service in services)
        {
            var trunks = service.Trunks.Count > 0 ? string.Join(", ", service.Trunks) : "—";
            lines.Add($"سرویس {service.ServiceId} | خطوط: {trunks}");
            if (service.Extens.Count == 0)
            {
                lines.Add("    داخلی‌ای ثبت نشده است.");
                continue;
            }
            foreach (var exten in service.Extens)
                lines.Add($"    داخلی {exten.Number} — {exten.Name ?? "بی‌نام"}");
        }
        return string.Join("\n", lines);
    }

    [HttpPost("/App/integrations/telefonchy/sync")]
    [ValidateAntiForgeryToken]
    public IActionResult SyncTelefonchy()
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");
        if (_tenant.TenantId is not int tenantId)
            return Forbid("Identity.Application");

        Hangfire.BackgroundJob.Enqueue<TelefonchyCallSyncJob>(j => j.SyncTenantAsync(tenantId, true));
        TempData["Success"] = "سینک تاریخچه تماس‌ها در صف قرار گرفت.";
        return RedirectToAction(nameof(Index));
    }

    private string TelefonchyPublicOrigin()
    {
        var host = Request.Host.Value ?? Request.Host.ToString();
        var https = Request.IsHttps || host.Contains("bamacrm.ir", StringComparison.OrdinalIgnoreCase);
        return $"{(https ? "https" : Request.Scheme)}://{host}";
    }

    [HttpPost("/App/integrations/api-keys/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateApiKey(
        string name, string kind, string? origins, int? actAsUserId, DateTime? expiresAt, bool fullAccess, string[]? scopes)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var isPublic = string.Equals(kind, ApiKeyService.KindPublic, StringComparison.OrdinalIgnoreCase);
        if (isPublic && string.IsNullOrWhiteSpace(origins))
        {
            TempData["Error"] = "برای کلید عمومی، دامنهٔ مجاز سایت را وارد کنید.";
            return RedirectToAction(nameof(Index));
        }

        if (!isPublic && !fullAccess && (scopes is null || scopes.Length == 0))
        {
            TempData["Error"] = "برای کلید محرمانه یا دسترسی کامل را بزنید یا ماژول‌ها را انتخاب کنید.";
            return RedirectToAction(nameof(Index));
        }

        var scopeList = fullAccess && !isPublic
            ? ["*"]
            : scopes ?? [];
        var scopesJson = ApiKeyService.BuildScopes(isPublic ? ApiKeyService.KindPublic : ApiKeyService.KindSecret, scopeList);
        var rawKey = ApiKeyService.NewRawKey(isPublic ? ApiKeyService.KindPublic : ApiKeyService.KindSecret);
        _db.ApiKeys.Add(new ApiKey
        {
            Name = string.IsNullOrWhiteSpace(name) ? "کلید API" : name.Trim(),
            Key = rawKey,
            KeyHash = ApiKeyService.Hash(rawKey),
            KeyPrefix = rawKey.Length > 12 ? rawKey[..12] : rawKey,
            Kind = isPublic ? ApiKeyService.KindPublic : ApiKeyService.KindSecret,
            ScopesJson = scopesJson,
            AllowedOrigins = isPublic ? origins?.Trim() : null,
            ActAsUserId = actAsUserId ?? _tenant.UserId,
            ExpiresAtUtc = expiresAt is DateTime day ? DateTime.SpecifyKind(day.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc) : null,
            CanRead = true,
            CanWrite = scopesJson == "*" ? fullAccess || !isPublic : scopesJson.Contains('w', StringComparison.OrdinalIgnoreCase)
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "کلید ساخته شد. از دکمهٔ کپی بردارید.";
        TempData["NewApiKey"] = rawKey;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/integrations/api-keys/{id:int}/reissue")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReissueApiKey(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id);
        if (key is null)
            return RedirectToAction(nameof(Index));

        var rawKey = ApiKeyService.NewRawKey(key.Kind);
        key.Key = rawKey;
        key.KeyHash = ApiKeyService.Hash(rawKey);
        key.KeyPrefix = rawKey.Length > 12 ? rawKey[..12] : rawKey;
        await _db.SaveChangesAsync();

        TempData["Success"] = "کلید جدید صادر شد. کلید قبلی دیگر کار نمی‌کند.";
        TempData["NewApiKey"] = rawKey;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/integrations/api-keys/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteApiKey(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id);
        if (key is not null)
        {
            _db.ApiKeys.Remove(key);
            await _db.SaveChangesAsync();
            TempData["Success"] = "کلید حذف شد.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/integrations/webhooks/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWebhook(string url, string? modules, string[]? events)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            TempData["Error"] = "آدرس وب‌هوک معتبر نیست.";
            return RedirectToAction(nameof(Index));
        }

        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var eventList = events is { Length: > 0 }
            ? string.Join(',', events)
            : "record.created,record.updated,record.deleted";
        _db.ApiWebhooks.Add(new ApiWebhook
        {
            Url = uri.ToString(),
            Secret = secret,
            Events = eventList,
            ModuleNames = string.IsNullOrWhiteSpace(modules) ? "*" : modules.Trim()
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "وب‌هوک ساخته شد. رمز امضا را همین حالا ذخیره کنید.";
        TempData["NewWebhookSecret"] = secret;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/integrations/webhooks/{id:int}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleWebhook(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var hook = await _db.ApiWebhooks.FirstOrDefaultAsync(h => h.Id == id);
        if (hook is not null)
        {
            hook.IsActive = !hook.IsActive;
            if (hook.IsActive)
                hook.FailCount = 0;
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/integrations/webhooks/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWebhook(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var hook = await _db.ApiWebhooks.FirstOrDefaultAsync(h => h.Id == id);
        if (hook is not null)
        {
            _db.ApiWebhooks.Remove(hook);
            await _db.SaveChangesAsync();
            TempData["Success"] = "وب‌هوک حذف شد.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/integrations/webhooks/{id:int}/test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestWebhook(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var status = await _webhooks.SendTestAsync(id);
        TempData["Success"] = $"ارسال آزمایشی انجام شد. وضعیت: {status}";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/App/integrations/api-docs")]
    public async Task<IActionResult> ApiDocs()
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var modules = await _metadata.GetActiveModulesAsync();
        var schemas = new List<(ModuleDef Module, IReadOnlyList<FieldDef> Fields)>();
        foreach (var module in modules.Where(m => !m.IsChildModule).OrderBy(m => m.PluralLabel))
            schemas.Add((module, await _metadata.GetFieldsAsync(module.Id)));

        ViewBag.Schemas = schemas;
        ViewBag.Origin = TelefonchyPublicOrigin();
        ViewData["Title"] = "مستندات API";
        return View();
    }

    [HttpGet("/App/integrations/swagger")]
    public async Task<IActionResult> Swagger()
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        ViewBag.TenantName = await CurrentTenantNameAsync();
        return View();
    }

    [HttpGet("/App/integrations/openapi.json")]
    public async Task<IActionResult> OpenApiSpec()
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var json = await _openApi.BuildJsonAsync(await CurrentTenantNameAsync(), TelefonchyPublicOrigin());
        return Content(json, "application/json; charset=utf-8");
    }

    private async Task<string> CurrentTenantNameAsync()
    {
        if (_tenant.TenantId is not int tenantId)
            return "CRM";
        return await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync() ?? "CRM";
    }

    [HttpPost("/App/integrations/api-keys/{id:int}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleApiKey(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id);
        if (key is not null)
        {
            key.IsActive = !key.IsActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = key.IsActive ? "کلید فعال شد." : "کلید غیرفعال شد.";
        }
        return RedirectToAction(nameof(Index));
    }
}
