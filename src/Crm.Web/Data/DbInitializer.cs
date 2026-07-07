using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Crm.Web.Models;
using Crm.Web.Services;

namespace Crm.Web.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SiteDbContext>();

        await context.Database.EnsureCreatedAsync();
        await SeedTaxonomyIfEmptyAsync(context);
        await SeedArticlesIfEmptyAsync(context);
        await SeedFaqsIfEmptyAsync(context);
        await SeedSitePagesIfEmptyAsync(context);
        await SeedAdminAccountIfEmptyAsync(context, scope.ServiceProvider);
    }

    private static async Task SeedTaxonomyIfEmptyAsync(SiteDbContext context)
    {
        if (await context.ContentCategories.AnyAsync())
            return;

        context.ContentCategories.AddRange(
            new ContentCategory { Name = "مدیریت فروش", Slug = "sales-management", Type = ContentCategoryType.Article, SortOrder = 1 },
            new ContentCategory { Name = "بازاریابی", Slug = "marketing", Type = ContentCategoryType.Article, SortOrder = 2 },
            new ContentCategory { Name = "پشتیبانی مشتری", Slug = "customer-support", Type = ContentCategoryType.Article, SortOrder = 3 },
            new ContentCategory { Name = "آموزش CRM", Slug = "crm-training", Type = ContentCategoryType.Article, SortOrder = 4 });

        await context.SaveChangesAsync();
    }

    private static async Task SeedArticlesIfEmptyAsync(SiteDbContext context)
    {
        if (await context.Articles.AnyAsync())
            return;

        var seedDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var categories = await context.ContentCategories
            .Where(c => c.Type == ContentCategoryType.Article)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        int? CatId(string slug) => categories.FirstOrDefault(c => c.Slug == slug)?.Id;

        context.Articles.AddRange(
            new Article
            {
                Title = "CRM چیست و چرا کسب‌وکار شما به آن نیاز دارد؟",
                Slug = "what-is-crm",
                Summary = "آشنایی با مفهوم مدیریت ارتباط با مشتری و نقش آن در رشد فروش و وفادارسازی مشتریان.",
                ThumbnailUrl = "",
                CategoryId = CatId("crm-training"),
                PublishedAt = seedDate,
                Content = """
                    <div class="elementor-section" data-layout="full" data-padding-top="20" data-padding-bottom="20" data-gap="20" data-reverse-mobile="0">
                      <div class="elementor-columns">
                        <div class="elementor-column" data-width-desktop="100" data-width-tablet="100" data-width-mobile="100" style="--col-width-desktop:100%;--col-width-tablet:100%;--col-width-mobile:100%;">
                          <div class="elementor-widget" data-type="heading">
                            <div class="elementor-widget-content"><div class="widget-heading"><h2>مدیریت ارتباط با مشتری از اولین تماس تا وفاداری</h2></div></div>
                          </div>
                          <div class="elementor-widget" data-type="text">
                            <div class="elementor-widget-content"><div class="widget-text"><p>نرم‌افزار CRM تمام تعاملات کسب‌وکار شما با مشتریان را در یک بستر یکپارچه جمع می‌کند؛ از ثبت سرنخ و پیگیری فرصت‌های فروش تا صدور فاکتور و خدمات پس از فروش. نتیجه، دید شفاف مدیریتی و رشد پایدار فروش است.</p></div></div>
                          </div>
                        </div>
                      </div>
                    </div>
                    <div class="elementor-section" data-layout="two-thirds-one-third" data-padding-top="20" data-padding-bottom="20" data-gap="20" data-reverse-mobile="0">
                      <div class="elementor-columns">
                        <div class="elementor-column" data-width-desktop="66.666" data-width-tablet="100" data-width-mobile="100" style="--col-width-desktop:66.666%;--col-width-tablet:100%;--col-width-mobile:100%;">
                          <div class="elementor-widget" data-type="list">
                            <div class="elementor-widget-content"><div class="widget-list"><ul><li>ثبت و پیگیری سرنخ‌های فروش بدون از دست رفتن هیچ مشتری</li><li>قیف فروش بصری برای مدیریت فرصت‌ها</li><li>اتوماسیون کارهای تکراری مثل پیامک و ایمیل</li><li>گزارش‌های دقیق برای تصمیم‌گیری مدیریتی</li></ul></div></div>
                          </div>
                        </div>
                        <div class="elementor-column" data-width-desktop="33.333" data-width-tablet="100" data-width-mobile="100" style="--col-width-desktop:33.333%;--col-width-tablet:100%;--col-width-mobile:100%;">
                          <div class="elementor-widget" data-type="quote">
                            <div class="elementor-widget-content"><div class="widget-quote"><blockquote><p>کسب‌وکاری که مشتریانش را نشناسد، آن‌ها را به رقیب واگذار می‌کند.</p></blockquote></div></div>
                          </div>
                        </div>
                      </div>
                    </div>
                    """
            },
            new Article
            {
                Title = "قیف فروش چیست و چگونه نرخ تبدیل سرنخ را افزایش دهیم؟",
                Slug = "sales-funnel-guide",
                Summary = "مراحل قیف فروش و تکنیک‌های عملی برای تبدیل سرنخ به مشتری وفادار.",
                ThumbnailUrl = "",
                CategoryId = CatId("sales-management"),
                PublishedAt = seedDate.AddDays(5),
                Content = "<p>قیف فروش مسیر حرکت مشتری از آشنایی اولیه تا خرید نهایی را نشان می‌دهد. با مدیریت مرحله‌به‌مرحله فرصت‌ها در CRM، نقاط ریزش مشتری را شناسایی و نرخ تبدیل را افزایش دهید.</p>"
            },
            new Article
            {
                Title = "اتوماسیون بازاریابی؛ چگونه کارهای تکراری را به نرم‌افزار بسپاریم؟",
                Slug = "marketing-automation",
                Summary = "با گردش‌کارهای خودکار، پیگیری مشتریان را بدون دخالت دستی انجام دهید.",
                ThumbnailUrl = "",
                CategoryId = CatId("marketing"),
                PublishedAt = seedDate.AddDays(10),
                Content = "<p>ارسال پیامک خوش‌آمد، ارجاع خودکار سرنخ به کارشناس و یادآوری پیگیری، نمونه‌هایی از اتوماسیون‌هایی هستند که ساعت‌ها کار روزانه تیم فروش را حذف می‌کنند.</p>"
            });

        await context.SaveChangesAsync();
    }

    private static async Task SeedFaqsIfEmptyAsync(SiteDbContext context)
    {
        if (await context.FaqItems.AnyAsync())
            return;

        context.FaqItems.AddRange(
            new FaqItem
            {
                Question = "آیا امکان تست و بررسی امکانات قبل از خرید وجود دارد؟",
                Answer = "بله، بعد از ثبت‌نام رایگان می‌توانید به مدت ۱۰ روز از تمام امکانات نرم‌افزار به‌صورت آزمایشی استفاده کنید.",
                SortOrder = 1
            },
            new FaqItem
            {
                Question = "آیا نرم‌افزار قابلیت سفارشی‌سازی دارد؟",
                Answer = "بله، فرم‌ها، فیلدها، گزارش‌ها و فرآیندها قابل ویرایش هستند تا با ساختار کسب‌وکار شما هماهنگ شوند.",
                SortOrder = 2
            },
            new FaqItem
            {
                Question = "آموزش و پشتیبانی نرم‌افزار چگونه است؟",
                Answer = "آموزش و پشتیبانی رایگان است. بعد از ثبت‌نام، کارشناسان ما برای راه‌اندازی و پیاده‌سازی همراه شما خواهند بود.",
                SortOrder = 3
            },
            new FaqItem
            {
                Question = "چه کسب‌وکارهایی به CRM نیاز دارند؟",
                Answer = "همه کسب‌وکارها برای مدیریت فروش، جلب رضایت مشتریان و مدیریت هوشمند تیم‌ها به نرم‌افزار CRM نیاز دارند.",
                SortOrder = 4
            });

        await context.SaveChangesAsync();
    }

    private static async Task SeedSitePagesIfEmptyAsync(SiteDbContext context)
    {
        if (await context.SitePages.AnyAsync(p => p.Key == "about"))
            return;

        context.SitePages.Add(new SitePage
        {
            Key = "about",
            Title = "درباره ما",
            Subtitle = "بستری برای رشد کسب‌وکار شما",
            HeroImageUrl = "",
            Content = """
                <div class="elementor-section" data-layout="full" data-padding-top="0" data-padding-bottom="12" data-gap="16" data-reverse-mobile="0">
                  <div class="elementor-columns">
                    <div class="elementor-column" data-width-desktop="100" data-width-tablet="100" data-width-mobile="100" style="--col-width-desktop:100%;--col-width-tablet:100%;--col-width-mobile:100%;">
                      <div class="elementor-widget" data-type="heading">
                        <div class="elementor-widget-content"><div class="widget-heading"><h2>ماموریت ما</h2></div></div>
                      </div>
                      <div class="elementor-widget" data-type="text">
                        <div class="elementor-widget-content"><div class="widget-text"><p>ما بر آنیم تا با ارائه بهترین نرم‌افزارهای مدیریتی، مسیری امن و مطمئن برای رشد کسب‌وکارهای ایرانی فراهم کنیم. CRM ما نه فقط یک نرم‌افزار، بلکه یک راهکار برای پیشرفت شما در کسب‌وکار است.</p></div></div>
                      </div>
                    </div>
                  </div>
                </div>
                <div class="elementor-section" data-layout="two-thirds-one-third" data-padding-top="12" data-padding-bottom="12" data-gap="20" data-reverse-mobile="0">
                  <div class="elementor-columns">
                    <div class="elementor-column" data-width-desktop="66.666" data-width-tablet="100" data-width-mobile="100" style="--col-width-desktop:66.666%;--col-width-tablet:100%;--col-width-mobile:100%;">
                      <div class="elementor-widget" data-type="list">
                        <div class="elementor-widget-content"><div class="widget-list"><ul><li>مدیریت بازاریابی و سرنخ‌های فروش</li><li>مدیریت فروش و صدور فاکتور</li><li>مدیریت خدمات پس از فروش و تیکتینگ</li><li>اتوماسیون فرآیندها و گزارش‌های پیشرفته</li></ul></div></div>
                      </div>
                    </div>
                    <div class="elementor-column" data-width-desktop="33.333" data-width-tablet="100" data-width-mobile="100" style="--col-width-desktop:33.333%;--col-width-tablet:100%;--col-width-mobile:100%;">
                      <div class="elementor-widget" data-type="quote">
                        <div class="elementor-widget-content"><div class="widget-quote"><blockquote><p>ما موفقیت خود را در موفقیت مشتریانمان جستجو می‌کنیم.</p></blockquote></div></div>
                      </div>
                    </div>
                  </div>
                </div>
                """
        });

        await context.SaveChangesAsync();
    }

    private static async Task SeedAdminAccountIfEmptyAsync(SiteDbContext context, IServiceProvider serviceProvider)
    {
        if (await context.AdminAccounts.AnyAsync())
            return;

        var settings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminSettings>>().Value;
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<AdminAccount>();
        var account = new AdminAccount
        {
            Id = 1,
            Username = settings.Username
        };
        account.PasswordHash = hasher.HashPassword(account, settings.Password);

        context.AdminAccounts.Add(account);
        await context.SaveChangesAsync();
    }
}
