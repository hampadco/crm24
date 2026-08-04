using Crm.Core.Entities;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Models;

public class RecordListViewModel
{
    public ModuleDef Module { get; set; } = null!;
    public IReadOnlyList<FieldDef> Fields { get; set; } = [];
    public IReadOnlyList<DynamicRecord> Records { get; set; } = [];
    public Dictionary<int, Dictionary<string, string?>> RecordData { get; set; } = new();

    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public string? SortField { get; set; }
    public string SortDir { get; set; } = "desc";
    public List<ColumnFilter> Filters { get; set; } = [];

    public static IReadOnlyList<(string Op, string Label)> FilterOps { get; } =
    [
        ("equals", "مساوی"),
        ("notequals", "نابرابر"),
        ("startswith", "شروع شده با"),
        ("endswith", "خاتمه یافته باشد با"),
        ("contains", "شامل شده باشد با"),
        ("notcontains", "شامل نشده باشد با"),
        ("isempty", "خالی باشد"),
        ("isnotempty", "خالی نباشد")
    ];

    /// <summary>عملگرهای مناسب فیلدهای لیست انتخابی.</summary>
    public static IReadOnlyList<(string Op, string Label)> PicklistFilterOps { get; } =
    [
        ("equals", "مساوی"),
        ("notequals", "نابرابر"),
        ("isempty", "خالی باشد"),
        ("isnotempty", "خالی نباشد")
    ];

    public static bool OpNeedsValue(string op) =>
        !string.Equals(op, "isempty", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(op, "isnotempty", StringComparison.OrdinalIgnoreCase);

    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }

    /// <summary>همه فیلدهای قابل‌نمایش برای مودال مدیریت ستون‌ها.</summary>
    public IReadOnlyList<FieldDef> AllFields { get; set; } = [];

    /// <summary>بلاک‌های ماژول برای گروه‌بندی فیلدهای موجود.</summary>
    public IReadOnlyList<FieldBlock> Blocks { get; set; } = [];

    /// <summary>شناسه ستون‌های انتخاب‌شده به ترتیب.</summary>
    public IReadOnlyList<int> SelectedColumnIds { get; set; } = [];

    /// <summary>fieldName → (recordId → title) برای نمایش فیلدهای Lookup در لیست.</summary>
    public Dictionary<string, Dictionary<string, string>> LookupTitles { get; set; } = new();

    /// <summary>آیا این ماژول فیلد مرحله‌ای برای نمای کاریز دارد؟</summary>
    public bool HasKanban { get; set; }

    public string? FilterValue(string fieldName) =>
        Filters.FirstOrDefault(f => string.Equals(f.Field, fieldName, StringComparison.OrdinalIgnoreCase))?.Value;

    public string FilterOp(string fieldName) =>
        Filters.FirstOrDefault(f => string.Equals(f.Field, fieldName, StringComparison.OrdinalIgnoreCase))?.Op
        ?? "contains";

    /// <summary>مقادیر query برای حفظ جستجو/فیلتر/مرتب‌سازی در لینک‌ها.</summary>
    public Dictionary<string, string> RouteValues(int? page = null, string? sort = null, string? dir = null)
    {
        var routes = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(Search))
            routes["q"] = Search!;
        routes["page"] = (page ?? Page).ToString();

        var sortField = sort ?? SortField;
        var sortDir = dir ?? SortDir;
        if (!string.IsNullOrWhiteSpace(sortField))
        {
            routes["sort"] = sortField!;
            routes["dir"] = string.IsNullOrWhiteSpace(sortDir) ? "desc" : sortDir;
        }

        foreach (var filter in Filters)
        {
            var needsValue = OpNeedsValue(filter.Op);
            if (needsValue && string.IsNullOrWhiteSpace(filter.Value))
                continue;
            routes[$"cf_{filter.Field}"] = filter.Value ?? "";
            routes[$"op_{filter.Field}"] = string.IsNullOrWhiteSpace(filter.Op) ? "contains" : filter.Op;
        }

