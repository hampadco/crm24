using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.Owner.Controllers;

public class OwnerDashboardViewModel
{
    public int TotalTenants { get; set; }
    public int TrialTenants { get; set; }
    public int ActiveTenants { get; set; }
    public int ExpiredTenants { get; set; }
    public int NewTenantsLast30Days { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal RevenueLast30Days { get; set; }
    public double TrialConversionPercent { get; set; }

    /// <summary>۶ ماه اخیر: (برچسب ماه، Tenant های جدید، درآمد).</summary>
    public List<(string Label, int NewTenants, decimal Revenue)> MonthlyStats { get; set; } = [];

    public List<Tenant> RecentTenants { get; set; } = [];
}

public class DashboardController : OwnerControllerBase
{
    private readonly CrmDbContext _db;

    public DashboardController(CrmDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var last30 = now.AddDays(-30);

        var tenants = await _db.Tenants.AsNoTracking().ToListAsync();
        var payments = await _db.SubscriptionPayments.AsNoTracking().ToListAsync();

        var trialCount = tenants.Count(t => t.Status == TenantStatus.Trial);
        var activeCount = tenants.Count(t => t.Status == TenantStatus.Active);
        var pastTrial = tenants.Count(t => t.Status is TenantStatus.Active or TenantStatus.Expired);

        var monthly = new List<(string, int, decimal)>();
        var pc = new System.Globalization.PersianCalendar();
        for (var i = 5; i >= 0; i--)
        {
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
            var monthEnd = monthStart.AddMonths(1);
            var label = $"{pc.GetYear(monthStart)}/{pc.GetMonth(monthStart):00}";
            monthly.Add((
                label,
                tenants.Count(t => t.CreatedAtUtc >= monthStart && t.CreatedAtUtc < monthEnd),
                payments.Where(p => p.PaidAtUtc >= monthStart && p.PaidAtUtc < monthEnd).Sum(p => p.Amount)));
        }

        var model = new OwnerDashboardViewModel
        {
            TotalTenants = tenants.Count,
            TrialTenants = trialCount,
            ActiveTenants = activeCount,
            ExpiredTenants = tenants.Count(t => t.Status == TenantStatus.Expired),
            NewTenantsLast30Days = tenants.Count(t => t.CreatedAtUtc >= last30),
            TotalRevenue = payments.Sum(p => p.Amount),
            RevenueLast30Days = payments.Where(p => p.PaidAtUtc >= last30).Sum(p => p.Amount),
            TrialConversionPercent = pastTrial == 0 ? 0 : Math.Round(activeCount * 100.0 / pastTrial, 1),
            MonthlyStats = monthly,
            RecentTenants = tenants.OrderByDescending(t => t.CreatedAtUtc).Take(8).ToList()
        };

        return View(model);
    }
}
