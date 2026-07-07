namespace Crm.Web.Models.Help;

/// <summary>یک موضوع آموزشی (مثلاً «پروژه‌ها») شامل معرفی، آموزش صفحه‌به‌صفحه، مثال و نکات.</summary>
public class HelpTopic
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>آیکون boxicons مثل bx-briefcase-alt-2.</summary>
    public string Icon { get; set; } = "bx-book-open";

    /// <summary>خلاصه یک‌خطی برای کارت صفحه فهرست آموزش.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>معرفی بخش: به چه دردی می‌خورد و چه زمانی سراغش برویم.</summary>
    public string Intro { get; set; } = string.Empty;

    /// <summary>آموزش صفحه‌به‌صفحه (لیست، ایجاد، ویرایش، جزئیات، صفحه‌های خاص).</summary>
    public List<HelpSection> Sections { get; set; } = [];

    /// <summary>مثال‌های کاربردی فرضی سرتاسری.</summary>
    public List<HelpExample> Examples { get; set; } = [];

    /// <summary>سؤالات پرتکرار و اشتباهات رایج.</summary>
    public List<string> Tips { get; set; } = [];

    /// <summary>صفحات مرتبط این بخش (لیست، ایجاد، ویرایش، جزئیات).</summary>
    public List<HelpPageLink> Pages { get; set; } = [];

    /// <summary>فیلدهای مهم و ارتباط هرکدام با سایر بخش‌ها.</summary>
    public List<HelpFieldGuide> Fields { get; set; } = [];

    /// <summary>روابط این بخش با ماژول‌ها، صفحات و موجودیت‌های دیگر.</summary>
    public List<HelpRelation> Relations { get; set; } = [];

    /// <summary>اسلاگ موضوعات آموزشی مرتبط برای لینک در سایدبار.</summary>
    public List<string> RelatedTopicSlugs { get; set; } = [];
}

/// <summary>آموزش یک صفحه یا یک قابلیت مشخص از بخش.</summary>
public class HelpSection
{
    public string Title { get; set; } = string.Empty;

    /// <summary>مسیر صفحه، مثل /App/projects — برای نمایش و لینک مستقیم.</summary>
    public string? Route { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>گام‌های انجام کار به‌ترتیب.</summary>
    public List<string> Steps { get; set; } = [];
}

/// <summary>مثال کاربردی فرضی با سناریوی گام‌به‌گام.</summary>
public class HelpExample
{
    public string Title { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = [];
}

/// <summary>یک صفحه UI مرتبط با این موضوع آموزشی.</summary>
public class HelpPageLink
{
    public string Title { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
}

/// <summary>راهنمای یک فیلد: کاربرد و اتصال به سایر بخش‌ها.</summary>
public class HelpFieldGuide
{
    public string Label { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;

    /// <summary>این فیلد به چه موجودیت/صفحه/ماژولی وصل می‌شود.</summary>
    public string? ConnectsTo { get; set; }
}

/// <summary>رابطه بین این بخش و بخش دیگر CRM.</summary>
public class HelpRelation
{
    /// <summary>عنوان بخش/موجودیت مقصد.</summary>
    public string Target { get; set; } = string.Empty;

    public string? TargetRoute { get; set; }
    public string? HelpSlug { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class HelpIndexViewModel
{
    public string Title { get; set; } = "مرکز آموزش";
    public string Description { get; set; } = string.Empty;

    /// <summary>مثل /App/help — برای ساخت لینک موضوع‌ها.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public List<HelpTopic> Topics { get; set; } = [];
}

public class HelpTopicViewModel
{
    public HelpTopic Topic { get; set; } = null!;
    public string BaseUrl { get; set; } = string.Empty;
    public List<HelpTopic> AllTopics { get; set; } = [];
}

/// <summary>لینک راهنمای درج‌شده در بالای صفحات دارای آموزش.</summary>
public class PageHelpLinkModel
{
    public string Url { get; set; } = string.Empty;
    public string TopicTitle { get; set; } = string.Empty;

    /// <summary>عنوان صفحه/بخش منطبق (در صورت وجود).</summary>
    public string? MatchedLabel { get; set; }
}
