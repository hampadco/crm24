using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>تنظیمات یکپارچگی‌ها (پیامک/ایمیل/بله/حسابداری/VoIP) + مدیریت کلیدهای API.</summary>
public class IntegrationsController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TenantIntegrationService _integrations;
    private readonly TelefonchyClient _telefonchy;
    private readonly SmsService _sms;

    public IntegrationsController(
        CrmDbContext db, ITenantContext tenant, TenantIntegrationService integrations,
        TelefonchyClient telefonchy, SmsService sms)
    {
        _db = db;
        _tenant = tenant;
        _integrations = integrations;
        _telefonchy = telefonchy;
        _sms = sms;
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
    public async Task<IActionResult> CreateApiKey(string name, bool canWrite)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var rawKey = $"crm_{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}";
        _db.ApiKeys.Add(new ApiKey
        {
            Name = string.IsNullOrWhiteSpace(name) ? "کلید API" : name.Trim(),
            Key = rawKey,
            CanRead = true,
            CanWrite = canWrite
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "کلید ساخته شد. آن را در جای امن نگه دارید.";
        TempData["NewApiKey"] = rawKey;
        return RedirectToAction(nameof(Index));
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
