namespace Crm.Core.Entities;

public enum TenantStatus
{
    Trial = 0,
    Active = 1,
    Suspended = 2,
    Expired = 3
}

/// <summary>سازمان مشتری (مستاجر). ریشه تفکیک داده کل پلتفرم.</summary>
public class Tenant
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>شناسه یکتای لاتین برای ساب‌دامین/مسیر.</summary>
    public string Slug { get; set; } = string.Empty;

    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? TrialEndsAtUtc { get; set; }

    /// <summary>تنظیمات پایه (واحد پولی، integrations، ...) به‌صورت jsonb.</summary>
    public string Settings { get; set; } = "{}";

    /// <summary>مسیر نسبی لوگوی شرکت برای سربرگ چاپ (مثلاً /uploads/tenants/1/logo.png).</summary>
    public string? LogoPath { get; set; }
}
