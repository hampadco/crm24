using Crm.Web.Models.Help;

namespace Crm.Web.Services.Help;

/// <summary>نقشه روابط CRM، صفحات و فیلدها — برای تکمیل موضوعات آموزشی App.</summary>
public static partial class AppHelpContent
{
    private static List<HelpTopic> EnrichTopics(List<HelpTopic> topics)
    {
        foreach (var topic in topics)
        {
            if (!_enrichment.TryGetValue(topic.Slug, out var data))
                continue;

            if (data.Pages is { Count: > 0 }) topic.Pages = data.Pages;
            if (data.Fields is { Count: > 0 }) topic.Fields = data.Fields;
            if (data.Relations is { Count: > 0 }) topic.Relations = data.Relations;
            if (data.RelatedTopicSlugs is { Count: > 0 }) topic.RelatedTopicSlugs = data.RelatedTopicSlugs;
        }

        return topics;
    }

    private static List<HelpTopic> BuildOverview() =>
    [
        new HelpTopic
        {
            Slug = "crm-map",
            Title = "نقشه روابط CRM",
            Icon = "bx-sitemap",
            Summary = "جریان داده از سرنخ تا فاکتور، پروژه، تیکت و پورتال — با تمام ارتباط فیلدها",
            Intro = """
                BamaCRM یک زنجیره داده‌ای دارد که همه بخش‌ها به هم وصل‌اند. این صفحه «نقشه» کلی است؛
                هر بخش دیگر جزئیات همان قسمت را توضیح می‌دهد.

                جریان اصلی فروش:
                سرنخ → (تبدیل) → مخاطب + سازمان + فرصت → (برنده) → پروژه / فاکتور
                محصول → آیتم‌های پیش‌فاکتور → سفارش → فاکتور → پرداخت / قسط → پورسانت

                جریان پشتیبانی:
                مخاطب → کاربر پورتال → تیکت ↔ قرارداد خدمات / گارانتی
                پروژه (ShowInPortal) → نمایش در پورتال مشتری

                جریان بازاریابی:
                کمپین → منبع سرنخ → گردش‌کار → قالب پیام / وب‌فرم
                """,
            Pages =
            [
                new() { Title = "این نقشه", Route = "/App/help/crm-map", Purpose = "مرجع روابط کل سیستم" },
                new() { Title = "ماژول‌های CRM", Route = "/App/m/leads", Purpose = "سرنخ، مخاطب، سازمان، فرصت" },
                new() { Title = "مالی", Route = "/App/finance/invoices", Purpose = "پیش‌فاکتور، سفارش، فاکتور" },
                new() { Title = "پورتال مشتری", Route = "/App/portal-users", Purpose = "دسترسی مشتری نهایی" }
            ],
            Relations =
            [
                new() { Target = "سرنخ", TargetRoute = "/App/m/leads", HelpSlug = "modules",
                    Description = "ورودی اولیه — با تبدیل به مخاطب+سازمان+فرصت وصل می‌شود." },
                new() { Target = "فرصت فروش", TargetRoute = "/App/m/opportunities", HelpSlug = "modules",
                    Description = "مرحله (Stage) در کاریز — برنده شدن → پروژه یا فاکتور." },
                new() { Target = "مخاطب", TargetRoute = "/App/m/contacts", HelpSlug = "modules",
                    Description = "ContactRecordId در فاکتور، تیکت، پروژه، گارانتی، کاربر پورتال." },
                new() { Target = "محصول", TargetRoute = "/App/products", HelpSlug = "products",
                    Description = "ProductId در خط فاکتور و گارانتی — قیمت و مالیات خودکار." },
                new() { Target = "فاکتور", TargetRoute = "/App/finance/invoices", HelpSlug = "finance",
                    Description = "ContactRecordId + خطوط محصول → پرداخت → پورسانت." },
                new() { Target = "پروژه", TargetRoute = "/App/projects", HelpSlug = "projects",
                    Description = "OpportunityRecordId از فرصت برنده — ShowInPortal → پورتال." },
                new() { Target = "کاربر پورتال", TargetRoute = "/App/portal-users", HelpSlug = "portal-users",
                    Description = "ContactRecordId — تعیین می‌کند مشتری چه تیکت/فاکتور/پروژه‌ای ببیند." },
                new() { Target = "گردش‌کار", TargetRoute = "/App/workflows", HelpSlug = "workflows",
                    Description = "روی هر ماژول Trigger می‌گیرد — به قالب، پیامک، وظیفه وصل می‌شود." }
            ],
            RelatedTopicSlugs = ["modules", "finance", "projects", "portal-users", "workflows"]
        }
    ];

