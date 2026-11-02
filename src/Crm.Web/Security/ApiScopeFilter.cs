using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Security;

/// <summary>بعد از احراز کلید، دسترسی ماژول و عملیات را چک می‌کند و سپس زمینه را مدیر همان محدوده می‌کند.</summary>
public class ApiScopeFilter : IAsyncActionFilter
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;

    public ApiScopeFilter(CrmDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!int.TryParse(http.User.FindFirst("crm:api_key_id")?.Value, out var keyId))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "API key is required. Send X-Api-Key or Authorization: Bearer." });
            return;
        }

        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.IsActive);
        if (key is null || (key.ExpiresAtUtc is DateTime expires && expires < DateTime.UtcNow))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "API key is not valid." });
            return;
        }

        var path = http.Request.Path.Value ?? "";
        var write = HttpMethods.IsPost(http.Request.Method)
                    || HttpMethods.IsPut(http.Request.Method)
                    || HttpMethods.IsPatch(http.Request.Method)
                    || HttpMethods.IsDelete(http.Request.Method);
        var module = context.RouteData.Values.TryGetValue("moduleName", out var raw) ? raw?.ToString() : null;

        if (path.Contains("/public/", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(key.Kind, ApiKeyService.KindPublic, StringComparison.OrdinalIgnoreCase))
            {
                context.Result = Denied("A public key is required for this endpoint.");
                return;
            }

            if (string.IsNullOrWhiteSpace(module) || !ApiKeyService.Allows(key, module, write: true))
            {
                context.Result = Denied("This key cannot create records in this module.");
                return;
            }
        }
        else if (path.Contains("/voip/", StringComparison.OrdinalIgnoreCase))
        {
            if (!ApiKeyService.CanWriteSomething(key))
            {
                context.Result = Denied("This key does not have write scope.");
                return;
            }
        }
        else if (!string.IsNullOrWhiteSpace(module) && !ApiKeyService.Allows(key, module, write))
        {
            context.Result = Denied("This key cannot access this module.");
            return;
        }

        if (_tenant is TenantContext mutable)
            mutable.IsTenantAdmin = true;

        await next();
    }

    private static ObjectResult Denied(string error) =>
        new(new { error }) { StatusCode = StatusCodes.Status403Forbidden };
}
