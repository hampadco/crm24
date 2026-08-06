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
    public IActionResult Index()
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        return Redirect("/App/access?tab=users");
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
            return Redirect("/App/access?tab=users");
        }

        var (canAdd, quotaError) = await _quota.CanAddUserAsync(tenantId);
        if (!canAdd)
        {
            TempData["Error"] = quotaError;
            return Redirect("/App/access?tab=users");
        }

        var email = model.Email.Trim().ToLowerInvariant();
        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            TempData["Error"] = "این ایمیل قبلاً در سیستم ثبت شده است.";
            return Redirect("/App/access?tab=users");
        }

        if (model.CrmRoleId is not int roleId || !await IsValidRoleAsync(tenantId, roleId))
        {
            TempData["Error"] = "انتخاب نقش الزامی است.";
            return Redirect("/App/access?tab=users");
        }

        var user = new CrmUser
        {
            UserName = email,
            Email = email,
            FullName = model.FullName.Trim(),
            TenantId = tenantId,
            CrmRoleId = roleId,
            IsTenantAdmin = model.IsTenantAdmin,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return Redirect("/App/access?tab=users");
        }

        TempData["Success"] = $"همکار «{user.FullName}» اضافه شد. می‌تواند از /App/Account/Login وارد شود.";
        return Redirect("/App/access?tab=users");
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

        if (model.CrmRoleId is not int roleId || !await IsValidRoleAsync(tenantId, roleId))
        {
            TempData["Error"] = "انتخاب نقش الزامی است.";
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
        user.CrmRoleId = roleId;
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
        return Redirect("/App/access?tab=users");
    }

    private Task<bool> EnsureTenantAdminAsync() => Task.FromResult(_tenant.IsTenantAdmin);

    private async Task LoadEditLookupsAsync(int tenantId)
    {
        ViewBag.Roles = await _db.CrmRoles.AsNoTracking()
            .Where(r => r.TenantId == tenantId).OrderBy(r => r.Name).ToListAsync();
    }

    private Task<bool> IsValidRoleAsync(int tenantId, int roleId) =>
        _db.CrmRoles.AnyAsync(r => r.Id == roleId && r.TenantId == tenantId);
}
