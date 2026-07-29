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

    public BusinessModuleSeeder(CrmDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task EnsureSeededAsync(int tenantId)
    {
        var cacheKey = $"business-modules-ok:{tenantId}";
        if (_cache.TryGetValue(cacheKey, out bool ok) && ok)
            return;

        var expected = new[]
        {
            "tickets", "products", "vendors", "campaigns", "quotes", "sales_orders", "invoices",
            "purchase_orders", "contracts", "warranties", "projects", "project_tasks", "project_phases",
            "leaves", "commissions", "documents", "services", "pricebooks", "payments", "warehouses", "product_sales"
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
            F("amount", "مبلغ", FieldType.Currency),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "draft",
                picklist: [P("draft", "پیش‌نویس"), P("sent", "ارسال‌شده"), P("accepted", "پذیرفته"), P("rejected", "رد شده")]),
            F("validUntil", "اعتبار تا", FieldType.Date),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "sales_orders", "سفارش فروش", "سفارش‌های فروش", "bx-cart", "sales", ++sort,
        [
            F("name", "شماره/عنوان سفارش", FieldType.Text, required: true),
            F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
            F("amount", "مبلغ", FieldType.Currency),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "new",
                picklist: [P("new", "جدید"), P("processing", "در حال پردازش"), P("shipped", "ارسال‌شده"), P("done", "تکمیل"), P("cancelled", "لغو")]),
            F("orderDate", "تاریخ سفارش", FieldType.Date),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "invoices", "فاکتور", "فاکتورها", "bx-receipt", "sales", ++sort,
        [
            F("name", "شماره فاکتور", FieldType.Text, required: true, unique: true),
            F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
            F("amount", "مبلغ", FieldType.Currency),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "unpaid",
                picklist: [P("unpaid", "پرداخت‌نشده"), P("partial", "نیمه‌پرداخت"), P("paid", "پرداخت‌شده"), P("void", "باطل")]),
            F("dueDate", "سررسید", FieldType.Date),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "purchase_orders", "سفارش خرید", "سفارش‌های خرید", "bx-cart-download", "inventory", ++sort,
        [
            F("name", "عنوان سفارش", FieldType.Text, required: true),
            F("vendor", "تأمین‌کننده", FieldType.Lookup, lookupModule: "vendors"),
            F("amount", "مبلغ", FieldType.Currency),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "draft",
                picklist: [P("draft", "پیش‌نویس"), P("ordered", "سفارش‌شده"), P("received", "دریافت‌شده"), P("cancelled", "لغو")]),
            F("orderDate", "تاریخ", FieldType.Date),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

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
            F("amount", "مبلغ", FieldType.Currency, required: true),
            F("method", "روش", FieldType.Picklist,
                picklist: [P("cash", "نقد"), P("card", "کارت"), P("transfer", "حواله"), P("cheque", "چک")]),
            F("paidAt", "تاریخ پرداخت", FieldType.Date),
            F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "warehouses", "انبار", "انبارها", "bx-building-house", "inventory", ++sort,
        [
            F("name", "نام انبار", FieldType.Text, required: true),
            F("code", "کد", FieldType.Text, unique: true),
            F("city", "شهر", FieldType.Text),
            F("isActive", "فعال", FieldType.Checkbox, defaultValue: "true"),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

        await EnsureAsync(tenantId, "product_sales", "پرونده فروش", "پرونده‌های فروش محصول", "bx-purchase-tag", "sales", ++sort,
        [
            F("name", "عنوان پرونده", FieldType.Text, required: true),
            F("product", "محصول", FieldType.Lookup, lookupModule: "products"),
            F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
            F("status", "وضعیت", FieldType.Picklist, defaultValue: "open",
                picklist: [P("open", "باز"), P("won", "برنده"), P("lost", "بازنده")]),
            F("amount", "مبلغ", FieldType.Currency),
            F("description", "توضیحات", FieldType.MultilineText, showInList: false)
        ]);

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
        FieldSpec[] specs)
    {
        if (await _db.Modules.AnyAsync(m => m.TenantId == tenantId && m.Name == name))
        {
            var existing = await _db.Modules.FirstAsync(m => m.TenantId == tenantId && m.Name == name);
            existing.ShowInMenu = true;
            existing.MenuGroup = menuGroup;
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
            ShowInMenu = true,
            MenuGroup = menuGroup,
            SortOrder = sortOrder,
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
