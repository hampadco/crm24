using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Security;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

public class KanbanViewModel
{
    public ModuleDef Module { get; set; } = null!;
    public FieldDef GroupByField { get; set; } = null!;
    public List<FieldDef> GroupByCandidates { get; set; } = [];
    public List<KanbanSortOption> SortOptions { get; set; } = [];
    public string SortField { get; set; } = "title";
    public string SortDir { get; set; } = "asc";
    public string? Search { get; set; }
    public HashSet<string> VisibleColumnValues { get; set; } = new(StringComparer.Ordinal);
    public List<KanbanColumn> Columns { get; set; } = [];
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanConvertLead { get; set; }
    public bool CanConvertDocument { get; set; }
    public string? ConvertDocumentLabel { get; set; }
    public bool ShowTags { get; set; } = true;
    public bool ExpandCardsByDefault { get; set; }
    public List<KanbanTagOption> AllTags { get; set; } = [];
    public HashSet<int> SelectedTagIds { get; set; } = [];
    public string CurrentUrl { get; set; } = string.Empty;
}

public class KanbanColumn
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsCatchAll { get; set; }
    public List<KanbanCard> Cards { get; set; } = [];
}

public class KanbanCard
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? PrimaryExtra { get; set; }
    public string? SecondaryExtra { get; set; }
    public string? SortKey { get; set; }
    public List<KanbanPreviewField> PreviewFields { get; set; } = [];
    public List<KanbanTagOption> Tags { get; set; } = [];
}

public class KanbanPreviewField
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class KanbanTagOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
}

