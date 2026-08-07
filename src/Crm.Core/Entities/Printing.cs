namespace Crm.Core.Entities;

/// <summary>قالب چاپ HTML برای یک ماژول — سربرگ/بدنه/پاورقی با توکن‌های جایگذاری.</summary>
public class PrintTemplate : TenantEntity
{
    public int ModuleId { get; set; }
    public string Name { get; set; } = "";
    public bool IsHtmlEditor { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }

    /// <summary>موتور تولید خروجی: browser (دیالوگ چاپ) | gutenberg (سرویس PDF).</summary>
    public string ServiceProvider { get; set; } = "browser";

    // ── تنظیمات صفحه ───────────────────────────────────────────────
    public string PageSize { get; set; } = "A4";
    public bool Landscape { get; set; }

    /// <summary>rtl | ltr</summary>
    public string TextDirection { get; set; } = "rtl";

    /// <summary>اسلاگ فونت فارسی (shabnam، vazir، iran-sans …).</summary>
    public string FontFamily { get; set; } = "shabnam";
    public int FontSize { get; set; } = 12;
    public string? CustomCss { get; set; }

    // ── تنظیمات حاشیه (میلی‌متر) ────────────────────────────────────
    public int MarginTop { get; set; } = 12;
    public int MarginRight { get; set; } = 12;
    public int MarginBottom { get; set; } = 12;
    public int MarginLeft { get; set; } = 12;

    /// <summary>تکرار سربرگ در تمام صفحات چاپی.</summary>
    public bool RepeatHeaderEachPage { get; set; }
    public bool ShowPageNumbers { get; set; }

    // ── واترمارک ──────────────────────────────────────────────────
    public bool WatermarkEnabled { get; set; }

    /// <summary>text | image</summary>
    public string WatermarkType { get; set; } = "text";
    public string? WatermarkText { get; set; }
    public string? WatermarkImagePath { get; set; }

    /// <summary>شفافیت به درصد (۱ تا ۱۰۰).</summary>
    public int WatermarkOpacity { get; set; } = 12;
    public int WatermarkRotation { get; set; } = -30;
    public int WatermarkFontSize { get; set; } = 72;
    public string? WatermarkColor { get; set; } = "#9e9e9e";

    // ── تنظیمات فایل ──────────────────────────────────────────────
    /// <summary>الگوی نام فایل خروجی؛ می‌تواند توکن داشته باشد مثل {$RECORD.number}.</summary>
    public string? FileNamePattern { get; set; }
    public bool AllowPdf { get; set; } = true;
    public bool AllowWord { get; set; } = true;

    // ── محتوا ─────────────────────────────────────────────────────
    public string? HeaderHtml { get; set; }
    public string? BodyHtml { get; set; }
    public string? FooterHtml { get; set; }

    public bool ShareWithAllRoles { get; set; } = true;
}

/// <summary>اشتراک قالب چاپ با نقش‌های مشخص (وقتی ShareWithAllRoles=false).</summary>
public class PrintTemplateRole : TenantEntity
{
    public int PrintTemplateId { get; set; }
    public int RoleId { get; set; }
}

/// <summary>فهرست فونت‌های فارسی قابل استفاده در قالب‌های چاپ.</summary>
public static class PrintFonts
{
    /// <summary>اسلاگ فایل فونت ↦ برچسب فارسی.</summary>
    public static readonly IReadOnlyList<(string Slug, string Label)> All =
    [
        ("shabnam", "شبنم"),
        ("vazir", "وزیر"),
        ("iran-sans", "ایران‌سنس"),
        ("iran-sans-cd", "ایران‌سنس فشرده"),
        ("iran-yekan", "ایران‌یکان"),
        ("iran-yekan-cd", "ایران‌یکان فشرده"),
        ("dana", "دانا"),
        ("estedad", "استعداد"),
        ("mikhak", "میخک"),
        ("mikhak-sd", "میخک سایه‌دار"),
        ("pinar", "پینار"),
        ("pinar-sd", "پینار سایه‌دار"),
        ("noora", "نورا"),
        ("azarmehr", "آذرمهر"),
        ("azarmehr-cd", "آذرمهر فشرده"),
        ("dubai", "دبی"),
        ("myriad", "مریاد"),
        ("palatino-sans", "پالاتینو"),
        ("helvetica-neue", "هلوتیکا"),
        ("vanda", "وندا"),
        ("pofak", "پفک"),
        ("dastnevis", "دست‌نویس")
    ];

    public static bool IsKnown(string? slug) =>
        !string.IsNullOrWhiteSpace(slug) &&
        All.Any(f => string.Equals(f.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static string Label(string? slug) =>
        All.FirstOrDefault(f => string.Equals(f.Slug, slug, StringComparison.OrdinalIgnoreCase)).Label ?? "شبنم";
}

/// <summary>اندازه‌های صفحه پشتیبانی‌شده و ابعاد میلی‌متری آن‌ها (عمودی).</summary>
public static class PrintPageSizes
{
    public static readonly IReadOnlyList<(string Name, int WidthMm, int HeightMm)> All =
    [
        ("A3", 297, 420),
        ("A4", 210, 297),
        ("A5", 148, 210),
        ("B5", 176, 250),
        ("Letter", 216, 279),
        ("Legal", 216, 356)
    ];

    public static (int Width, int Height) Dimensions(string? name, bool landscape)
    {
        var entry = All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry.Name is null)
            entry = All.First(s => s.Name == "A4");
        return landscape ? (entry.HeightMm, entry.WidthMm) : (entry.WidthMm, entry.HeightMm);
    }
}