        return routes;
    }

    public Dictionary<string, string> SortRouteValues(string field)
    {
        var nextDir = string.Equals(SortField, field, StringComparison.OrdinalIgnoreCase)
                      && !string.Equals(SortDir, "asc", StringComparison.OrdinalIgnoreCase)
            ? "asc"
            : "desc";
        return RouteValues(page: 1, sort: field, dir: nextDir);
    }
}

public class RecordFormViewModel
{
    public ModuleDef Module { get; set; } = null!;
    public IReadOnlyList<FieldDef> Fields { get; set; } = [];
    public IReadOnlyList<FieldBlock> Blocks { get; set; } = [];

    /// <summary>fieldId → دسترسی فیلد برای پروفایل جاری (Hidden/ReadOnly/Editable).</summary>
    public Dictionary<int, FieldAccess> FieldAccessMap { get; set; } = new();

    public int? RecordId { get; set; }
    public Dictionary<string, string?> Values { get; set; } = new();
    public Dictionary<string, string> Errors { get; set; } = new();

    /// <summary>fieldName → گزینه‌های ماژول مقصد برای فیلدهای Lookup.</summary>
    public Dictionary<string, List<(int Id, string Title)>> LookupOptions { get; set; } = new();

    public FieldAccess AccessFor(FieldDef field) =>
        FieldAccessMap.TryGetValue(field.Id, out var access) ? access : FieldAccess.Editable;

    /// <summary>
    /// فیلدهای قابل‌نمایش به‌ترتیب بلاک سپس SortOrder؛ اگر بلاکی نباشد، همان ترتیب فیلدها.
    /// </summary>
    public IEnumerable<(FieldBlock? Block, IReadOnlyList<FieldDef> Fields)> GroupedFields()
    {
        var visible = Fields.Where(f => f.IsVisible).ToList();
        if (Blocks.Count == 0)
        {
            yield return (null, visible.OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToList());
            yield break;
        }

        foreach (var block in Blocks.OrderBy(b => b.SortOrder).ThenBy(b => b.Id))
        {
            var blockFields = visible
                .Where(f => f.BlockId == block.Id)
                .OrderBy(f => f.SortOrder)
                .ThenBy(f => f.Id)
                .ToList();
            if (blockFields.Count > 0)
                yield return (block, blockFields);
        }

        var ungrouped = visible
            .Where(f => f.BlockId is null || Blocks.All(b => b.Id != f.BlockId))
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .ToList();
        if (ungrouped.Count > 0)
            yield return (null, ungrouped);
    }
}

public class RecycleBinViewModel
{
    public IReadOnlyList<DynamicRecord> Records { get; set; } = [];
}

public class RecordDetailViewModel
{
    public ModuleDef Module { get; set; } = null!;
    public DynamicRecord Record { get; set; } = null!;
    public IReadOnlyList<FieldDef> Fields { get; set; } = [];
    public IReadOnlyList<FieldBlock> Blocks { get; set; } = [];
    public Dictionary<string, string?> Values { get; set; } = new();

