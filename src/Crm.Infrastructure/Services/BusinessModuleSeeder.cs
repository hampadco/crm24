using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Crm.Infrastructure.Services;

/// <summary>
/// ماژول‌های کسب‌وکاری باقی‌ماندهٔ منو را به‌صورت metadata-driven می‌سازد
/// (تیکت، محصول، مالی، پروژه، …) — idempotent.
/// </summary>
public class BusinessModuleSeeder
{
    private readonly CrmDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly RolePermissionService _rolePerms;

    public BusinessModuleSeeder(CrmDbContext db, IMemoryCache cache, RolePermissionService rolePerms)
    {
        _db = db;
        _cache = cache;
        _rolePerms = rolePerms;
    }

    public async Task EnsureSeededAsync(int tenantId)
    {
        await _rolePerms.EnsureMigratedAsync(tenantId);

        var cacheKey = $"business-modules-ok:{tenantId}";
        if (!_cache.TryGetValue(cacheKey, out bool ok) || !ok)
        {
            var expected = new[]
            {
                "tickets", "products", "vendors", "campaigns", "quotes", "sales_orders", "invoices",
                "purchase_orders", "contracts", "warranties", "projects", "project_tasks", "project_phases",
                "leaves", "commissions", "documents", "services", "pricebooks", "payments", "installments",
                "warehouses", "product_sales",
                "quote_lines", "sales_order_lines", "invoice_lines", "purchase_order_lines"
            };
            var have = await _db.Modules.CountAsync(m => m.TenantId == tenantId && expected.Contains(m.Name));
            var mutated = false;

            if (have < expected.Length)
            {
                var adminProfile = await _db.Profiles.Where(p => p.TenantId == tenantId && p.IsAdmin).Select(p => p.Id).FirstOrDefaultAsync();
                var userProfile = await _db.Profiles.Where(p => p.TenantId == tenantId && !p.IsAdmin).Select(p => p.Id).FirstOrDefaultAsync();
                await SeedAsync(tenantId, adminProfile, userProfile);
                mutated = true;
            }

            var extrasKey = $"business-demo-extras-ok:{tenantId}";
            if (!_cache.TryGetValue(extrasKey, out bool extrasOk) || !extrasOk)
            {
                await EnsureDemoExtrasAsync(tenantId);
                _cache.Set(extrasKey, true, TimeSpan.FromHours(24));
                mutated = true;
            }

            if (mutated)
                _cache.Remove($"modules:{tenantId}");
            _cache.Set(cacheKey, true, TimeSpan.FromHours(6));
        }

        // فرصت → پیش‌فاکتور/فاکتور (حتی برای tenantهای از قبل seedشده)
        var financeKey = $"business-finance-opp-links:{tenantId}";
        if (!_cache.TryGetValue(financeKey, out bool finOk) || !finOk)
        {
            await EnsureFinanceOpportunityLinksAsync(tenantId);
            _cache.Set(financeKey, true, TimeSpan.FromHours(24));
        }

        // ارتقای فیلد/بلاک/ماژول خطوط اسناد فروش
        var docsKey = $"business-doc-modules-v2:{tenantId}";
        if (!_cache.TryGetValue(docsKey, out bool docsOk) || !docsOk)
        {
            await EnsureDocumentModulesAsync(tenantId);
            _cache.Set(docsKey, true, TimeSpan.FromHours(24));
        }
    }

