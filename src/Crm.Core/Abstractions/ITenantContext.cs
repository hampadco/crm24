namespace Crm.Core.Abstractions;

/// <summary>
/// زمینه جاری درخواست: Tenant و کاربر. توسط لایه وب (از Claim ها) پر می‌شود
/// و EF برای Global Query Filter و فیلدهای ممیزی از آن می‌خواند.
/// </summary>
public interface ITenantContext
{
    /// <summary>Tenant جاری؛ null یعنی خارج از زمینه Tenant (سایت عمومی/پنل مالک).</summary>
    int? TenantId { get; }

    int? UserId { get; }

    int? RoleId { get; }

    int? ProfileId { get; }

    bool IsTenantAdmin { get; }
}

/// <summary>پیاده‌سازی قابل تنظیم — per-request scope پر می‌شود.</summary>
public class TenantContext : ITenantContext
{
    public int? TenantId { get; set; }
    public int? UserId { get; set; }
    public int? RoleId { get; set; }
    public int? ProfileId { get; set; }
    public bool IsTenantAdmin { get; set; }
}