    private sealed class TopicEnrichment
    {
        public List<HelpPageLink>? Pages { get; init; }
        public List<HelpFieldGuide>? Fields { get; init; }
        public List<HelpRelation>? Relations { get; init; }
        public List<string>? RelatedTopicSlugs { get; init; }
    }

    private static readonly Dictionary<string, TopicEnrichment> _enrichment = new()
    {
        ["dashboard"] = new()
        {
            Pages = [new() { Title = "داشبورد", Route = "/App/dashboard", Purpose = "ویجت‌های آماری از همه ماژول‌ها" }],
            Relations =
            [
                new() { Target = "همه ماژول‌ها", TargetRoute = "/App/m/leads", HelpSlug = "modules",
                    Description = "هر ویجت به یک ModuleDef وصل است — داده از رکوردهای همان ماژول خوانده می‌شود." },
                new() { Target = "گزارش‌ساز", TargetRoute = "/App/reports", HelpSlug = "reports",
                    Description = "برای گزارش تفصیلی‌تر از ویجت داشبورد استفاده کنید." }
            ],
            RelatedTopicSlugs = ["modules", "reports", "crm-map"]
        },

        ["modules"] = new()
        {
            Pages =
            [
                new() { Title = "لیست رکورد", Route = "/App/m/{module}", Purpose = "نمایش، جستجو، CSV، اکسل" },
                new() { Title = "ایجاد", Route = "/App/m/{module}/create", Purpose = "فرم داینامیک از FieldDef" },
                new() { Title = "ویرایش", Route = "/App/m/{module}/{id}/edit", Purpose = "ویرایش با سطح دسترسی فیلد" },
                new() { Title = "کاریز", Route = "/App/kanban/{module}", Purpose = "فقط ماژول‌های دارای فیلد Stage" },
                new() { Title = "سطل بازیابی", Route = "/App/recycle-bin", Purpose = "رکوردهای حذف‌شده — آموزش: recycle-bin" }
            ],
            Fields =
            [
                new() { Label = "عنوان (Title)", Purpose = "شناسه انسانی رکورد در لیست و جستجو", ConnectsTo = "همه صفحات لیست و Lookup سایر ماژول‌ها" },
                new() { Label = "مرحله (Stage) — فرصت", Purpose = "وضعیت معامله در قیف فروش", ConnectsTo = "کاریز /App/kanban/opportunities — PicklistValues" },
                new() { Label = "مبلغ — فرصت", Purpose = "ارزش معامله", ConnectsTo = "گزارش جمع، داشبورد، تبدیل به پروژه (Budget)" },
                new() { Label = "سازمان (Lookup)", Purpose = "شرکت والد مخاطب", ConnectsTo = "ماژول accounts — OrganizationRecordId در فاکتور" },
                new() { Label = "منبع — سرنخ", Purpose = "کمپین یا کانال ورود", ConnectsTo = "کمپین‌ها /App/campaigns — گزارش اثربخشی" },
                new() { Label = "مسئول (Owner)", Purpose = "کاربر CRM مسئول پیگیری", ConnectsTo = "همکاران /App/team-users — فیلتر گزارش و دسترسی" },
                new() { Label = "فیلدهای Date/DateTime", Purpose = "تاریخ شمسی", ConnectsTo = "تقویم /App/calendar — گردش‌کار زمان‌بندی‌شده" }
            ],
            Relations =
            [
                new() { Target = "تبدیل سرنخ", HelpSlug = "modules",
                    Description = "Lead → Contact + Account + Opportunity (یک‌کلیک از لیست سرنخ)." },
                new() { Target = "فاکتور", TargetRoute = "/App/finance/invoices/create", HelpSlug = "finance",
                    Description = "فیلد «مخاطب مرتبط» = ContactRecordId رکورد contacts." },
                new() { Target = "وب‌فرم", TargetRoute = "/App/webforms", HelpSlug = "webforms",
                    Description = "ثبت فرم سایت → رکورد جدید در ماژول انتخابی (معمولاً leads)." },
                new() { Target = "گردش‌کار", TargetRoute = "/App/workflows", HelpSlug = "workflows",
                    Description = "Trigger روی create/update هر ماژول — شرط روی فیلدهای همان رکورد." }
            ],
            RelatedTopicSlugs = ["crm-map", "kanban", "finance", "webforms", "workflows"]
        },

        ["kanban"] = new()
        {
            Pages = [new() { Title = "کاریز", Route = "/App/kanban/opportunities", Purpose = "ستون = مقدار فیلد Stage" }],
            Fields =
            [
                new() { Label = "Stage (Picklist)", Purpose = "هر ستون کاریز یک مقدار این فیلد است", ConnectsTo = "ماژول opportunities — FieldDef.PicklistValues" },
                new() { Label = "recordId (کارت)", Purpose = "شناسه رکورد برای drag & drop", ConnectsTo = "API /App/kanban/{module}/move → به‌روزرسانی Stage" }
            ],
            Relations =
            [
                new() { Target = "فرصت‌ها", TargetRoute = "/App/m/opportunities", HelpSlug = "modules",
                    Description = "همان رکوردها — فقط نمای متفاوت؛ تغییر ستون = تغییر فیلد Stage." },
                new() { Target = "گزارش", TargetRoute = "/App/reports", HelpSlug = "reports",
                    Description = "گروه‌بندی گزارش روی Stage معادل خلاصه کاریز است." }
            ],
            RelatedTopicSlugs = ["modules", "reports"]
        },

        ["calendar"] = new()
        {
            Pages = [new() { Title = "تقویم", Route = "/App/calendar", Purpose = "Feed از tasks و events" }],
            Fields =
            [
                new() { Label = "StartUtc / EndUtc", Purpose = "بازه نمایش رویداد", ConnectsTo = "ماژول‌های tasks و events — فیلد DateTime" },
                new() { Label = "AssignedUserId", Purpose = "مسئول وظیفه", ConnectsTo = "همکاران — فیلتر تقویم شخصی (آینده)" }
            ],
            Relations =
            [
                new() { Target = "وظایف (tasks)", TargetRoute = "/App/m/tasks/create", HelpSlug = "modules",
                    Description = "رکورد task با تاریخ → رویداد تقویم." },
                new() { Target = "رویدادها (events)", TargetRoute = "/App/m/events/create", HelpSlug = "modules",
                    Description = "جلسات و دموها روی تقویم." },
                new() { Target = "گردش‌کار", HelpSlug = "workflows",
                    Description = "اکشن «ساخت وظیفه» → رکورد task → ظاهر شدن در تقویم." }
            ],
            RelatedTopicSlugs = ["modules", "workflows"]
        },

        ["recycle-bin"] = new()
        {
            Pages = [new() { Title = "سطل بازیابی", Route = "/App/recycle-bin", Purpose = "DeletedAtUtc — بازیابی به ماژول مبدأ" }],
            Relations =
            [
                new() { Target = "همه ماژول‌ها", HelpSlug = "modules",
                    Description = "هر رکورد حذف‌شده ModuleId خود را حفظ می‌کند — بازیابی به /App/m/{module}." }
            ],
            RelatedTopicSlugs = ["modules"]
        },

        ["team-users"] = new()
        {
            Pages =
            [
                new() { Title = "لیست همکاران", Route = "/App/team-users", Purpose = "کاربران Identity این Tenant" },
                new() { Title = "افزودن", Route = "/App/team-users/create", Purpose = "ایجاد کاربر + نقش" },
                new() { Title = "ویرایش", Route = "/App/team-users/{id}/edit", Purpose = "نقش، پروفایل، فعال/غیرفعال" }
            ],
            Fields =
            [
                new() { Label = "Email", Purpose = "نام کاربری ورود به /App/Account/Login", ConnectsTo = "Identity — جدا از PortalUser.Email" },
                new() { Label = "IsTenantAdmin", Purpose = "دسترسی مدیریت تیم و تنظیمات", ConnectsTo = "صفحات team-users، subscription" },
                new() { Label = "Role / Profile", Purpose = "سطح دسترسی ماژول و فیلد", ConnectsTo = "RecordAccessService — فیلتر لیست‌ها" },
                new() { Label = "IsActive", Purpose = "مسدودسازی ورود", ConnectsTo = "آزاد شدن ظرفیت پلن Subscription" }
            ],
            Relations =
            [
                new() { Target = "اشتراک", TargetRoute = "/App/subscription", HelpSlug = "subscription",
                    Description = "MaxUsers پلن ← تعداد کاربران فعال." },
                new() { Target = "تیکت", TargetRoute = "/App/tickets", HelpSlug = "tickets",
                    Description = "AssignedUserId تیکت = کاربر CRM." },
                new() { Target = "پروژه", HelpSlug = "projects",
                    Description = "AssignedUserId وظیفه پروژه = همکار." }
            ],
            RelatedTopicSlugs = ["subscription", "portal-users", "modules"]
        },

        ["subscription"] = new()
        {
            Pages = [new() { Title = "اشتراک", Route = "/App/subscription", Purpose = "پلن فعال Tenant + خرید" }],
            Fields =
            [
                new() { Label = "Plan.MaxUsers", Purpose = "سقف همکار", ConnectsTo = "team-users — TenantLifecycleService" },
                new() { Label = "EndsAtUtc", Purpose = "پایان دسترسی", ConnectsTo = "Account/Expired — قطع ورود" }
            ],
            Relations =
            [
                new() { Target = "همکاران", TargetRoute = "/App/team-users", HelpSlug = "team-users",
                    Description = "بیش از MaxUsers → خطا هنگام افزودن همکار." }
            ],
            RelatedTopicSlugs = ["team-users"]
        },

        ["integrations"] = new()
        {
            Relations =
            [
                new() { Target = "گردش‌کار", HelpSlug = "workflows",
                    Description = "اکشن پیامک/ایمیل بدون تنظیم اینجا کار نمی‌کند." },
                new() { Target = "قالب پیام", HelpSlug = "templates",
                    Description = "متن پیام از Template در گردش‌کار." }
            ],
            RelatedTopicSlugs = ["workflows", "templates"]
        },

        ["products"] = new()
        {
            Pages =
            [
                new() { Title = "لیست محصولات", Route = "/App/products", Purpose = "کاتالوگ فروش" },
                new() { Title = "ایجاد/ویرایش", Route = "/App/products/create", Purpose = "قیمت، مالیات، موجودی" }
            ],
            Fields =
            [
                new() { Label = "SalePrice", Purpose = "قیمت پیش‌فرض در خط فاکتور", ConnectsTo = "SalesDocumentLine.UnitPrice" },
                new() { Label = "TaxPercent", Purpose = "مالیات خط", ConnectsTo = "SalesDocumentLine.TaxPercent + GrandTotal" },
                new() { Label = "StockQty / ReorderPoint", Purpose = "موجودی انبار", ConnectsTo = "سفارش خرید — هشدار کمبود" },
                new() { Label = "IsService", Purpose = "بدون موجودی", ConnectsTo = "TrackInventory=false" }
            ],
            Relations =
            [
                new() { Target = "فاکتور", HelpSlug = "finance", TargetRoute = "/App/finance/invoices/create",
                    Description = "ProductId در Lines → Title, UnitPrice, TaxPercent خودکار." },
                new() { Target = "گارانتی", HelpSlug = "warranties",
                    Description = "ProductId + SerialNumber در Warranty." },
                new() { Target = "سفارش خرید", HelpSlug = "purchase-orders",
                    Description = "قیمت خرید در PO (فعلاً دستی یا از محصول)." }
            ],
            RelatedTopicSlugs = ["finance", "warranties", "purchase-orders"]
        },

        ["finance"] = new()
        {
            Pages =
            [
                new() { Title = "پیش‌فاکتورها", Route = "/App/finance/quotes", Purpose = "Kind=Quote" },
                new() { Title = "سفارش‌ها", Route = "/App/finance/orders", Purpose = "Kind=Order" },
                new() { Title = "فاکتورها", Route = "/App/finance/invoices", Purpose = "Kind=Invoice" },
                new() { Title = "ایجاد", Route = "/App/finance/quotes/create", Purpose = "فرم مشترک SalesDocument" },
                new() { Title = "جزئیات", Route = "/App/finance/invoices/{id}", Purpose = "تبدیل، پرداخت، قسط" },
                new() { Title = "چاپ", Route = "/App/finance/{id}/print", Purpose = "IssueDateUtc شمسی" }
            ],
            Fields =
            [
                new() { Label = "CustomerName", Purpose = "نام چاپی مشتری", ConnectsTo = "مستقل از CRM — می‌تواند از Contact پر شود" },
                new() { Label = "ContactRecordId", Purpose = "پیوند به مخاطب CRM", ConnectsTo = "ماژول contacts — پورتال: فاکتورهای همان Contact" },
                new() { Label = "OrganizationRecordId", Purpose = "پیوند به سازمان", ConnectsTo = "ماژول accounts" },
                new() { Label = "ValidUntil (Quote)", Purpose = "اعتبار پیش‌فاکتور", ConnectsTo = "فیلد jalali-date — ValidUntilUtc" },
                new() { Label = "Lines[].ProductId", Purpose = "محصول خط", ConnectsTo = "products — قیمت/مالیات" },
                new() { Label = "SourceDocumentId", Purpose = "سند مبدأ تبدیل", ConnectsTo = "Quote→Order→Invoice زنجیره" },
                new() { Label = "Status", Purpose = "Draft/Confirmed/Paid/...", ConnectsTo = "پرداخت‌ها → PartiallyPaid/Paid" },
                new() { Label = "GrandTotal", Purpose = "جمع نهایی", ConnectsTo = "PaymentRecord + Installment + Commission" }
            ],
            Relations =
            [
                new() { Target = "محصولات", HelpSlug = "products", TargetRoute = "/App/products",
                    Description = "هر Line به Product وصل — یا Title دستی." },
                new() { Target = "مخاطب", HelpSlug = "modules", TargetRoute = "/App/m/contacts",
                    Description = "ContactRecordId برای CRM و فیلتر پورتال." },
                new() { Target = "پورسانت", HelpSlug = "commissions",
                    Description = "Commission روی SalesDocument پرداخت‌شده." },
                new() { Target = "پورتال", HelpSlug = "portal-users",
                    Description = "PortalUser.ContactRecordId = ContactRecordId فاکتور → مشتری فاکتورش را می‌بیند." },
                new() { Target = "گردش‌کار", HelpSlug = "workflows",
                    Description = "Trigger روی Invoice Paid → پیامک تشکر." }
            ],
            RelatedTopicSlugs = ["products", "modules", "commissions", "portal-users", "crm-map"]
        },

        ["commissions"] = new()
        {
            Fields =
            [
                new() { Label = "SalesDocumentId", Purpose = "فاکتور مبنا", ConnectsTo = "finance — فقط Paid" },
                new() { Label = "UserId", Purpose = "فروشنده", ConnectsTo = "team-users" }
            ],
            Relations =
            [
                new() { Target = "فاکتور", HelpSlug = "finance", Description = "مبلغ پورسانت معمولاً درصدی از GrandTotal." }
            ],
            RelatedTopicSlugs = ["finance", "team-users"]
        },

        ["projects"] = new()
        {
            Pages =
            [
                new() { Title = "لیست", Route = "/App/projects", Purpose = "پیشنهاد تبدیل فرصت برنده" },
                new() { Title = "ایجاد", Route = "/App/projects/create", Purpose = "StartUtc/EndUtc شمسی" },
                new() { Title = "جزئیات", Route = "/App/projects/{id}", Purpose = "فاز، وظیفه، گانت" }
            ],
            Fields =
            [
                new() { Label = "OpportunityRecordId", Purpose = "فرصت مبدأ", ConnectsTo = "ماژول opportunities — دکمه تبدیل در لیست" },
                new() { Label = "ContactRecordId / CustomerName", Purpose = "مشتری پروژه", ConnectsTo = "contacts — پورتال" },
                new() { Label = "Budget", Purpose = "بودجه", ConnectsTo = "مبلغ فرصت برنده (اختیاری)" },
                new() { Label = "ShowInPortal", Purpose = "نمایش به مشتری", ConnectsTo = "Portal /Portal/projects — ContactRecordId" },
                new() { Label = "ProjectTask.AssignedUserId", Purpose = "مسئول وظیفه", ConnectsTo = "team-users + calendar" },
                new() { Label = "ProgressPercent", Purpose = "پیشرفت وظیفه", ConnectsTo = "میانگین → پیشرفت کل پروژه" }
            ],
            Relations =
            [
                new() { Target = "فرصت", HelpSlug = "modules", Description = "تبدیل Opportunity برنده → Project." },
                new() { Target = "تقویم", HelpSlug = "calendar", Description = "وظایف با تاریخ → feed تقویم." },
                new() { Target = "پورتال", HelpSlug = "portal-users", TargetRoute = "/Portal/projects",
                    Description = "ShowInPortal + Contact یکسان." }
            ],
            RelatedTopicSlugs = ["modules", "portal-users", "calendar", "crm-map"]
        },

        ["vendors"] = new()
        {
            Relations =
            [
                new() { Target = "سفارش خرید", HelpSlug = "purchase-orders",
                    Description = "PurchaseOrder.VendorId — هر PO یک تأمین‌کننده." }
            ],
            RelatedTopicSlugs = ["purchase-orders"]
        },

        ["purchase-orders"] = new()
        {
            Fields =
            [
                new() { Label = "VendorId", Purpose = "تأمین‌کننده", ConnectsTo = "vendors" },
                new() { Label = "Lines[].ProductId", Purpose = "کالای خرید", ConnectsTo = "products — StockQty (آینده)" },
                new() { Label = "IssueDateUtc", Purpose = "تاریخ سفارش", ConnectsTo = "نمایش شمسی" }
            ],
            Relations =
            [
                new() { Target = "تأمین‌کنندگان", HelpSlug = "vendors" },
                new() { Target = "محصولات", HelpSlug = "products" }
            ],
            RelatedTopicSlugs = ["vendors", "products"]
        },

        ["campaigns"] = new()
        {
            Fields =
            [
                new() { Label = "StartUtc / EndUtc", Purpose = "بازه کمپین", ConnectsTo = "jalali-date" },
                new() { Label = "Budget / ActualCost", Purpose = "بودجه vs واقعی", ConnectsTo = "گزارش ROI" }
            ],
            Relations =
            [
                new() { Target = "سرنخ", HelpSlug = "modules",
                    Description = "فیلد «منبع» سرنخ = نام کمپین — اتصال دستی/گردش‌کار." },
                new() { Target = "وب‌فرم", HelpSlug = "webforms",
                    Description = "لندینگ کمپین → فرم → سرنخ." }
            ],
            RelatedTopicSlugs = ["modules", "webforms"]
        },

        ["tickets"] = new()
        {
            Pages =
            [
                new() { Title = "لیست", Route = "/App/tickets", Purpose = "همه تیکت‌های Tenant" },
                new() { Title = "جزئیات", Route = "/App/tickets/{id}", Purpose = "پیام‌ها + تغییر وضعیت" },
                new() { Title = "پورتال مشتری", Route = "/Portal/tickets", Purpose = "همان Ticket از دید مشتری" }
            ],
            Fields =
            [
                new() { Label = "PortalUserId", Purpose = "ثبت از پورتال", ConnectsTo = "portal-users — IsFromCustomer در پیام" },
                new() { Label = "ContactRecordId", Purpose = "مخاطب CRM", ConnectsTo = "contacts — تیکت دستی از CRM" },
                new() { Label = "ServiceContractId", Purpose = "قرارداد پوشش", ConnectsTo = "contracts — سقف MaxTickets" },
                new() { Label = "AssignedUserId", Purpose = "پشتیبان مسئول", ConnectsTo = "team-users" },
                new() { Label = "Priority / Status", Purpose = "SLA و گردش", ConnectsTo = "SlaPolicy.ResponseHours → DueAtUtc" }
            ],
            Relations =
            [
                new() { Target = "کاربر پورتال", HelpSlug = "portal-users",
                    Description = "PortalUser → Ticket → Message دوطرفه App↔Portal." },
                new() { Target = "قرارداد", HelpSlug = "contracts" },
                new() { Target = "پایگاه دانش", HelpSlug = "kb",
                    Description = "لینک مقاله در پاسخ → کاهش تیکت تکراری." }
            ],
            RelatedTopicSlugs = ["portal-users", "contracts", "kb"]
        },

        ["contracts"] = new()
        {
            Fields =
            [
                new() { Label = "ContactRecordId", Purpose = "مشتری قرارداد", ConnectsTo = "contacts" },
                new() { Label = "StartUtc / EndUtc", Purpose = "بازه اعتبار", ConnectsTo = "تیکت: ServiceContractId" },
                new() { Label = "MaxTickets / TicketsUsed", Purpose = "سقف تیکت", ConnectsTo = "tickets — IsActive" }
            ],
            Relations =
            [
                new() { Target = "تیکت", HelpSlug = "tickets",
                    Description = "تیکت با ServiceContractId زیر پوشش SLA قرارداد است." },
                new() { Target = "گردش‌کار", HelpSlug = "workflows",
                    Description = "Trigger تاریخ EndUtc → وظیفه تمدید." }
            ],
            RelatedTopicSlugs = ["tickets", "workflows"]
        },

        ["warranties"] = new()
        {
            Fields =
            [
                new() { Label = "SerialNumber", Purpose = "شناسه یکتا دستگاه", ConnectsTo = "جستجو در لیست" },
                new() { Label = "ProductId", Purpose = "مدل محصول", ConnectsTo = "products" },
                new() { Label = "ContactRecordId", Purpose = "مالک", ConnectsTo = "contacts + فاکتور فروش (دستی)" }
            ],
            Relations =
            [
                new() { Target = "محصول", HelpSlug = "products" },
                new() { Target = "مخاطب", HelpSlug = "modules" }
            ],
            RelatedTopicSlugs = ["products", "finance"]
        },

        ["kb"] = new()
        {
            Fields =
            [
                new() { Label = "IsPublishedToPortal", Purpose = "عمومی/داخلی", ConnectsTo = "Portal /Portal/kb" }
            ],
            Relations =
            [
                new() { Target = "تیکت", HelpSlug = "tickets" },
                new() { Target = "پورتال", TargetRoute = "/Portal/kb", HelpSlug = "portal-users" }
            ],
            RelatedTopicSlugs = ["tickets", "portal-users"]
        },

        ["portal-users"] = new()
        {
            Pages =
            [
                new() { Title = "لیست", Route = "/App/portal-users", Purpose = "مدیریت از CRM" },
                new() { Title = "ورود مشتری", Route = "/Portal/Account/Login", Purpose = "کوکی Portal — نه Identity" }
            ],
            Fields =
            [
                new() { Label = "Email + PasswordHash", Purpose = "احراز هویت پورتال", ConnectsTo = "PortalControllerBase — جدا از CRM User" },
                new() { Label = "ContactRecordId", Purpose = "کلید اتصال داده", ConnectsTo = "Ticket, Invoice(Contact), Project(ShowInPortal+Contact)" },
                new() { Label = "IsActive", Purpose = "مسدود ورود پورتال", ConnectsTo = "—" }
            ],
            Relations =
            [
                new() { Target = "مخاطب", HelpSlug = "modules", TargetRoute = "/App/m/contacts/create",
                    Description = "بدون ContactRecordId پورتال خالی است — اول مخاطب بسازید." },
                new() { Target = "تیکت", TargetRoute = "/Portal/tickets", HelpSlug = "tickets" },
                new() { Target = "فاکتور", TargetRoute = "/Portal/invoices", HelpSlug = "finance" },
                new() { Target = "پروژه", TargetRoute = "/Portal/projects", HelpSlug = "projects" }
            ],
            RelatedTopicSlugs = ["modules", "tickets", "finance", "projects", "crm-map"]
        },

        ["surveys"] = new()
        {
            Relations =
            [
                new() { Target = "تیکت", HelpSlug = "tickets",
                    Description = "لینک نظرسنجی بعد از بستن تیکت — Response.CreatedAtUtc." }
            ],
            RelatedTopicSlugs = ["tickets"]
        },

        ["leaves"] = new()
        {
            Fields =
            [
                new() { Label = "UserId", Purpose = "درخواست‌دهنده", ConnectsTo = "team-users" },
                new() { Label = "FromUtc / ToUtc", Purpose = "بازه مرخصی", ConnectsTo = "تقویم تیم" },
                new() { Label = "Status", Purpose = "Pending/Approved/Rejected", ConnectsTo = "مدیر TenantAdmin" }
            ],
            RelatedTopicSlugs = ["team-users"]
        },

        ["workflows"] = new()
        {
            Fields =
            [
                new() { Label = "ModuleId + Trigger", Purpose = "رویداد شروع", ConnectsTo = "Record create/update/schedule" },
                new() { Label = "Conditions (FiltersJson)", Purpose = "شرط روی فیلدهای رکورد", ConnectsTo = "همان FieldDef ماژول" },
                new() { Label = "Actions", Purpose = "پیامک، ایمیل، task، update field", ConnectsTo = "integrations, templates, calendar" }
            ],
            Relations =
            [
                new() { Target = "همه ماژول‌ها", HelpSlug = "modules" },
                new() { Target = "یکپارچه‌سازی", HelpSlug = "integrations" },
                new() { Target = "قالب", HelpSlug = "templates" },
                new() { Target = "لاگ", TargetRoute = "/App/workflows/logs", HelpSlug = "workflows" }
            ],
            RelatedTopicSlugs = ["modules", "integrations", "templates", "crm-map"]
        },

        ["reports"] = new()
        {
            Fields =
            [
                new() { Label = "ModuleId", Purpose = "منبع داده", ConnectsTo = "DynamicRecord — همان رکوردهای /App/m/" },
                new() { Label = "ColumnsJson", Purpose = "ستون‌ها", ConnectsTo = "FieldDef — Date→شمسی در خروجی" },
                new() { Label = "GroupByField / SumField", Purpose = "خلاصه", ConnectsTo = "Picklist label + جمع عددی" }
            ],
            Relations =
            [
                new() { Target = "ماژول‌ها", HelpSlug = "modules" },
                new() { Target = "داشبورد", HelpSlug = "dashboard", Description = "ویجت = گزارش ساده‌تر." }
            ],
            RelatedTopicSlugs = ["modules", "dashboard"]
        },

        ["webforms"] = new()
        {
            Fields =
            [
                new() { Label = "TargetModuleId", Purpose = "مقصد ثبت", ConnectsTo = "معمولاً leads" },
                new() { Label = "Field mapping", Purpose = "input name → FieldDef", ConnectsTo = "فرم HTML embed" }
            ],
            Relations =
            [
                new() { Target = "سرنخ", HelpSlug = "modules" },
                new() { Target = "گردش‌کار", HelpSlug = "workflows", Description = "Trigger create lead." }
            ],
            RelatedTopicSlugs = ["modules", "workflows", "campaigns"]
        },

        ["templates"] = new()
        {
            Relations =
            [
                new() { Target = "گردش‌کار", HelpSlug = "workflows" },
                new() { Target = "یکپارچه‌سازی", HelpSlug = "integrations" }
            ],
            RelatedTopicSlugs = ["workflows", "integrations"]
        }
    };
}