    public async Task SeedAsync(int tenantId, int adminProfileId, int userProfileId)
    {
        var sort = 100;

        await EnsureAsync(tenantId, "tickets", "تیکت", "تیکت‌ها", "bx-support", "support", ++sort,
        [
            F("name", "موضوع", FieldType.Text, required: true),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "open",
                picklist: [P("open", "باز"), P("pending", "در انتظار"), P("closed", "بسته")]),
            F("priority", "اولویت", FieldType.Picklist, defaultValue: "normal",
                picklist: [P("low", "کم"), P("normal", "عادی"), P("high", "بالا"), P("urgent", "فوری")]),
            F("category", "دسته", FieldType.Picklist,
                picklist: [P("support", "پشتیبانی"), P("sales", "فروش"), P("billing", "مالی"), P("other", "سایر")]),
            F("contact", "مخاطب", FieldType.Lookup, lookupModule: "contacts"),
            F("description", "شرح", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "products", "محصول", "محصولات", "bx-package", "inventory", ++sort,
        [
            F("name", "نام محصول", FieldType.Text, required: true),
            F("sku", "کد کالا", FieldType.Text, unique: true),
            F("unit", "واحد", FieldType.Text),
            F("salePrice", "قیمت فروش", FieldType.Currency),
            F("isService", "خدمت است", FieldType.Checkbox, showInList: false),
            F("stockQty", "موجودی", FieldType.Number),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "vendors", "تأمین‌کننده", "تأمین‌کنندگان", "bx-store", "inventory", ++sort,
        [
            F("name", "نام تأمین‌کننده", FieldType.Text, required: true, unique: true),
            F("phone", "تلفن", FieldType.Phone),
            F("email", "ایمیل", FieldType.Email),
            F("city", "شهر", FieldType.Text),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "campaigns", "کمپین", "کمپین‌های تبلیغاتی", "bx-broadcast", "marketing", ++sort,
        [
            F("name", "نام کمپین", FieldType.Text, required: true),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "planned",
                picklist: [P("planned", "برنامه‌ریزی"), P("active", "فعال"), P("completed", "پایان‌یافته"), P("cancelled", "لغو")]),
            F("channel", "کانال", FieldType.Picklist,
                picklist: [P("sms", "پیامک"), P("email", "ایمیل"), P("social", "شبکه اجتماعی"), P("ads", "تبلیغات")]),
            F("budget", "بودجه", FieldType.Currency),
            F("startDate", "شروع", FieldType.Date),
            F("endDate", "پایان", FieldType.Date),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "quotes", "پیش‌فاکتور", "پیش‌فاکتورها", "bx-file", "sales", ++sort,
        [
            F("name", "عنوان", FieldType.Text, required: true),
            F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
            F("contact", "مخاطب", FieldType.Lookup, lookupModule: "contacts"),
            F("opportunity", "فرصت فروش", FieldType.Lookup, lookupModule: "opportunities"),
            F("number", "شماره", FieldType.Text, showInList: true),
            F("issueDate", "تاریخ", FieldType.Date, defaultValue: "__TODAY__"),
            F("amount", "مبلغ", FieldType.Currency),
            F("subTotal", "جمع جزء", FieldType.Currency, showInList: false),
            F("discountPercent", "تخفیف ٪", FieldType.Percent, showInList: false),
            F("discountAmount", "مبلغ تخفیف", FieldType.Currency, showInList: false),
            F("taxTotal", "مالیات", FieldType.Currency, showInList: false),
            F("grandTotal", "جمع کل", FieldType.Currency),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "Draft",
                picklist: [P("Draft", "پیش‌نویس"), P("Confirmed", "تأیید شده"), P("Converted", "تبدیل‌شده"), P("Canceled", "لغو")]),
            F("printTitle", "عنوان چاپی", FieldType.Text, showInList: false),
            F("validUntil", "اعتبار تا", FieldType.Date),
            F("sourceRecordId", "سند مبدأ", FieldType.Text, showInList: false),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ], documentKind: DocumentKind.SalesQuote, numberPrefix: "Q-", convertsTo: "sales_orders");

        await EnsureAsync(tenantId, "sales_orders", "سفارش فروش", "سفارش‌های فروش", "bx-cart", "sales", ++sort,
        [
            F("name", "شماره/عنوان سفارش", FieldType.Text, required: true),
            F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
            F("contact", "مخاطب", FieldType.Lookup, lookupModule: "contacts"),
            F("opportunity", "فرصت فروش", FieldType.Lookup, lookupModule: "opportunities"),
            F("number", "شماره", FieldType.Text),
            F("issueDate", "تاریخ", FieldType.Date, defaultValue: "__TODAY__"),
            F("amount", "مبلغ", FieldType.Currency),
            F("subTotal", "جمع جزء", FieldType.Currency, showInList: false),
            F("discountPercent", "تخفیف ٪", FieldType.Percent, showInList: false),
            F("discountAmount", "مبلغ تخفیف", FieldType.Currency, showInList: false),
            F("taxTotal", "مالیات", FieldType.Currency, showInList: false),
            F("grandTotal", "جمع کل", FieldType.Currency),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "Draft",
                picklist: [P("Draft", "پیش‌نویس"), P("Confirmed", "تأیید شده"), P("Converted", "تبدیل‌شده"), P("Canceled", "لغو")]),
            F("printTitle", "عنوان چاپی", FieldType.Text, showInList: false),
            F("orderDate", "تاریخ سفارش", FieldType.Date),
            F("sourceRecordId", "سند مبدأ", FieldType.Text, showInList: false),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ], documentKind: DocumentKind.SalesOrder, numberPrefix: "SO-", convertsTo: "invoices");

        await EnsureAsync(tenantId, "invoices", "فاکتور", "فاکتورها", "bx-receipt", "sales", ++sort,
        [
            F("name", "شماره فاکتور", FieldType.Text, required: true, unique: true),
            F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
            F("contact", "مخاطب", FieldType.Lookup, lookupModule: "contacts"),
            F("opportunity", "فرصت فروش", FieldType.Lookup, lookupModule: "opportunities"),
            F("number", "شماره", FieldType.Text),
            F("issueDate", "تاریخ", FieldType.Date, defaultValue: "__TODAY__"),
            F("amount", "مبلغ", FieldType.Currency),
            F("subTotal", "جمع جزء", FieldType.Currency, showInList: false),
            F("discountPercent", "تخفیف ٪", FieldType.Percent, showInList: false),
            F("discountAmount", "مبلغ تخفیف", FieldType.Currency, showInList: false),
            F("taxTotal", "مالیات", FieldType.Currency, showInList: false),
            F("grandTotal", "جمع کل", FieldType.Currency),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "Draft",
                picklist: [P("Draft", "پیش‌نویس"), P("Confirmed", "تأیید شده"), P("PartiallyPaid", "نیمه‌پرداخت"), P("Paid", "پرداخت‌شده"), P("Canceled", "لغو")]),
            F("printTitle", "عنوان چاپی", FieldType.Text, showInList: false),
            F("dueDate", "سررسید", FieldType.Date),
            F("sourceRecordId", "سند مبدأ", FieldType.Text, showInList: false),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ], documentKind: DocumentKind.SalesInvoice, numberPrefix: "INV-");

        await EnsureAsync(tenantId, "purchase_orders", "سفارش خرید", "سفارش‌های خرید", "bx-cart-download", "inventory", ++sort,
        [
            F("name", "عنوان سفارش", FieldType.Text, required: true),
            F("vendor", "تأمین‌کننده", FieldType.Lookup, lookupModule: "vendors"),
            F("number", "شماره", FieldType.Text),
            F("issueDate", "تاریخ", FieldType.Date, defaultValue: "__TODAY__"),
            F("amount", "مبلغ", FieldType.Currency),
            F("subTotal", "جمع جزء", FieldType.Currency, showInList: false),
            F("discountPercent", "تخفیف ٪", FieldType.Percent, showInList: false),
            F("discountAmount", "مبلغ تخفیف", FieldType.Currency, showInList: false),
            F("taxTotal", "مالیات", FieldType.Currency, showInList: false),
            F("grandTotal", "جمع کل", FieldType.Currency),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "Draft",
                picklist: [P("Draft", "پیش‌نویس"), P("Ordered", "سفارش‌شده"), P("Received", "دریافت‌شده"), P("Canceled", "لغو")]),
            F("printTitle", "عنوان چاپی", FieldType.Text, showInList: false),
            F("orderDate", "تاریخ", FieldType.Date),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ], documentKind: DocumentKind.PurchaseOrder, numberPrefix: "PO-");

