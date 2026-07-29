using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

/// <summary>دسترسی به متادیتای ماژول‌ها و فیلدها (با کش per-tenant).</summary>
public class MetadataService
{
    private static readonly Regex SystemNameRegex = new(@"^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IMemoryCache _cache;

    public MetadataService(CrmDbContext db, ITenantContext tenant, IMemoryCache cache)
    {
        _db = db;
        _tenant = tenant;
        _cache = cache;
    }

    public async Task<IReadOnlyList<ModuleDef>> GetActiveModulesAsync()
    {
        var cacheKey = ModulesCacheKey();
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<ModuleDef>? cached) && cached is not null)
            return cached;

        var modules = await _db.Modules
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .ToListAsync();

        _cache.Set(cacheKey, (IReadOnlyList<ModuleDef>)modules, TimeSpan.FromMinutes(5));
        return modules;
    }

    public async Task<ModuleDef?> GetModuleByNameAsync(string name)
    {
        var modules = await GetActiveModulesAsync();
        return modules.FirstOrDefault(m => m.Name == name);
    }

    public async Task<IReadOnlyList<FieldDef>> GetFieldsAsync(int moduleId)
    {
        var cacheKey = FieldsCacheKey(moduleId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<FieldDef>? cached) && cached is not null)
            return cached;

        var fields = await _db.Fields
            .AsNoTracking()
            .Include(f => f.PicklistValues.Where(p => p.IsActive && !p.IsDeleted))
            .Where(f => f.ModuleId == moduleId)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .ToListAsync();

        _cache.Set(cacheKey, (IReadOnlyList<FieldDef>)fields, TimeSpan.FromMinutes(5));
        return fields;
    }

    public async Task<IReadOnlyList<FieldBlock>> GetBlocksAsync(int moduleId)
    {
        var cacheKey = BlocksCacheKey(moduleId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<FieldBlock>? cached) && cached is not null)
            return cached;

        var blocks = await _db.FieldBlocks
            .AsNoTracking()
            .Where(b => b.ModuleId == moduleId)
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.Id)
            .ToListAsync();

        _cache.Set(cacheKey, (IReadOnlyList<FieldBlock>)blocks, TimeSpan.FromMinutes(5));
        return blocks;
    }

    public async Task<FieldBlock> CreateBlockAsync(int moduleId, string name, string label, int sortOrder)
    {
        var module = await _db.Modules.FirstOrDefaultAsync(m => m.Id == moduleId)
            ?? throw new InvalidOperationException("ماژول یافت نشد.");

        name = NormalizeSystemName(name);
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("نام سیستمی بلاک معتبر نیست.");

        if (await _db.FieldBlocks.AnyAsync(b => b.ModuleId == moduleId && b.Name == name))
            throw new InvalidOperationException("بلاکی با این نام سیستمی از قبل وجود دارد.");

        var block = new FieldBlock
        {
            ModuleId = module.Id,
            Name = name,
            Label = string.IsNullOrWhiteSpace(label) ? name : label.Trim(),
            SortOrder = sortOrder
        };
        _db.FieldBlocks.Add(block);
        await _db.SaveChangesAsync();
        InvalidateFieldCache(moduleId);
        return block;
    }

    public async Task UpdateBlockAsync(
        int id, string label, int sortOrder, bool isCollapsed, string? visibilityRuleJson = null)
    {
        var block = await _db.FieldBlocks.FirstOrDefaultAsync(b => b.Id == id)
            ?? throw new InvalidOperationException("بلاک یافت نشد.");

        block.Label = string.IsNullOrWhiteSpace(label) ? block.Label : label.Trim();
        block.SortOrder = sortOrder;
        block.IsCollapsed = isCollapsed;
        if (visibilityRuleJson is not null)
            block.VisibilityRuleJson = string.IsNullOrWhiteSpace(visibilityRuleJson) ? null : visibilityRuleJson.Trim();
        await _db.SaveChangesAsync();
        InvalidateFieldCache(block.ModuleId);
    }

    public async Task DeleteBlockAsync(int id)
    {
        var block = await _db.FieldBlocks.FirstOrDefaultAsync(b => b.Id == id)
            ?? throw new InvalidOperationException("بلاک یافت نشد.");

        var moduleId = block.ModuleId;
        var fields = await _db.Fields.Where(f => f.BlockId == id).ToListAsync();
        foreach (var field in fields)
            field.BlockId = null;

        _db.FieldBlocks.Remove(block);
        await _db.SaveChangesAsync();
        InvalidateFieldCache(moduleId);
    }

    public async Task<FieldDef> CreateFieldAsync(
        int moduleId,
        string name,
        string label,
        FieldType type,
        bool isRequired = false,
        bool showInList = true,
        int? blockId = null,
        int? maxLength = null,
        bool isVisible = true,
        bool isUniqueCheck = false,
        string? defaultValue = null,
        string? visibilityRuleJson = null,
        string? lookupModule = null,
        int? integerDigits = null,
        int? decimalDigits = null,
        string? formulaExpression = null,
        string? validationRulesJson = null,
        IReadOnlyList<(string Value, string Label)>? picklistOptions = null)
    {
        var module = await _db.Modules.FirstOrDefaultAsync(m => m.Id == moduleId)
            ?? throw new InvalidOperationException("ماژول یافت نشد.");

        name = NormalizeSystemName(name);
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("نام سیستمی فیلد معتبر نیست.");

        if (await _db.Fields.AnyAsync(f => f.ModuleId == moduleId && f.Name == name))
            throw new InvalidOperationException("فیلدی با این نام سیستمی از قبل وجود دارد.");

        if (blockId is int bid)
        {
            var blockOk = await _db.FieldBlocks.AnyAsync(b => b.Id == bid && b.ModuleId == moduleId);
            if (!blockOk)
                throw new InvalidOperationException("بلاک انتخاب‌شده معتبر نیست.");
        }

        if (type == FieldType.Picklist && (picklistOptions is null || picklistOptions.Count == 0))
            throw new InvalidOperationException("برای فیلد انتخابی حداقل یک گزینه لازم است.");

        var maxSort = await _db.Fields.Where(f => f.ModuleId == moduleId).MaxAsync(f => (int?)f.SortOrder) ?? 0;
        var field = new FieldDef
        {
            ModuleId = module.Id,
            Name = name,
            Label = string.IsNullOrWhiteSpace(label) ? name : label.Trim(),
            Type = type,
            IsCustom = true,
            IsRequired = isRequired,
            ShowInList = showInList,
            BlockId = blockId,
            MaxLength = maxLength,
            IntegerDigits = integerDigits,
            DecimalDigits = decimalDigits,
            IsVisible = isVisible,
            IsUniqueCheck = isUniqueCheck,
            DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue.Trim(),
            VisibilityRuleJson = string.IsNullOrWhiteSpace(visibilityRuleJson) ? null : visibilityRuleJson.Trim(),
            FormulaExpression = string.IsNullOrWhiteSpace(formulaExpression) ? null : formulaExpression.Trim(),
            ValidationRulesJson = string.IsNullOrWhiteSpace(validationRulesJson) ? null : validationRulesJson.Trim(),
            LookupModule = lookupModule,
            SortOrder = maxSort + 1
        };
        _db.Fields.Add(field);
        await _db.SaveChangesAsync();

        if ((type is FieldType.Picklist or FieldType.MultiPicklist) && picklistOptions is { Count: > 0 })
        {
            var order = 0;
            foreach (var (value, optLabel) in picklistOptions)
            {
                var v = string.IsNullOrWhiteSpace(value) ? NormalizeSystemName(optLabel) : value.Trim();
                if (string.IsNullOrWhiteSpace(v))
                    v = "opt_" + (++order);
                _db.PicklistValues.Add(new PicklistValue
                {
                    FieldId = field.Id,
                    Value = v,
                    Label = string.IsNullOrWhiteSpace(optLabel) ? v : optLabel.Trim(),
                    SortOrder = ++order,
                    IsActive = true
                });
            }
            await _db.SaveChangesAsync();
        }

        InvalidateFieldCache(moduleId);
        return field;
    }

    public async Task UpdateFieldAsync(
        int id,
        string label,
        bool isRequired,
        bool showInList,
        int sortOrder,
        int? blockId,
        int? maxLength,
        bool isVisible,
        bool isUniqueCheck,
        string? defaultValue,
        string? visibilityRuleJson,
        int? integerDigits = null,
        int? decimalDigits = null,
        string? formulaExpression = null,
        string? validationRulesJson = null,
        IReadOnlyList<(string Value, string Label)>? picklistOptions = null)
    {
        var field = await _db.Fields.FirstOrDefaultAsync(f => f.Id == id)
            ?? throw new InvalidOperationException("فیلد یافت نشد.");

        if (blockId is int bid)
        {
            var blockOk = await _db.FieldBlocks.AnyAsync(b => b.Id == bid && b.ModuleId == field.ModuleId);
            if (!blockOk)
                throw new InvalidOperationException("بلاک انتخاب‌شده معتبر نیست.");
        }

        field.Label = string.IsNullOrWhiteSpace(label) ? field.Label : label.Trim();
        field.IsRequired = isRequired;
        field.ShowInList = showInList;
        field.SortOrder = sortOrder;
        field.BlockId = blockId;
        field.MaxLength = maxLength;
        field.IntegerDigits = integerDigits;
        field.DecimalDigits = decimalDigits;
        field.IsVisible = isVisible;
        field.IsUniqueCheck = isUniqueCheck;
        field.DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue.Trim();
        field.VisibilityRuleJson = string.IsNullOrWhiteSpace(visibilityRuleJson) ? null : visibilityRuleJson.Trim();
        field.FormulaExpression = string.IsNullOrWhiteSpace(formulaExpression) ? null : formulaExpression.Trim();
        field.ValidationRulesJson = string.IsNullOrWhiteSpace(validationRulesJson) ? null : validationRulesJson.Trim();

        if (picklistOptions is not null && field.Type is FieldType.Picklist or FieldType.MultiPicklist)
        {
            var existing = await _db.PicklistValues.Where(p => p.FieldId == field.Id).ToListAsync();
            _db.PicklistValues.RemoveRange(existing);
            var order = 0;
            foreach (var (value, optLabel) in picklistOptions)
            {
                var v = string.IsNullOrWhiteSpace(value) ? NormalizeSystemName(optLabel) : value.Trim();
                if (string.IsNullOrWhiteSpace(v))
                    v = "opt_" + (++order);
                _db.PicklistValues.Add(new PicklistValue
                {
                    FieldId = field.Id,
                    Value = v,
                    Label = string.IsNullOrWhiteSpace(optLabel) ? v : optLabel.Trim(),
                    SortOrder = ++order,
                    IsActive = true
                });
            }
        }

        await _db.SaveChangesAsync();
        InvalidateFieldCache(field.ModuleId);
    }

    /// <summary>
    /// فیلدهای بدون بلاک را داخل بلاک پیش‌فرض («اطلاعات اولیه» / main) قرار می‌دهد.
    /// اگر بلاک وجود نداشت می‌سازد؛ اگر «main» یا اولین بلاک موجود بود همان را استفاده می‌کند.
    /// </summary>
    public async Task EnsureUngroupedFieldsInDefaultBlockAsync(int moduleId)
    {
        var ungrouped = await _db.Fields
            .Where(f => f.ModuleId == moduleId && f.BlockId == null)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
            .ToListAsync();
        if (ungrouped.Count == 0)
            return;

        var block = await _db.FieldBlocks
            .Where(b => b.ModuleId == moduleId)
            .OrderBy(b => b.Name == "main" ? 0 : 1)
            .ThenBy(b => b.SortOrder)
            .ThenBy(b => b.Id)
            .FirstOrDefaultAsync();

        if (block is null)
        {
            block = new FieldBlock
            {
                ModuleId = moduleId,
                Name = "main",
                Label = "اطلاعات اولیه",
                SortOrder = 1
            };
            _db.FieldBlocks.Add(block);
            await _db.SaveChangesAsync();
        }

        var order = await _db.Fields
            .Where(f => f.ModuleId == moduleId && f.BlockId == block.Id)
            .MaxAsync(f => (int?)f.SortOrder) ?? 0;

        foreach (var field in ungrouped)
        {
            field.BlockId = block.Id;
            field.SortOrder = ++order;
        }

        await _db.SaveChangesAsync();
        InvalidateFieldCache(moduleId);
    }

    /// <summary>
    /// ذخیره ترتیب بلاک‌ها و فیلدها.
    /// هر آیتم: شناسه بلاک (null = بدون بلاک) و لیست شناسه فیلدها به ترتیب.
    /// </summary>
    public async Task ReorderLayoutAsync(int moduleId, List<(int? BlockId, List<int> FieldIds)> layout)
    {
        var moduleExists = await _db.Modules.AnyAsync(m => m.Id == moduleId);
        if (!moduleExists)
            throw new InvalidOperationException("ماژول یافت نشد.");

        var blocks = await _db.FieldBlocks.Where(b => b.ModuleId == moduleId).ToListAsync();
        var fields = await _db.Fields.Where(f => f.ModuleId == moduleId).ToListAsync();
        var fieldMap = fields.ToDictionary(f => f.Id);
        var blockMap = blocks.ToDictionary(b => b.Id);

        var blockOrder = 0;
        foreach (var (blockId, fieldIds) in layout)
        {
            if (blockId is int bid && blockMap.TryGetValue(bid, out var block))
            {
                block.SortOrder = ++blockOrder;
            }

            var fieldOrder = 0;
            foreach (var fieldId in fieldIds)
            {
                if (!fieldMap.TryGetValue(fieldId, out var field))
                    continue;
                field.BlockId = blockId;
                field.SortOrder = ++fieldOrder;
            }
        }

        await _db.SaveChangesAsync();
        InvalidateFieldCache(moduleId);
    }

    public async Task<IReadOnlyList<RelationDef>> GetRelationsForModuleAsync(int moduleId)
    {
        return await _db.Relations
            .AsNoTracking()
            .Where(r => r.SourceModuleId == moduleId || r.TargetModuleId == moduleId)
            .OrderBy(r => r.Id)
            .ToListAsync();
    }

    public async Task<RelationDef> CreateRelationAsync(
        int sourceModuleId,
        int targetModuleId,
        string label,
        RelationKind kind,
        string? relatedFieldLabel = null,
        string? linkFieldName = null)
    {
        var source = await _db.Modules.FirstOrDefaultAsync(m => m.Id == sourceModuleId)
            ?? throw new InvalidOperationException("ماژول مبدأ یافت نشد.");
        var target = await _db.Modules.FirstOrDefaultAsync(m => m.Id == targetModuleId)
            ?? throw new InvalidOperationException("ماژول مقصد یافت نشد.");

        if (sourceModuleId == targetModuleId)
            throw new InvalidOperationException("مبدأ و مقصد نمی‌توانند یک ماژول باشند.");

        if (string.IsNullOrWhiteSpace(label))
            throw new InvalidOperationException("نام زبانه الزامی است.");

        var tabLabel = label.Trim();
        var fieldLabel = string.IsNullOrWhiteSpace(relatedFieldLabel)
            ? source.SingularLabel
            : relatedFieldLabel.Trim();

        // سمتی که Lookup روی آن قرار می‌گیرد (سمت «چند» یا ManyToOne روی مبدأ)
        int lookupModuleId;
        int lookupTargetModuleId;
        switch (kind)
        {
            case RelationKind.OneToMany:
                // Source یک → Target چند : Lookup روی Target به Source
                lookupModuleId = targetModuleId;
                lookupTargetModuleId = sourceModuleId;
                break;
            case RelationKind.ManyToOne:
            case RelationKind.OneToOne:
                // Source چند → Target یک : Lookup روی Source به Target
                lookupModuleId = sourceModuleId;
                lookupTargetModuleId = targetModuleId;
                break;
            case RelationKind.ManyToMany:
                lookupModuleId = 0;
                lookupTargetModuleId = 0;
                break;
            default:
                throw new InvalidOperationException("نوع رابطه نامعتبر است.");
        }

        string? link = string.IsNullOrWhiteSpace(linkFieldName) ? null : NormalizeSystemName(linkFieldName);
        if (kind != RelationKind.ManyToMany)
        {
            var lookupTargetName = await _db.Modules.Where(m => m.Id == lookupTargetModuleId)
                .Select(m => m.Name).FirstAsync();

            if (link is null)
            {
                // نام سیستمی از ماژول مقصد Lookup
                link = NormalizeSystemName(lookupTargetName);
                if (string.IsNullOrWhiteSpace(link))
                    link = "related_" + Guid.NewGuid().ToString("N")[..6];
            }

            var existing = await _db.Fields.FirstOrDefaultAsync(f =>
                f.ModuleId == lookupModuleId && f.Name == link);

            if (existing is null)
            {
                var maxSort = await _db.Fields.Where(f => f.ModuleId == lookupModuleId)
                    .MaxAsync(f => (int?)f.SortOrder) ?? 0;
                var blockId = await _db.FieldBlocks
                    .Where(b => b.ModuleId == lookupModuleId)
                    .OrderBy(b => b.SortOrder)
                    .Select(b => (int?)b.Id)
                    .FirstOrDefaultAsync();

                _db.Fields.Add(new FieldDef
                {
                    ModuleId = lookupModuleId,
                    BlockId = blockId,
                    Name = link,
                    Label = fieldLabel,
                    Type = FieldType.Lookup,
                    LookupModule = lookupTargetName,
                    IsCustom = true,
                    ShowInList = true,
                    IsVisible = true,
                    SortOrder = maxSort + 1
                });
                await _db.SaveChangesAsync();
                InvalidateFieldCache(lookupModuleId);
            }
            else if (existing.Type != FieldType.Lookup)
            {
                throw new InvalidOperationException($"فیلدی با نام «{link}» از قبل وجود دارد و Lookup نیست.");
            }
            else
            {
                // هم‌راستا کردن LookupModule در صورت خالی بودن
                if (string.IsNullOrWhiteSpace(existing.LookupModule))
                {
                    existing.LookupModule = lookupTargetName;
                    await _db.SaveChangesAsync();
                    InvalidateFieldCache(lookupModuleId);
                }
                link = existing.Name;
            }
        }

        var relation = new RelationDef
        {
            SourceModuleId = sourceModuleId,
            TargetModuleId = targetModuleId,
            Label = tabLabel,
            RelatedFieldLabel = fieldLabel,
            Kind = kind,
            IsManyToMany = kind == RelationKind.ManyToMany,
            LinkFieldName = kind == RelationKind.ManyToMany ? null : link
        };
        _db.Relations.Add(relation);
        await _db.SaveChangesAsync();
        return relation;
    }

    /// <summary>سازگاری با فراخوانی‌های قدیمی (checkbox چند-به-چند).</summary>
    public Task<RelationDef> CreateRelationAsync(
        int sourceModuleId,
        int targetModuleId,
        string label,
        bool isManyToMany,
        string? linkFieldName = null) =>
        CreateRelationAsync(
            sourceModuleId, targetModuleId, label,
            isManyToMany ? RelationKind.ManyToMany : RelationKind.OneToMany,
            relatedFieldLabel: null,
            linkFieldName: linkFieldName);

    public async Task DeleteRelationAsync(int id)
    {
        var relation = await _db.Relations.FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("رابطه یافت نشد.");
        _db.Relations.Remove(relation);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateModuleDuplicateModeAsync(int moduleId, string mode)
    {
        var module = await _db.Modules.FirstOrDefaultAsync(m => m.Id == moduleId)
            ?? throw new InvalidOperationException("ماژول یافت نشد.");

        mode = (mode ?? "or").Trim().ToLowerInvariant();
        if (mode is not ("or" or "and"))
            throw new InvalidOperationException("حالت تطبیق تکراری فقط or یا and است.");

        module.DuplicateMatchMode = mode;
        await _db.SaveChangesAsync();
        InvalidateModulesCache();
    }

    public void InvalidateFieldCache(int moduleId)
    {
        _cache.Remove(FieldsCacheKey(moduleId));
        _cache.Remove(BlocksCacheKey(moduleId));
    }

    public void InvalidateModulesCache() =>
        _cache.Remove(ModulesCacheKey());

    private string ModulesCacheKey() => $"modules:{_tenant.TenantId}";
    private string FieldsCacheKey(int moduleId) => $"fields:{_tenant.TenantId}:{moduleId}";
    private string BlocksCacheKey(int moduleId) => $"blocks:{_tenant.TenantId}:{moduleId}";

    private static string NormalizeSystemName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        name = name.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        return SystemNameRegex.IsMatch(name) ? name : string.Empty;
    }
}
