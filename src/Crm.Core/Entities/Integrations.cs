namespace Crm.Core.Entities;

/// <summary>کلید API عمومی هر Tenant با Scope خواندن/نوشتن.</summary>
public class ApiKey : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>دیگر پر نمی‌شود. کلید خام فقط یک‌بار به کاربر نشان داده می‌شود.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>SHA-256 هگز کلید خام.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>چند کاراکتر اول برای نمایش در پنل.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>secret یا public.</summary>
    public string Kind { get; set; } = "secret";

    /// <summary><c>*</c> یا JSON مثل {"leads":"rw","products":"r"}.</summary>
    public string ScopesJson { get; set; } = "*";

    /// <summary>Originهای مجاز کلید عمومی، هر خط یکی.</summary>
    public string? AllowedOrigins { get; set; }

    /// <summary>مالک رکوردهایی که با این کلید ساخته می‌شوند.</summary>
    public int? ActAsUserId { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public bool CanRead { get; set; } = true;
    public bool CanWrite { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? LastUsedAtUtc { get; set; }
    public long RequestCount { get; set; }
}

/// <summary>وب‌هوک خروجی: CRM پس از تغییر رکورد به سایت مشتری خبر می‌دهد.</summary>
public class ApiWebhook : TenantEntity
{
    public string Url { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;

    /// <summary>رویدادها با ویرگول: record.created, record.updated, record.deleted</summary>
    public string Events { get; set; } = "record.created,record.updated,record.deleted";

    /// <summary><c>*</c> یا نام ماژول‌ها با ویرگول.</summary>
    public string ModuleNames { get; set; } = "*";

    public bool IsActive { get; set; } = true;
    public int FailCount { get; set; }
    public string? LastStatus { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
}

/// <summary>پاسخ ذخیره‌شدهٔ هدر Idempotency-Key تا ۲۴ ساعت.</summary>
public class ApiIdempotency
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ApiKeyId { get; set; }
    public string Key { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResponseJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
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