        await EnsureAsync(tenantId, "contracts", "قرارداد خدمات", "قراردادهای خدمات", "bx-file-blank", "support", ++sort,
        [
            F("name", "عنوان قرارداد", FieldType.Text, required: true),
            F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "active",
                picklist: [P("draft", "پیش‌نویس"), P("active", "فعال"), P("expired", "منقضی"), P("cancelled", "لغو")]),
            F("startDate", "شروع", FieldType.Date),
            F("endDate", "پایان", FieldType.Date),
            F("amount", "مبلغ", FieldType.Currency),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "warranties", "گارانتی", "گارانتی‌ها", "bx-shield-quarter", "support", ++sort,
        [
            F("name", "عنوان گارانتی", FieldType.Text, required: true),
            F("product", "محصول", FieldType.Lookup, lookupModule: "products"),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "active",
                picklist: [P("active", "فعال"), P("expired", "منقضی"), P("claimed", "استفاده‌شده")]),
            F("startDate", "شروع", FieldType.Date),
            F("endDate", "پایان", FieldType.Date),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "projects", "پروژه", "پروژه‌ها", "bx-briefcase-alt-2", "projects", ++sort,
        [
            F("name", "نام پروژه", FieldType.Text, required: true),
            F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "active",
                picklist: [P("planned", "برنامه‌ریزی"), P("active", "فعال"), P("onhold", "متوقف"), P("done", "تکمیل"), P("cancelled", "لغو")]),
            F("startDate", "شروع", FieldType.Date),
            F("endDate", "پایان", FieldType.Date),
            F("budget", "بودجه", FieldType.Currency),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "project_tasks", "وظیفه پروژه", "وظایف پروژه", "bx-task", "projects", ++sort,
        [
            F("name", "عنوان وظیفه", FieldType.Text, required: true),
            F("project", "پروژه", FieldType.Lookup, lookupModule: "projects"),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "todo",
                picklist: [P("todo", "در انتظار"), P("doing", "در حال انجام"), P("done", "انجام شد")]),
            F("dueDate", "سررسید", FieldType.Date),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "project_phases", "فاز پروژه", "فازهای پروژه", "bx-git-branch", "projects", ++sort,
        [
            F("name", "نام فاز", FieldType.Text, required: true),
            F("project", "پروژه", FieldType.Lookup, lookupModule: "projects"),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "planned",
                picklist: [P("planned", "برنامه"), P("active", "جاری"), P("done", "تمام")]),
            F("sortOrder", "ترتیب", FieldType.Number),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "leaves", "مرخصی", "مرخصی و مأموریت", "bx-calendar-minus", "projects", ++sort,
        [
            F("name", "عنوان", FieldType.Text, required: true),
            F("type", "نوع", FieldType.Picklist, defaultValue: "leave",
                picklist: [P("leave", "مرخصی"), P("mission", "مأموریت"), P("remote", "دورکاری")]),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "pending",
                picklist: [P("pending", "در انتظار"), P("approved", "تأیید"), P("rejected", "رد")]),
            F("fromDate", "از تاریخ", FieldType.Date, required: true),
            F("toDate", "تا تاریخ", FieldType.Date, required: true),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "commissions", "پورسانت", "مشارکت در فروش", "bx-gift", "sales", ++sort,
        [
            F("name", "عنوان", FieldType.Text, required: true),
            F("amount", "مبلغ", FieldType.Currency),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "pending",
                picklist: [P("pending", "در انتظار"), P("paid", "پرداخت‌شده"), P("cancelled", "لغو")]),
            F("period", "دوره", FieldType.Text),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "documents", "سند", "اسناد", "bx-folder", "tools", ++sort,
        [
            F("name", "عنوان سند", FieldType.Text, required: true),
            F("category", "دسته", FieldType.Picklist,
                picklist: [P("contract", "قرارداد"), P("invoice", "فاکتور"), P("other", "سایر")]),
            F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "services", "سرویس", "سرویس‌ها", "bx-cog", "support", ++sort,
        [
            F("name", "نام سرویس", FieldType.Text, required: true),
            F("price", "قیمت", FieldType.Currency),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "active",
                picklist: [P("active", "فعال"), P("inactive", "غیرفعال")]),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "pricebooks", "دفترچه قیمت", "دفترچه‌های قیمت", "bx-book", "sales", ++sort,
        [
            F("name", "نام دفترچه", FieldType.Text, required: true),
            F("currency", "ارز", FieldType.Text, defaultValue: "IRR"),
            F("isActive", "فعال", FieldType.Checkbox, defaultValue: "true"),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "payments", "پرداخت", "پرداخت‌ها", "bx-money", "sales", ++sort,
        [
            F("name", "عنوان پرداخت", FieldType.Text, required: true),
            F("invoice", "فاکتور", FieldType.Lookup, lookupModule: "invoices"),
            F("amount", "مبلغ", FieldType.Currency, required: true),
            F("method", "روش", FieldType.Picklist,
                picklist: [P("cash", "نقد"), P("card", "کارت"), P("transfer", "حواله"), P("cheque", "چک")]),
            F("paidAt", "تاریخ پرداخت", FieldType.Date, defaultValue: "__TODAY__"),
            F("reference", "شماره پیگیری", FieldType.Text, showInList: false),
            F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ], isChild: true, showInMenu: false);

        await EnsureAsync(tenantId, "installments", "قسط", "اقساط", "bx-calendar-check", "sales", ++sort,
        [
            F("name", "عنوان قسط", FieldType.Text, required: true),
            F("invoice", "فاکتور", FieldType.Lookup, lookupModule: "invoices"),
            F("amount", "مبلغ", FieldType.Currency, required: true),
            F("dueDate", "سررسید", FieldType.Date, required: true),
            F("isPaid", "پرداخت‌شده", FieldType.Checkbox, defaultValue: "false"),
            F("paidAt", "تاریخ پرداخت", FieldType.Date, showInList: false)
        ], isChild: true, showInMenu: false);

        await EnsureAsync(tenantId, "warehouses", "انبار", "انبارها", "bx-building-house", "inventory", ++sort,
        [
            F("name", "نام انبار", FieldType.Text, required: true),
            F("code", "کد", FieldType.Text, unique: true),
            F("city", "شهر", FieldType.Text),
            F("isActive", "فعال", FieldType.Checkbox, defaultValue: "true"),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        // product_sales از منو مخفی — خطوط فاکتور جایگزین آن است
        await EnsureAsync(tenantId, "product_sales", "پرونده فروش", "پرونده‌های فروش محصول", "bx-purchase-tag", "sales", ++sort,
        [
            F("name", "عنوان پرونده", FieldType.Text, required: true),
            F("product", "محصول", FieldType.Lookup, lookupModule: "products"),
            F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "open",
                picklist: [P("open", "باز"), P("won", "برنده"), P("lost", "بازنده")]),
            F("amount", "مبلغ", FieldType.Currency),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ], showInMenu: false);

        _ = (adminProfileId, userProfileId);
    }

    /// <summary>فیلد/بلاک/وابستگی/رکورد نمونه برای دمو — روی همهٔ ماژول‌های tenant.</summary>
    public async Task EnsureDemoExtrasAsync(int tenantId)
    {
        // قبلاً اعمال شده — حلقهٔ سنگین نزن (حتی بعد از ریستارت حافظه)
        if (await _db.Fields.AsNoTracking().AnyAsync(f => f.TenantId == tenantId && f.Name == "demo_tag"))
            return;

        var modules = await _db.Modules.Where(m => m.TenantId == tenantId && m.IsActive).ToListAsync();
        foreach (var module in modules)
        {
            await EnsureMenuFlagsAsync(module);
            await EnsureExtraBlockAndCustomFieldsAsync(tenantId, module);
            await EnsureSampleDependencyAsync(tenantId, module);
            await EnsureSampleRecordsAsync(tenantId, module);
        }
        await _db.SaveChangesAsync();
        _cache.Remove($"modules:{tenantId}");
    }

    private async Task EnsureMenuFlagsAsync(ModuleDef module)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(module.MenuGroup))
        {
            module.MenuGroup = GuessMenuGroup(module.Name);
            changed = true;
        }
        if (!module.ShowInMenu && IsBusinessModule(module.Name))
        {
            module.ShowInMenu = true;
            changed = true;
        }
        if (changed)
            await _db.SaveChangesAsync();
    }

