namespace Crm.Infrastructure.Services;

/// <summary>پارامترهای لیست رکورد: جستجو، صفحه‌بندی، مرتب‌سازی و فیلتر ستون‌ها.</summary>
public class RecordListQuery
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortField { get; set; }
    public string SortDir { get; set; } = "desc";
    public List<ColumnFilter> Filters { get; set; } = [];
}

public class ColumnFilter
{
    public string Field { get; set; } = string.Empty;
    /// <summary>contains | notcontains | startswith | endswith | equals | notequals | isempty | isnotempty</summary>
    public string Op { get; set; } = "contains";
    public string Value { get; set; } = string.Empty;
}
