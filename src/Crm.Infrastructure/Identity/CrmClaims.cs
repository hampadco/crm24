using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Crm.Infrastructure.Identity;

public static class CrmClaimTypes
{
    public const string TenantId = "crm:tenant_id";
    public const string ProfileId = "crm:profile_id";
    public const string CrmRoleId = "crm:role_id";
    public const string IsTenantAdmin = "crm:is_tenant_admin";
    public const string FullName = "crm:full_name";

    /// <summary>نام ادمین مالک هنگام Impersonation — برای نمایش بنر و Audit.</summary>
    public const string ImpersonatedBy = "crm:impersonated_by";
}

/// <summary>Claim های Tenant و RBAC را به کوکی کاربر CRM اضافه می‌کند.</summary>
public class CrmUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<CrmUser>
{
    public CrmUserClaimsPrincipalFactory(UserManager<CrmUser> userManager, IOptions<IdentityOptions> options)
        : base(userManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(CrmUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(CrmClaimTypes.TenantId, user.TenantId.ToString()));
        identity.AddClaim(new Claim(CrmClaimTypes.FullName, user.FullName));
        identity.AddClaim(new Claim(CrmClaimTypes.IsTenantAdmin, user.IsTenantAdmin ? "1" : "0"));

        if (user.ProfileId is int profileId)
            identity.AddClaim(new Claim(CrmClaimTypes.ProfileId, profileId.ToString()));

        if (user.CrmRoleId is int roleId)
            identity.AddClaim(new Claim(CrmClaimTypes.CrmRoleId, roleId.ToString()));

        return identity;
    }
}
