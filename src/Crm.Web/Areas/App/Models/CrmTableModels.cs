namespace Crm.Web.Areas.App.Models;

public enum CrmTableFilterKind
{
    None = 0,
    Text = 1,
    Select = 2
}

public class CrmTableColumn
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Sortable { get; set; } = true;
    public CrmTableFilterKind FilterKind { get; set; } = CrmTableFilterKind.Text;
    public IReadOnlyList<(string Value, string Label)> SelectOptions { get; set; } = [];
    public string? CssClass { get; set; }
}

public class CrmTableRow
{
    public string Id { get; set; } = "";
    public Dictionary<string, string> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? DetailUrl { get; set; }
    public string? EditUrl { get; set; }
    public string? DeleteUrl { get; set; }
    public string? DeleteConfirm { get; set; }
    /// <summary>HTML اضافی در ستون عملیات (قبل از دکمه‌های استاندارد).</summary>
    public string? ExtraActionsHtml { get; set; }
}

/// <summary>مدل عمومی جدول لیست App — ظاهر مشترک با لیست ماژول‌های پویا.</summary>
public class CrmTableModel
{
    public IReadOnlyList<CrmTableColumn> Columns { get; set; } = [];
    public IReadOnlyList<CrmTableRow> Rows { get; set; } = [];

    public string? SortField { get; set; }
    public string SortDir { get; set; } = "desc";
    public string? Search { get; set; }

    /// <summary>فیلتر ستونی: key → (op, value)</summary>
    public Dictionary<string, (string Op, string? Value)> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool SelectionEnabled { get; set; } = true;
    public bool CanDelete { get; set; }
    public string? BulkDeleteUrl { get; set; }
    public string BulkDeleteConfirm { get; set; } = "موارد انتخاب‌شده حذف شوند؟";

    public bool ShowColumnFilters { get; set; } = true;
    public bool ShowColumnManager { get; set; }
    public bool ShowSearchBox { get; set; }
    public bool ShowFilterBar { get; set; } = true;
    public bool ShowFooter { get; set; } = true;
    public string SearchPlaceholder { get; set; } = "جستجو...";
    /// <summary>HTML اضافی در نوار فیلتر (مثلاً select وضعیت) — باید form=crmDtFilterForm داشته باشد.</summary>
    public string? FilterExtraHtml { get; set; }
    public string? ColumnManagerModalId { get; set; } = "columnManagerModal";

    /// <summary>مسیر پایه برای پاک‌کردن فیلتر / لینک‌های sort (مثلاً /App/products).</summary>
    public string ListPath { get; set; } = "";

    /// <summary>نام action فرم GET فیلتر (معمولاً Index).</summary>
    public string FormAction { get; set; } = "Index";

    /// <summary>route values ثابت برای فرم (مثلاً type برای Finance).</summary>
    public Dictionary<string, string> FormRouteValues { get; set; } = new();

    public string EmptyMessage { get; set; } = "هنوز موردی ثبت نشده است.";
    public string EmptyFilteredMessage { get; set; } = "با این فیلتر موردی پیدا نشد.";

    public static IReadOnlyList<(string Op, string Label)> TextFilterOps { get; } =
    [
        ("contains", "شامل شده باشد با"),
        ("equals", "مساوی"),
        ("notequals", "نابرابر"),
        ("startswith", "شروع شده با"),
        ("endswith", "خاتمه یافته باشد با"),
        ("isempty", "خالی باشد"),
        ("isnotempty", "خالی نباشد")
    ];

    public static IReadOnlyList<(string Op, string Label)> SelectFilterOps { get; } =
    [
        ("equals", "مساوی"),
        ("notequals", "نابرابر"),
        ("isempty", "خالی باشد"),
        ("isnotempty", "خالی نباشد")
    ];

    public static bool OpNeedsValue(string op) =>
        !string.Equals(op, "isempty", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(op, "isnotempty", StringComparison.OrdinalIgnoreCase);

    public string? FilterValue(string key) =>
        Filters.TryGetValue(key, out var f) ? f.Value : null;

    public string FilterOp(string key, string defaultOp = "contains") =>
        Filters.TryGetValue(key, out var f) && !string.IsNullOrWhiteSpace(f.Op) ? f.Op : defaultOp;

    public Dictionary<string, string> RouteValues(int? page = null, string? sort = null, string? dir = null)
    {
        var routes = new Dictionary<string, string>(FormRouteValues);
        if (!string.IsNullOrWhiteSpace(Search))
            routes["q"] = Search!;
        if (page is int p)
            routes["page"] = p.ToString();

        var sortField = sort ?? SortField;
        var sortDir = dir ?? SortDir;
        if (!string.IsNullOrWhiteSpace(sortField))
        {
            routes["sort"] = sortField!;
            routes["dir"] = string.IsNullOrWhiteSpace(sortDir) ? "desc" : sortDir;
        }

        foreach (var (key, (op, value)) in Filters)
        {
            var needsValue = OpNeedsValue(op);
            if (needsValue && string.IsNullOrWhiteSpace(value))
                continue;
            routes[$"cf_{key}"] = value ?? "";
            routes[$"op_{key}"] = string.IsNullOrWhiteSpace(op) ? "contains" : op;
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
