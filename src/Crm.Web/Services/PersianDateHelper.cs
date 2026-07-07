using System.Globalization;

namespace Crm.Web.Services;

public static class PersianDateHelper
{
    public static string ToJalaliDate(DateTime dateTime)
    {
        var dt = Normalize(dateTime);
        var pc = new PersianCalendar();
        return PersianFormattingHelper.ToPersianDigits(
            $"{pc.GetYear(dt):0000}/{pc.GetMonth(dt):00}/{pc.GetDayOfMonth(dt):00}");
    }

    public static string ToJalaliDateTime(DateTime dateTime)
    {
        var dt = Normalize(dateTime);
        var pc = new PersianCalendar();
        return PersianFormattingHelper.ToPersianDigits(
            $"{pc.GetYear(dt):0000}/{pc.GetMonth(dt):00}/{pc.GetDayOfMonth(dt):00} {dt.Hour:00}:{dt.Minute:00}");
    }

    /// <summary>مقدار ISO برای مقداردهی اولیه flatpickr (تاریخ).</summary>
    public static string ToIsoDate(DateTime dateTime) => Normalize(dateTime).ToString("yyyy-MM-dd");

    /// <summary>مقدار ISO برای مقداردهی اولیه flatpickr (تاریخ و ساعت).</summary>
    public static string ToIsoDateTime(DateTime dateTime) => Normalize(dateTime).ToString("yyyy-MM-dd'T'HH:mm");

    public static string? ToIsoDate(DateTime? dateTime) => dateTime is null ? null : ToIsoDate(dateTime.Value);

    public static string? ToIsoDateTime(DateTime? dateTime) => dateTime is null ? null : ToIsoDateTime(dateTime.Value);

    public static DateTime FromJalaliDateTime(string? jalali)
    {
        if (string.IsNullOrWhiteSpace(jalali))
            return DateTime.UtcNow;

        jalali = PersianFormattingHelper.FromPersianDigits(jalali);

        var parts = jalali.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var dateParts = parts[0].Split('/', '-');
        if (dateParts.Length != 3)
            return DateTime.UtcNow;

        if (!int.TryParse(dateParts[0], out var year) ||
            !int.TryParse(dateParts[1], out var month) ||
            !int.TryParse(dateParts[2], out var day))
            return DateTime.UtcNow;

        var hour = 0;
        var minute = 0;
        if (parts.Length > 1)
        {
            var timeParts = parts[1].Split(':');
            if (timeParts.Length >= 2)
            {
                int.TryParse(timeParts[0], out hour);
                int.TryParse(timeParts[1], out minute);
            }
        }

        var pc = new PersianCalendar();
        return pc.ToDateTime(year, month, day, hour, minute, 0, 0);
    }

    /// <summary>
    /// نمایش شمسی یک مقدار متنی ISO (مثل «2026-07-07» یا «2026-07-07T14:30») که در رکوردهای داینامیک ذخیره شده است.
    /// اگر مقدار تاریخ معتبر نباشد، همان متن برگردانده می‌شود.
    /// </summary>
    public static string ToJalaliFromIso(string? isoValue)
    {
        if (string.IsNullOrWhiteSpace(isoValue))
            return "—";

        var s = isoValue.Trim().Replace("T", " ");
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return isoValue;

        var hasTime = dt.TimeOfDay != TimeSpan.Zero || s.Contains(':');
        var pc = new PersianCalendar();
        var formatted = $"{pc.GetYear(dt):0000}/{pc.GetMonth(dt):00}/{pc.GetDayOfMonth(dt):00}";
        if (hasTime)
            formatted += $" {dt.Hour:00}:{dt.Minute:00}";
        return PersianFormattingHelper.ToPersianDigits(formatted);
    }

    private static DateTime Normalize(DateTime dateTime)
    {
        return dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime.ToLocalTime(),
            _ => dateTime
        };
    }
}
