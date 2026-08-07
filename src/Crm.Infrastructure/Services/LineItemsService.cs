using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Services;

/// <summary>
/// ذخیره و بازیابی سطرهای سند (ماژول فرزند LineItems) و محاسبه جمع‌ها روی والد.
/// </summary>
public class LineItemsService
{
    private static readonly Regex IndexRegex = new(@"^li\[(\d+)\]\[([a-zA-Z0-9_]+)\]$", RegexOptions.Compiled);

    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly MetadataService _metadata;
    private readonly DynamicRecordService _records;

    public LineItemsService(
        CrmDbContext db,
        ITenantContext tenant,
        MetadataService metadata,
        DynamicRecordService records)
    {
        _db = db;
        _tenant = tenant;
        _metadata = metadata;
        _records = records;
    }

    public async Task<(FieldBlock? Block, ModuleDef? LineModule, IReadOnlyList<FieldDef> LineFields)>
        GetLineBlockAsync(int parentModuleId)
    {
        var blocks = await _metadata.GetBlocksAsync(parentModuleId);
        var block = blocks.FirstOrDefault(b => b.Kind == BlockKind.LineItems);
        if (block is null || string.IsNullOrWhiteSpace(block.LineModuleName))
            return (null, null, []);

        var lineModule = await _metadata.GetModuleByNameAsync(block.LineModuleName);
        if (lineModule is null)
            return (block, null, []);

        var fields = (await _metadata.GetFieldsAsync(lineModule.Id))
            .Where(f => f.IsVisible && f.Name != block.LineLinkField)
            .OrderBy(f => f.SortOrder)
            .ToList();
        return (block, lineModule, fields);
    }

    public async Task<IReadOnlyList<Dictionary<string, string?>>> LoadLinesAsync(
        int parentModuleId, int parentRecordId)
    {
        var (block, lineModule, _) = await GetLineBlockAsync(parentModuleId);
        if (block is null || lineModule is null || string.IsNullOrWhiteSpace(block.LineLinkField))
            return [];

        return await LoadLinesAsync(lineModule.Id, block.LineLinkField!, parentRecordId);
    }

