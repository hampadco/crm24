using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Web.Services;

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
        ViewBag.IsTenantAdmin = _tenant.IsTenantAdmin;

        ViewData["Title"] = "اشتراک و تمدید";
        return View();
    }

    [HttpPost("/App/subscription/company")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCompany(
        string? website,
        string? phone,
        string? address,
        string? national_id,
        string? registration_number,
        string? economic_code)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var tenant = await _db.Tenants.FirstAsync(t => t.Id == _tenant.TenantId!.Value);
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!string.IsNullOrWhiteSpace(tenant.Settings))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(tenant.Settings);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        map[prop.Name] = prop.Value.ValueKind switch
                        {
                            System.Text.Json.JsonValueKind.String => prop.Value.GetString(),
                            System.Text.Json.JsonValueKind.Number => prop.Value.GetDecimal(),
                            System.Text.Json.JsonValueKind.True => true,
                            System.Text.Json.JsonValueKind.False => false,
                            _ => prop.Value.ToString()
                        };
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            map.Clear();
        }

        static string? Clean(string? value)
        {
            value = value?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        map["website"] = Clean(website);
        map["phone"] = Clean(phone);
        map["address"] = Clean(address);
        map["national_id"] = Clean(national_id);
        map["registration_number"] = Clean(registration_number);
        map["economic_code"] = Clean(economic_code);

        tenant.Settings = System.Text.Json.JsonSerializer.Serialize(map);
        await _db.SaveChangesAsync();
        TempData["Success"] = "اطلاعات شرکت برای چاپ ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/subscription/logo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadLogo(IFormFile? logo, [FromServices] MediaUploadService media)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var tenant = await _db.Tenants.FirstAsync(t => t.Id == _tenant.TenantId!.Value);
        var (ok, url, error) = await media.UploadImageAsync(logo, $"uploads/tenants/{tenant.Id}");
        if (!ok)
        {
            TempData["Error"] = error ?? "آپلود لوگو ناموفق بود.";
            return RedirectToAction(nameof(Index));
        }

        tenant.LogoPath = url;
        await _db.SaveChangesAsync();
        TempData["Success"] = "لوگوی شرکت برای چاپ اسناد ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/App/subscription/logo/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLogo()
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var tenant = await _db.Tenants.FirstAsync(t => t.Id == _tenant.TenantId!.Value);
        tenant.LogoPath = null;
        await _db.SaveChangesAsync();
        TempData["Success"] = "لوگو حذف شد.";
        return RedirectToAction(nameof(Index));
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