public class KanbanSortOption
{
    public string Field { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class KanbanQuickViewModel
{
    public ModuleDef Module { get; set; } = null!;
    public DynamicRecord Record { get; set; } = null!;
    public Dictionary<string, string?> Values { get; set; } = new();
    public List<FieldDef> Fields { get; set; } = [];
    public List<KanbanTagOption> Tags { get; set; } = [];
    public bool CanEdit { get; set; }
    public bool CanConvertLead { get; set; }
    public bool CanConvertDocument { get; set; }
}

/// <summary>نمای کاریز (Kanban) با drag & drop برای هر ماژول دارای فیلد Picklist.</summary>
public class KanbanController : AppControllerBase
{
    public const string EmptyColumnValue = "__empty__";
    private const int PageSize = 200;
    private static readonly TimeSpan CookieLife = TimeSpan.FromDays(90);
    private static readonly string[] PreviewPriority =
        ["phone", "mobile", "email", "company", "organization", "source", "amount", "priority", "dueDate", "expectedCloseDate", "city"];

    private readonly MetadataService _metadata;
    private readonly DynamicRecordService _records;
    private readonly RecordAccessService _access;
    private readonly CrmDbContext _db;

    public KanbanController(
        MetadataService metadata,
        DynamicRecordService records,
        RecordAccessService access,
        CrmDbContext db)
    {
        _metadata = metadata;
        _records = records;
        _access = access;
        _db = db;
    }

    [HttpGet("/App/kanban/{moduleName}")]
    public async Task<IActionResult> Index(
        string moduleName,
        string? groupBy = null,
        string? columns = null,
        string? sort = null,
        string? dir = null,
        string? q = null,
        string? tags = null,
        bool? showTags = null,
        bool? expandCards = null)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var fields = (await _metadata.GetFieldsAsync(module.Id)).ToList();
        var candidates = GetKanbanPicklistFields(fields);
        if (candidates.Count == 0)
            return RedirectToAction("Index", "Records", new { moduleName });

        var groupByField = ResolveGroupByField(moduleName, groupBy, candidates);
        RememberCookie(GroupByCookieName(moduleName), groupByField.Name);

        var showTagsFlag = ResolveBoolSetting(showTags, ShowTagsCookieName(moduleName), defaultValue: true);
        RememberCookie(ShowTagsCookieName(moduleName), showTagsFlag ? "1" : "0");
        var expandFlag = ResolveBoolSetting(expandCards, ExpandCookieName(moduleName), defaultValue: false);
        RememberCookie(ExpandCookieName(moduleName), expandFlag ? "1" : "0");

        var allValues = groupByField.PicklistValues
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .ToList();

        var visibleValues = ParseVisibleColumns(columns, allValues);
        var sortOptions = BuildSortOptions(fields, groupByField.Name);
        var sortField = ResolveSortField(sort, sortOptions);
        var sortDir = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
        var selectedTagIds = ParseTagIds(tags);

        var allTags = await _db.Tags.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new KanbanTagOption { Id = t.Id, Name = t.Name, Color = t.Color })
            .ToListAsync();

        var (listed, _) = await _records.ListAsync(
            module.Id,
            search: q,
            page: 1,
            pageSize: PageSize,
            includeTotal: false);
        IReadOnlyList<DynamicRecord> items = listed;

        var recordIds = items.Select(r => r.Id).ToList();
        var tagLinks = recordIds.Count == 0
            ? []
            : await _db.TagLinks.AsNoTracking()
                .Where(l => l.ModuleName == module.Name && recordIds.Contains(l.RecordId))
                .Include(l => l.Tag)
                .ToListAsync();

        var tagsByRecord = tagLinks
            .GroupBy(l => l.RecordId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(l => new KanbanTagOption
                {
                    Id = l.Tag.Id,
                    Name = l.Tag.Name,
                    Color = l.Tag.Color
                }).ToList());

        if (selectedTagIds.Count > 0)
        {
            items = items
                .Where(r => tagsByRecord.TryGetValue(r.Id, out var rt)
                            && rt.Any(t => selectedTagIds.Contains(t.Id)))
                .ToList();
        }

        var previewFields = ResolvePreviewFieldDefs(fields, groupByField.Name);
        var amountField = fields.FirstOrDefault(f =>
            f.Type is FieldType.Currency or FieldType.Decimal or FieldType.Number
            && !string.Equals(f.Name, groupByField.Name, StringComparison.OrdinalIgnoreCase));
        var dateField = fields.FirstOrDefault(f =>
            f.Type is FieldType.Date or FieldType.DateTime
            && !string.Equals(f.Name, groupByField.Name, StringComparison.OrdinalIgnoreCase));

        var knownValues = allValues.Select(v => v.Value).ToHashSet(StringComparer.Ordinal);
        var cardsByValue = allValues.ToDictionary(
            v => v.Value,
            _ => new List<KanbanCard>(),
            StringComparer.Ordinal);
        var catchAll = new List<KanbanCard>();

        foreach (var record in items)
        {
            var data = DynamicRecordService.ParseData(record);
            var raw = data.GetValueOrDefault(groupByField.Name);
            if (string.IsNullOrWhiteSpace(raw))
                raw = groupByField.DefaultValue;

            tagsByRecord.TryGetValue(record.Id, out var recordTags);
            var card = BuildCard(record, data, amountField, dateField, sortField, previewFields, recordTags ?? []);
            if (!string.IsNullOrWhiteSpace(raw) && knownValues.Contains(raw) && cardsByValue.TryGetValue(raw, out var bucket))
                bucket.Add(card);
            else
                catchAll.Add(card);
        }

        var columnsVm = new List<KanbanColumn>();
        foreach (var stage in allValues)
        {
            if (!visibleValues.Contains(stage.Value))
                continue;
            columnsVm.Add(new KanbanColumn
            {
                Value = stage.Value,
                Label = stage.Label,
                Color = stage.Color,
                Cards = SortCards(cardsByValue[stage.Value], sortField, sortDir)
            });
        }

        if (catchAll.Count > 0 || visibleValues.Contains(EmptyColumnValue))
        {
            columnsVm.Add(new KanbanColumn
            {
                Value = EmptyColumnValue,
                Label = "بدون مقدار / سایر",
                Color = "#94a3b8",
                IsCatchAll = true,
                Cards = SortCards(catchAll, sortField, sortDir)
            });
        }

        var canEdit = await _access.CanEditAsync(module.Id);
        var model = new KanbanViewModel
        {
            Module = module,
            GroupByField = groupByField,
            GroupByCandidates = candidates,
            SortOptions = sortOptions,
            SortField = sortField,
            SortDir = sortDir,
            Search = q,
            VisibleColumnValues = visibleValues,
            Columns = columnsVm,
            CanEdit = canEdit,
            CanDelete = await _access.CanDeleteAsync(module.Id),
            CanConvertLead = canEdit && string.Equals(module.Name, "leads", StringComparison.OrdinalIgnoreCase),
            CanConvertDocument = canEdit && !string.IsNullOrWhiteSpace(module.ConvertsToModule),
            ConvertDocumentLabel = string.IsNullOrWhiteSpace(module.ConvertsToModule) ? null : "تبدیل سند",
            ShowTags = showTagsFlag,
            ExpandCardsByDefault = expandFlag,
            AllTags = allTags,
            SelectedTagIds = selectedTagIds,
            CurrentUrl = $"{Request.Path}{Request.QueryString}"
        };

        ViewData["Title"] = $"کاریز {module.PluralLabel}";
        return View(model);
    }

