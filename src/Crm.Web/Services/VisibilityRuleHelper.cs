using System.Text.Json;
using System.Text.Json.Nodes;

namespace Crm.Web.Services;

/// <summary>
/// Parse / normalize / summarize VisibilityRuleJson.
/// Legacy: {"field","op","value"} — New: {"action":"show","logic":"and","conditions":[{...}]}
/// </summary>
public static class VisibilityRuleHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public sealed record Condition(string Field, string Op, string Value);

    public sealed record Rule(string Action, string Logic, IReadOnlyList<Condition> Conditions);

    public static Rule? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var action = root.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String
                ? (a.GetString() ?? "show")
                : "show";
            var logic = root.TryGetProperty("logic", out var l) && l.ValueKind == JsonValueKind.String
                ? (l.GetString() ?? "and")
                : "and";

            var conditions = new List<Condition>();

            if (root.TryGetProperty("conditions", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var c = ReadCondition(item);
                    if (c is not null)
                        conditions.Add(c);
                }
            }
            else
            {
                var legacy = ReadCondition(root);
                if (legacy is not null)
                    conditions.Add(legacy);
            }

            if (conditions.Count == 0)
                return null;

            return new Rule(action, logic, conditions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Condition? ReadCondition(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return null;
        if (!el.TryGetProperty("field", out var f) || f.ValueKind != JsonValueKind.String)
            return null;
        var field = f.GetString();
        if (string.IsNullOrWhiteSpace(field))
            return null;
        var op = el.TryGetProperty("op", out var o) && o.ValueKind == JsonValueKind.String
            ? (o.GetString() ?? "eq")
            : "eq";
        var value = el.TryGetProperty("value", out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
            ? v.ToString()
            : "";
        return new Condition(field.Trim(), (op ?? "eq").Trim().ToLowerInvariant(), value ?? "");
    }

    /// <summary>Normalize to new shape, or null/empty if no valid conditions.</summary>
    public static string? Normalize(string? json)
    {
        var rule = Parse(json);
        if (rule is null || rule.Conditions.Count == 0)
            return null;

        var node = new JsonObject
        {
            ["action"] = string.IsNullOrWhiteSpace(rule.Action) ? "show" : rule.Action,
            ["logic"] = string.IsNullOrWhiteSpace(rule.Logic) ? "and" : rule.Logic,
            ["conditions"] = new JsonArray(
                rule.Conditions.Select(c => (JsonNode)new JsonObject
                {
                    ["field"] = c.Field,
                    ["op"] = string.IsNullOrWhiteSpace(c.Op) ? "eq" : c.Op,
                    ["value"] = c.Value ?? ""
                }).ToArray())
        };
        return node.ToJsonString(JsonOpts);
    }

    public static string Build(IEnumerable<Condition> conditions, string action = "show", string logic = "and")
    {
        var list = conditions
            .Where(c => !string.IsNullOrWhiteSpace(c.Field))
            .Select(c => new Condition(c.Field.Trim(), string.IsNullOrWhiteSpace(c.Op) ? "eq" : c.Op.Trim().ToLowerInvariant(), c.Value ?? ""))
            .ToList();
        if (list.Count == 0)
            return "";

        return Normalize(JsonSerializer.Serialize(new
        {
            action,
            logic,
            conditions = list.Select(c => new { field = c.Field, op = c.Op, value = c.Value })
        }, JsonOpts)) ?? "";
    }

    public static string OpLabelFa(string op) => (op ?? "eq").ToLowerInvariant() switch
    {
        "neq" => "نابرابر با",
        "contains" => "شامل شده باشد با",
        _ => "برابر با"
    };

    public static string ActionLabelFa(string? action) =>
        "نمایش (خواندن + نوشتن)";

    /// <summary>خلاصه شرط‌ها به فارسی: «اگر نوع شخص برابر با حقوقی»</summary>
    public static string SummarizeConditionsFa(
        Rule rule,
        Func<string, string> fieldLabel,
        Func<string, string, string>? valueLabel = null)
    {
        if (rule.Conditions.Count == 0)
            return "";

        var parts = rule.Conditions.Select(c =>
        {
            var fl = fieldLabel(c.Field);
            var vl = valueLabel?.Invoke(c.Field, c.Value) ?? c.Value;
            return $"{fl} {OpLabelFa(c.Op)} {vl}".Trim();
        });

        var joined = string.Join(" و ", parts);
        return "اگر " + joined;
    }

    public static bool EvaluateCondition(string current, string op, string expected)
    {
        current ??= "";
        expected ??= "";
        op = (op ?? "eq").ToLowerInvariant();
        return op switch
        {
            "neq" => current != expected,
            "contains" => current.Contains(expected, StringComparison.Ordinal),
            _ => current == expected
        };
    }
}
