namespace Crm.Core.Entities;

/// <summary>
/// پایه تمام موجودیت‌های Tenant-scoped: تفکیک داده، حذف نرم و فیلدهای ممیزی از روز اول.
/// </summary>
public abstract class TenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedByUserId { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public int? DeletedByUserId { get; set; }
}

/// <summary>موجودیت رکوردی CRM: مالک دارد و فیلد سفارشی (jsonb) می‌پذیرد.</summary>
public abstract class CrmRecordEntity : TenantEntity
{
    public int? OwnerUserId { get; set; }

    /// <summary>فیلدهای سفارشی به‌صورت jsonb.</summary>
    public string CustomData { get; set; } = "{}";
}
