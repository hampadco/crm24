using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Security;

/// <summary>CORS فقط برای مسیر /api و فقط Originهایی که روی کلید عمومی ثبت شده‌اند.</summary>
public class ApiCorsPolicyProvider : ICorsPolicyProvider
{
    private readonly IServiceScopeFactory _scopes;

    public ApiCorsPolicyProvider(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
            return null;

        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
            return new CorsPolicyBuilder().AllowAnyHeader().AllowAnyMethod().Build();

        if (!await OriginIsAllowedAsync(origin))
            return new CorsPolicyBuilder().Build();

        return new CorsPolicyBuilder()
            .WithOrigins(origin)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10))
            .Build();
    }

    private async Task<bool> OriginIsAllowedAsync(string origin)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var rows = await db.ApiKeys.IgnoreQueryFilters().AsNoTracking()
            .Where(k => k.IsActive && !k.IsDeleted && k.Kind == ApiKeyService.KindPublic)
            .Select(k => k.AllowedOrigins)
            .ToListAsync();

        foreach (var allowed in rows)
        {
            if (string.IsNullOrWhiteSpace(allowed))
                continue;
            foreach (var line in allowed.Split(['\r', '\n', ',', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(line.Trim().TrimEnd('/'), origin.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
