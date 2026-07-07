using Crm.Core.Entities;

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
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }

    /// <summary>fieldName → (recordId → title) برای نمایش فیلدهای Lookup در لیست.</summary>
    public Dictionary<string, Dictionary<string, string>> LookupTitles { get; set; } = new();

    /// <summary>آیا این ماژول فیلد مرحله‌ای برای نمای کاریز دارد؟</summary>
    public bool HasKanban { get; set; }
}

public class RecordFormViewModel
{
    public ModuleDef Module { get; set; } = null!;
    public IReadOnlyList<FieldDef> Fields { get; set; } = [];

    /// <summary>fieldId → دسترسی فیلد برای پروفایل جاری (Hidden/ReadOnly/Editable).</summary>
    public Dictionary<int, FieldAccess> FieldAccessMap { get; set; } = new();

    public int? RecordId { get; set; }
    public Dictionary<string, string?> Values { get; set; } = new();
    public Dictionary<string, string> Errors { get; set; } = new();

    /// <summary>fieldName → گزینه‌های ماژول مقصد برای فیلدهای Lookup.</summary>
    public Dictionary<string, List<(int Id, string Title)>> LookupOptions { get; set; } = new();

    public FieldAccess AccessFor(FieldDef field) =>
        FieldAccessMap.TryGetValue(field.Id, out var access) ? access : FieldAccess.Editable;
}

public class RecycleBinViewModel
{
    public IReadOnlyList<DynamicRecord> Records { get; set; } = [];
}
