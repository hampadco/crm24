namespace Crm.Core.Entities;

/// <summary>محصول یا سرویس قابل فروش.</summary>
public class Product : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string Unit { get; set; } = "عدد";

    public decimal SalePrice { get; set; }
    public decimal TaxPercent { get; set; }

    /// <summary>سرویس‌ها موجودی انبار ندارند.</summary>
    public bool IsService { get; set; }

    public bool TrackInventory { get; set; } = true;
    public decimal StockQty { get; set; }

    /// <summary>نقطه سفارش — کمتر از این مقدار هشدار کمبود.</summary>
    public decimal ReorderPoint { get; set; }

    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
}

/// <summary>دفترچه قیمت — چند قیمت برای هر محصول (همکار/مصرف‌کننده و ...).</summary>
public class PriceBook : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<PriceBookEntry> Entries { get; set; } = new List<PriceBookEntry>();
}

public class PriceBookEntry : TenantEntity
{
    public int PriceBookId { get; set; }
    public PriceBook PriceBook { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal Price { get; set; }
}

public enum SalesDocumentKind
{
    Quote = 0,
    Order = 1,
    Invoice = 2
}

public enum SalesDocumentStatus
{
    Draft = 0,
    Confirmed = 1,
    Converted = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Canceled = 5
}

/// <summary>سند فروش مشترک: پیش‌فاکتور / سفارش فروش / فاکتور.</summary>
public class SalesDocument : TenantEntity
{
    public SalesDocumentKind Kind { get; set; }

    /// <summary>شماره سند per-tenant per-kind.</summary>
    public int Number { get; set; }

    public SalesDocumentStatus Status { get; set; } = SalesDocumentStatus.Draft;

    /// <summary>رکورد مخاطب/سازمان (ماژول‌های metadata-driven).</summary>
    public int? ContactRecordId { get; set; }
    public int? OrganizationRecordId { get; set; }

    /// <summary>نام طرف حساب (denormalized برای چاپ و لیست).</summary>
    public string CustomerName { get; set; } = string.Empty;

    public DateTime IssueDateUtc { get; set; }

    /// <summary>تاریخ اعتبار (پیش‌فاکتور).</summary>
    public DateTime? ValidUntilUtc { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }

    public string? Note { get; set; }

    /// <summary>سند مبدأ در تبدیل (پیش‌فاکتور ← سفارش ← فاکتور).</summary>
    public int? SourceDocumentId { get; set; }

    public ICollection<SalesDocumentLine> Lines { get; set; } = new List<SalesDocumentLine>();
    public ICollection<PaymentRecord> Payments { get; set; } = new List<PaymentRecord>();
    public ICollection<Installment> Installments { get; set; } = new List<Installment>();
}

/// <summary>آیتم خطی سند فروش.</summary>
public class SalesDocumentLine : TenantEntity
{
    public int DocumentId { get; set; }
    public SalesDocument Document { get; set; } = null!;

    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public string Title { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TaxPercent { get; set; }

    /// <summary>جمع خط پس از تخفیف و مالیات.</summary>
    public decimal LineTotal { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>پرداخت ثبت‌شده روی فاکتور.</summary>
public class PaymentRecord : TenantEntity
{
    public int DocumentId { get; set; }
    public SalesDocument Document { get; set; } = null!;

    public decimal Amount { get; set; }
    public DateTime PaidAtUtc { get; set; }
    public string Method { get; set; } = "cash";
    public string? Reference { get; set; }
    public string? Note { get; set; }
}

/// <summary>قسط با سررسید — یادآوری خودکار با Hangfire.</summary>
public class Installment : TenantEntity
{
    public int DocumentId { get; set; }
    public SalesDocument Document { get; set; } = null!;

    public DateTime DueDateUtc { get; set; }
    public decimal Amount { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidAtUtc { get; set; }
}

/// <summary>قانون پورسانت: درصدی (کل فاکتور یا محصول خاص) یا مبلغ ثابت.</summary>
public class CommissionRule : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>null یعنی روی کل فاکتور اعمال می‌شود.</summary>
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public decimal Percent { get; set; }
    public decimal FixedAmount { get; set; }

    /// <summary>حداقل مبلغ فاکتور برای فعال شدن قانون (پلکان فروش).</summary>
    public decimal MinInvoiceAmount { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>پورسانت محاسبه‌شده هر کارشناس per-فاکتور.</summary>
public class CommissionEntry : TenantEntity
{
    public int DocumentId { get; set; }
    public SalesDocument Document { get; set; } = null!;

    public int UserId { get; set; }
    public int RuleId { get; set; }
    public decimal Amount { get; set; }
}
