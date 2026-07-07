using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;

namespace Crm.Infrastructure.Services;

public class TenantProvisioningResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public Tenant? Tenant { get; init; }
    public CrmUser? AdminUser { get; init; }
}

/// <summary>
/// ثبت‌نام از سایت ← ساخت خودکار Tenant + نقش/پروفایل پیش‌فرض + کاربر ادمین
/// + دوره آزمایشی ۱۰ روزه + ماژول نمونه metadata-driven.
/// </summary>
public class TenantProvisioningService
{
    private const int TrialDays = 10;

    private readonly CrmDbContext _db;
    private readonly UserManager<CrmUser> _userManager;
    private readonly SalesModuleSeeder _salesSeeder;

    public TenantProvisioningService(CrmDbContext db, UserManager<CrmUser> userManager, SalesModuleSeeder salesSeeder)
    {
        _db = db;
        _userManager = userManager;
        _salesSeeder = salesSeeder;
    }

    public async Task<TenantProvisioningResult> RegisterAsync(
        string companyName, string fullName, string email, string password)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail.ToUpperInvariant()))
            return new TenantProvisioningResult { Success = false, Error = "کاربری با این ایمیل قبلاً ثبت‌نام کرده است." };

        var slug = await GenerateUniqueSlugAsync(companyName);

        var tenant = new Tenant
        {
            Name = companyName.Trim(),
            Slug = slug,
            Status = TenantStatus.Trial,
            CreatedAtUtc = DateTime.UtcNow,
            TrialEndsAtUtc = DateTime.UtcNow.AddDays(TrialDays)
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        // نقش‌ها و پروفایل‌های پیش‌فرض
        var ceoRole = new Role { TenantId = tenant.Id, Name = "مدیر عامل" };
        _db.CrmRoles.Add(ceoRole);
        await _db.SaveChangesAsync();

        var salesRole = new Role { TenantId = tenant.Id, Name = "کارشناس فروش", ParentRoleId = ceoRole.Id };
        _db.CrmRoles.Add(salesRole);

        var adminProfile = new Profile { TenantId = tenant.Id, Name = "مدیر سیستم", IsAdmin = true };
        var userProfile = new Profile { TenantId = tenant.Id, Name = "کاربر استاندارد" };
        _db.Profiles.AddRange(adminProfile, userProfile);
        await _db.SaveChangesAsync();

        // کاربر ادمین Tenant
        var user = new CrmUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            FullName = fullName.Trim(),
            TenantId = tenant.Id,
            CrmRoleId = ceoRole.Id,
            ProfileId = adminProfile.Id,
            IsTenantAdmin = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            _db.Tenants.Remove(tenant);
            await _db.SaveChangesAsync();
            var error = createResult.Errors.FirstOrDefault()?.Description ?? "خطا در ساخت حساب کاربری.";
            return new TenantProvisioningResult { Success = false, Error = error };
        }

        await _salesSeeder.SeedAsync(tenant.Id, adminProfile.Id, userProfile.Id);

        return new TenantProvisioningResult { Success = true, Tenant = tenant, AdminUser = user };
    }

    private async Task<string> GenerateUniqueSlugAsync(string companyName)
    {
        var baseSlug = new string(companyName
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-')
            .ToArray())
            .Trim('-');

        if (string.IsNullOrWhiteSpace(baseSlug))
            baseSlug = "org";

        var slug = baseSlug;
        var suffix = 2;
        while (await _db.Tenants.AnyAsync(t => t.Slug == slug))
        {
            slug = $"{baseSlug}-{suffix}";
            suffix++;
        }

        return slug;
    }
}
