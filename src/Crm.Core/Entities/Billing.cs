namespace Crm.Core.Entities;

/// <summary>پلن تعرفه پلتفرم (معادل «تعرفه و پنل‌ها»). صفحه Pricing سایت از همین داده تغذیه می‌شود.</summary>
public class Plan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }

    public int MaxUsers { get; set; } = 5;
    public int MaxRecords { get; set; } = 10_000;
    public int MaxStorageMb { get; set; } = 1_024;

    /// <summary>لیست امکانات برای نمایش در صفحه تعرفه (هر خط یک مورد).</summary>
    public string Features { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
}

public enum SubscriptionStatus
{
    Active = 0,
    Expired = 1,
    Canceled = 2
}

/// <summary>اشتراک یک Tenant روی یک پلن — دوره، وضعیت و مبلغ.</summary>
public class Subscription
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public int PlanId { get; set; }
    public Plan Plan { get; set; } = null!;

    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<SubscriptionPayment> Payments { get; set; } = new List<SubscriptionPayment>();
}

/// <summary>پرداخت ثبت‌شده روی اشتراک (فعلاً دستی؛ درگاه آنلاین در پلن ۹).</summary>
public class SubscriptionPayment
{
    public int Id { get; set; }
    public int SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = null!;

    public decimal Amount { get; set; }
    public DateTime PaidAtUtc { get; set; }
    public string Method { get; set; } = "manual";
    public string? Reference { get; set; }
    public string? Note { get; set; }
}
