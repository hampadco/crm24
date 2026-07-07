namespace Crm.Core.Entities;

public enum TicketPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

public enum TicketStatus
{
    Open = 0,
    InProgress = 1,
    WaitingCustomer = 2,
    Resolved = 3,
    Closed = 4
}

/// <summary>تیکت پشتیبانی — از پنل CRM یا پورتال مشتری نهایی ثبت می‌شود.</summary>
public class Ticket : TenantEntity
{
    public int Number { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Category { get; set; }

    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public TicketStatus Status { get; set; } = TicketStatus.Open;

    public int? AssignedUserId { get; set; }
    public int? PortalUserId { get; set; }
    public PortalUser? PortalUser { get; set; }
    public int? ContactRecordId { get; set; }
    public int? ServiceContractId { get; set; }
    public ServiceContract? ServiceContract { get; set; }

    /// <summary>مهلت پاسخ بر اساس SLA اولویت.</summary>
    public DateTime? DueAtUtc { get; set; }
    public DateTime? FirstResponseAtUtc { get; set; }
    public DateTime? EscalatedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public ICollection<TicketMessage> Messages { get; set; } = new List<TicketMessage>();
}

public class TicketMessage : TenantEntity
{
    public int TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;

    public string Body { get; set; } = string.Empty;
    public bool IsFromCustomer { get; set; }
    public string AuthorName { get; set; } = string.Empty;
}

/// <summary>سیاست SLA per-اولویت: مهلت پاسخ به ساعت.</summary>
public class SlaPolicy : TenantEntity
{
    public TicketPriority Priority { get; set; }
    public int ResponseHours { get; set; }
}

/// <summary>قرارداد خدمات: بازه + سقف تیکت.</summary>
public class ServiceContract : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public int? ContactRecordId { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    /// <summary>۰ یعنی نامحدود.</summary>
    public int MaxTickets { get; set; }
    public int TicketsUsed { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>گارانتی / پرونده فروش محصول با سریال.</summary>
public class Warranty : TenantEntity
{
    public string SerialNumber { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public int? ContactRecordId { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string? Notes { get; set; }
}

/// <summary>مقاله پایگاه دانش داخلی — جدا از FAQ سایت عمومی.</summary>
public class KbArticle : TenantEntity
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Category { get; set; }

    /// <summary>نمایش در پورتال مشتریان نهایی.</summary>
    public bool IsPublishedToPortal { get; set; }
}

/// <summary>کاربر پورتال مشتری نهایی هر Tenant.</summary>
public class PortalUser : TenantEntity
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int? ContactRecordId { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum LeaveType
{
    Leave = 0,
    Mission = 1
}

public enum LeaveStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>درخواست مرخصی/مأموریت پرسنل.</summary>
public class LeaveRequest : TenantEntity
{
    public int UserId { get; set; }
    public LeaveType Type { get; set; }
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public string? Reason { get; set; }

    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public int? ReviewedByUserId { get; set; }
    public string? ReviewNote { get; set; }
}
