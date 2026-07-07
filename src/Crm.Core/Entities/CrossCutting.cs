namespace Crm.Core.Entities;

/// <summary>لاگ ممیزی تمام تغییرات رکوردها.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int? UserId { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public int RecordId { get; set; }

    /// <summary>Create / Update / Delete / Restore / Login / Impersonate ...</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>تغییرات به‌صورت jsonb: { field: { old, new } }</summary>
    public string Changes { get; set; } = "{}";

    public DateTime AtUtc { get; set; }
    public string? Ip { get; set; }
}

/// <summary>برچسب قابل اتصال به هر رکورد هر ماژول.</summary>
public class Tag : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
}

public class TagLink : TenantEntity
{
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
    public string ModuleName { get; set; } = string.Empty;
    public int RecordId { get; set; }
}

/// <summary>یادداشت روی هر رکورد.</summary>
public class Note : TenantEntity
{
    public string ModuleName { get; set; } = string.Empty;
    public int RecordId { get; set; }
    public string Body { get; set; } = string.Empty;
}

/// <summary>پیوست فایل روی هر رکورد.</summary>
public class Attachment : TenantEntity
{
    public string ModuleName { get; set; } = string.Empty;
    public int RecordId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

/// <summary>اعلان درون‌سیستمی (همراه SignalR).</summary>
public class Notification : TenantEntity
{
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public bool IsRead { get; set; }
}

/// <summary>فیلتر ذخیره‌شده (لیست سفارشی) per-module.</summary>
public class SavedListView : TenantEntity
{
    public int ModuleId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>ستون‌ها، فیلترها و مرتب‌سازی به‌صورت jsonb.</summary>
    public string Definition { get; set; } = "{}";

    public bool IsShared { get; set; }
}
