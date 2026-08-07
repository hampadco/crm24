using System.Globalization;
using System.Text.Json;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Services;

/// <summary>
/// موتور سند روی DynamicRecord: شماره‌گذاری، تأیید، تبدیل، کسر انبار، پرداخت و اقساط.
/// </summary>
public class SalesDocumentService
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly MetadataService _metadata;
    private readonly DynamicRecordService _records;
    private readonly LineItemsService _lines;
    private readonly AuditService _audit;

    public SalesDocumentService(
        CrmDbContext db,
        ITenantContext tenant,
        MetadataService metadata,
        DynamicRecordService records,
        LineItemsService lines,
        AuditService audit)
    {
        _db = db;
        _tenant = tenant;
        _metadata = metadata;
        _records = records;
        _lines = lines;
        _audit = audit;
    }

    public async Task AssignNumberIfNeededAsync(ModuleDef module, DynamicRecord record)
    {
        if (module.DocumentKind == DocumentKind.None)
            return;

        var data = DynamicRecordService.ParseData(record);
        if (!string.IsNullOrWhiteSpace(data.GetValueOrDefault("number")))
            return;

        var tracked = await _db.Modules.FirstAsync(m => m.Id == module.Id);
        var next = tracked.NextNumber <= 0 ? 1001 : tracked.NextNumber;
        tracked.NextNumber = next + 1;
        var number = (tracked.NumberPrefix ?? "") + next.ToString(CultureInfo.InvariantCulture);
        data["number"] = number;
        if (string.IsNullOrWhiteSpace(data.GetValueOrDefault("name")))
            data["name"] = number;
        if (string.IsNullOrWhiteSpace(data.GetValueOrDefault("status")))
            data["status"] = "Draft";
        if (string.IsNullOrWhiteSpace(data.GetValueOrDefault("issueDate")))
            data["issueDate"] = DateTime.Today.ToString("yyyy-MM-dd");

        record.CustomData = JsonSerializer.Serialize(data);
        record.Title = data.GetValueOrDefault("name") ?? number;
        await _db.SaveChangesAsync();
    }

    public async Task ConfirmAsync(string moduleName, int recordId)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName)
            ?? throw new InvalidOperationException("Module not found.");
        var record = await _db.Records.FirstOrDefaultAsync(r => r.ModuleId == module.Id && r.Id == recordId)
            ?? throw new InvalidOperationException("Record not found.");

        var data = DynamicRecordService.ParseData(record);
        var status = data.GetValueOrDefault("status") ?? "Draft";
        if (!string.Equals(status, "Draft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("فقط اسناد پیش‌نویس قابل تأیید هستند.");

        await EnsureApprovalsSatisfiedAsync(module, record, data);

        data["status"] = "Confirmed";
        record.CustomData = JsonSerializer.Serialize(data);
        await _db.SaveChangesAsync();

        if (module.DocumentKind == DocumentKind.SalesInvoice)
            await DeductInventoryAsync(module.Id, recordId);

        _audit.Log(module.Name, recordId, "Confirm");
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// اگر قانونی با شرط منطبق باشد و درخواست Approved وجود نداشته باشد،
    /// درخواست Pending می‌سازد و تأیید را مسدود می‌کند.
    /// </summary>
    private async Task EnsureApprovalsSatisfiedAsync(
        ModuleDef module, DynamicRecord record, Dictionary<string, string?> data)
    {
        var rules = await _db.ApprovalRules.AsNoTracking()
            .Where(r => r.ModuleId == module.Id)
            .ToListAsync();
        if (rules.Count == 0)
            return;

        var matching = rules.Where(r => RuleMatches(r, data)).ToList();
        if (matching.Count == 0)
            return;

        var ruleIds = matching.Select(r => r.Id).ToList();
        var existing = await _db.ApprovalRequests
            .Where(a => a.ModuleName == module.Name
                        && a.RecordId == record.Id
                        && ruleIds.Contains(a.RuleId))
            .ToListAsync();

        var needsApproval = false;
        foreach (var rule in matching)
        {
            var forRule = existing.Where(a => a.RuleId == rule.Id).ToList();
            if (forRule.Any(a => a.Status == ApprovalStatus.Approved))
                continue;

            needsApproval = true;
            if (!forRule.Any(a => a.Status == ApprovalStatus.Pending))
            {
                _db.ApprovalRequests.Add(new ApprovalRequest
                {
                    ModuleName = module.Name,
                    RecordId = record.Id,
                    Status = ApprovalStatus.Pending,
                    RequestedByUserId = _tenant.UserId,
                    RuleId = rule.Id
                });
            }
        }

        if (!needsApproval)
            return;

        await _db.SaveChangesAsync();
        throw new InvalidOperationException("نیاز به تأیید دارد");
    }

    private static bool RuleMatches(ApprovalRule rule, Dictionary<string, string?> data)
    {
        if (string.IsNullOrWhiteSpace(rule.ConditionField))
            return true;

        data.TryGetValue(rule.ConditionField, out var raw);
        var value = raw ?? "";
        var expected = rule.ConditionValue ?? "";
        var op = (rule.ConditionOp ?? "eq").Trim().ToLowerInvariant();

        return op switch
        {
            "ne" or "notequals" => !string.Equals(value, expected, StringComparison.OrdinalIgnoreCase),
            "contains" => value.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "isempty" => string.IsNullOrWhiteSpace(value),
            "isnotempty" => !string.IsNullOrWhiteSpace(value),
            _ => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)
        };
    }

    public async Task<DynamicRecord> ConvertAsync(string moduleName, int recordId)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName)
            ?? throw new InvalidOperationException("Module not found.");
        if (string.IsNullOrWhiteSpace(module.ConvertsToModule))
            throw new InvalidOperationException("این ماژول مقصد تبدیل ندارد.");

        var targetModule = await _metadata.GetModuleByNameAsync(module.ConvertsToModule)
            ?? throw new InvalidOperationException("ماژول مقصد یافت نشد.");

        var source = await _db.Records.FirstOrDefaultAsync(r => r.ModuleId == module.Id && r.Id == recordId)
            ?? throw new InvalidOperationException("Record not found.");

        var sourceData = DynamicRecordService.ParseData(source);
        var status = sourceData.GetValueOrDefault("status") ?? "";
        if (string.Equals(status, "Converted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("این سند قابل تبدیل نیست.");

        var targetData = new Dictionary<string, string?>(sourceData, StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = "Confirmed",
            ["sourceRecordId"] = recordId.ToString(),
            ["number"] = null,
            ["name"] = sourceData.GetValueOrDefault("name") ?? source.Title
        };

        var created = await _records.CreateAsync(targetModule.Id, targetData);
        var tracked = await _db.Records.FirstAsync(r => r.Id == created.Id);
        await AssignNumberIfNeededAsync(targetModule, tracked);

        // کپی خطوط
        var (srcBlock, srcLineModule, _) = await _lines.GetLineBlockAsync(module.Id);
        var (dstBlock, dstLineModule, _) = await _lines.GetLineBlockAsync(targetModule.Id);
        if (srcBlock is not null && srcLineModule is not null && dstBlock is not null && dstLineModule is not null)
        {
            var lines = await _lines.LoadLinesAsync(module.Id, recordId);
            var copies = lines.Select(l =>
            {
                var copy = new Dictionary<string, string?>(l, StringComparer.OrdinalIgnoreCase);
                copy.Remove("__id");
                return copy;
            }).ToList();
            await _lines.ReplaceLinesAsync(targetModule.Id, tracked.Id, dstBlock, dstLineModule, copies);
        }

        if (targetModule.DocumentKind == DocumentKind.SalesInvoice)
            await DeductInventoryAsync(targetModule.Id, tracked.Id);

        sourceData["status"] = "Converted";
        source.CustomData = JsonSerializer.Serialize(sourceData);
        _audit.Log(module.Name, recordId, "Convert", new { To = targetModule.Name, NewId = tracked.Id });
        await _db.SaveChangesAsync();

        return tracked;
    }

    public async Task AddPaymentAsync(int invoiceRecordId, decimal amount, string method, string? reference, string? note)
    {
        if (amount <= 0)
            throw new InvalidOperationException("مبلغ پرداخت باید مثبت باشد.");

        var invoiceModule = await _metadata.GetModuleByNameAsync("invoices")
            ?? throw new InvalidOperationException("ماژول فاکتور یافت نشد.");
        var invoice = await _db.Records.FirstOrDefaultAsync(r => r.ModuleId == invoiceModule.Id && r.Id == invoiceRecordId)
            ?? throw new InvalidOperationException("فاکتور یافت نشد.");

        var paymentsModule = await _metadata.GetModuleByNameAsync("payments")
            ?? throw new InvalidOperationException("ماژول پرداخت یافت نشد.");

        var data = new Dictionary<string, string?>
        {
            ["name"] = $"پرداخت {amount:N0}",
            ["invoice"] = invoiceRecordId.ToString(),
            ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
            ["method"] = method,
            ["paidAt"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["reference"] = reference,
            ["description"] = note
        };
        await _records.CreateAsync(paymentsModule.Id, data);

        await RecalcInvoicePaymentStatusAsync(invoiceModule, invoice);
        _audit.Log("invoices", invoiceRecordId, "Payment", new { amount, method });
        await _db.SaveChangesAsync();
    }

    public async Task CreateInstallmentsAsync(int invoiceRecordId, int count, DateTime firstDueDateUtc)
    {
        if (count is < 1 or > 60)
            throw new InvalidOperationException("تعداد اقساط نامعتبر است.");

        var invoiceModule = await _metadata.GetModuleByNameAsync("invoices")
            ?? throw new InvalidOperationException("ماژول فاکتور یافت نشد.");
        var invoice = await _db.Records.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ModuleId == invoiceModule.Id && r.Id == invoiceRecordId)
            ?? throw new InvalidOperationException("فاکتور یافت نشد.");

        var installmentsModule = await _metadata.GetModuleByNameAsync("installments")
            ?? throw new InvalidOperationException("ماژول اقساط یافت نشد.");

        var invoiceData = DynamicRecordService.ParseData(invoice);
        var grand = ParseDec(invoiceData, "grandTotal", ParseDec(invoiceData, "amount", 0));
        if (grand <= 0)
            throw new InvalidOperationException("مبلغ فاکتور برای اقساط کافی نیست.");

        var each = Math.Round(grand / count, 0, MidpointRounding.AwayFromZero);
        var remaining = grand;

        for (var i = 0; i < count; i++)
        {
            var amt = i == count - 1 ? remaining : each;
            remaining -= amt;
            var due = firstDueDateUtc.AddMonths(i).ToString("yyyy-MM-dd");
            await _records.CreateAsync(installmentsModule.Id, new Dictionary<string, string?>
            {
                ["name"] = $"قسط {i + 1} از {count}",
                ["invoice"] = invoiceRecordId.ToString(),
                ["amount"] = amt.ToString(CultureInfo.InvariantCulture),
                ["dueDate"] = due,
                ["isPaid"] = "false"
            });
        }

        _audit.Log("invoices", invoiceRecordId, "Installments", new { count });
        await _db.SaveChangesAsync();
    }

    private async Task RecalcInvoicePaymentStatusAsync(ModuleDef invoiceModule, DynamicRecord invoice)
    {
        var paymentsModule = await _metadata.GetModuleByNameAsync("payments");
        if (paymentsModule is null) return;

        var tenantId = _tenant.TenantId;
        var key = invoice.Id.ToString();
        var paidRows = await _db.Database.SqlQuery<AmountRow>($"""
            SELECT COALESCE(NULLIF(r."CustomData" ->> 'amount', '')::numeric, 0) AS "Amount"
            FROM "Records" r
            WHERE r."ModuleId" = {paymentsModule.Id}
              AND r."TenantId" = {tenantId}
              AND r."IsDeleted" = FALSE
              AND COALESCE(r."CustomData" ->> 'invoice', '') = {key}
            """).ToListAsync();

        var paid = paidRows.Sum(r => r.Amount);
        var data = DynamicRecordService.ParseData(invoice);
        var grand = ParseDec(data, "grandTotal", ParseDec(data, "amount", 0));

        if (paid <= 0)
            data["status"] = "Confirmed";
        else if (paid + 0.01m >= grand)
        {
            data["status"] = "Paid";
            await ComputeCommissionsAsync(invoice.Id, grand);
        }
        else
            data["status"] = "PartiallyPaid";

        invoice.CustomData = JsonSerializer.Serialize(data);
        await _db.SaveChangesAsync();
    }

    private async Task DeductInventoryAsync(int invoiceModuleId, int invoiceRecordId)
    {
        var lines = await _lines.LoadLinesAsync(invoiceModuleId, invoiceRecordId);
        foreach (var line in lines)
        {
            if (!int.TryParse(line.GetValueOrDefault("product"), out var productId) || productId <= 0)
                continue;
            var qty = ParseDec(line, "quantity", 0);
            if (qty <= 0) continue;

            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
            if (product is null || !product.TrackInventory || product.IsService)
                continue;
            product.StockQty -= qty;
        }
        await _db.SaveChangesAsync();
    }

    private async Task ComputeCommissionsAsync(int invoiceRecordId, decimal grandTotal)
    {
        var rules = await _db.CommissionRules.AsNoTracking()
            .Where(r => r.IsActive)
            .ToListAsync();
        if (rules.Count == 0) return;

        var commissionsModule = await _metadata.GetModuleByNameAsync("commissions");
        if (commissionsModule is null) return;

        foreach (var rule in rules)
        {
            if (rule.MinInvoiceAmount > 0 && grandTotal < rule.MinInvoiceAmount)
                continue;
            var amount = rule.FixedAmount + (grandTotal * rule.Percent / 100m);
            if (amount <= 0) continue;

            await _records.CreateAsync(commissionsModule.Id, new Dictionary<string, string?>
            {
                ["name"] = rule.Name,
                ["amount"] = amount.ToString("0.##", CultureInfo.InvariantCulture),
                ["status"] = "pending",
                ["period"] = DateTime.Today.ToString("yyyy-MM"),
                ["description"] = $"فاکتور #{invoiceRecordId}"
            });
        }
    }

    private static decimal ParseDec(Dictionary<string, string?> data, string key, decimal fallback)
    {
        if (!data.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
            return v;
        if (decimal.TryParse(raw, out v))
            return v;
        return fallback;
    }

    private sealed class AmountRow
    {
        public decimal Amount { get; set; }
    }
}
