namespace Crm.Core.Entities;

/// <summary>کلید API عمومی هر Tenant با Scope خواندن/نوشتن.</summary>
public class ApiKey : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>مقدار کلید — فقط هنگام ساخت نمایش داده می‌شود.</summary>
    public string Key { get; set; } = string.Empty;

    public bool CanRead { get; set; } = true;
    public bool CanWrite { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? LastUsedAtUtc { get; set; }
    public long RequestCount { get; set; }
}

public enum PaymentTransactionKind
{
    Invoice = 0,
    SubscriptionRenewal = 1
}

public enum PaymentTransactionStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2
}

/// <summary>تراکنش درگاه پرداخت — فاکتور مشتری نهایی یا تمدید اشتراک Tenant.</summary>
public class PaymentTransaction : TenantEntity
{
    /// <summary>توکن یکتای صفحه پرداخت.</summary>
    public string Token { get; set; } = string.Empty;

    public PaymentTransactionKind Kind { get; set; }

    /// <summary>شناسه هدف: فاکتور (SalesDocument) یا پلن اشتراک.</summary>
    public int TargetId { get; set; }

    public decimal Amount { get; set; }
    public PaymentTransactionStatus Status { get; set; } = PaymentTransactionStatus.Pending;

    public DateTime? PaidAtUtc { get; set; }
    public string? Reference { get; set; }
    public string? Description { get; set; }

    /// <summary>آدرس بازگشت پس از پرداخت.</summary>
    public string? ReturnUrl { get; set; }
}
