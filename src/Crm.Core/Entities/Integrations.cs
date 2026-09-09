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

/// <summary>لاگ خام رویدادها و درخواست‌های تلفنچی برای عیب‌یابی سناریوهای تماس.</summary>
public class TelefonchyTrace
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public DateTime AtUtc { get; set; }

    /// <summary>webhook / api / call / recording / job</summary>
    public string Kind { get; set; } = "";

    /// <summary>ringing / hangup / cdr / smartcall / upsert / merged ...</summary>
    public string Action { get; set; } = "";

    public string? Direction { get; set; }
    public string? Status { get; set; }
    public string? Cuid { get; set; }
    public string? CallId { get; set; }
    public string? RequestId { get; set; }
    public string? Phone { get; set; }
    public string? Summary { get; set; }
    public string? Path { get; set; }
    public int? HttpStatus { get; set; }
    public int? CrmRecordId { get; set; }

    /// <summary>بدنه خام JSON یا خلاصه پاسخ API.</summary>
    public string? Payload { get; set; }
}
