namespace Crm.Core;

/// <summary>
/// تنها منبع نام محصول در کل سیستم.
/// برای تغییر برند، فقط بخش Branding در appsettings را ویرایش کنید.
/// </summary>
public class BrandingOptions
{
    public const string SectionName = "Branding";

    /// <summary>نام فارسی محصول (برای متن‌های فارسی).</summary>
    public string ProductName { get; set; } = "CRM";

    /// <summary>نام انگلیسی محصول — لوگو و هدر از این استفاده می‌کنند.</summary>
    public string? ProductNameEn { get; set; }

    /// <summary>پسوند رنگی لوگو انگلیسی (مثلاً CRM در AcmeCRM).</summary>
    public string? BrandAccentEn { get; set; }

    /// <summary>ایمیل حساب دموی پنل ادمین.</summary>
    public string DemoEmail { get; set; } = "demo@crm.local";

    /// <summary>نام فارسی برای جملات فارسی.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(ProductName) ? LatinName : ProductName.Trim();

    /// <summary>نام انگلیسی برای لوگو، هدر، فوتر و عنوان‌ها.</summary>
    public string LatinName =>
        !string.IsNullOrWhiteSpace(ProductNameEn) ? ProductNameEn.Trim()
        : (string.IsNullOrWhiteSpace(ProductName) ? "CRM" : ProductName.Trim());

    public string PrintCredit => $"تهیه‌شده با {LatinName}";

    public string CopyrightLine(string yearPersian = "۱۴۰۵") =>
        $"© {yearPersian} {LatinName}. تمامی حقوق محفوظ است.";

    public string CopyrightLineShort(string yearPersian = "۱۴۰۵") =>
        $"© {yearPersian} {LatinName} — تمامی حقوق محفوظ است.";

    /// <summary>لوگو دو‌رنگ: پیشوند + پسوند رنگی از نام انگلیسی.</summary>
    public bool HasSplitMark =>
        !string.IsNullOrWhiteSpace(BrandAccentEn)
        && LatinName.EndsWith(BrandAccentEn, StringComparison.OrdinalIgnoreCase)
        && LatinName.Length > BrandAccentEn.Length;

    public string BrandPrefix =>
        HasSplitMark ? LatinName[..^BrandAccentEn!.Length] : LatinName;

    public string BrandAccentText =>
        HasSplitMark ? LatinName[^BrandAccentEn!.Length..] : string.Empty;
}
