using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>مشاهده وضعیت اشتراک Tenant و تمدید آنلاین (پرداخت درگاه).</summary>
public class SubscriptionController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;

    public SubscriptionController(CrmDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [HttpGet("/App/subscription")]
    public async Task<IActionResult> Index()
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == _tenant.TenantId);
        var current = await _db.Subscriptions.AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => s.TenantId == tenant.Id && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.EndsAtUtc)
            .FirstOrDefaultAsync();

        ViewBag.Tenant = tenant;
        ViewBag.Current = current;
        ViewBag.Plans = await _db.Plans.AsNoTracking()
            .Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();

        ViewData["Title"] = "اشتراک و تمدید";
        return View();
    }

    [HttpPost("/App/subscription/renew")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Renew(int planId, [FromServices] IPaymentGateway gateway)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var plan = await _db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId && p.IsActive);
        if (plan is null)
            return NotFound();

        var transaction = new PaymentTransaction
        {
            Token = Guid.NewGuid().ToString("N"),
            Kind = PaymentTransactionKind.SubscriptionRenewal,
            TargetId = plan.Id,
            Amount = plan.PriceYearly > 0 ? plan.PriceYearly : plan.PriceMonthly * 12,
            Description = $"تمدید اشتراک سالانه پلن «{plan.Name}»",
            ReturnUrl = "/App/subscription"
        };
        _db.PaymentTransactions.Add(transaction);
        await _db.SaveChangesAsync();

        return Redirect(await gateway.BeginAsync(transaction));
    }
}
