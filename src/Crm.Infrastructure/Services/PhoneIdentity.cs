namespace Crm.Infrastructure.Services;

/// <summary>نرمال‌سازی و مقایسه شماره تماس در کل CRM.</summary>
public static class PhoneIdentity
{
    public static string? Canonical(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains('*'))
            return null;

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return null;
        if (digits.StartsWith("0098", StringComparison.Ordinal))
            digits = digits[4..];
        else if (digits.StartsWith("98", StringComparison.Ordinal) && digits.Length > 10)
            digits = digits[2..];
        if (digits.StartsWith('0'))
            digits = digits[1..];
        return digits.Length >= 8 ? digits : null;
    }

    /// <summary>چند شماره داخل یک فیلد را جدا می‌کند؛ فاصله فقط وقتی هر تکه خودش شماره باشد.</summary>
    public static IReadOnlyList<string> Split(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var result = new List<string>();
        var chunks = raw.Split(['\n', '\r', ',', '،', ';', '؛', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var chunk in chunks)
        {
            var tokens = chunk.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length > 1 && tokens.All(token => token.Count(char.IsDigit) >= 8))
            {
                foreach (var token in tokens)
                    AddDistinct(result, token);
            }
            else
            {
                AddDistinct(result, chunk);
            }
        }

        return result;
    }

    public static string Join(IEnumerable<string> phones) =>
        string.Join('\n', phones.Select(phone => phone.Trim()).Where(phone => phone.Length > 0));

    private static void AddDistinct(List<string> list, string phone)
    {
        var text = phone.Trim();
        if (text.Length == 0)
            return;
        if (list.Any(existing => Same(existing, text) || existing.Equals(text, StringComparison.Ordinal)))
            return;
        list.Add(text);
    }

    public static bool Same(string? a, string? b) =>
        Canonical(a) is { } left && Canonical(b) is { } right && left == right;

    /// <summary>اشکال رایج ذخیره‌شده برای جستجوی SQL.</summary>
    public static IReadOnlyList<string> SearchVariants(string? raw)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(raw))
            set.Add(raw.Trim());

        var canon = Canonical(raw);
        if (canon is null)
            return set.ToList();

        set.Add(canon);
        set.Add("0" + canon);
        set.Add("98" + canon);
        set.Add("0098" + canon);
        set.Add("+98" + canon);
        return set.ToList();
    }

    public static string? FromRecord(IReadOnlyDictionary<string, string?> data)
    {
        foreach (var key in new[] { "mobile", "phone", "phoneNumber" })
        {
            if (data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
