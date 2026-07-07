using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Services;
using Crm.Web.Areas.App.Models;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>مدیریت کاربران CRM (همکاران Tenant) — فقط ادمین Tenant.</summary>
public class TeamUsersController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly UserManager<CrmUser> _userManager;
    private readonly TenantQuotaService _quota;

    public TeamUsersController(
        CrmDbContext db,
        ITenantContext tenant,
        UserManager<CrmUser> userManager,
        TenantQuotaService quota)
    {
        _db = db;
        _tenant = tenant;
        _userManager = userManager;
        _quota = quota;
    }

    [HttpGet("/App/team-users")]
    public async Task<IActionResult> Index()
    {
        if (!await EnsureTenantAdminAsync())
            return Forbid("Identity.Application");

        var tenantId = _tenant.TenantId!.Value;
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .OrderByDescending(u => u.IsTenantAdmin)
            .ThenBy(u => u.FullName)
            .ToListAsync();

        var profiles = await _db.Profiles.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .ToDictionaryAsync(p => p.Id, p => p.Name);

        var roles = await _db.CrmRoles.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .ToDictionaryAsync(r => r.Id, r => r.Name);

        var model = users.Select(u => new TeamUserListItem
        {
            User = u,
            ProfileName = u.ProfileId is int pid ? profiles.GetValueOrDefault(pid) : null,
            RoleName = u.CrmRoleId is int rid ? roles.GetValueOrDefault(rid) : null
        }).ToList();

        ViewBag.Limits = await _quota.GetLimitsAsync(tenantId);
        ViewBag.UserCount = users.Count(u => u.IsActive);
        ViewBag.Profiles = profiles;
        ViewBag.Roles = roles;

        ViewData["Title"] = "همکاران";
        return View(model);
    }

    [HttpPost("/App/team-users/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TeamUserCreateModel model)
    {
        if (!await EnsureTenantAdminAsync())
            return Forbid("Identity.Application");

        var tenantId = _tenant.TenantId!.Value;

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "اطلاعات فرم نامعتبر است.";
            return RedirectToAction(nameof(Index));
        }

        var (canAdd, quotaError) = await _quota.CanAddUserAsync(tenantId);
        if (!canAdd)
        {
            TempData["Error"] = quotaError;
            return RedirectToAction(nameof(Index));
        }

        var email = model.Email.Trim().ToLowerInvariant();
        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            TempData["Error"] = "این ایمیل قبلاً در سیستم ثبت شده است.";
            return RedirectToAction(nameof(Index));
        }

        if (!await IsValidProfileAndRoleAsync(tenantId, model.ProfileId, model.CrmRoleId))
        {
            TempData["Error"] = "پروفایل یا نقش انتخاب‌شده معتبر نیست.";
            return RedirectToAction(nameof(Index));
        }

        var user = new CrmUser
        {
            UserName = email,
            Email = email,
            FullName = model.FullName.Trim(),
            TenantId = tenantId,
            ProfileId = model.ProfileId,
            CrmRoleId = model.CrmRoleId,
            IsTenantAdmin = model.IsTenantAdmin,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"همکار «{user.FullName}» اضافه شد. می‌تواند از /App/Account/Login وارد شود.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/App/team-users/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        if (!await EnsureTenantAdminAsync())
            return Forbid("Identity.Application");

        var tenantId = _tenant.TenantId!.Value;
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId);
        if (user is null)
            return NotFound();

        await LoadEditLookupsAsync(tenantId);

        ViewData["Title"] = $"ویرایش — {user.FullName}";
        return View(new TeamUserEditModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? "",
            ProfileId = user.ProfileId,
            CrmRoleId = user.CrmRoleId,
            IsTenantAdmin = user.IsTenantAdmin,
            IsActive = user.IsActive
        });
    }

    [HttpPost("/App/team-users/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TeamUserEditModel model)
    {
        if (!await EnsureTenantAdminAsync())
            return Forbid("Identity.Application");

        if (id != model.Id)
            return BadRequest();

        var tenantId = _tenant.TenantId!.Value;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId);
        if (user is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            await LoadEditLookupsAsync(tenantId);
            ViewData["Title"] = $"ویرایش — {user.FullName}";
            return View(model);
        }

        if (!await IsValidProfileAndRoleAsync(tenantId, model.ProfileId, model.CrmRoleId))
        {
            TempData["Error"] = "پروفایل یا نقش انتخاب‌شده معتبر نیست.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var email = model.Email.Trim().ToLowerInvariant();
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null && existing.Id != user.Id)
        {
            TempData["Error"] = "این ایمیل متعلق به کاربر دیگری است.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        if (user.IsTenantAdmin && !model.IsTenantAdmin)
        {
            var adminCount = await _db.Users.CountAsync(u => u.TenantId == tenantId && u.IsTenantAdmin && u.IsActive && u.Id != user.Id);
            if (adminCount == 0)
            {
                TempData["Error"] = "حداقل یک مدیر Tenant باید باقی بماند.";
                return RedirectToAction(nameof(Edit), new { id });
            }
        }

        if (user.Id == _tenant.UserId && !model.IsActive)
        {
            TempData["Error"] = "نمی‌توانید حساب خود را غیرفعال کنید.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        if (!user.IsActive && model.IsActive)
        {
            var (canAdd, quotaError) = await _quota.CanAddUserAsync(tenantId);
            if (!canAdd)
            {
                TempData["Error"] = quotaError;
                return RedirectToAction(nameof(Edit), new { id });
            }
        }

        user.FullName = model.FullName.Trim();
        user.Email = email;
        user.UserName = email;
        user.NormalizedEmail = email.ToUpperInvariant();
        user.NormalizedUserName = email.ToUpperInvariant();
        user.ProfileId = model.ProfileId;
        user.CrmRoleId = model.CrmRoleId;
        user.IsTenantAdmin = model.IsTenantAdmin;
        user.IsActive = model.IsActive;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            TempData["Error"] = string.Join(" ", updateResult.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Edit), new { id });
        }

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var pwdResult = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!pwdResult.Succeeded)
            {
                TempData["Error"] = string.Join(" ", pwdResult.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Edit), new { id });
            }
        }

        TempData["Success"] = "اطلاعات همکار به‌روزرسانی شد.";
        return RedirectToAction(nameof(Index));
    }

    private Task<bool> EnsureTenantAdminAsync() => Task.FromResult(_tenant.IsTenantAdmin);

    private async Task LoadEditLookupsAsync(int tenantId)
    {
        ViewBag.Profiles = await _db.Profiles.AsNoTracking()
            .Where(p => p.TenantId == tenantId).OrderBy(p => p.Name).ToListAsync();
        ViewBag.Roles = await _db.CrmRoles.AsNoTracking()
            .Where(r => r.TenantId == tenantId).OrderBy(r => r.Name).ToListAsync();
    }

    private async Task<bool> IsValidProfileAndRoleAsync(int tenantId, int? profileId, int? roleId)
    {
        if (profileId is int pid && !await _db.Profiles.AnyAsync(p => p.Id == pid && p.TenantId == tenantId))
            return false;
        if (roleId is int rid && !await _db.CrmRoles.AnyAsync(r => r.Id == rid && r.TenantId == tenantId))
            return false;
        return true;
    }
}