    /// <summary>بارگذاری سطرها وقتی بلاک/ماژول خط از قبل resolve شده.</summary>
    public async Task<IReadOnlyList<Dictionary<string, string?>>> LoadLinesAsync(
        int lineModuleId, string linkField, int parentRecordId)
    {
        if (string.IsNullOrWhiteSpace(linkField)
            || linkField.Length > 64
            || !linkField.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            return [];

        var parentKey = parentRecordId.ToString();
        var tenantId = _tenant.TenantId;

        var rows = await _db.Database.SqlQuery<LineDataRow>($"""
            SELECT r."Id" AS "Id", r."CustomData" AS "CustomData"
            FROM "Records" r
            WHERE r."ModuleId" = {lineModuleId}
              AND r."TenantId" = {tenantId}
              AND r."IsDeleted" = FALSE
              AND COALESCE(r."CustomData" ->> {linkField}, '') = {parentKey}
            ORDER BY COALESCE(NULLIF(r."CustomData" ->> 'sortOrder', '')::int, r."Id") ASC
            """).ToListAsync();

        var result = new List<Dictionary<string, string?>>(rows.Count);
        foreach (var row in rows)
        {
            var data = string.IsNullOrWhiteSpace(row.CustomData)
                ? new Dictionary<string, string?>()
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(row.CustomData)
                  ?? new Dictionary<string, string?>();
            data["__id"] = row.Id.ToString();
            result.Add(data);
        }
        return result;
    }

    /// <summary>خواندن سطرها از فرم (li[i][field]) و ذخیره روی والد.</summary>
    public async Task SaveFromFormAsync(int parentModuleId, int parentRecordId, IFormCollection form)
    {
        var (block, lineModule, lineFields) = await GetLineBlockAsync(parentModuleId);
        if (block is null || lineModule is null || string.IsNullOrWhiteSpace(block.LineLinkField))
            return;

        var rows = ParseFormRows(form, lineFields);
        await ReplaceLinesAsync(parentModuleId, parentRecordId, block, lineModule, rows);
    }

    public async Task ReplaceLinesAsync(
        int parentModuleId,
        int parentRecordId,
        FieldBlock block,
        ModuleDef lineModule,
        List<Dictionary<string, string?>> rows)
    {
        var linkField = block.LineLinkField!;
        var parentKey = parentRecordId.ToString();
        var tenantId = _tenant.TenantId
            ?? throw new InvalidOperationException("Tenant required.");

        var existing = await _db.Database.SqlQuery<IdRow>($"""
            SELECT r."Id" AS "Id"
            FROM "Records" r
            WHERE r."ModuleId" = {lineModule.Id}
              AND r."TenantId" = {tenantId}
              AND r."IsDeleted" = FALSE
              AND COALESCE(r."CustomData" ->> {linkField}, '') = {parentKey}
            """).ToListAsync();

        var existingIds = existing.Select(x => x.Id).ToHashSet();
        var keepIds = new HashSet<int>();

        var parentModuleName = await _db.Modules.AsNoTracking()
            .Where(m => m.Id == parentModuleId)
            .Select(m => m.Name)
            .FirstOrDefaultAsync();
        var isPriceBook = string.Equals(parentModuleName, "pricebooks", StringComparison.OrdinalIgnoreCase);

        decimal subTotal = 0, taxTotal = 0;
        var sort = 0;

        foreach (var row in rows)
        {
            row[linkField] = parentKey;
            row["sortOrder"] = (++sort).ToString(CultureInfo.InvariantCulture);

            var qty = ParseDec(row, "quantity", 1);
            var price = Math.Round(ParseDec(row, "unitPrice", 0), MidpointRounding.AwayFromZero);
            var disc = ParseDec(row, "discountPercent", 0);
            var tax = ParseDec(row, "taxPercent", 0);
            if (isPriceBook)
            {
                qty = 1;
                disc = 0;
                tax = 0;
                row["quantity"] = "1";
                row["discountPercent"] = "0";
                row["taxPercent"] = "0";
            }

            row["unitPrice"] = price.ToString("0", CultureInfo.InvariantCulture);

            var net = qty * price * (1 - disc / 100m);
            var lineTax = net * (tax / 100m);
            var lineTotal = Math.Round(net + lineTax, MidpointRounding.AwayFromZero);
            row["lineTotal"] = lineTotal.ToString("0", CultureInfo.InvariantCulture);
            subTotal += net;
            taxTotal += lineTax;

            if (!string.IsNullOrWhiteSpace(row.GetValueOrDefault("title")))
            { /* ok */ }
            else if (!string.IsNullOrWhiteSpace(row.GetValueOrDefault("product")))
                row["title"] = "محصول #" + row["product"];
            else
                row["title"] = "سطر " + sort;

            if (row.TryGetValue("__id", out var idRaw) && int.TryParse(idRaw, out var id) && existingIds.Contains(id))
            {
                var entity = await _db.Records.FirstAsync(r => r.Id == id);
                var data = row.Where(kv => kv.Key != "__id")
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                entity.CustomData = JsonSerializer.Serialize(data);
                entity.Title = row.GetValueOrDefault("title") ?? entity.Title;
                keepIds.Add(id);
            }
            else
            {
                var data = row.Where(kv => kv.Key != "__id")
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                var entity = new DynamicRecord
                {
                    ModuleId = lineModule.Id,
                    Title = row.GetValueOrDefault("title") ?? "سطر",
                    OwnerUserId = _tenant.UserId,
                    CustomData = JsonSerializer.Serialize(data)
                };
                _db.Records.Add(entity);
            }
        }

        foreach (var id in existingIds.Except(keepIds))
        {
            var entity = await _db.Records.FirstOrDefaultAsync(r => r.Id == id);
            if (entity is not null)
                _db.Records.Remove(entity);
        }

        await _db.SaveChangesAsync();

        // نوشتن جمع‌ها روی والد (نه برای دفترچه قیمت)
        var parent = await _db.Records.FirstOrDefaultAsync(r => r.Id == parentRecordId && r.ModuleId == parentModuleId);
        if (parent is null) return;

        if (isPriceBook)
            return;

        var parentData = DynamicRecordService.ParseData(parent);
        var discountPercent = ParseDec(parentData, "discountPercent", 0);
        var discountAmount = subTotal * (discountPercent / 100m);
        var grand = subTotal - discountAmount + taxTotal;

        parentData["subTotal"] = Math.Round(subTotal, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        parentData["discountAmount"] = Math.Round(discountAmount, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        parentData["taxTotal"] = Math.Round(taxTotal, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        parentData["grandTotal"] = Math.Round(grand, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        parentData["amount"] = Math.Round(grand, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

        parent.CustomData = JsonSerializer.Serialize(parentData);
        await _db.SaveChangesAsync();
    }

    public static List<Dictionary<string, string?>> ParseFormRows(
        IFormCollection form, IReadOnlyList<FieldDef> lineFields)
    {
        var byIndex = new SortedDictionary<int, Dictionary<string, string?>>();
        foreach (var key in form.Keys)
        {
            var m = IndexRegex.Match(key);
            if (!m.Success) continue;
            var idx = int.Parse(m.Groups[1].Value);
            var field = m.Groups[2].Value;
            if (!byIndex.TryGetValue(idx, out var row))
            {
                row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                byIndex[idx] = row;
            }
            row[field] = form[key].ToString();
        }

        var fieldNames = lineFields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        fieldNames.Add("__id");
        fieldNames.Add("title");
        fieldNames.Add("product");
        fieldNames.Add("quantity");
        fieldNames.Add("unitPrice");
        fieldNames.Add("discountPercent");
        fieldNames.Add("taxPercent");

        return byIndex.Values
            .Where(r => r.Any(kv => kv.Key != "__id" && !string.IsNullOrWhiteSpace(kv.Value)))
            .Select(r => r.Where(kv => fieldNames.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static decimal ParseDec(Dictionary<string, string?> data, string key, decimal fallback)
    {
        if (!data.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return fallback;
        raw = raw.Replace(",", "").Trim();
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
            return v;
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out v))
            return v;
        return fallback;
    }

    private sealed class IdRow
    {
        public int Id { get; set; }
    }

    private sealed class LineDataRow
    {
        public int Id { get; set; }
        public string CustomData { get; set; } = "{}";
    }
}
