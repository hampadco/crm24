using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Services;
using Crm.Web.Services;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>استودیوی سفارشی‌سازی ماژول (Layout / Fields / Relations / Duplicates) — فقط مدیر Tenant.</summary>
public class CustomizeController : AppControllerBase
{
    private readonly MetadataService _metadata;
    private readonly ITenantContext _tenant;

    public CustomizeController(MetadataService metadata, ITenantContext tenant)
    {
        _metadata = metadata;
        _tenant = tenant;
    }

    [HttpGet("/App/customize")]
    public async Task<IActionResult> Index()
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var modules = await _metadata.GetActiveModulesAsync();
        ViewData["Title"] = "سفارشی‌سازی ماژول";
        return View(modules);
    }

    [HttpGet("/App/customize/{moduleName}")]
    public async Task<IActionResult> Studio(string moduleName, string? tab = null, string? dep = null)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        var activeTab = string.IsNullOrWhiteSpace(tab) ? "layout" : tab.Trim().ToLowerInvariant();
        if (activeTab is "fields" or "layout" or "paging")
            activeTab = "layout";

        // فیلدهای بدون بلاک را به بلاک پیش‌فرض منتقل کن (یک‌بار روی باز شدن استودیو)
        if (activeTab == "layout")
            await _metadata.EnsureUngroupedFieldsInDefaultBlockAsync(module.Id);

        var blocks = await _metadata.GetBlocksAsync(module.Id);
        var fields = await _metadata.GetFieldsAsync(module.Id);

        ViewBag.Module = module;
        ViewBag.Blocks = blocks;
        ViewBag.Fields = fields;
        ViewBag.Tab = activeTab;
        ViewBag.DepMode = string.Equals(dep, "block", StringComparison.OrdinalIgnoreCase) ? "block" : "field";

        if (activeTab is "relations" or "duplicates" or "layout" or "dependencies")
        {
            var allModules = await _metadata.GetActiveModulesAsync();
            ViewBag.AllModules = allModules;
            ViewBag.Relations = await _metadata.GetRelationsForModuleAsync(module.Id);

            if (activeTab == "relations")
            {
                var fieldMap = await _metadata.GetFieldsForModulesAsync(allModules.Select(m => m.Id));
                var lookups = fieldMap.Values
                    .SelectMany(list => list.Where(f => f.Type == FieldType.Lookup))
                    .ToList();
                ViewBag.LookupFields = lookups;
            }
        }

        ViewData["Title"] = $"صفحه‌بندی ماژول {module.PluralLabel}";
        return View();
    }

    [HttpPost("/App/customize/{moduleName}/blocks")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBlock(string moduleName, string label)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            var blocks = await _metadata.GetBlocksAsync(module.Id);
            var sort = blocks.Count == 0 ? 1 : blocks.Max(b => b.SortOrder) + 1;
            var name = $"block_{sort}_{Guid.NewGuid():N}"[..20];
            await _metadata.CreateBlockAsync(module.Id, name, label, sort);
            TempData["Success"] = "بلاک افزوده شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "layout" });
    }

    [HttpPost("/App/customize/{moduleName}/blocks/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateBlock(
        string moduleName,
        int id,
        string label,
        int sortOrder,
        bool isCollapsed,
        string? visibilityRuleJson)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            await _metadata.UpdateBlockAsync(id, label, sortOrder, isCollapsed,
                VisibilityRuleHelper.Normalize(visibilityRuleJson) ?? visibilityRuleJson ?? "");
            TempData["Success"] = "بلاک به‌روز شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "layout" });
    }

    [HttpPost("/App/customize/{moduleName}/fields")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddField(
        string moduleName,
        string name,
        string label,
        FieldType type,
        bool isRequired,
        bool showInList,
        int? blockId,
        int? maxLength,
        bool isVisible = true,
        bool isUniqueCheck = false,
        string? defaultValue = null,
        string? visibilityRuleJson = null,
        int? integerDigits = null,
        int? decimalDigits = null,
        string? formulaExpression = null,
        string? validationRulesJson = null,
        string? picklistOptions = null,
        bool defaultToday = false)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(label))
                name = "f_" + Guid.NewGuid().ToString("N")[..8];

            if (defaultToday && type is FieldType.Date or FieldType.DateTime)
                defaultValue = "__TODAY__";

            // برای عدد صحیح/ارز حداکثر رقم را در MaxLength نگه می‌داریم اگر جداگانه نیامده
            if (maxLength is null && integerDigits is int digits && type is FieldType.Number or FieldType.Currency)
                maxLength = digits;

            var options = ParsePicklistOptions(picklistOptions);

            await _metadata.CreateFieldAsync(
                module.Id, name, label, type,
                isRequired: isRequired,
                showInList: showInList,
                blockId: blockId,
                maxLength: maxLength,
                isVisible: isVisible,
                isUniqueCheck: isUniqueCheck,
                defaultValue: defaultValue,
                visibilityRuleJson: visibilityRuleJson,
                integerDigits: integerDigits,
                decimalDigits: decimalDigits,
                formulaExpression: formulaExpression,
                validationRulesJson: validationRulesJson,
                picklistOptions: options);
            TempData["Success"] = "فیلد سفارشی افزوده شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "layout" });
    }

    [HttpPost("/App/customize/{moduleName}/fields/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateField(
        string moduleName,
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
        string? picklistOptions = null,
        bool defaultToday = false)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            if (defaultToday)
                defaultValue = "__TODAY__";

            var options = picklistOptions is null ? null : ParsePicklistOptions(picklistOptions);

            await _metadata.UpdateFieldAsync(
                id, label, isRequired, showInList, sortOrder, blockId,
                maxLength, isVisible, isUniqueCheck, defaultValue,
                VisibilityRuleHelper.Normalize(visibilityRuleJson) ?? visibilityRuleJson,
                integerDigits, decimalDigits, formulaExpression, validationRulesJson, options);
            TempData["Success"] = "فیلد به‌روز شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "layout" });
    }

    [HttpPost("/App/customize/{moduleName}/fields/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteField(string moduleName, int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            await _metadata.DeleteFieldAsync(id);
            TempData["Success"] = "فیلد سفارشی حذف شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "layout" });
    }

    [HttpPost("/App/customize/{moduleName}/visibility/batch")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveVisibilityBatch(
        string moduleName,
        string? actionsJson,
        string? dep = "field")
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            if (string.IsNullOrWhiteSpace(actionsJson))
                throw new InvalidOperationException("هیچ عملی برای ذخیره ارسال نشده است.");

            using var doc = JsonDocument.Parse(actionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                throw new InvalidOperationException("حداقل یک عمل لازم است.");

            var fields = await _metadata.GetFieldsAsync(module.Id);
            var blocks = await _metadata.GetBlocksAsync(module.Id);
            var fieldIds = fields.Select(f => f.Id).ToHashSet();
            var blockIds = blocks.Select(b => b.Id).ToHashSet();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var savedFieldIds = new HashSet<int>();
            var touchedBlockIds = new HashSet<int>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var kind = item.TryGetProperty("kind", out var k) ? (k.GetString() ?? "") : "";
                if (!item.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id))
                    throw new InvalidOperationException("شناسه هدف نامعتبر است.");

                var ruleRaw = item.TryGetProperty("rule", out var r) ? r.GetString() : null;
                var normalized = VisibilityRuleHelper.Normalize(ruleRaw)
                    ?? throw new InvalidOperationException("شرط وابستگی نامعتبر یا خالی است.");

                var key = $"{kind}:{id}";
                if (!seen.Add(key))
                    throw new InvalidOperationException("هدف تکراری در لیست عمل‌ها وجود دارد.");

                if (string.Equals(kind, "field", StringComparison.OrdinalIgnoreCase))
                {
                    if (!fieldIds.Contains(id))
                        throw new InvalidOperationException("فیلد هدف یافت نشد.");
                    var target = fields.First(f => f.Id == id);
                    ValidateVisibilityRule(normalized, target.Name, fields);
                    await _metadata.SetFieldVisibilityRuleAsync(id, normalized);
                    savedFieldIds.Add(id);
                }
                else if (string.Equals(kind, "block", StringComparison.OrdinalIgnoreCase))
                {
                    if (!blockIds.Contains(id))
                        throw new InvalidOperationException("بلاک هدف یافت نشد.");
                    ValidateVisibilityRule(normalized, controllingFieldName: null, fields);
                    await _metadata.SetBlockVisibilityRuleAsync(id, normalized);
                    touchedBlockIds.Add(id);
                }
                else
                {
                    throw new InvalidOperationException("نوع هدف نامعتبر است.");
                }
            }

            // اگر وابستگی بلاک ذخیره شد، فیلدهای داخل آن بلاک که در payload نیستند پاک شوند
            // (مدل رقیب: عمل‌های تو‌در‌تو زیر بلاک کامل تعریف می‌شوند)
            foreach (var blockId in touchedBlockIds)
            {
                foreach (var f in fields.Where(x =>
                             x.BlockId == blockId
                             && !string.IsNullOrWhiteSpace(x.VisibilityRuleJson)
                             && !savedFieldIds.Contains(x.Id)))
                {
                    await _metadata.SetFieldVisibilityRuleAsync(f.Id, null);
                }
            }

            TempData["Success"] = "وابستگی‌ها ذخیره شدند.";
        }
        catch (JsonException)
        {
            TempData["Error"] = "دادهٔ وابستگی نامعتبر است.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "dependencies", dep });
    }

    [HttpPost("/App/customize/{moduleName}/fields/{id:int}/visibility")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetFieldVisibility(
        string moduleName,
        int id,
        string? visibilityRuleJson,
        string? dep = "field")
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            var normalized = VisibilityRuleHelper.Normalize(visibilityRuleJson);
            var fields = await _metadata.GetFieldsAsync(module.Id);
            var target = fields.FirstOrDefault(f => f.Id == id)
                ?? throw new InvalidOperationException("فیلد یافت نشد.");

            ValidateVisibilityRule(normalized, target.Name, fields);

            await _metadata.SetFieldVisibilityRuleAsync(id, normalized);
            TempData["Success"] = "وابستگی فیلد ذخیره شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "dependencies", dep });
    }

    [HttpPost("/App/customize/{moduleName}/blocks/{id:int}/visibility")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetBlockVisibility(
        string moduleName,
        int id,
        string? visibilityRuleJson,
        string? dep = "block")
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            var normalized = VisibilityRuleHelper.Normalize(visibilityRuleJson);
            var fields = await _metadata.GetFieldsAsync(module.Id);
            ValidateVisibilityRule(normalized, controllingFieldName: null, fields);

            await _metadata.SetBlockVisibilityRuleAsync(id, normalized);
            TempData["Success"] = "وابستگی بلاک ذخیره شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "dependencies", dep });
    }

    [HttpPost("/App/customize/{moduleName}/fields/{id:int}/visibility/clear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearFieldVisibility(string moduleName, int id, string? dep = "field")
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            await _metadata.SetFieldVisibilityRuleAsync(id, null);
            TempData["Success"] = "وابستگی فیلد حذف شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "dependencies", dep });
    }

    [HttpPost("/App/customize/{moduleName}/blocks/{id:int}/visibility/clear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearBlockVisibility(
        string moduleName,
        int id,
        string? dep = "block",
        bool clearNestedFields = true)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            await _metadata.SetBlockVisibilityRuleAsync(id, null);

            // مثل رقیب: حذف وابستگی بلاک، وابستگی فیلدهای داخل همان بلاک را هم پاک می‌کند
            if (clearNestedFields)
            {
                var fields = await _metadata.GetFieldsAsync(module.Id);
                foreach (var f in fields.Where(x => x.BlockId == id && !string.IsNullOrWhiteSpace(x.VisibilityRuleJson)))
                    await _metadata.SetFieldVisibilityRuleAsync(f.Id, null);
            }

            TempData["Success"] = "وابستگی بلاک حذف شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "dependencies", dep });
    }

    private static void ValidateVisibilityRule(
        string? normalized,
        string? controllingFieldName,
        IReadOnlyList<FieldDef> fields)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        var rule = VisibilityRuleHelper.Parse(normalized)
            ?? throw new InvalidOperationException("شرط وابستگی نامعتبر است.");

        var picklistNames = fields
            .Where(f => f.Type is FieldType.Picklist or FieldType.MultiPicklist)
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var c in rule.Conditions)
        {
            if (string.IsNullOrWhiteSpace(c.Field))
                throw new InvalidOperationException("فیلد شرط نمی‌تواند خالی باشد.");

            if (!string.IsNullOrWhiteSpace(controllingFieldName)
                && string.Equals(c.Field, controllingFieldName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"نمی‌توانید فیلد «{controllingFieldName}» را هم برای وابستگی و هم برای شرط انتخاب کنید.");

            if (!picklistNames.Contains(c.Field))
                throw new InvalidOperationException("فقط فیلدهای انتخابی (picklist) می‌توانند در شرط استفاده شوند.");

            if (string.IsNullOrWhiteSpace(c.Value))
                throw new InvalidOperationException($"مقدار فیلد «{c.Field}» نمی‌تواند خالی باشد.");
        }
    }

    private static List<(string Value, string Label)> ParsePicklistOptions(string? raw)
    {
        var list = new List<(string Value, string Label)>();
        if (string.IsNullOrWhiteSpace(raw))
            return list;

        foreach (var line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            // پشتیبانی از "value|label" یا فقط label
            var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
                list.Add((parts[0], parts[1]));
            else
                list.Add((parts[0], parts[0]));
        }

        return list;
    }

    [HttpPost("/App/customize/{moduleName}/layout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLayout(string moduleName, string layoutJson)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            var layout = ParseLayoutJson(layoutJson);
            await _metadata.ReorderLayoutAsync(module.Id, layout);
            TempData["Success"] = "چیدمان ذخیره شد.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "layout" });
    }

    [HttpPost("/App/customize/{moduleName}/blocks/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBlock(string moduleName, int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        try
        {
            await _metadata.DeleteBlockAsync(id);
            TempData["Success"] = "بلاک حذف شد؛ فیلدها بدون گروه باقی ماندند.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "layout" });
    }

    [HttpPost("/App/customize/{moduleName}/relations")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRelation(
        string moduleName,
        int targetModuleId,
        string label,
        RelationKind kind,
        string? relatedFieldLabel = null,
        string? linkFieldName = null,
        bool isManyToMany = false)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            // سازگاری اگر فقط checkbox قدیمی ارسال شود
            if (isManyToMany && kind == RelationKind.OneToMany)
                kind = RelationKind.ManyToMany;

            await _metadata.CreateRelationAsync(
                module.Id, targetModuleId, label, kind, relatedFieldLabel, linkFieldName);
            TempData["Success"] = "رابطه افزوده شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "relations" });
    }

    [HttpPost("/App/customize/{moduleName}/relations/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRelation(string moduleName, int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        try
        {
            await _metadata.DeleteRelationAsync(id);
            TempData["Success"] = "رابطه حذف شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "relations" });
    }

    [HttpPost("/App/customize/{moduleName}/duplicates")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDuplicates(
        string moduleName,
        bool enabled,
        string mode,
        bool ignoreEmpty,
        string syncPolicy,
        bool globalEnabled,
        int[]? uniqueFieldIds,
        int[]? globalFieldIds)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            var selected = (uniqueFieldIds ?? Array.Empty<int>()).Distinct().Take(3).ToHashSet();
            var globalSelected = (globalFieldIds ?? Array.Empty<int>()).Distinct().ToHashSet();

            await _metadata.UpdateModuleDuplicateSettingsAsync(
                module.Id, enabled, mode, ignoreEmpty, syncPolicy, globalEnabled);

            var fields = await _metadata.GetFieldsAsync(module.Id);
            foreach (var field in fields)
            {
                var wantUnique = selected.Contains(field.Id);
                var wantGlobal = globalEnabled && field.Type == FieldType.Phone && globalSelected.Contains(field.Id);
                if (field.IsUniqueCheck == wantUnique && field.IsGlobalUniqueCheck == wantGlobal)
                    continue;
                await _metadata.SetFieldUniqueFlagsAsync(field.Id, wantUnique, wantGlobal);
            }

            TempData["Success"] = "تنظیمات تکراری ذخیره شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "duplicates" });
    }

    private static List<(int? BlockId, List<int> FieldIds)> ParseLayoutJson(string? layoutJson)
    {
        if (string.IsNullOrWhiteSpace(layoutJson))
            throw new InvalidOperationException("چیدمان خالی است.");

        using var doc = JsonDocument.Parse(layoutJson);
        var result = new List<(int? BlockId, List<int> FieldIds)>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            int? blockId = null;
            if (item.TryGetProperty("blockId", out var blockProp) && blockProp.ValueKind != JsonValueKind.Null)
                blockId = blockProp.GetInt32();

            var fieldIds = new List<int>();
            if (item.TryGetProperty("fieldIds", out var fieldsProp) && fieldsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fieldsProp.EnumerateArray())
                    fieldIds.Add(f.GetInt32());
            }

            result.Add((blockId, fieldIds));
        }

        return result;
    }
}
