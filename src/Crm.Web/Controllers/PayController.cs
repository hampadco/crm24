using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Controllers;

/// <summary>صفحه پرداخت درگاه آزمایشی + اعمال نتیجه روی فاکتور یا اشتراک.</summary>
public class PayController : Controller
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;

    public PayController(CrmDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    private async Task<PaymentTransaction?> FindAsync(string token) =>
        await _db.PaymentTransactions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Token == token && !t.IsDeleted);

    [HttpGet("/pay/{token}")]
    public async Task<IActionResult> Index(string token)
    {
        var transaction = await FindAsync(token);
        if (transaction is null)
            return NotFound();

        return View(transaction);
    }

    [HttpPost("/pay/{token}/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string token, string action)
    {
        var transaction = await FindAsync(token);
        if (transaction is null)
            return NotFound();

        if (transaction.Status != PaymentTransactionStatus.Pending)
            return RedirectToReturn(transaction);

        if (action != "pay")
        {
            transaction.Status = PaymentTransactionStatus.Failed;
            await _db.SaveChangesAsync();
            return RedirectToReturn(transaction);
        }

        // زمینه Tenant برای سرویس‌های پایین‌دستی
        if (_tenant is TenantContext mutable)
        {
            mutable.TenantId = transaction.TenantId;
            mutable.IsTenantAdmin = true;
        }

        transaction.Status = PaymentTransactionStatus.Paid;
        transaction.PaidAtUtc = DateTime.UtcNow;
        transaction.Reference = $"SBX-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";

        if (transaction.Kind == PaymentTransactionKind.Invoice)
        {
            var finance = HttpContext.RequestServices.GetRequiredService<FinanceService>();
            await finance.AddPaymentAsync(transaction.TargetId, transaction.Amount, "online", transaction.Reference, "پرداخت آنلاین درگاه");
        }
        else
        {
            await ApplySubscriptionRenewalAsync(transaction);
        }

        await _db.SaveChangesAsync();
        return RedirectToReturn(transaction);
    }

    private async Task ApplySubscriptionRenewalAsync(PaymentTransaction transaction)
    {
        var tenant = await _db.Tenants.FirstAsync(t => t.Id == transaction.TenantId);
        var plan = await _db.Plans.AsNoTracking().FirstAsync(p => p.Id == transaction.TargetId);

        var now = DateTime.UtcNow;
        var lastActiveEnd = await _db.Subscriptions
            .Where(s => s.TenantId == tenant.Id && s.Status == SubscriptionStatus.Active && s.EndsAtUtc > now)
            .MaxAsync(s => (DateTime?)s.EndsAtUtc);

        var start = lastActiveEnd ?? now;
        var subscription = new Subscription
        {
            TenantId = tenant.Id,
            PlanId = plan.Id,
            StartsAtUtc = start,
            EndsAtUtc = start.AddMonths(12),
            Status = SubscriptionStatus.Active,
            Amount = transaction.Amount,
            Note = "تمدید آنلاین",
            CreatedAtUtc = now
        };
        subscription.Payments.Add(new SubscriptionPayment
        {
            Amount = transaction.Amount,
            PaidAtUtc = now,
            Method = "online",
            Reference = transaction.Reference
        });
        _db.Subscriptions.Add(subscription);

        tenant.Status = TenantStatus.Active;
    }

    private RedirectResult RedirectToReturn(PaymentTransaction transaction)
    {
        var url = string.IsNullOrEmpty(transaction.ReturnUrl) ? "/" : transaction.ReturnUrl;
        var separator = url.Contains('?') ? "&" : "?";
        var status = transaction.Status == PaymentTransactionStatus.Paid ? "success" : "failed";
        return Redirect($"{url}{separator}payment={status}");
    }
}
