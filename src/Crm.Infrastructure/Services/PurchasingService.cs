using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

/// <summary>
/// تأمین و خرید: شماره‌گذاری سفارش خرید، دریافت کالا (شارژ انبار) و پرداخت به تأمین‌کننده.
/// </summary>
public class PurchasingService
{
    public record PoLineInput(int? ProductId, string Title, decimal Quantity, decimal UnitCost);

    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly AuditService _audit;

    public PurchasingService(CrmDbContext db, ITenantContext tenant, AuditService audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<PurchaseOrder> CreateAsync(int vendorId, string? note, IReadOnlyList<PoLineInput> lines)
    {
        if (lines.Count == 0)
            throw new InvalidOperationException("حداقل یک آیتم الزامی است.");

        var maxNumber = await _db.PurchaseOrders.IgnoreQueryFilters()
            .Where(p => p.TenantId == _tenant.TenantId)
            .MaxAsync(p => (int?)p.Number);

        var order = new PurchaseOrder
        {
            Number = (maxNumber ?? 1000) + 1,
            VendorId = vendorId,
            IssueDateUtc = DateTime.UtcNow,
            Note = note?.Trim()
        };
        ApplyLines(order, lines);

        _db.PurchaseOrders.Add(order);
        await _db.SaveChangesAsync();

        _audit.Log("purchase-orders", order.Id, "Create", new { order.Number, order.Total });
        await _db.SaveChangesAsync();
        return order;
    }

    public async Task UpdateAsync(int id, int vendorId, string? note, IReadOnlyList<PoLineInput> lines)
    {
        var order = await _db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("سفارش خرید یافت نشد.");
        if (order.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("فقط سفارش پیش‌نویس قابل ویرایش است.");

        order.VendorId = vendorId;
        order.Note = note?.Trim();
        _db.PurchaseOrderLines.RemoveRange(order.Lines);
        order.Lines.Clear();
        ApplyLines(order, lines);

        await _db.SaveChangesAsync();
    }

    public async Task MarkOrderedAsync(int id)
    {
        var order = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("سفارش خرید یافت نشد.");
        if (order.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("فقط سفارش پیش‌نویس قابل ثبت است.");

        order.Status = PurchaseOrderStatus.Ordered;
        _audit.Log("purchase-orders", order.Id, "Ordered");
        await _db.SaveChangesAsync();
    }

    /// <summary>دریافت کالا: شارژ خودکار انبار برای آیتم‌های دارای محصول.</summary>
    public async Task ReceiveAsync(int id)
    {
        var order = await _db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("سفارش خرید یافت نشد.");
        if (order.Status != PurchaseOrderStatus.Ordered)
            throw new InvalidOperationException("فقط سفارش ثبت‌شده قابل دریافت است.");

        var productIds = order.Lines.Where(l => l.ProductId != null).Select(l => l.ProductId!.Value).ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        foreach (var line in order.Lines)
        {
            if (line.ProductId is int productId &&
                products.TryGetValue(productId, out var product) &&
                product.TrackInventory)
            {
                product.StockQty += line.Quantity;
            }
        }

        order.Status = PurchaseOrderStatus.Received;
        order.ReceivedAtUtc = DateTime.UtcNow;

        _audit.Log("purchase-orders", order.Id, "Received");
        await _db.SaveChangesAsync();
    }

    public async Task AddPaymentAsync(int id, decimal amount, string? method, string? reference)
    {
        if (amount <= 0)
            throw new InvalidOperationException("مبلغ پرداخت معتبر نیست.");

        var order = await _db.PurchaseOrders.Include(p => p.Payments).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("سفارش خرید یافت نشد.");

        var paid = order.Payments.Sum(p => p.Amount);
        if (paid + amount > order.Total)
            throw new InvalidOperationException("مبلغ پرداخت از مانده سفارش بیشتر است.");

        order.Payments.Add(new VendorPayment
        {
            Amount = amount,
            PaidAtUtc = DateTime.UtcNow,
            Method = method?.Trim(),
            Reference = reference?.Trim()
        });

        _audit.Log("purchase-orders", order.Id, "Payment", new { amount });
        await _db.SaveChangesAsync();
    }

    private static void ApplyLines(PurchaseOrder order, IReadOnlyList<PoLineInput> lines)
    {
        var sortOrder = 0;
        foreach (var input in lines)
        {
            var quantity = Math.Max(0, input.Quantity);
            var total = Math.Round(quantity * Math.Max(0, input.UnitCost));
            order.Lines.Add(new PurchaseOrderLine
            {
                ProductId = input.ProductId,
                Title = input.Title.Trim(),
                Quantity = quantity,
                UnitCost = input.UnitCost,
                LineTotal = total,
                SortOrder = ++sortOrder
            });
        }
        order.Total = order.Lines.Sum(l => l.LineTotal);
    }
}
