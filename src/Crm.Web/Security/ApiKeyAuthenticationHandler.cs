using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Services;

namespace Crm.Web.Security;

/// <summary>احراز هدر X-Api-Key یا Authorization: Bearer.</summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";

    private readonly ApiKeyService _keys;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyService keys)
        : base(options, logger, encoder)
    {
        _keys = keys;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var raw = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            var authorization = Request.Headers.Authorization.ToString();
            const string bearer = "Bearer ";
            if (authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
                raw = authorization[bearer.Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(raw))
            return AuthenticateResult.NoResult();

        var key = await _keys.FindByRawAsync(raw);
        if (key is null)
            return AuthenticateResult.Fail("Invalid API key.");

        if (key.ExpiresAtUtc is DateTime expires && expires < DateTime.UtcNow)
            return AuthenticateResult.Fail("API key expired.");

        if (string.Equals(key.Kind, ApiKeyService.KindPublic, StringComparison.OrdinalIgnoreCase)
            && !ApiKeyService.OriginAllowed(key, Request.Headers.Origin.ToString()))
            return AuthenticateResult.Fail("Origin is not allowed for this public key.");

        _keys.Touch(key);
        await Context.RequestServices.GetRequiredService<Crm.Infrastructure.Data.CrmDbContext>().SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(CrmClaimTypes.TenantId, key.TenantId.ToString()),
            new("crm:api_key_id", key.Id.ToString()),
            new("crm:api_kind", key.Kind),
            new("crm:api_scopes", key.ScopesJson ?? "*")
        };
        if (key.ActAsUserId is int userId)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
