using System.Globalization;

namespace Crm.Web.Services;

public static class PersianFormattingHelper
{
    private static readonly char[] PersianDigits = ['۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹'];

    public static string ToPersianDigits(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9')
                chars[i] = PersianDigits[chars[i] - '0'];
        }

        return new string(chars);
    }

    public static string FromPersianDigits(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '۰' && chars[i] <= '۹')
                chars[i] = (char)('0' + (chars[i] - '۰'));
        }

        return new string(chars);
    }

    public static string FormatNumber(decimal value, string format = "N0")
        => ToPersianDigits(value.ToString(format, CultureInfo.InvariantCulture));

    public static string FormatNumber(int value)
        => ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    public static string FormatNumber(long value)
        => ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));
}

public static class PersianFormattingExtensions
{
    public static string ToFa(this decimal value, string format = "N0")
        => PersianFormattingHelper.FormatNumber(value, format);

    public static string ToFa(this int value)
        => PersianFormattingHelper.FormatNumber(value);

    public static string ToFa(this long value)
        => PersianFormattingHelper.FormatNumber(value);

    public static string ToFa(this string? value)
        => PersianFormattingHelper.ToPersianDigits(value);
}
