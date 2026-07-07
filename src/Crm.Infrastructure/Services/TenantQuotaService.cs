using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

public class TenantPlanLimits
{
    public int MaxUsers { get; set; } = 5;
    public int MaxRecords { get; set; } = 10_000;
    public int MaxStorageMb { get; set; } = 1_024;
    public string PlanName { get; set; } = "دوره آزمایشی";
}

/// <summary>محدودیت‌های پلن فعال Tenant — برای نمایش و اعمال سقف کاربر/رکورد.</summary>
public class TenantQuotaService
{
    private const int TrialFallbackMaxUsers = 5;

    private readonly CrmDbContext _db;

    public TenantQuotaService(CrmDbContext db)
    {
        _db = db;
    }

    public async Task<TenantPlanLimits> GetLimitsAsync(int tenantId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var sub = await _db.Subscriptions.AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active && s.EndsAtUtc >= now)
            .OrderByDescending(s => s.EndsAtUtc)
            .FirstOrDefaultAsync(ct);

        if (sub?.Plan is not null)
        {
            return new TenantPlanLimits
            {
                MaxUsers = sub.Plan.MaxUsers,
                MaxRecords = sub.Plan.MaxRecords,
                MaxStorageMb = sub.Plan.MaxStorageMb,
                PlanName = sub.Plan.Name
            };
        }

        return new TenantPlanLimits
        {
            MaxUsers = TrialFallbackMaxUsers,
            PlanName = "دوره آزمایشی / بدون اشتراک فعال"
        };
    }

    public Task<int> CountActiveUsersAsync(int tenantId, CancellationToken ct = default) =>
        _db.Users.CountAsync(u => u.TenantId == tenantId && u.IsActive, ct);

    public async Task<(bool Ok, string? Error)> CanAddUserAsync(int tenantId, CancellationToken ct = default)
    {
        var limits = await GetLimitsAsync(tenantId, ct);
        var count = await CountActiveUsersAsync(tenantId, ct);
        if (count >= limits.MaxUsers)
            return (false, $"سقف کاربران پلن «{limits.PlanName}» ({limits.MaxUsers} نفر) پر شده است. اشتراک را ارتقا دهید.");
        return (true, null);
    }
}
