using Microsoft.AspNetCore.Http;

namespace Crm.Web.Areas.App.Models;

/// <summary>پارس فیلتر/مرتب‌سازی از QueryString برای جداول CrmTable.</summary>
public static class CrmTableQuery
{
    public static Dictionary<string, (string Op, string? Value)> ParseFilters(
        IQueryCollection query, IEnumerable<string>? allowedKeys = null)
    {
        var allowed = allowedKeys?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filters = new Dictionary<string, (string Op, string? Value)>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in query.Keys)
        {
            if (!key.StartsWith("cf_", StringComparison.OrdinalIgnoreCase))
                continue;

            var field = key[3..];
            if (allowed is not null && !allowed.Contains(field))
                continue;

            var op = query.TryGetValue($"op_{field}", out var opVal) && !string.IsNullOrWhiteSpace(opVal)
                ? opVal.ToString()!
                : "contains";
            var value = (query[key].ToString() ?? "").Trim();
            if (CrmTableModel.OpNeedsValue(op) && string.IsNullOrWhiteSpace(value))
                continue;

            filters[field] = (op, value);
        }

        return filters;
    }

    public static bool MatchText(string? haystack, string op, string? needle)
    {
        var h = haystack ?? "";
        var n = needle ?? "";
        return op.ToLowerInvariant() switch
        {
            "equals" => string.Equals(h, n, StringComparison.OrdinalIgnoreCase),
            "notequals" => !string.Equals(h, n, StringComparison.OrdinalIgnoreCase),
            "startswith" => h.StartsWith(n, StringComparison.OrdinalIgnoreCase),
            "endswith" => h.EndsWith(n, StringComparison.OrdinalIgnoreCase),
            "isempty" => string.IsNullOrWhiteSpace(h),
            "isnotempty" => !string.IsNullOrWhiteSpace(h),
            _ => h.Contains(n, StringComparison.OrdinalIgnoreCase)
        };
    }

    public static Dictionary<string, string?> PagingRoutes(
        Dictionary<string, (string Op, string? Value)> filters,
        string? q = null,
        string? sort = null,
        string? dir = null,
        IEnumerable<KeyValuePair<string, string?>>? extra = null)
    {
        var routes = new Dictionary<string, string?>();
        if (extra is not null)
        {
            foreach (var kv in extra)
                routes[kv.Key] = kv.Value;
        }
        if (!string.IsNullOrWhiteSpace(q))
            routes["q"] = q;
        if (!string.IsNullOrWhiteSpace(sort))
        {
            routes["sort"] = sort;
            routes["dir"] = string.IsNullOrWhiteSpace(dir) ? "desc" : dir;
        }
        foreach (var (key, (op, value)) in filters)
        {
            if (CrmTableModel.OpNeedsValue(op) && string.IsNullOrWhiteSpace(value))
                continue;
            routes[$"cf_{key}"] = value ?? "";
            routes[$"op_{key}"] = string.IsNullOrWhiteSpace(op) ? "contains" : op;
        }
        return routes;
    }
}
