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

    private static DateTime Normalize(DateTime dateTime)
    {
        return dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime.ToLocalTime(),
            _ => dateTime
        };
    }
}
