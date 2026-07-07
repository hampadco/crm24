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

    public IntegrationsController(CrmDbContext db, ITenantContext tenant, TenantIntegrationService integrations)
    {
        _db = db;
        _tenant = tenant;
        _integrations = integrations;
    }

    [HttpGet("/App/integrations")]
    public async Task<IActionResult> Index()
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        ViewBag.Config = await _integrations.GetAsync();
        ViewBag.ApiKeys = await _db.ApiKeys.AsNoTracking()
            .OrderByDescending(k => k.Id).ToListAsync();

        ViewData["Title"] = "یکپارچگی‌ها و API";
        return View();
    }

    [HttpPost("/App/integrations/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(IntegrationConfig config)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        await _integrations.SaveAsync(config);
        TempData["Success"] = "تنظیمات یکپارچگی ذخیره شد.";
        return RedirectToAction(nameof(Index));
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
