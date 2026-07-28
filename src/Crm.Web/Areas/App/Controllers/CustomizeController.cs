using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Services;

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
    public async Task<IActionResult> Studio(string moduleName, string? tab = null)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        var blocks = await _metadata.GetBlocksAsync(module.Id);
        var fields = await _metadata.GetFieldsAsync(module.Id);
        var activeTab = string.IsNullOrWhiteSpace(tab) ? "layout" : tab.Trim().ToLowerInvariant();

        ViewBag.Module = module;
        ViewBag.Blocks = blocks;
        ViewBag.Fields = fields;
        ViewBag.Tab = activeTab;

        if (activeTab is "relations" or "duplicates" or "layout")
        {
            var allModules = await _metadata.GetActiveModulesAsync();
            ViewBag.AllModules = allModules;
            ViewBag.Relations = await _metadata.GetRelationsForModuleAsync(module.Id);

            if (activeTab == "relations")
            {
                var lookups = new List<FieldDef>();
                foreach (var m in allModules)
                {
                    var mFields = await _metadata.GetFieldsAsync(m.Id);
                    lookups.AddRange(mFields.Where(f => f.Type == FieldType.Lookup));
                }
                ViewBag.LookupFields = lookups;
            }
        }

        ViewData["Title"] = $"سفارشی‌سازی — {module.PluralLabel}";
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
            await _metadata.UpdateBlockAsync(id, label, sortOrder, isCollapsed, visibilityRuleJson ?? "");
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
        int? blockId)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            await _metadata.CreateFieldAsync(
                module.Id, name, label, type,
                isRequired: isRequired,
                showInList: showInList,
                blockId: blockId);
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
        string? visibilityRuleJson)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            await _metadata.UpdateFieldAsync(
                id, label, isRequired, showInList, sortOrder, blockId,
                maxLength, isVisible, isUniqueCheck, defaultValue, visibilityRuleJson);
            TempData["Success"] = "فیلد به‌روز شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Studio), new { moduleName, tab = "fields" });
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
        bool isManyToMany,
        string? linkFieldName)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            await _metadata.CreateRelationAsync(
                module.Id, targetModuleId, label, isManyToMany, linkFieldName);
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
    public async Task<IActionResult> SaveDuplicates(string moduleName, string mode, int[]? uniqueFieldIds)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        try
        {
            await _metadata.UpdateModuleDuplicateModeAsync(module.Id, mode);
            var fields = await _metadata.GetFieldsAsync(module.Id);
            var selected = new HashSet<int>(uniqueFieldIds ?? Array.Empty<int>());
            foreach (var field in fields)
            {
                var wantUnique = selected.Contains(field.Id);
                if (field.IsUniqueCheck == wantUnique)
                    continue;
                await _metadata.UpdateFieldAsync(
                    field.Id, field.Label, field.IsRequired, field.ShowInList, field.SortOrder,
                    field.BlockId, field.MaxLength, field.IsVisible, wantUnique,
                    field.DefaultValue, field.VisibilityRuleJson);
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
