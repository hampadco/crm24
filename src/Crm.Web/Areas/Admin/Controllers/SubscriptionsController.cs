using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Web.Models.Admin;
using Crm.Web.Services;

namespace Crm.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class SubscriptionsController : Controller
{
    private readonly PlatformAdminService _platform;
    private readonly CrmDbContext _db;

    public SubscriptionsController(PlatformAdminService platform, CrmDbContext db)
    {
        _platform = platform;
        _db = db;
    }

    public async Task<IActionResult> Index(int page = 1)
    {
        var listQuery = new ContentListQuery { Page = page, PageSize = 20 };
        var model = await _db.Subscriptions.AsNoTracking()
            .Include(s => s.Tenant)
            .Include(s => s.Plan)
            .Include(s => s.Payments)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToPagedListAsync(listQuery);
        ViewBag.ListQuery = listQuery;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int tenantId)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null)
            return NotFound();

        ViewData["Tenant"] = tenant;
        ViewData["Plans"] = await _db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
        return View(new SubscriptionCreateModel { TenantId = tenantId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SubscriptionCreateModel model)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == model.TenantId);
        if (tenant is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            ViewData["Tenant"] = tenant;
            ViewData["Plans"] = await _db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
            return View(model);
        }

        var (subscription, error) = await _platform.CreateSubscriptionAsync(model, User.Identity?.Name, ClientIp());
        if (subscription is null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Create), new { tenantId = model.TenantId });
        }

        TempData["Success"] = $"اشتراک تا {PersianDateHelper.ToJalaliDate(subscription.EndsAtUtc)} ثبت شد.";
        return RedirectToAction("Details", "Tenants", new { id = model.TenantId });
    }

    [HttpGet]
    public async Task<IActionResult> Gift(int tenantId)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null)
            return NotFound();

        ViewData["Tenant"] = tenant;
        ViewData["Plans"] = await _db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
        return View(new GiftSubscriptionModel { TenantId = tenantId, Months = 1 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Gift(GiftSubscriptionModel model)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == model.TenantId);
        if (tenant is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            ViewData["Tenant"] = tenant;
            ViewData["Plans"] = await _db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
            return View(model);
        }

        var (subscription, error) = await _platform.CreateGiftSubscriptionAsync(model, User.Identity?.Name, ClientIp());
        if (subscription is null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Gift), new { tenantId = model.TenantId });
        }

        TempData["Success"] = $"اشتراک هدیه تا {PersianDateHelper.ToJalaliDate(subscription.EndsAtUtc)} فعال شد.";
        return RedirectToAction("Details", "Tenants", new { id = model.TenantId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var sub = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null)
            return NotFound();

        var (ok, error) = await _platform.CancelSubscriptionAsync(id, User.Identity?.Name, ClientIp());
        TempData[ok ? "Success" : "Error"] = ok ? "اشتراک لغو شد." : error;
        return RedirectToAction("Details", "Tenants", new { id = sub.TenantId });
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
