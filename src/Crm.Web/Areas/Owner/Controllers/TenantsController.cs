using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;

namespace Crm.Web.Areas.Owner.Controllers;

public class TenantListItem
{
    public Tenant Tenant { get; set; } = null!;
    public int UserCount { get; set; }
    public int RecordCount { get; set; }
}

public class TenantDetailsViewModel
{
    public Tenant Tenant { get; set; } = null!;
    public int UserCount { get; set; }
    public int RecordCount { get; set; }
    public long StorageBytes { get; set; }
    public List<CrmUser> Users { get; set; } = [];
    public List<Subscription> Subscriptions { get; set; } = [];
}

public class TenantsController : OwnerControllerBase
{
    private readonly CrmDbContext _db;
    private readonly SignInManager<CrmUser> _signInManager;

    public TenantsController(CrmDbContext db, SignInManager<CrmUser> signInManager)
    {
        _db = db;
        _signInManager = signInManager;
    }

    public async Task<IActionResult> Index(string? q, TenantStatus? status)
    {
        var query = _db.Tenants.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(t => EF.Functions.ILike(t.Name, $"%{q.Trim()}%") || EF.Functions.ILike(t.Slug, $"%{q.Trim()}%"));

        if (status is not null)
            query = query.Where(t => t.Status == status);

        var tenants = await query.OrderByDescending(t => t.CreatedAtUtc).Take(200).ToListAsync();
        var ids = tenants.Select(t => t.Id).ToList();

        var userCounts = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.TenantId))
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count);

        var recordCounts = await _db.Records.IgnoreQueryFilters().AsNoTracking()
            .Where(r => ids.Contains(r.TenantId) && !r.IsDeleted)
            .GroupBy(r => r.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count);

        var model = tenants.Select(t => new TenantListItem
        {
            Tenant = t,
            UserCount = userCounts.GetValueOrDefault(t.Id),
            RecordCount = recordCounts.GetValueOrDefault(t.Id)
        }).ToList();

        ViewData["Search"] = q;
        ViewData["Status"] = status;
        return View(model);
    }

    public async Task<IActionResult> Details(int id)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null)
            return NotFound();

        var model = new TenantDetailsViewModel
        {
            Tenant = tenant,
            UserCount = await _db.Users.CountAsync(u => u.TenantId == id),
            RecordCount = await _db.Records.IgnoreQueryFilters().CountAsync(r => r.TenantId == id && !r.IsDeleted),
            StorageBytes = await _db.Attachments.IgnoreQueryFilters()
                .Where(a => a.TenantId == id && !a.IsDeleted)
                .SumAsync(a => (long?)a.SizeBytes) ?? 0,
            Users = await _db.Users.AsNoTracking().Where(u => u.TenantId == id).OrderBy(u => u.Id).ToListAsync(),
            Subscriptions = await _db.Subscriptions.AsNoTracking()
                .IgnoreQueryFilters()
                .Include(s => s.Plan)
                .Include(s => s.Payments)
                .Where(s => s.TenantId == id)
                .OrderByDescending(s => s.EndsAtUtc)
                .ToListAsync()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, TenantStatus status)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null)
            return NotFound();

        tenant.Status = status;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenant.Id,
            ModuleName = "tenants",
            RecordId = tenant.Id,
            Action = "SetStatus",
            Changes = $"{{\"status\":\"{status}\",\"by\":\"{User.Identity?.Name}\"}}",
            AtUtc = DateTime.UtcNow,
            Ip = HttpContext.Connection.RemoteIpAddress?.ToString()
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"وضعیت «{tenant.Name}» به {StatusLabel(status)} تغییر کرد.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>ورود مالک به پنل Tenant برای پشتیبانی — با ثبت در Audit.</summary>
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
            TempData["Error"] = "این Tenant کاربر ادمینی ندارد.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var principal = await _signInManager.CreateUserPrincipalAsync(adminUser);
        ((System.Security.Claims.ClaimsIdentity)principal.Identity!)
            .AddClaim(new System.Security.Claims.Claim(CrmClaimTypes.ImpersonatedBy, User.Identity?.Name ?? "owner"));

        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal);

        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenant.Id,
            ModuleName = "tenants",
            RecordId = tenant.Id,
            Action = "Impersonate",
            Changes = $"{{\"by\":\"{User.Identity?.Name}\",\"asUserId\":{adminUser.Id}}}",
            AtUtc = DateTime.UtcNow,
            Ip = HttpContext.Connection.RemoteIpAddress?.ToString()
        });
        await _db.SaveChangesAsync();

        return Redirect("/App");
    }

    private static string StatusLabel(TenantStatus status) => status switch
    {
        TenantStatus.Trial => "آزمایشی",
        TenantStatus.Active => "فعال",
        TenantStatus.Suspended => "معلق",
        TenantStatus.Expired => "منقضی",
        _ => status.ToString()
    };
}
