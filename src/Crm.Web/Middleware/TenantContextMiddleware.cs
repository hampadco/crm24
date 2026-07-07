using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Crm.Core.Abstractions;
using Crm.Infrastructure.Identity;

namespace Crm.Web.Middleware;

/// <summary>
/// زمینه Tenant را از کوکی Identity (کاربر CRM) پر می‌کند تا
/// Global Query Filter های EF و سرویس‌های دسترسی، داده درست را ببینند.
/// </summary>
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var result = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (result.Succeeded && result.Principal is not null && tenantContext is TenantContext mutable)
        {
            var principal = result.Principal;

            if (int.TryParse(principal.FindFirst(CrmClaimTypes.TenantId)?.Value, out var tenantId))
                mutable.TenantId = tenantId;

            if (int.TryParse(principal.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value
                    ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId))
                mutable.UserId = userId;

            if (int.TryParse(principal.FindFirst(CrmClaimTypes.ProfileId)?.Value, out var profileId))
                mutable.ProfileId = profileId;

            if (int.TryParse(principal.FindFirst(CrmClaimTypes.CrmRoleId)?.Value, out var roleId))
                mutable.RoleId = roleId;

            mutable.IsTenantAdmin = principal.FindFirst(CrmClaimTypes.IsTenantAdmin)?.Value == "1";

            // در Area های App و هاب اعلان، کاربر CRM به‌عنوان کاربر جاری شناخته شود
            if (context.Request.Path.StartsWithSegments("/App") ||
                context.Request.Path.StartsWithSegments("/hubs"))
                context.User = principal;
        }

        // پورتال مشتریان نهایی: زمینه Tenant از کوکی Portal
        if (context.Request.Path.StartsWithSegments("/Portal") && tenantContext is TenantContext portalMutable)
        {
            var portalResult = await context.AuthenticateAsync("Portal");
            if (portalResult.Succeeded && portalResult.Principal is not null)
            {
                if (int.TryParse(portalResult.Principal.FindFirst(CrmClaimTypes.TenantId)?.Value, out var portalTenantId))
                    portalMutable.TenantId = portalTenantId;

                context.User = portalResult.Principal;
            }
        }

        await _next(context);
    }
}