    private static bool IsBusinessModule(string name) =>
        name is not ("webforms" or "surveys" or "templates" or "workflows" or "reports");

    private static string GuessMenuGroup(string name) => name switch
    {
        "leads" or "campaigns" => "marketing",
        "contacts" or "organizations" or "opportunities" or "quotes" or "sales_orders" or "invoices"
            or "commissions" or "pricebooks" or "payments" or "product_sales" => "sales",
        "tickets" or "contracts" or "warranties" or "services" or "calls" => "support",
        "products" or "vendors" or "purchase_orders" or "warehouses" => "inventory",
        "projects" or "project_tasks" or "project_phases" or "leaves" => "projects",
        "tasks" or "events" or "documents" => "tools",
        _ => "tools"
    };

    private async Task EnsureExtraBlockAndCustomFieldsAsync(int tenantId, ModuleDef module)
    {
        var blocks = await _db.FieldBlocks.Where(b => b.ModuleId == module.Id).OrderBy(b => b.SortOrder).ToListAsync();
        var main = blocks.FirstOrDefault(b => b.Name == "main") ?? blocks.FirstOrDefault();
        if (main is null) return;

        if (!blocks.Any(b => b.Name == "extra"))
        {
            _db.FieldBlocks.Add(new FieldBlock
            {
                TenantId = tenantId,
                ModuleId = module.Id,
                Name = "extra",
                Label = "اطلاعات تکمیلی",
                SortOrder = (blocks.Max(b => b.SortOrder) + 1)
            });
            await _db.SaveChangesAsync();
            blocks = await _db.FieldBlocks.Where(b => b.ModuleId == module.Id).ToListAsync();
        }

        var extra = blocks.First(b => b.Name == "extra");
        var existing = await _db.Fields.Where(f => f.ModuleId == module.Id).Select(f => f.Name).ToListAsync();
        var maxSort = await _db.Fields.Where(f => f.ModuleId == module.Id).MaxAsync(f => (int?)f.SortOrder) ?? 0;

        async Task AddCustom(string name, string label, FieldType type, (string v, string l)[]? pick = null, bool showInList = true)
        {
            if (existing.Contains(name)) return;
            var field = new FieldDef
            {
                TenantId = tenantId,
                ModuleId = module.Id,
                BlockId = extra.Id,
                Name = name,
                Label = label,
                Type = type,
                IsCustom = true,
                ShowInList = showInList,
                SortOrder = ++maxSort
            };
            _db.Fields.Add(field);
            await _db.SaveChangesAsync();
            if (pick is { Length: > 0 })
            {
                var o = 0;
                foreach (var (v, l) in pick)
                {
                    _db.PicklistValues.Add(new PicklistValue
                    {
                        TenantId = tenantId,
                        FieldId = field.Id,
                        Value = v,
                        Label = l,
                        SortOrder = ++o,
                        IsActive = true
                    });
                }
                await _db.SaveChangesAsync();
            }
            existing.Add(name);
        }

        // فیلدهای سفارشی مشترک + اختصاصی سبک
        await AddCustom("demo_tag", "برچسب دمو", FieldType.Picklist,
            [("vip", "VIP"), ("normal", "عادی"), ("trial", "آزمایشی")]);
        await AddCustom("demo_note", "یادداشت داخلی دمو", FieldType.MultilineText, showInList: false);

        switch (module.Name)
        {
            case "tickets":
                await AddCustom("customer_mood", "خلق مشتری", FieldType.Picklist,
                    [("calm", "آرام"), ("upset", "ناراضی"), ("angry", "عصبانی")]);
                break;
            case "products":
                await AddCustom("brand_demo", "برند نمایشی", FieldType.Text);
                break;
            case "leads":
                await AddCustom("booth_type", "نوع غرفه", FieldType.Picklist,
                    [("gold", "طلایی"), ("silver", "نقره‌ای"), ("normal", "عادی")]);
                break;
            case "invoices":
                await AddCustom("payment_ref", "شناسه پیگیری پرداخت", FieldType.Text);
                break;
            case "projects":
                await AddCustom("risk_level", "سطح ریسک", FieldType.Picklist,
                    [("low", "کم"), ("medium", "متوسط"), ("high", "بالا")]);
                break;
        }
    }

