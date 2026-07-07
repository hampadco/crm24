using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

/// <summary>
/// چرخه عمر اشتراک: انقضای تریال/اشتراک (جاب روزانه Hangfire) و بررسی دسترسی Tenant.
/// </summary>
public class TenantLifecycleService
{
    private readonly CrmDbContext _db;

    public TenantLifecycleService(CrmDbContext db)
    {
        _db = db;
    }

    /// <summary>جاب روزانه: اشتراک‌های سررسیدشده و Tenant های منقضی را علامت می‌زند.</summary>
    public async Task CheckExpirationsAsync()
    {
        var now = DateTime.UtcNow;

        var expiredSubs = await _db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active && s.EndsAtUtc < now)
            .ToListAsync();
        foreach (var sub in expiredSubs)
            sub.Status = SubscriptionStatus.Expired;

        var tenants = await _db.Tenants
            .Where(t => t.Status == TenantStatus.Trial || t.Status == TenantStatus.Active)
            .ToListAsync();

        var activeSubTenantIds = await _db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active && s.EndsAtUtc >= now)
            .Select(s => s.TenantId)
            .Distinct()
            .ToListAsync();

        foreach (var tenant in tenants)
        {
            var hasActiveSub = activeSubTenantIds.Contains(tenant.Id);

            if (tenant.Status == TenantStatus.Trial && tenant.TrialEndsAtUtc < now)
                tenant.Status = hasActiveSub ? TenantStatus.Active : TenantStatus.Expired;
            else if (tenant.Status == TenantStatus.Active && !hasActiveSub)
                tenant.Status = TenantStatus.Expired;
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>آیا این Tenant الان اجازه استفاده از پنل را دارد؟ (بررسی زنده، مستقل از جاب)</summary>
    public static bool HasAccess(Tenant tenant) => tenant.Status switch
    {
        TenantStatus.Active => true,
        TenantStatus.Trial => tenant.TrialEndsAtUtc is null || tenant.TrialEndsAtUtc >= DateTime.UtcNow,
        _ => false
    };

    /// <summary>پلن‌های پیش‌فرض تعرفه (یک‌بار در راه‌اندازی).</summary>
    public async Task SeedDefaultPlansAsync()
    {
        if (await _db.Plans.AnyAsync())
            return;

        _db.Plans.AddRange(
            new Plan
            {
                Name = "پایه", Description = "برای تیم‌های کوچک فروش",
                PriceMonthly = 990_000, PriceYearly = 9_900_000,
                MaxUsers = 3, MaxRecords = 10_000, MaxStorageMb = 1_024,
                Features = "مدیریت سرنخ و مخاطب\nکاریز فروش\nگزارش‌های پایه\nپشتیبانی ایمیلی",
                SortOrder = 1
            },
            new Plan
            {
                Name = "حرفه‌ای", Description = "پرفروش‌ترین پلن برای کسب‌وکارهای در حال رشد",
                PriceMonthly = 2_490_000, PriceYearly = 24_900_000,
                MaxUsers = 10, MaxRecords = 100_000, MaxStorageMb = 10_240,
                Features = "همه امکانات پلن پایه\nاتوماسیون گردش‌کار\nفاکتور و مالی\nتیکتینگ و SLA\nپشتیبانی تلفنی",
                IsFeatured = true, SortOrder = 2
            },
            new Plan
            {
                Name = "سازمانی", Description = "برای سازمان‌های بزرگ با نیازهای خاص",
                PriceMonthly = 5_990_000, PriceYearly = 59_900_000,
                MaxUsers = 50, MaxRecords = 1_000_000, MaxStorageMb = 51_200,
                Features = "همه امکانات پلن حرفه‌ای\nAPI اختصاصی\nاتصال VoIP و حسابداری\nمدیر موفقیت مشتری\nنصب On-Premise",
                SortOrder = 3
            });

        await _db.SaveChangesAsync();
    }
}
