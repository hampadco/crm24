namespace Crm.Core.Entities;

/// <summary>تأمین‌کننده کالا/خدمت.</summary>
public class Vendor : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum PurchaseOrderStatus
{
    Draft = 0,
    Ordered = 1,
    Received = 2,
    Cancelled = 3
}

/// <summary>سفارش خرید از تأمین‌کننده — دریافت کالا انبار را شارژ می‌کند.</summary>
public class PurchaseOrder : TenantEntity
{
    public int Number { get; set; }

    public int VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public DateTime IssueDateUtc { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }

    public decimal Total { get; set; }
    public string? Note { get; set; }

    public ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();
    public ICollection<VendorPayment> Payments { get; set; } = new List<VendorPayment>();
}

public class PurchaseOrderLine : TenantEntity
{
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public string Title { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>پرداخت به تأمین‌کننده روی سفارش خرید.</summary>
public class VendorPayment : TenantEntity
{
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public decimal Amount { get; set; }
    public DateTime PaidAtUtc { get; set; }
    public string? Method { get; set; }
    public string? Reference { get; set; }
}
