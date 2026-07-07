using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

public class LineInput
{
    public int? ProductId { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TaxPercent { get; set; }
}

/// <summary>
/// چرخه مالی: پیش‌فاکتور ← سفارش ← فاکتور، محاسبه خودکار مالیات/تخفیف،
/// کسر انبار، پرداخت و اقساط و محاسبه پورسانت.
/// </summary>
public class FinanceService
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly AuditService _audit;

    public FinanceService(CrmDbContext db, ITenantContext tenant, AuditService audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<SalesDocument> CreateAsync(
        SalesDocumentKind kind, string customerName, int? contactRecordId, int? organizationRecordId,
        decimal discountPercent, string? note, DateTime? validUntilUtc, List<LineInput> lines)
    {
        if (lines.Count == 0)
            throw new InvalidOperationException("سند بدون آیتم قابل ثبت نیست.");

        var document = new SalesDocument
        {
            Kind = kind,
            Number = await NextNumberAsync(kind),
            CustomerName = customerName.Trim(),
            ContactRecordId = contactRecordId,
            OrganizationRecordId = organizationRecordId,
            IssueDateUtc = DateTime.UtcNow,
            ValidUntilUtc = validUntilUtc,
            DiscountPercent = discountPercent,
            Note = note?.Trim()
        };

        BuildLines(document, lines);
        ComputeTotals(document);

        _db.SalesDocuments.Add(document);
        await _db.SaveChangesAsync();

        _audit.Log("salesdocs", document.Id, "Create", new { kind = kind.ToString(), document.Number, document.GrandTotal });
        await _db.SaveChangesAsync();

        return document;
    }

    public async Task UpdateAsync(int id, string customerName, decimal discountPercent, string? note, List<LineInput> lines)
    {
        var document = await GetAsync(id) ?? throw new InvalidOperationException("سند یافت نشد.");
        if (document.Status != SalesDocumentStatus.Draft)
            throw new InvalidOperationException("فقط سند پیش‌نویس قابل ویرایش است.");

        document.CustomerName = customerName.Trim();
        document.DiscountPercent = discountPercent;
        document.Note = note?.Trim();

        _db.SalesDocumentLines.RemoveRange(document.Lines.ToList());
        document.Lines.Clear();
        BuildLines(document, lines);
        ComputeTotals(document);

        _audit.Log("salesdocs", document.Id, "Update");
        await _db.SaveChangesAsync();
    }

    /// <summary>تأیید سند؛ فاکتور تأییدشده موجودی انبار را کم می‌کند.</summary>
    public async Task ConfirmAsync(int id)
    {
        var document = await GetAsync(id) ?? throw new InvalidOperationException("سند یافت نشد.");
        if (document.Status != SalesDocumentStatus.Draft)
            return;

        document.Status = SalesDocumentStatus.Confirmed;

        if (document.Kind == SalesDocumentKind.Invoice)
            await DeductInventoryAsync(document);

        _audit.Log("salesdocs", document.Id, "Confirm");
        await _db.SaveChangesAsync();
    }

    /// <summary>تبدیل یک‌کلیکی: پیش‌فاکتور ← سفارش فروش ← فاکتور.</summary>
    public async Task<SalesDocument> ConvertAsync(int id)
    {
        var source = await GetAsync(id) ?? throw new InvalidOperationException("سند یافت نشد.");
        if (source.Kind == SalesDocumentKind.Invoice)
            throw new InvalidOperationException("فاکتور به سند دیگری تبدیل نمی‌شود.");
        if (source.Status == SalesDocumentStatus.Canceled)
            throw new InvalidOperationException("سند لغوشده قابل تبدیل نیست.");

        var targetKind = source.Kind == SalesDocumentKind.Quote ? SalesDocumentKind.Order : SalesDocumentKind.Invoice;

        var target = new SalesDocument
        {
            Kind = targetKind,
            Number = await NextNumberAsync(targetKind),
            CustomerName = source.CustomerName,
            ContactRecordId = source.ContactRecordId,
            OrganizationRecordId = source.OrganizationRecordId,
            IssueDateUtc = DateTime.UtcNow,
            DiscountPercent = source.DiscountPercent,
            Note = source.Note,
            SourceDocumentId = source.Id,
            Status = SalesDocumentStatus.Confirmed
        };

        foreach (var line in source.Lines.OrderBy(l => l.SortOrder))
        {
            target.Lines.Add(new SalesDocumentLine
            {
                ProductId = line.ProductId,
                Title = line.Title,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                TaxPercent = line.TaxPercent,
                LineTotal = line.LineTotal,
                SortOrder = line.SortOrder
            });
        }

        ComputeTotals(target);
        source.Status = SalesDocumentStatus.Converted;

        _db.SalesDocuments.Add(target);

        if (targetKind == SalesDocumentKind.Invoice)
            await DeductInventoryAsync(target);

        await _db.SaveChangesAsync();

        _audit.Log("salesdocs", source.Id, "Convert", new { to = targetKind.ToString(), targetNumber = target.Number });
        await _db.SaveChangesAsync();

        return target;
    }

    /// <summary>ثبت پرداخت روی فاکتور؛ با تسویه کامل پورسانت محاسبه می‌شود.</summary>
    public async Task AddPaymentAsync(int documentId, decimal amount, string method, string? reference, string? note)
    {
        var document = await GetAsync(documentId) ?? throw new InvalidOperationException("سند یافت نشد.");
        if (document.Kind != SalesDocumentKind.Invoice)
            throw new InvalidOperationException("پرداخت فقط روی فاکتور ثبت می‌شود.");

        document.Payments.Add(new PaymentRecord
        {
            Amount = amount,
            PaidAtUtc = DateTime.UtcNow,
            Method = method,
            Reference = reference?.Trim(),
            Note = note?.Trim()
        });

        var totalPaid = document.Payments.Sum(p => p.Amount);

        document.Status = totalPaid >= document.GrandTotal
            ? SalesDocumentStatus.Paid
            : SalesDocumentStatus.PartiallyPaid;

        if (document.Status == SalesDocumentStatus.Paid)
            await ComputeCommissionsAsync(document);

        _audit.Log("salesdocs", document.Id, "Payment", new { amount, method });
        await _db.SaveChangesAsync();
    }

    /// <summary>تقسیط مانده فاکتور به اقساط مساوی ماهانه.</summary>
    public async Task CreateInstallmentsAsync(int documentId, int count, DateTime firstDueDateUtc)
    {
        var document = await GetAsync(documentId) ?? throw new InvalidOperationException("سند یافت نشد.");
        if (document.Kind != SalesDocumentKind.Invoice)
            throw new InvalidOperationException("اقساط فقط روی فاکتور تعریف می‌شود.");
        if (count < 2 || count > 60)
            throw new InvalidOperationException("تعداد اقساط باید بین ۲ و ۶۰ باشد.");

        var remaining = document.GrandTotal - document.Payments.Sum(p => p.Amount);
        if (remaining <= 0)
            throw new InvalidOperationException("مانده‌ای برای تقسیط وجود ندارد.");

        _db.Installments.RemoveRange(document.Installments.Where(i => !i.IsPaid).ToList());

        var per = Math.Round(remaining / count, 0);
        for (var i = 0; i < count; i++)
        {
            document.Installments.Add(new Installment
            {
                DueDateUtc = firstDueDateUtc.AddMonths(i),
                Amount = i == count - 1 ? remaining - per * (count - 1) : per
            });
        }

        _audit.Log("salesdocs", document.Id, "Installments", new { count, remaining });
        await _db.SaveChangesAsync();
    }

    /// <summary>پرداخت قسط — پرداخت متناظر هم ثبت می‌شود.</summary>
    public async Task PayInstallmentAsync(int installmentId)
    {
        var installment = await _db.Installments
            .Include(i => i.Document).ThenInclude(d => d.Payments)
            .FirstOrDefaultAsync(i => i.Id == installmentId)
            ?? throw new InvalidOperationException("قسط یافت نشد.");

        if (installment.IsPaid)
            return;

        installment.IsPaid = true;
        installment.PaidAtUtc = DateTime.UtcNow;

        await AddPaymentAsync(installment.DocumentId, installment.Amount, "installment", $"قسط #{installment.Id}", null);
    }

    /// <summary>جاب روزانه: اعلان برای اقساط سررسیدشده پرداخت‌نشده.</summary>
    public async Task RemindDueInstallmentsAsync()
    {
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(3);

        var due = await _db.Installments
            .IgnoreQueryFilters()
            .Include(i => i.Document)
            .Where(i => !i.IsPaid && !i.IsDeleted && i.DueDateUtc <= horizon)
            .ToListAsync();

        foreach (var installment in due)
        {
            var userId = installment.Document.CreatedByUserId;
            if (userId is null)
                continue;

            var exists = await _db.Notifications
                .IgnoreQueryFilters()
                .AnyAsync(n => n.TenantId == installment.TenantId &&
                               n.LinkUrl == $"/App/finance/invoice/{installment.DocumentId}" &&
                               n.CreatedAtUtc > now.AddDays(-1));
            if (exists)
                continue;

            _db.Notifications.Add(new Notification
            {
                TenantId = installment.TenantId,
                UserId = userId.Value,
                Title = "سررسید قسط",
                Body = $"قسط {installment.Amount:N0} تومانی فاکتور {installment.Document.Number} در تاریخ {installment.DueDateUtc:yyyy/MM/dd} سررسید می‌شود.",
                LinkUrl = $"/App/finance/invoice/{installment.DocumentId}"
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task<SalesDocument?> GetAsync(int id) =>
        await _db.SalesDocuments
            .Include(d => d.Lines.OrderBy(l => l.SortOrder))
            .Include(d => d.Payments)
            .Include(d => d.Installments)
            .FirstOrDefaultAsync(d => d.Id == id);

    private async Task<int> NextNumberAsync(SalesDocumentKind kind)
    {
        var max = await _db.SalesDocuments
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == _tenant.TenantId && d.Kind == kind)
            .MaxAsync(d => (int?)d.Number);
        return (max ?? 1000) + 1;
    }

    private static void BuildLines(SalesDocument document, List<LineInput> lines)
    {
        var order = 0;
        foreach (var input in lines.Where(l => !string.IsNullOrWhiteSpace(l.Title) && l.Quantity > 0))
        {
            var gross = input.Quantity * input.UnitPrice;
            var afterDiscount = gross * (1 - input.DiscountPercent / 100m);
            var withTax = afterDiscount * (1 + input.TaxPercent / 100m);

            document.Lines.Add(new SalesDocumentLine
            {
                ProductId = input.ProductId,
                Title = input.Title.Trim(),
                Quantity = input.Quantity,
                UnitPrice = input.UnitPrice,
                DiscountPercent = input.DiscountPercent,
                TaxPercent = input.TaxPercent,
                LineTotal = Math.Round(withTax, 0),
                SortOrder = ++order
            });
        }

        if (document.Lines.Count == 0)
            throw new InvalidOperationException("سند بدون آیتم معتبر قابل ثبت نیست.");
    }

    private static void ComputeTotals(SalesDocument document)
    {
        document.SubTotal = document.Lines.Sum(l =>
            Math.Round(l.Quantity * l.UnitPrice * (1 - l.DiscountPercent / 100m), 0));
        document.TaxTotal = document.Lines.Sum(l =>
            Math.Round(l.Quantity * l.UnitPrice * (1 - l.DiscountPercent / 100m) * l.TaxPercent / 100m, 0));
        document.DiscountAmount = Math.Round((document.SubTotal + document.TaxTotal) * document.DiscountPercent / 100m, 0);
        document.GrandTotal = document.SubTotal + document.TaxTotal - document.DiscountAmount;
    }

    private async Task DeductInventoryAsync(SalesDocument document)
    {
        var productIds = document.Lines.Where(l => l.ProductId is not null).Select(l => l.ProductId!.Value).ToList();
        if (productIds.Count == 0)
            return;

        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        foreach (var line in document.Lines)
        {
            if (line.ProductId is int pid && products.TryGetValue(pid, out var product) &&
                product.TrackInventory && !product.IsService)
            {
                product.StockQty -= line.Quantity;
            }
        }
    }

    /// <summary>محاسبه پورسانت بعد از تسویه فاکتور بر اساس قوانین فعال.</summary>
    private async Task ComputeCommissionsAsync(SalesDocument document)
    {
        var userId = document.CreatedByUserId ?? _tenant.UserId;
        if (userId is null)
            return;

        var alreadyComputed = await _db.CommissionEntries.AnyAsync(c => c.DocumentId == document.Id);
        if (alreadyComputed)
            return;

        var rules = await _db.CommissionRules.AsNoTracking().Where(r => r.IsActive).ToListAsync();
        foreach (var rule in rules)
        {
            if (document.GrandTotal < rule.MinInvoiceAmount)
                continue;

            decimal amount;
            if (rule.ProductId is int productId)
            {
                var productLines = document.Lines.Where(l => l.ProductId == productId).Sum(l => l.LineTotal);
                if (productLines <= 0)
                    continue;
                amount = Math.Round(productLines * rule.Percent / 100m, 0) + rule.FixedAmount;
            }
            else
            {
                amount = Math.Round(document.GrandTotal * rule.Percent / 100m, 0) + rule.FixedAmount;
            }

            if (amount <= 0)
                continue;

            _db.CommissionEntries.Add(new CommissionEntry
            {
                DocumentId = document.Id,
                UserId = userId.Value,
                RuleId = rule.Id,
                Amount = amount
            });
        }
    }
}