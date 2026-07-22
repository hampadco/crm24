using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Services;
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
    private readonly DemoTenantSeeder _demoSeeder;
    private readonly IWebHostEnvironment _env;

    public TenantsController(
        PlatformAdminService platform,
        CrmDbContext db,
        SignInManager<CrmUser> signInManager,
        DemoTenantSeeder demoSeeder,
        IWebHostEnvironment env)
    {
        _platform = platform;
        _db = db;
        _signInManager = signInManager;
        _demoSeeder = demoSeeder;
        _env = env;
    }

    public async Task<IActionResult> Index(string? q, TenantStatus? status, int page = 1)
    {
        var listQuery = new TenantListQuery { Q = q, Status = status, Page = page };
        var model = await _platform.GetTenantsAsync(listQuery);
        ViewBag.TenantListQuery = listQuery;
        ViewBag.PaginationRoutes = BuildTenantPaginationRoutes(listQuery);
        ViewBag.AllowDemoTenant = _env.IsDevelopment();
        if (_env.IsDevelopment())
        {
            ViewBag.DemoExists = await _demoSeeder.DemoExistsAsync();
            ViewBag.DemoEmail = DemoTenantSeeder.DemoEmail;
            ViewBag.DemoPassword = DemoTenantSeeder.DemoPassword;
        }
        return View(model);
    }

    private static Dictionary<string, object?> BuildTenantPaginationRoutes(TenantListQuery query)
    {
        var routes = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(query.Q))
            routes["q"] = query.Q;
        if (query.Status is not null)
            routes["status"] = query.Status.ToString();
        return routes;
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
    public async Task<IActionResult> CreateDemo()
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var (ok, message, tenantId) = await _demoSeeder.CreateOrRefreshAsync();
        if (!ok)
        {
            TempData["Error"] = message;
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = message;
        if (tenantId is int id)
            return RedirectToAction(nameof(Details), new { id });

        return RedirectToAction(nameof(Index));
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