    private async Task EnsureSampleDependencyAsync(int tenantId, ModuleDef module)
    {
        var fields = await _db.Fields
            .Include(f => f.PicklistValues)
            .Where(f => f.ModuleId == module.Id)
            .ToListAsync();

        var controller = fields.FirstOrDefault(f =>
            f.Type == FieldType.Picklist && !f.IsCustom && f.PicklistValues.Any());
        var target = fields.FirstOrDefault(f => f.IsCustom && f.Name == "demo_note");
        if (controller is null || target is null) return;
        if (!string.IsNullOrWhiteSpace(target.VisibilityRuleJson)) return;

        var firstVal = controller.PicklistValues.OrderBy(p => p.SortOrder).FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(firstVal)) return;

        target.VisibilityRuleJson =
            $"{{\"action\":\"show\",\"logic\":\"and\",\"conditions\":[{{\"field\":\"{controller.Name}\",\"op\":\"eq\",\"value\":\"{firstVal}\"}}]}}";
        await _db.SaveChangesAsync();
        _ = tenantId;
    }

    private async Task EnsureSampleRecordsAsync(int tenantId, ModuleDef module)
    {
        var count = await _db.Records.CountAsync(r => r.ModuleId == module.Id && r.TenantId == tenantId);
        if (count >= 2) return;

        var fields = await _db.Fields.Where(f => f.ModuleId == module.Id).OrderBy(f => f.SortOrder).ToListAsync();
        var titleField = fields.FirstOrDefault(f => f.Name is "name" or "subject") ?? fields.FirstOrDefault(f => f.Type == FieldType.Text);
        if (titleField is null) return;

        for (var i = count; i < 2; i++)
        {
            var data = new Dictionary<string, string?>();
            foreach (var f in fields.Where(x => x.IsVisible))
            {
                if (f.Name == titleField.Name)
                    data[f.Name] = $"{module.SingularLabel} نمونه {i + 1}";
                else if (f.Type == FieldType.Picklist && !string.IsNullOrWhiteSpace(f.DefaultValue))
                    data[f.Name] = f.DefaultValue;
                else if (f.Type == FieldType.Currency || f.Type == FieldType.Number)
                    data[f.Name] = (1000 * (i + 1)).ToString();
                else if (f.Type == FieldType.Checkbox)
                    data[f.Name] = "false";
            }

            _db.Records.Add(new DynamicRecord
            {
                TenantId = tenantId,
                ModuleId = module.Id,
                Title = data.GetValueOrDefault(titleField.Name) ?? $"{module.SingularLabel} {i + 1}",
                CustomData = System.Text.Json.JsonSerializer.Serialize(data)
            });
        }
        await _db.SaveChangesAsync();
    }

    private async Task EnsureAsync(
        int tenantId,
        string name, string singular, string plural, string icon, string menuGroup, int sortOrder,
        FieldSpec[] specs,
        DocumentKind documentKind = DocumentKind.None,
        string? numberPrefix = null,
        string? convertsTo = null,
        bool isChild = false,
        bool showInMenu = true)
    {
        if (await _db.Modules.AnyAsync(m => m.TenantId == tenantId && m.Name == name))
        {
            var existing = await _db.Modules.FirstAsync(m => m.TenantId == tenantId && m.Name == name);
            existing.ShowInMenu = showInMenu && !isChild;
            existing.MenuGroup = menuGroup;
            existing.IsChildModule = isChild;
            if (documentKind != DocumentKind.None)
            {
                existing.DocumentKind = documentKind;
                existing.NumberPrefix ??= numberPrefix;
                existing.ConvertsToModule ??= convertsTo;
            }
            if (existing.SortOrder == 0) existing.SortOrder = sortOrder;
            await _db.SaveChangesAsync();
            return;
        }

        var module = new ModuleDef
        {
            TenantId = tenantId,
            Name = name,
            SingularLabel = singular,
            PluralLabel = plural,
            Icon = icon,
            IsSystem = true,
            IsActive = true,
            ShowInMenu = showInMenu && !isChild,
            MenuGroup = menuGroup,
            SortOrder = sortOrder,
            IsChildModule = isChild,
            DocumentKind = documentKind,
            NumberPrefix = numberPrefix,
            ConvertsToModule = convertsTo,
            NextNumber = 1001,
            DuplicateCheckEnabled = specs.Any(s => s.Unique),
            DuplicateMatchMode = "or",
            DuplicateIgnoreEmpty = true,
            DuplicateSyncPolicy = "latest"
        };
        _db.Modules.Add(module);
        await _db.SaveChangesAsync();

        var mainBlock = new FieldBlock
        {
            TenantId = tenantId,
            ModuleId = module.Id,
            Name = "main",
            Label = "اطلاعات اصلی",
            SortOrder = 1
        };
        _db.FieldBlocks.Add(mainBlock);
        await _db.SaveChangesAsync();

        var order = 0;
        var fields = specs.Select(s => new FieldDef
        {
            TenantId = tenantId,
            ModuleId = module.Id,
            BlockId = mainBlock.Id,
            Name = s.Name,
            Label = s.Label,
            Type = s.Type,
            IsCustom = false,
            IsRequired = s.Required,
            IsUniqueCheck = s.Unique,
            ShowInList = s.ShowInList,
            DefaultValue = s.DefaultValue,
            LookupModule = s.LookupModule,
            SortOrder = ++order
        }).ToList();
        _db.Fields.AddRange(fields);
        await _db.SaveChangesAsync();

        for (var i = 0; i < specs.Length; i++)
        {
            var pOrder = 0;
            foreach (var (value, label, color) in specs[i].Picklist)
            {
                _db.PicklistValues.Add(new PicklistValue
                {
                    TenantId = tenantId,
                    FieldId = fields[i].Id,
                    Value = value,
                    Label = label,
                    Color = color,
                    SortOrder = ++pOrder,
                    IsActive = true
                });
            }
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>ماژول‌های خط سند + بلاک LineItems + فیلدهای گمشده روی هدرهای موجود.</summary>
    private async Task EnsureDocumentModulesAsync(int tenantId)
    {
        var lineFields = new FieldSpec[]
        {
            F("title", "عنوان", FieldType.Text, required: true),
            F("product", "محصول", FieldType.Lookup, lookupModule: "products"),
            F("quantity", "تعداد", FieldType.Decimal, required: true, defaultValue: "1"),
            F("unitPrice", "قیمت واحد", FieldType.Currency, required: true),
            F("discountPercent", "تخفیف ٪", FieldType.Percent, defaultValue: "0"),
            F("taxPercent", "مالیات ٪", FieldType.Percent, defaultValue: "0"),
            F("lineTotal", "جمع سطر", FieldType.Currency),
            F("sortOrder", "ترتیب", FieldType.Number, showInList: false, defaultValue: "0")
        };

        var lineDefs = new (string LineModule, string ParentModule, string Singular, string Plural, string LinkField)[]
        {
            ("quote_lines", "quotes", "سطر پیش‌فاکتور", "سطرهای پیش‌فاکتور", "quote"),
            ("sales_order_lines", "sales_orders", "سطر سفارش", "سطرهای سفارش فروش", "sales_order"),
            ("invoice_lines", "invoices", "سطر فاکتور", "سطرهای فاکتور", "invoice"),
            ("purchase_order_lines", "purchase_orders", "سطر سفارش خرید", "سطرهای سفارش خرید", "purchase_order")
        };

        var sort = 900;
        foreach (var (lineModule, parentModule, singular, plural, linkField) in lineDefs)
        {
            var specs = lineFields
                .Concat([F(linkField, "سند والد", FieldType.Lookup, lookupModule: parentModule, showInList: false)])
                .ToArray();

            await EnsureAsync(tenantId, lineModule, singular, plural, "bx-list-ul", "sales", ++sort,
                specs, isChild: true, showInMenu: false);

            await EnsureLineItemsBlockAsync(tenantId, parentModule, lineModule, linkField);
            await EnsureMissingDocumentHeaderFieldsAsync(tenantId, parentModule);
        }

        // مخفی کردن ماژول‌های یتیم/جایگزین‌شده برای tenantهای قدیمی
        foreach (var orphan in new[] { "product_sales", "payments" })
        {
            var mod = await _db.Modules.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == orphan);
            if (mod is not null && mod.ShowInMenu)
            {
                mod.ShowInMenu = false;
                if (orphan == "payments")
                    mod.IsChildModule = true;
                await _db.SaveChangesAsync();
            }
        }

        // تنظیم DocumentKind روی ماژول‌های موجود
        await EnsureDocumentKindAsync(tenantId, "quotes", DocumentKind.SalesQuote, "Q-", "sales_orders");
        await EnsureDocumentKindAsync(tenantId, "sales_orders", DocumentKind.SalesOrder, "SO-", "invoices");
        await EnsureDocumentKindAsync(tenantId, "invoices", DocumentKind.SalesInvoice, "INV-", null);
        await EnsureDocumentKindAsync(tenantId, "purchase_orders", DocumentKind.PurchaseOrder, "PO-", null);

        _cache.Remove($"modules:{tenantId}");

        var touched = lineDefs.Select(d => d.LineModule)
            .Concat(lineDefs.Select(d => d.ParentModule))
            .ToList();
        var touchedIds = await _db.Modules
            .Where(m => m.TenantId == tenantId && touched.Contains(m.Name))
            .Select(m => m.Id)
            .ToListAsync();
        foreach (var id in touchedIds)
        {
            _cache.Remove($"fields:{tenantId}:{id}");
            _cache.Remove($"blocks:{tenantId}:{id}");
        }
    }

    private async Task EnsureDocumentKindAsync(
        int tenantId, string name, DocumentKind kind, string prefix, string? convertsTo)
    {
        var module = await _db.Modules.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == name);
        if (module is null) return;
        module.DocumentKind = kind;
        module.NumberPrefix ??= prefix;
        if (module.NextNumber < 1001) module.NextNumber = 1001;
        module.ConvertsToModule ??= convertsTo;
        await _db.SaveChangesAsync();
    }

    private async Task EnsureLineItemsBlockAsync(
        int tenantId, string parentModuleName, string lineModuleName, string linkField)
    {
        var parent = await _db.Modules.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == parentModuleName);
        if (parent is null) return;

        var existing = await _db.FieldBlocks.FirstOrDefaultAsync(b =>
            b.ModuleId == parent.Id && b.Name == "line_items");
        if (existing is not null)
        {
            existing.Kind = BlockKind.LineItems;
            existing.LineModuleName = lineModuleName;
            existing.LineLinkField = linkField;
            await _db.SaveChangesAsync();
            return;
        }

        var maxSort = await _db.FieldBlocks.Where(b => b.ModuleId == parent.Id)
            .MaxAsync(b => (int?)b.SortOrder) ?? 0;

        _db.FieldBlocks.Add(new FieldBlock
        {
            TenantId = tenantId,
            ModuleId = parent.Id,
            Name = "line_items",
            Label = "اطلاعات آیتم",
            SortOrder = maxSort + 1,
            Kind = BlockKind.LineItems,
            LineModuleName = lineModuleName,
            LineLinkField = linkField
        });
        await _db.SaveChangesAsync();
    }

    private async Task EnsureMissingDocumentHeaderFieldsAsync(int tenantId, string moduleName)
    {
        var module = await _db.Modules.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == moduleName);
        if (module is null) return;

        var needed = new (string Name, string Label, FieldType Type, string? Lookup)[]
        {
            ("number", "شماره", FieldType.Text, null),
            ("contact", "مخاطب", FieldType.Lookup, "contacts"),
            ("issueDate", "تاریخ", FieldType.Date, null),
            ("subTotal", "جمع جزء", FieldType.Currency, null),
            ("discountPercent", "تخفیف ٪", FieldType.Percent, null),
            ("discountAmount", "مبلغ تخفیف", FieldType.Currency, null),
            ("taxTotal", "مالیات", FieldType.Currency, null),
            ("grandTotal", "جمع کل", FieldType.Currency, null),
            ("printTitle", "عنوان چاپی", FieldType.Text, null),
            ("sourceRecordId", "سند مبدأ", FieldType.Text, null)
        };

        var blockId = await _db.FieldBlocks.Where(b => b.ModuleId == module.Id)
            .OrderBy(b => b.SortOrder).Select(b => (int?)b.Id).FirstOrDefaultAsync();
        var maxSort = await _db.Fields.Where(f => f.ModuleId == module.Id).MaxAsync(f => (int?)f.SortOrder) ?? 0;
        var existingNames = await _db.Fields.Where(f => f.ModuleId == module.Id).Select(f => f.Name).ToListAsync();
        var added = false;

        foreach (var (name, label, type, lookup) in needed)
        {
            if (existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            _db.Fields.Add(new FieldDef
            {
                TenantId = tenantId,
                ModuleId = module.Id,
                BlockId = blockId,
                Name = name,
                Label = label,
                Type = type,
                LookupModule = lookup,
                IsCustom = false,
                ShowInList = name is "number" or "grandTotal" or "contact",
                SortOrder = ++maxSort
            });
            added = true;
        }

        if (added)
            await _db.SaveChangesAsync();
    }

    /// <summary>Lookup فرصت روی مالی + RelationDef فرصت→پیش‌فاکتور/فاکتور/سفارش.</summary>
    private async Task EnsureFinanceOpportunityLinksAsync(int tenantId)
    {
        await EnsureLookupFieldAsync(tenantId, "quotes", "opportunity", "فرصت فروش", "opportunities");
        await EnsureLookupFieldAsync(tenantId, "invoices", "opportunity", "فرصت فروش", "opportunities");
        await EnsureLookupFieldAsync(tenantId, "sales_orders", "opportunity", "فرصت فروش", "opportunities");

        var modules = await _db.Modules.AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .ToDictionaryAsync(m => m.Name, m => m.Id);

        if (modules.TryGetValue("opportunities", out var oppId))
        {
            if (modules.TryGetValue("quotes", out var quotesId))
                await EnsureRelationAsync(tenantId, oppId, quotesId, "پیش‌فاکتورها", "فرصت فروش", "opportunity");
            if (modules.TryGetValue("invoices", out var invoicesId))
                await EnsureRelationAsync(tenantId, oppId, invoicesId, "فاکتورها", "فرصت فروش", "opportunity");
            if (modules.TryGetValue("sales_orders", out var ordersId))
                await EnsureRelationAsync(tenantId, oppId, ordersId, "سفارش‌های فروش", "فرصت فروش", "opportunity");
        }

        _cache.Remove($"modules:{tenantId}");
        foreach (var id in modules.Values)
        {
            _cache.Remove($"fields:{tenantId}:{id}");
            _cache.Remove($"blocks:{tenantId}:{id}");
        }
    }

    private async Task EnsureLookupFieldAsync(
        int tenantId, string moduleName, string fieldName, string label, string lookupModule)
    {
        var module = await _db.Modules.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == moduleName);
        if (module is null) return;

        if (await _db.Fields.AnyAsync(f => f.ModuleId == module.Id && f.Name == fieldName))
            return;

        var blockId = await _db.FieldBlocks.Where(b => b.ModuleId == module.Id)
            .OrderBy(b => b.SortOrder)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync();
        var maxSort = await _db.Fields.Where(f => f.ModuleId == module.Id).MaxAsync(f => (int?)f.SortOrder) ?? 0;

        _db.Fields.Add(new FieldDef
        {
            TenantId = tenantId,
            ModuleId = module.Id,
            BlockId = blockId,
            Name = fieldName,
            Label = label,
            Type = FieldType.Lookup,
            LookupModule = lookupModule,
            IsCustom = false,
            IsRequired = false,
            ShowInList = true,
            SortOrder = maxSort + 1
        });
        await _db.SaveChangesAsync();
    }

    private async Task EnsureRelationAsync(
        int tenantId, int sourceModuleId, int targetModuleId,
        string tabLabel, string relatedFieldLabel, string linkFieldName)
    {
        var exists = await _db.Relations.AnyAsync(r =>
            r.TenantId == tenantId
            && r.SourceModuleId == sourceModuleId
            && r.TargetModuleId == targetModuleId
            && r.LinkFieldName == linkFieldName);
        if (exists) return;

        var lookupOk = await _db.Fields.AnyAsync(f =>
            f.TenantId == tenantId
            && f.ModuleId == targetModuleId
            && f.Name == linkFieldName
            && f.Type == FieldType.Lookup);
        if (!lookupOk) return;

        _db.Relations.Add(new RelationDef
        {
            TenantId = tenantId,
            SourceModuleId = sourceModuleId,
            TargetModuleId = targetModuleId,
            Label = tabLabel,
            RelatedFieldLabel = relatedFieldLabel,
            LinkFieldName = linkFieldName,
            Kind = RelationKind.OneToMany
        });
        await _db.SaveChangesAsync();
    }

    private static FieldSpec F(
        string name, string label, FieldType type,
        bool required = false, bool unique = false, bool showInList = true,
        string? defaultValue = null, string? lookupModule = null,
        (string Value, string Label, string? Color)[]? picklist = null)
        => new(name, label, type, required, unique, showInList, defaultValue, lookupModule, picklist ?? []);

    private static (string Value, string Label, string? Color) P(string value, string label, string? color = null)
        => (value, label, color);

    private sealed record FieldSpec(
        string Name, string Label, FieldType Type,
        bool Required, bool Unique, bool ShowInList,
        string? DefaultValue, string? LookupModule,
        (string Value, string Label, string? Color)[] Picklist);
}
