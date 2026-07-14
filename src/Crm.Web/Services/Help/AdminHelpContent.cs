using Crm.Web.Models.Help;

namespace Crm.Web.Services.Help;

/// <summary>محتوای آموزش پنل مدیریت اصلی (Area: Admin).</summary>
public static class AdminHelpContent
{
    private static List<HelpTopic>? _topics;

    public static List<HelpTopic> Topics => _topics ??= BuildTopics();

    public static HelpTopic? Find(string slug) =>
        Topics.FirstOrDefault(t => t.Slug == slug);

    private static List<HelpTopic> BuildTopics() =>
    [
        new HelpTopic
        {
            Slug = "admin-map",
            Title = "نقشه پنل مدیریت",
            Icon = "bx-sitemap",
            Summary = "روابط مشتری (Tenant)، اشتراک، تراکنش و محتوای سایت",
            Intro = """
                پنل /Admin برای مالک پلتفرم BamaCRM است — نه برای مشتریان CRM.
                Tenant (مشتری SaaS) ← Subscription (اشتراک) ← Plan (پلن)
                SubscriptionPayment / PaymentTransaction ← درآمد پلتفرم
                Articles / SitePages / FAQ ← محتوای سایت عمومی marketing
                """,
            Pages =
            [
                new() { Title = "داشبورد", Route = "/Admin", Purpose = "آمار Tenant، درآمد، محتوا" },
                new() { Title = "مشتریان", Route = "/Admin/Tenants", Purpose = "Tenant + TrialEndsAtUtc" },
                new() { Title = "اشتراک‌ها", Route = "/Admin/Subscriptions", Purpose = "StartsAt/EndsAt شمسی" }
            ],
            Relations =
            [
                new() { Target = "Tenant", TargetRoute = "/Admin/Tenants", HelpSlug = "tenants",
                    Description = "هر Tenant = یک شرکت CRM — Users، Records، Subscriptions زیرمجموعه." },
                new() { Target = "Plan", TargetRoute = "/Admin/Plans", HelpSlug = "plans",
                    Description = "MaxUsers/MaxRecords → محدودیت Tenant در App." },
                new() { Target = "Subscription", HelpSlug = "subscriptions",
                    Description = "TenantId + PlanId + StartsAtUtc/EndsAtUtc." },
                new() { Target = "تراکنش", HelpSlug = "transactions",
                    Description = "PaymentTransaction ← پرداخت آنلاین یا دستی." }
            ],
            RelatedTopicSlugs = ["tenants", "subscriptions", "plans", "transactions"]
        },

        new HelpTopic
        {
            Slug = "tenants",
            Title = "مشتریان (Tenant)",
            Icon = "bx-buildings",
            Summary = "مدیریت شرکت‌های مشترک CRM — وضعیت، ورود به پنل، حذف",
            Intro = "Tenant موجودیت اصلی multi-tenancy است. Slug شناسه URL داخلی است. Status: Trial / Active / Suspended.",
            Pages =
            [
                new() { Title = "لیست", Route = "/Admin/Tenants", Purpose = "CreatedAtUtc، TrialEndsAtUtc شمسی" },
                new() { Title = "جزئیات", Route = "/Admin/Tenants/{id}", Purpose = "کاربران، اشتراک‌ها، حذف" }
            ],
            Fields =
            [
                new() { Label = "Slug", Purpose = "شناسه یکتا — برای تأیید حذف", ConnectsTo = "TenantContext.TenantId در App" },
                new() { Label = "Status", Purpose = "Trial/Active/Suspended", ConnectsTo = "TenantLifecycleService.HasAccess → App/Expired" },
                new() { Label = "TrialEndsAtUtc", Purpose = "پایان آزمایشی", ConnectsTo = "subscriptions — تمدید" },
                new() { Label = "CreatedAtUtc", Purpose = "تاریخ ثبت", ConnectsTo = "PersianDateHelper" }
            ],
            Relations =
            [
                new() { Target = "اشتراک", HelpSlug = "subscriptions", TargetRoute = "/Admin/Subscriptions/Create?tenantId=",
                    Description = "ثبت دستی یا هدیه از صفحه جزئیات Tenant." },
                new() { Target = "ورود به پنل مشتری", Description = "Impersonate → کوکی Identity ادمین Tenant → /App." },
                new() { Target = "پلن", HelpSlug = "plans", Description = "Subscription.PlanId." }
            ],
            Sections =
            [
                new HelpSection
                {
                    Title = "صفحه جزئیات مشتری",
                    Route = "/Admin/Tenants/{id}",
                    Steps =
                    [
                        "کارت‌های آماری: UserCount، RecordCount، StorageBytes.",
                        "«ورود به پنل مشتری»: Impersonate با کاربر TenantAdmin.",
                        "«ثبت اشتراک» / «اشتراک هدیه»: Subscription جدید.",
                        "تغییر Status: Active / Suspended / Trial.",
                        "حذف: confirmSlug باید برابر Slug باشد — غیرقابل بازگشت."
                    ]
                }
            ],
            RelatedTopicSlugs = ["admin-map", "subscriptions", "plans"]
        },

        new HelpTopic
        {
            Slug = "subscriptions",
            Title = "اشتراک‌ها",
            Icon = "bx-id-card",
            Summary = "ثبت، هدیه، لغو اشتراک مشتریان",
            Pages =
            [
                new() { Title = "لیست", Route = "/Admin/Subscriptions", Purpose = "StartsAt/EndsAt شمسی" },
                new() { Title = "ثبت", Route = "/Admin/Subscriptions/Create", Purpose = "tenantId + planId" },
                new() { Title = "هدیه", Route = "/Admin/Subscriptions/Gift", Purpose = "Amount=0" }
            ],
            Fields =
            [
                new() { Label = "TenantId", Purpose = "مشتری", ConnectsTo = "Tenants" },
                new() { Label = "PlanId", Purpose = "پلن و سقف‌ها", ConnectsTo = "Plans" },
                new() { Label = "StartsAtUtc / EndsAtUtc", Purpose = "بازه اعتبار", ConnectsTo = "App subscription + HasAccess" },
                new() { Label = "Amount", Purpose = "مبلغ — ۰ = هدیه/رایگان", ConnectsTo = "IsGiftSubscription" },
                new() { Label = "Status", Purpose = "Active/Expired/Canceled", ConnectsTo = "TenantLifecycleService" }
            ],
            Relations =
            [
                new() { Target = "Tenant", HelpSlug = "tenants" },
                new() { Target = "Plan", HelpSlug = "plans" },
                new() { Target = "تراکنش", HelpSlug = "transactions", Description = "پرداخت آنلاین App → PaymentTransaction." }
            ],
            RelatedTopicSlugs = ["tenants", "plans", "transactions"]
        },

        new HelpTopic
        {
            Slug = "plans",
            Title = "پلن‌ها",
            Icon = "bx-purchase-tag-alt",
            Summary = "تعرفه SaaS — سقف کاربر، رکورد، فضا",
            Pages = [new() { Title = "لیست/فرم", Route = "/Admin/Plans", Purpose = "SortOrder، MaxUsers، MaxRecords" }],
            Fields =
            [
                new() { Label = "MaxUsers", Purpose = "سقف همکار", ConnectsTo = "App/team-users + TenantQuotaService" },
                new() { Label = "MaxRecords", Purpose = "سقف رکورد CRM", ConnectsTo = "DynamicRecord create" },
                new() { Label = "PriceMonthly / PriceYearly", Purpose = "نمایش App/subscription", ConnectsTo = "PaymentTransaction" }
            ],
            Relations =
            [
                new() { Target = "اشتراک", HelpSlug = "subscriptions" },
                new() { Target = "پنل مشتری", Description = "مشتری از /App/subscription پلن می‌خرد." }
            ],
            RelatedTopicSlugs = ["subscriptions", "tenants"]
        },

        new HelpTopic
        {
            Slug = "transactions",
            Title = "تراکنش‌ها",
            Icon = "bx-money",
            Summary = "تاریخچه پرداخت‌های پلتفرم",
            Pages = [new() { Title = "لیست", Route = "/Admin/Transactions", Purpose = "AtUtc شمسی، مبلغ، Tenant" }],
            Fields =
            [
                new() { Label = "TenantId", Purpose = "پرداخت‌کننده", ConnectsTo = "Tenants" },
                new() { Label = "AtUtc", Purpose = "زمان تراکنش", ConnectsTo = "PersianDateHelper.ToJalaliDateTime" },
                new() { Label = "Amount", Purpose = "درآمد", ConnectsTo = "Dashboard TotalRevenue" }
            ],
            Relations =
            [
                new() { Target = "اشتراک", HelpSlug = "subscriptions" },
                new() { Target = "داشبورد", TargetRoute = "/Admin", Description = "RevenueLast30Days." }
            ],
            RelatedTopicSlugs = ["subscriptions", "admin-map"]
        },

        new HelpTopic
        {
            Slug = "articles",
            Title = "مقالات وبلاگ",
            Icon = "bx-news",
            Summary = "محتوای marketing — Category، Tags، Elementor",
            Pages =
            [
                new() { Title = "لیست", Route = "/Admin/Articles", Purpose = "PublishedAt شمسی" },
                new() { Title = "ایجاد", Route = "/Admin/Articles/Create", Purpose = "تب اطلاعات + Elementor" }
            ],
            Fields =
            [
                new() { Label = "Slug", Purpose = "URL عمومی /Articles/{slug}", ConnectsTo = "سایت" },
                new() { Label = "CategoryId", Purpose = "دسته", ConnectsTo = "Categories type=Article" },
                new() { Label = "PublishedAt", Purpose = "تاریخ انتشار", ConnectsTo = "jalali-datetime + Detail sidebar" },
                new() { Label = "ThumbnailUrl", Purpose = "تصویر شاخص", ConnectsTo = "Media/Upload" },
                new() { Label = "Content", Purpose = "JSON Elementor", ConnectsTo = "ElementorEditor component" }
            ],
            Relations =
            [
                new() { Target = "دسته‌بندی", TargetRoute = "/Admin/Categories", Description = "ContentCategory." },
                new() { Target = "صفحات سایت", HelpSlug = "site-pages", Description = "هر دو CMS marketing." }
            ],
            RelatedTopicSlugs = ["site-pages", "admin-map"]
        },

        new HelpTopic
        {
            Slug = "site-pages",
            Title = "صفحات سایت",
            Icon = "bx-file",
            Summary = "صفحات ثابت مثل درباره ما",
            Pages = [new() { Title = "درباره ما", Route = "/Admin/SitePages/EditAbout", Purpose = "Elementor + SEO" }],
            Fields =
            [
                new() { Label = "Content", Purpose = "Elementor JSON", ConnectsTo = "taben-elementor-content.css" }
            ],
            RelatedTopicSlugs = ["articles"]
        },

        new HelpTopic
        {
            Slug = "faqs",
            Title = "سوالات متداول",
            Icon = "bx-help-circle",
            Summary = "FAQ سایت عمومی — جدا از Kb داخلی App",
            Pages = [new() { Title = "لیست", Route = "/Admin/Faqs", Purpose = "SortOrder" }],
            Relations =
            [
                new() { Target = "پایگاه دانش App", Description = "KbArticle داخل Tenant — FAQ اینجا برای سایت marketing." }
            ],
            RelatedTopicSlugs = ["articles"]
        }
    ];
}
