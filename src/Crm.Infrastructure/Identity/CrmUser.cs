using Microsoft.AspNetCore.Identity;

namespace Crm.Infrastructure.Identity;

/// <summary>کاربر پنل CRM (Tenant-scoped).</summary>
public class CrmUser : IdentityUser<int>
{
    public int TenantId { get; set; }
    public string FullName { get; set; } = string.Empty;

    /// <summary>نقش درختی CRM (لایه اول RBAC) — جدا از Identity Roles.</summary>
    public int? CrmRoleId { get; set; }

    /// <summary>پروفایل دسترسی (لایه دوم RBAC).</summary>
    public int? ProfileId { get; set; }

    public bool IsTenantAdmin { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}
