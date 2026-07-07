using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Web.Models.Admin;
using Crm.Web.Services;

namespace Crm.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class TenantsController : Controller
{
    private readonly PlatformAdminService _platform;
    private readonly CrmDbContext _db;
    private readonly SignInManager<CrmUser> _signInManager;

    public TenantsController(PlatformAdminService platform, CrmDbContext db, SignInManager<CrmUser> signInManager)
    {
        _platform = platform;
        _db = db;
        _signInManager = signInManager;
    }

    public async Task<IActionResult> Index(string? q, TenantStatus? status)
    {
        var model = await _platform.GetTenantsAsync(q, status);
        ViewData["Search"] = q;
        ViewData["Status"] = status;
        return View(model);
    }

    public async Task<IActionResult> Details(int id)
    {
        var model = await _platform.GetTenantDetailsAsync(id);
        if (model is null)
            return NotFound();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, TenantStatus status)
    {
        var (ok, error) = await _platform.SetTenantStatusAsync(id, status, User.Identity?.Name, ClientIp());
        if (!ok)
            TempData["Error"] = error;
        else
            TempData["Success"] = "وضعیت مشتری به‌روزرسانی شد.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Impersonate(int id)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null)
            return NotFound();

        var adminUser = await _db.Users
            .Where(u => u.TenantId == id && u.IsTenantAdmin)
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync();

        if (adminUser is null)
        {
            TempData["Error"] = "این مشتری کاربر ادمینی ندارد.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var principal = await _signInManager.CreateUserPrincipalAsync(adminUser);
        ((System.Security.Claims.ClaimsIdentity)principal.Identity!)
            .AddClaim(new System.Security.Claims.Claim(CrmClaimTypes.ImpersonatedBy, User.Identity?.Name ?? "admin"));

        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal);

        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenant.Id,
            ModuleName = "tenants",
            RecordId = tenant.Id,
            Action = "Impersonate",
            Changes = $"{{\"by\":\"{User.Identity?.Name}\",\"asUserId\":{adminUser.Id}}}",
            AtUtc = DateTime.UtcNow,
            Ip = ClientIp()
        });
        await _db.SaveChangesAsync();

        return Redirect("/App");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string confirmSlug)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null)
            return NotFound();

        var (ok, error) = await _platform.DeleteTenantAsync(id, confirmSlug, User.Identity?.Name, ClientIp());
        if (!ok)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = $"مشتری «{tenant.Name}» و تمام داده‌هایش حذف شد.";
        return RedirectToAction(nameof(Index));
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