    [HttpGet("/App/kanban/{moduleName}/card/{id:int}")]
    public async Task<IActionResult> QuickView(string moduleName, int id)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var record = await _records.GetAsync(module.Id, id);
        if (record is null)
            return NotFound();

        var fields = (await _metadata.GetFieldsAsync(module.Id))
            .Where(f => f.IsVisible)
            .OrderBy(f => f.SortOrder)
            .ToList();
        var values = DynamicRecordService.ParseData(record);
        var tags = await _db.TagLinks.AsNoTracking()
            .Where(l => l.ModuleName == module.Name && l.RecordId == id)
            .Include(l => l.Tag)
            .Select(l => new KanbanTagOption { Id = l.Tag.Id, Name = l.Tag.Name, Color = l.Tag.Color })
            .ToListAsync();

        var canEdit = await _access.CanEditAsync(module.Id);
        var model = new KanbanQuickViewModel
        {
            Module = module,
            Record = record,
            Values = values,
            Fields = fields.Where(f => f.ShowInList || f.IsRequired).Take(24).ToList(),
            Tags = tags,
            CanEdit = canEdit,
            CanConvertLead = canEdit && string.Equals(module.Name, "leads", StringComparison.OrdinalIgnoreCase),
            CanConvertDocument = canEdit && !string.IsNullOrWhiteSpace(module.ConvertsToModule)
        };

