using Crm.Web.Models.Help;

namespace Crm.Web.Services.Help;

/// <summary>محتوای آموزش پورتال مشتری نهایی (Area: Portal).</summary>
public static class PortalHelpContent
{
    private static List<HelpTopic>? _topics;

    public static List<HelpTopic> Topics => _topics ??= BuildTopics();

    public static HelpTopic? Find(string slug) =>
        Topics.FirstOrDefault(t => t.Slug == slug);

    private static List<HelpTopic> BuildTopics() =>
    [
        new HelpTopic
        {
            Slug = "portal-map",
            Title = "نقشه پورتال مشتری",
            Icon = "bx-sitemap",
            Summary = "چه داده‌ای از CRM به مشتری نشان داده می‌شود و بر چه اساسی",
            Intro = """
                پورتال برای «مشتری نهایی شرکت شما» است — نه همکار CRM.
                کلید اتصال: PortalUser.ContactRecordId = ContactRecordId در CRM.

                مشتری می‌بیند:
                • تیکت‌های خودش (PortalUserId یا ContactRecordId)
                • فاکتورهایی که ContactRecordId شان با مخاطب او یکی است
                • پروژه‌هایی که ShowInPortal=true و ContactRecordId یکسان
                • مقالات Kb با IsPublishedToPortal=true
                """,
            Pages =
            [
                new() { Title = "ورود", Route = "/Portal/Account/Login", Purpose = "کوکی Portal" },
                new() { Title = "داشبورد", Route = "/Portal", Purpose = "خلاصه تیکت و فاکتور" }
            ],
            Relations =
            [
                new() { Target = "کاربر پورتال (CRM)", HelpSlug = "portal-setup",
                    Description = "مدیر CRM از /App/portal-users می‌سازد." },
                new() { Target = "مخاطب CRM", Description = "ContactRecordId مشترک." },
                new() { Target = "تیکت App", TargetRoute = "/App/tickets", Description = "همان Ticket — پاسخ دوطرفه." }
            ],
            RelatedTopicSlugs = ["portal-setup", "portal-tickets", "portal-invoices", "portal-projects"]
        },

        new HelpTopic
        {
            Slug = "portal-setup",
            Title = "ورود و حساب کاربری",
            Icon = "bx-log-in",
            Summary = "تفاوت ورود پورتال با پنل CRM",
            Intro = "PortalUser جدا از User CRM است. ورود: /Portal/Account/Login — نه /App/Account/Login.",
            Pages =
            [
                new() { Title = "ورود", Route = "/Portal/Account/Login", Purpose = "Email + Password PortalUser" },
                new() { Title = "مدیریت (CRM)", Route = "/App/portal-users", Purpose = "فقط مدیر Tenant" }
            ],
            Fields =
            [
                new() { Label = "PortalUser.Email", Purpose = "نام کاربری", ConnectsTo = "Authentication Portal scheme" },
                new() { Label = "ContactRecordId", Purpose = "فیلتر داده", ConnectsTo = "Ticket, Invoice, Project" },
                new() { Label = "IsActive", Purpose = "فعال/غیرفعال", ConnectsTo = "App/portal-users edit" }
            ],
            Relations =
            [
                new() { Target = "مخاطب", Description = "اول در /App/m/contacts بسازید، بعد PortalUser." },
                new() { Target = "نقشه پورتال", HelpSlug = "portal-map" }
            ],
            RelatedTopicSlugs = ["portal-map"]
        },

        new HelpTopic
        {
            Slug = "portal-tickets",
            Title = "تیکت‌های من",
            Icon = "bx-support",
            Summary = "ثبت درخواست پشتیبانی و پیگیری پاسخ",
            Pages =
            [
                new() { Title = "لیست", Route = "/Portal/tickets", Purpose = "CreatedAtUtc شمسی" },
                new() { Title = "جزئیات", Route = "/Portal/tickets/{id}", Purpose = "پیام + CreatedAt شمسی" },
                new() { Title = "ثبت (CRM)", Route = "/App/tickets", Purpose = "تیم پشتیبانی می‌بیند" }
            ],
            Fields =
            [
                new() { Label = "PortalUserId", Purpose = "ثبت‌کننده", ConnectsTo = "Ticket در App" },
                new() { Label = "Status", Purpose = "Open → Closed", ConnectsTo = "همگام App/Portal" },
                new() { Label = "TicketMessage.IsFromCustomer", Purpose = "جهت پیام", ConnectsTo = "true=مشتری، false=پشتیبان" }
            ],
            Relations =
            [
                new() { Target = "پشتیبانی CRM", TargetRoute = "/App/tickets", Description = "AssignedUserId پاسخ می‌دهد." },
                new() { Target = "قرارداد", Description = "ServiceContractId — سقف تیکت." }
            ],
            RelatedTopicSlugs = ["portal-map", "portal-setup"]
        },

        new HelpTopic
        {
            Slug = "portal-invoices",
            Title = "فاکتورهای من",
            Icon = "bx-receipt",
            Summary = "مشاهده فاکتور و اقساط صادرشده برای مخاطب شما",
            Pages =
            [
                new() { Title = "لیست", Route = "/Portal/invoices", Purpose = "IssueDateUtc شمسی" },
                new() { Title = "جزئیات", Route = "/Portal/invoices/{id}", Purpose = "خطوط + DueDateUtc اقساط" }
            ],
            Fields =
            [
                new() { Label = "SalesDocument.ContactRecordId", Purpose = "فیلتر پورتال", ConnectsTo = "PortalUser.ContactRecordId" },
                new() { Label = "Installment.DueDateUtc", Purpose = "سررسید قسط", ConnectsTo = "App/finance details" },
                new() { Label = "GrandTotal / Status", Purpose = "مبلغ و تسویه", ConnectsTo = "PaymentRecord در CRM" }
            ],
            Relations =
            [
                new() { Target = "فاکتور CRM", TargetRoute = "/App/finance/invoices",
                    Description = "همان SalesDocument — مشتری فقط read-only می‌بیند." }
            ],
            RelatedTopicSlugs = ["portal-map"]
        },

        new HelpTopic
        {
            Slug = "portal-projects",
            Title = "پروژه‌های من",
            Icon = "bx-briefcase-alt-2",
            Summary = "پیشرفت پروژه‌هایی که شرکت برایتان باز کرده",
            Pages = [new() { Title = "لیست", Route = "/Portal/projects", Purpose = "StartUtc/EndUtc شمسی" }],
            Fields =
            [
                new() { Label = "Project.ShowInPortal", Purpose = "باید true باشد", ConnectsTo = "App/projects edit" },
                new() { Label = "Project.ContactRecordId", Purpose = "تطابق با PortalUser", ConnectsTo = "فیلتر لیست" },
                new() { Label = "ProgressPercent", Purpose = "پیشرفت", ConnectsTo = "میانگین Taskها در App" }
            ],
            Relations =
            [
                new() { Target = "پروژه CRM", TargetRoute = "/App/projects", Description = "مدیر ShowInPortal را فعال می‌کند." }
            ],
            RelatedTopicSlugs = ["portal-map"]
        },

        new HelpTopic
        {
            Slug = "portal-kb",
            Title = "پایگاه دانش",
            Icon = "bx-book-open",
            Summary = "مقالات راهنمای عمومی منتشرشده توسط شرکت",
            Pages = [new() { Title = "لیست", Route = "/Portal/kb", Purpose = "KbArticle.IsPublishedToPortal" }],
            Relations =
            [
                new() { Target = "Kb در CRM", TargetRoute = "/App/kb", Description = "IsPublishedToPortal=true." },
                new() { Target = "تیکت", HelpSlug = "portal-tickets", Description = "اگر جواب نبود، تیکت بزنید." }
            ],
            RelatedTopicSlugs = ["portal-map", "portal-tickets"]
        }
    ];
}
