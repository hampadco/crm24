using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.Owner.Controllers;

public class SubscriptionCreateModel
{
    [Required]
    public int TenantId { get; set; }

    [Required(ErrorMessage = "انتخاب پلن الزامی است.")]
    [Display(Name = "پلن")]
    public int PlanId { get; set; }

    [Display(Name = "مدت (ماه)")]
    [Range(1, 36)]
    public int Months { get; set; } = 12;

    [Display(Name = "مبلغ (تومان)")]
    [Range(0, 9_999_999_999)]
    public decimal Amount { get; set; }

    [Display(Name = "پرداخت همین حالا ثبت شود")]
    public bool RecordPayment { get; set; } = true;

    [Display(Name = "شماره پیگیری پرداخت")]
    public string? PaymentReference { get; set; }

    [Display(Name = "یادداشت")]
    public string? Note { get; set; }
}

public class SubscriptionsController : OwnerControllerBase
{
    private readonly CrmDbContext _db;

    public SubscriptionsController(CrmDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var subs = await _db.Subscriptions.AsNoTracking()
            .Include(s => s.Tenant)
            .Include(s => s.Plan)
            .Include(s => s.Payments)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(200)
            .ToListAsync();
        return View(subs);
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
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == model.TenantId);
        var plan = await _db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == model.PlanId);
        if (tenant is null || plan is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            ViewData["Tenant"] = tenant;
            ViewData["Plans"] = await _db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
            return View(model);
        }

        var now = DateTime.UtcNow;

        // تمدید: اگر اشتراک فعالی هست از پایان آن ادامه پیدا می‌کند
        var lastActiveEnd = await _db.Subscriptions
            .Where(s => s.TenantId == tenant.Id && s.Status == SubscriptionStatus.Active && s.EndsAtUtc > now)
            .MaxAsync(s => (DateTime?)s.EndsAtUtc);

        var start = lastActiveEnd ?? now;
        var subscription = new Subscription
        {
            TenantId = tenant.Id,
            PlanId = plan.Id,
            StartsAtUtc = start,
            EndsAtUtc = start.AddMonths(model.Months),
            Status = SubscriptionStatus.Active,
            Amount = model.Amount,
            Note = model.Note?.Trim(),
            CreatedAtUtc = now
        };
        _db.Subscriptions.Add(subscription);

        if (model.RecordPayment && model.Amount > 0)
        {
            subscription.Payments.Add(new SubscriptionPayment
            {
                Amount = model.Amount,
                PaidAtUtc = now,
                Method = "manual",
                Reference = model.PaymentReference?.Trim(),
                Note = model.Note?.Trim()
            });
        }

        tenant.Status = TenantStatus.Active;

        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenant.Id,
            ModuleName = "subscriptions",
            RecordId = tenant.Id,
            Action = "Subscribe",
            Changes = $"{{\"plan\":\"{plan.Name}\",\"months\":{model.Months},\"amount\":{model.Amount},\"by\":\"{User.Identity?.Name}\"}}",
            AtUtc = now,
            Ip = HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        await _db.SaveChangesAsync();
        TempData["Success"] = $"اشتراک «{plan.Name}» برای «{tenant.Name}» تا {subscription.EndsAtUtc:yyyy/MM/dd} ثبت شد.";
        return RedirectToAction("Details", "Tenants", new { id = tenant.Id });
    }
}