        return PartialView("_KanbanQuickView", model);
    }

    [HttpPost("/App/kanban/{moduleName}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(
        string moduleName,
        [FromForm] int recordId,
        [FromForm] string field,
        [FromForm] string? value)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanEditAsync(module.Id))
            return Forbid("Identity.Application");

        if (string.IsNullOrWhiteSpace(field))
            return BadRequest(new { ok = false, error = "فیلد مشخص نیست." });

        var fields = await _metadata.GetFieldsAsync(module.Id);
        var groupField = fields.FirstOrDefault(f =>
            string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase)
            && f.Type == FieldType.Picklist);
        if (groupField is null)
            return BadRequest(new { ok = false, error = "فیلد گروه‌بندی معتبر نیست." });

        string? nextValue = value;
        if (string.Equals(value, EmptyColumnValue, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(value))
            nextValue = null;

        try
        {
            await _records.UpdateFieldAsync(module.Id, recordId, groupField.Name, nextValue);
            return Ok(new { ok = true });
        }
        catch (RecordValidationException)
        {
            return BadRequest(new { ok = false, error = "مقدار مجاز نیست." });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, new { ok = false, error = "دسترسی ندارید." });
        }
    }

    internal static List<FieldDef> GetKanbanPicklistFields(IEnumerable<FieldDef> fields) =>
        fields
            .Where(f => f.Type == FieldType.Picklist && f.PicklistValues.Any(p => p.IsActive))
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .ToList();

    internal static bool ModuleSupportsKanban(IEnumerable<FieldDef> fields) =>
        GetKanbanPicklistFields(fields).Count > 0;

    private FieldDef ResolveGroupByField(string moduleName, string? groupBy, List<FieldDef> candidates)
    {
        if (!string.IsNullOrWhiteSpace(groupBy))
        {
            var fromQuery = candidates.FirstOrDefault(f =>
                string.Equals(f.Name, groupBy, StringComparison.OrdinalIgnoreCase));
            if (fromQuery is not null)
                return fromQuery;
        }

        if (Request.Cookies.TryGetValue(GroupByCookieName(moduleName), out var cookieVal)
            && !string.IsNullOrWhiteSpace(cookieVal))
        {
            var fromCookie = candidates.FirstOrDefault(f =>
                string.Equals(f.Name, cookieVal, StringComparison.OrdinalIgnoreCase));
            if (fromCookie is not null)
                return fromCookie;
        }

        return candidates.FirstOrDefault(f => f.Name == "stage")
            ?? candidates.FirstOrDefault(f => f.Name == "status")
            ?? candidates[0];
    }

    private bool ResolveBoolSetting(bool? query, string cookieName, bool defaultValue)
    {
        if (query.HasValue)
            return query.Value;
        if (Request.Cookies.TryGetValue(cookieName, out var raw))
        {
            if (raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (raw == "0" || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return defaultValue;
    }

    private void RememberCookie(string name, string value)
    {
        Response.Cookies.Append(name, value, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.Add(CookieLife),
            Path = "/"
        });
    }

    private static string GroupByCookieName(string moduleName) =>
        "kanban_" + moduleName.Trim().ToLowerInvariant() + "_gb";

    private static string ShowTagsCookieName(string moduleName) =>
        "kanban_" + moduleName.Trim().ToLowerInvariant() + "_tags";

    private static string ExpandCookieName(string moduleName) =>
        "kanban_" + moduleName.Trim().ToLowerInvariant() + "_exp";

    private static HashSet<int> ParseTagIds(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return [];
        return tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToHashSet();
    }

    private static HashSet<string> ParseVisibleColumns(string? columns, List<PicklistValue> allValues)
    {
        var all = allValues.Select(v => v.Value).ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(columns))
            return all;

        var selected = columns
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => all.Contains(v) || string.Equals(v, EmptyColumnValue, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        return selected.Count > 0 ? selected : all;
    }

    private static List<KanbanSortOption> BuildSortOptions(List<FieldDef> fields, string groupByName)
    {
        var options = new List<KanbanSortOption>
        {
            new() { Field = "title", Label = "عنوان" },
            new() { Field = "createdAt", Label = "زمان ایجاد" },
            new() { Field = "updatedAt", Label = "زمان ویرایش" }
        };

        foreach (var f in fields.Where(f => f.IsVisible && f.ShowInList))
        {
            if (string.Equals(f.Name, groupByName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (f.Type is FieldType.MultilineText or FieldType.MultiPicklist)
                continue;
            if (options.Any(o => string.Equals(o.Field, f.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            options.Add(new KanbanSortOption { Field = f.Name, Label = f.Label });
        }

        return options;
    }

    private static string ResolveSortField(string? sort, List<KanbanSortOption> options)
    {
        if (!string.IsNullOrWhiteSpace(sort)
            && options.Any(o => string.Equals(o.Field, sort, StringComparison.OrdinalIgnoreCase)))
            return options.First(o => string.Equals(o.Field, sort, StringComparison.OrdinalIgnoreCase)).Field;
        return "title";
    }

    private static List<FieldDef> ResolvePreviewFieldDefs(List<FieldDef> fields, string groupByName)
    {
        var list = fields
            .Where(f => f.IsVisible && f.ShowInList
                        && !string.Equals(f.Name, groupByName, StringComparison.OrdinalIgnoreCase)
                        && f.Type is not FieldType.MultilineText)
            .ToList();

        return list
            .OrderBy(f =>
            {
                var idx = Array.FindIndex(PreviewPriority,
                    p => string.Equals(p, f.Name, StringComparison.OrdinalIgnoreCase));
                return idx < 0 ? 100 + f.SortOrder : idx;
            })
            .Take(5)
            .ToList();
    }

    private static KanbanCard BuildCard(
        DynamicRecord record,
        Dictionary<string, string?> data,
        FieldDef? amountField,
        FieldDef? dateField,
        string sortField,
        List<FieldDef> previewDefs,
        List<KanbanTagOption> tags)
    {
        string? primary = null;
        if (amountField is not null && data.TryGetValue(amountField.Name, out var amountRaw)
            && !string.IsNullOrWhiteSpace(amountRaw))
        {
            primary = amountField.Type == FieldType.Currency && decimal.TryParse(amountRaw, out var a)
                ? a.ToString("N0") + " تومان"
                : amountRaw;
        }

        string? secondary = null;
        if (dateField is not null)
            secondary = FormatJalaliSubtitle(data.GetValueOrDefault(dateField.Name));

        var preview = new List<KanbanPreviewField>();
        foreach (var f in previewDefs)
        {
            var display = FormatFieldValue(f, data.GetValueOrDefault(f.Name));
            if (string.IsNullOrWhiteSpace(display))
                continue;
            preview.Add(new KanbanPreviewField
            {
                Name = f.Name,
                Label = f.Label,
                Value = display
            });
        }

        if (string.IsNullOrWhiteSpace(primary) && preview.Count > 0)
            primary = preview[0].Value;
        if (string.IsNullOrWhiteSpace(secondary) && preview.Count > 1)
            secondary = preview[1].Value;

        return new KanbanCard
        {
            Id = record.Id,
            Title = record.Title,
            PrimaryExtra = primary,
            SecondaryExtra = secondary,
            SortKey = BuildSortKey(record, data, sortField),
            PreviewFields = preview,
            Tags = tags
        };
    }

    private static string FormatFieldValue(FieldDef field, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        switch (field.Type)
        {
            case FieldType.Picklist:
                return field.PicklistValues.FirstOrDefault(p => p.Value == raw)?.Label ?? raw;
            case FieldType.Checkbox:
                return raw is "1" or "true" or "True" ? "بله" : "خیر";
            case FieldType.Currency:
                return decimal.TryParse(raw, out var money) ? money.ToString("N0") + " تومان" : raw;
            case FieldType.Date:
            case FieldType.DateTime:
                return FormatJalaliSubtitle(raw) ?? raw;
            default:
                return raw;
        }
    }

    private static string BuildSortKey(DynamicRecord record, Dictionary<string, string?> data, string sortField)
    {
        if (string.Equals(sortField, "title", StringComparison.OrdinalIgnoreCase))
            return record.Title ?? string.Empty;
        if (string.Equals(sortField, "createdAt", StringComparison.OrdinalIgnoreCase))
            return record.CreatedAtUtc.ToString("O");
        if (string.Equals(sortField, "updatedAt", StringComparison.OrdinalIgnoreCase))
            return (record.UpdatedAtUtc ?? record.CreatedAtUtc).ToString("O");
        return data.GetValueOrDefault(sortField) ?? string.Empty;
    }

    private static List<KanbanCard> SortCards(List<KanbanCard> cards, string sortField, string sortDir)
    {
        var asc = !string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(sortField, "createdAt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sortField, "updatedAt", StringComparison.OrdinalIgnoreCase))
        {
            DateTime Key(KanbanCard c) =>
                DateTime.TryParse(c.SortKey, out var dt) ? dt : DateTime.MinValue;
            return (asc
                ? cards.OrderBy(Key).ThenBy(c => c.Id)
                : cards.OrderByDescending(Key).ThenByDescending(c => c.Id)).ToList();
        }

        if (cards.Count > 0 && cards.All(c =>
                string.IsNullOrWhiteSpace(c.SortKey) || decimal.TryParse(c.SortKey, out _)))
        {
            decimal Key(KanbanCard c) =>
                decimal.TryParse(c.SortKey, out var n) ? n : decimal.MinValue;
            return (asc
                ? cards.OrderBy(Key).ThenBy(c => c.Id)
                : cards.OrderByDescending(Key).ThenByDescending(c => c.Id)).ToList();
        }

        return (asc
            ? cards.OrderBy(c => c.SortKey ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(c => c.Id)
            : cards.OrderByDescending(c => c.SortKey ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(c => c.Id)).ToList();
    }

    private static string? FormatJalaliSubtitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (DateTime.TryParse(raw, out var dt))
            return Crm.Web.Services.PersianDateHelper.ToJalaliDate(dt);
        return raw;
    }
}