    /// <summary>fieldName → (recordId → title) برای نمایش Lookup.</summary>
    public Dictionary<string, Dictionary<string, string>> LookupTitles { get; set; } = new();

    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }

    public IReadOnlyList<Note> Notes { get; set; } = [];
    public IReadOnlyList<AuditLog> AuditLogs { get; set; } = [];
    public IReadOnlyList<RelatedRecordItem> Activities { get; set; } = [];
    public IReadOnlyList<RelatedRecordGroup> Relations { get; set; } = [];
    public IReadOnlyList<Attachment> Attachments { get; set; } = [];
    public IReadOnlyList<Tag> Tags { get; set; } = [];

    public string? DisplayValue(FieldDef field)
    {
        Values.TryGetValue(field.Name, out var value);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (field.Type == FieldType.Lookup
            && LookupTitles.TryGetValue(field.Name, out var titles)
            && titles.TryGetValue(value, out var title))
            return title;

        if (field.Type == FieldType.Picklist)
        {
            var pick = field.PicklistValues.FirstOrDefault(p => p.Value == value);
            return pick?.Label ?? value;
        }

        if (field.Type == FieldType.Checkbox)
            return value is "true" or "1" or "True" ? "بله" : "خیر";

        return value;
    }

    public string? LookupModuleFor(FieldDef field) =>
        field.Type == FieldType.Lookup ? field.LookupModule : null;

    public string? LookupRecordId(FieldDef field)
    {
        if (field.Type != FieldType.Lookup)
            return null;
        Values.TryGetValue(field.Name, out var value);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>فیلدهای قابل‌نمایش گروه‌بندی‌شده بر اساس بلاک (مثل فرم).</summary>
    public IEnumerable<(FieldBlock? Block, IReadOnlyList<FieldDef> Fields)> GroupedFields()
    {
        var visible = Fields.Where(f => f.IsVisible).ToList();
        if (Blocks.Count == 0)
        {
            yield return (null, visible.OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToList());
            yield break;
        }

        foreach (var block in Blocks.OrderBy(b => b.SortOrder).ThenBy(b => b.Id))
        {
            var blockFields = visible
                .Where(f => f.BlockId == block.Id)
                .OrderBy(f => f.SortOrder)
                .ThenBy(f => f.Id)
                .ToList();
            if (blockFields.Count > 0)
                yield return (block, blockFields);
        }

        var ungrouped = visible
            .Where(f => f.BlockId is null || Blocks.All(b => b.Id != f.BlockId))
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .ToList();
        if (ungrouped.Count > 0)
            yield return (null, ungrouped);
    }

    /// <summary>فیلدهای کلیدی خلاصه: عنوان‌مانند + ShowInList.</summary>
    public IReadOnlyList<FieldDef> SummaryFields()
    {
        var ordered = Fields.Where(f => f.IsVisible)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .ToList();

        var summary = ordered
            .Where(f => f.ShowInList || f.Name is "name" or "title" or "subject" or "stage" or "status")
            .Take(8)
            .ToList();

        return summary.Count > 0 ? summary : ordered.Take(6).ToList();
    }

    /// <summary>کارت‌های بالای صفحه جزئیات — فیلدهای تماسی/وضعیتی (مثل رقیب).</summary>
    public IReadOnlyList<FieldDef> HighlightFields()
    {
        var preferred = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "email", "phone", "mobile", "cellphone", "tel", "website", "web", "url",
            "industry", "status", "stage", "leadstatus", "lead_status", "source",
            "company", "organization", "city", "priority"
        };

        var ordered = Fields.Where(f => f.IsVisible)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .ToList();

        var picks = ordered
            .Where(f => preferred.Contains(f.Name)
                        || f.Type is FieldType.Email or FieldType.Phone or FieldType.Url
                        || (f.ShowInList && f.Type is FieldType.Picklist or FieldType.Text))
            .Where(f => f.Name is not ("name" or "title" or "subject" or "firstName" or "lastName" or "firstname" or "lastname"))
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(6)
            .ToList();

        if (picks.Count >= 3)
            return picks;

        foreach (var f in SummaryFields())
        {
            if (picks.Any(p => p.Id == f.Id)) continue;
            if (f.Name is "name" or "title" or "subject") continue;
            picks.Add(f);
            if (picks.Count >= 6) break;
        }

        return picks;
    }
}

public class RelatedRecordItem
{
    public string ModuleName { get; set; } = string.Empty;
    public string ModuleLabel { get; set; } = string.Empty;
    public int RecordId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? FieldLabel { get; set; }
}

public class RelatedRecordGroup
{
    public string Label { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    /// <summary>شناسه پایدار برای تب سایدبار (مثلاً rel-products).</summary>
    public string TabKey { get; set; } = string.Empty;
    public IReadOnlyList<RelatedRecordItem> Records { get; set; } = [];
}
