namespace Crm.Core.Entities;

/// <summary>یک ارسال پیامک (تکی یا فله) که در CRM ثبت می‌شود.</summary>
public class SmsDispatch : TenantEntity
{
    public string Provider { get; set; } = "ippanel";
    /// <summary>single، bulk، personal</summary>
    public string Kind { get; set; } = "single";
    public string Message { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public DateTime? ScheduledUtc { get; set; }
    public string OutboxIdsJson { get; set; } = "[]";
    public int RecipientCount { get; set; }
    public decimal? Cost { get; set; }
    /// <summary>queued، sending، sent، delivered، partial، failed، canceled</summary>
    public string Status { get; set; } = "queued";
    public string? Error { get; set; }
    public string? ModuleName { get; set; }

    public ICollection<SmsRecipient> Recipients { get; set; } = new List<SmsRecipient>();
}

public class SmsRecipient : TenantEntity
{
    public int DispatchId { get; set; }
    public SmsDispatch Dispatch { get; set; } = null!;

    public string Phone { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? ModuleName { get; set; }
    public int? RecordId { get; set; }
    public string? Message { get; set; }
    /// <summary>pending، operator، received، delivered، failed، blacklist</summary>
    public string DeliveryStatus { get; set; } = "pending";
}
