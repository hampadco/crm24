using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>
/// پایه کنترلرهای پنل CRM — فقط با کوکی Identity کاربر CRM.
/// قطع دسترسی خودکار Tenant های منقضی/معلق را نیز اعمال می‌کند.
/// </summary>
[Area("App")]
[Authorize(AuthenticationSchemes = "Identity.Application")]
public abstract class AppControllerBase : Controller
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var tenantContext = services.GetRequiredService<ITenantContext>();

        if (tenantContext.TenantId is int tenantId)
        {
            var cache = services.GetRequiredService<IMemoryCache>();
            var tenant = await cache.GetOrCreateAsync($"tenant-status:{tenantId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                var db = services.GetRequiredService<CrmDbContext>();
                return await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
            });

            if (tenant is null || !TenantLifecycleService.HasAccess(tenant))
            {
                context.Result = RedirectToAction("Expired", "Account", new { area = "App" });
                return;
            }
        }

        await next();
    }
}
