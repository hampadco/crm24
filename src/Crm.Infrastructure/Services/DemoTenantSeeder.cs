using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Crm.Core;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;

namespace Crm.Infrastructure.Services;

/// <summary>
/// یک Tenant نمونه همیشه با حداقل ~۵۵ رکورد در هر صفحهٔ پنل App.
/// تاریخ‌ها نسبت به زمان اجرای seed (UtcNow) ساخته می‌شوند. Idempotent است.
/// </summary>
public class DemoTenantSeeder
{
    public const string DemoSlug = "demo";
    public const string DemoPassword = "Demo@1405";
    public const int TargetCount = 55;

    public string DemoEmail => _branding.DemoEmail;

    private static readonly string[] FirstNames =
    [
        "علی", "محمد", "رضا", "حسین", "مهدی", "امیر", "سعید", "حامد", "پارسا", "آرین",
        "زهرا", "فاطمه", "مریم", "سارا", "نگار", "نیلوفر", "هستی", "آیدا", "یلدا", "لیلا",
        "کیان", "دانیال", "نیما", "پویا", "آرش", "شایان", "یاسمن", "نازنین", "الهام", "سمیرا"
    ];

    private static readonly string[] LastNames =
    [
        "محمدی", "رضایی", "حسینی", "کریمی", "موسوی", "نوری", "جعفری", "احمدی", "کاظمی", "صادقی",
        "اکبری", "باقری", "نجفی", "شریفی", "طاهری", "امینی", "رحیمی", "قاسمی", "مرادی", "یوسفی",
        "حیدری", "عزیزی", "اسدی", "جوان", "فرهادی", "سلطانی", "پناهی", "رستمی", "نادری", "کمالی"
    ];

    private static readonly string[] Cities =
    [
        "تهران", "اصفهان", "شیراز", "مشهد", "تبریز", "کرج", "اهواز", "قم", "کرمان", "رشت",
        "یزد", "همدان", "ارومیه", "کرمانشاه", "زاهدان"
    ];

    private static readonly string[] Companies =
    [
        "آریا تجارت", "پارس نوین", "سپهر صنعت", "افق روشن", "نیک‌سازان", "پیشگامان داده",
        "زرین کاله", "آسمان آبی", "کارا سیستم", "راه‌کار پرداز", "نگاه هوشمند", "بوم‌رنگ",
        "تدبیر گستر", "فن‌آوران مهر", "صنایع سبک", "بازرگانی امید", "مهندسی سازان", "خدمات برتر",
        "نوآوران فردا", "پیمان‌سازان", "داده گستر تهران", "سپید رایان", "آتیه‌سازان", "کیمیا پردازش",
        "پژواک سیستم", "هوشمندسازان", "سایان تجارت", "ارغوان صنعت", "نگین رایانه", "سپنتا گروه"
    ];

    private static readonly (string Name, string Sku, string Unit, decimal Price)[] ProductCatalog =
    [
        ("آچار فرانسه ۱۰ اینچ", "TOOL-WR-10", "عدد", 185_000m),
        ("آچار تخت ۱۲ میلی‌متر", "TOOL-FL-12", "عدد", 65_000m),
        ("پیچ‌گوشتی چهارسو ست ۶ عددی", "TOOL-SD-06", "ست", 95_000m),
        ("دریل شارژی ۱۸ ولت", "TOOL-DR-18", "عدد", 3_850_000m),
        ("متر ۵ متری فلزی", "TOOL-TP-05", "عدد", 120_000m),
        ("میخ فولادی جعبه ۵۰۰ گرم", "HW-NAIL-500", "جعبه", 78_000m),
        ("پیچ خودکار بسته ۱۰۰ عددی", "HW-SCR-100", "بسته", 45_000m),
        ("میز اداری MDF ۱۲۰ سانتی", "FURN-DSK-120", "عدد", 4_850_000m),
        ("صندلی اداری مشبک", "FURN-CHR-01", "عدد", 2_250_000m),
        ("کمد بایگانی فلزی", "FURN-CAB-01", "عدد", 6_400_000m),
        ("قفسه انبار ۴ طبقه", "FURN-SHF-04", "عدد", 1_980_000m),
        ("لپ‌تاپ ۱۵ اینچ Core i5", "ELEC-LAP-15", "عدد", 28_900_000m),
        ("مانیتور ۲۷ اینچ IPS", "ELEC-MON-27", "عدد", 8_750_000m),
        ("گوشی موبایل ۶۴ گیگ", "ELEC-PHN-64", "عدد", 12_500_000m),
        ("تبلت ۱۰ اینچ", "ELEC-TAB-10", "عدد", 9_200_000m),
        ("کیبورد و ماوس بی‌سیم", "ELEC-KM-01", "ست", 680_000m),
        ("هارد اکسترنال ۱ ترابایت", "ELEC-HDD-1T", "عدد", 2_450_000m),
        ("روغن موتور ۴ لیتری SN", "AUTO-OIL-4L", "عدد", 780_000m),
        ("فیلتر روغن پراید", "AUTO-FOIL-PR", "عدد", 95_000m),
        ("سپر جلو پراید", "AUTO-BMP-PR", "عدد", 1_450_000m),
        ("چراغ جلو پژو ۴۰۵", "AUTO-LGT-405", "عدد", 890_000m),
        ("لنت ترمز جلو سمند", "AUTO-BRK-SM", "جفت", 520_000m),
        ("باتری ۶۶ آمپر اتمی", "AUTO-BAT-66", "عدد", 3_200_000m),
        ("تایر ۲۰۵/۶۰R15", "AUTO-TYR-205", "حلقه", 4_100_000m),
        ("جاروبرقی صنعتی ۱۵۰۰ وات", "HOME-VAC-15", "عدد", 5_600_000m),
        ("آبگرمکن دیواری گازی", "HOME-WH-01", "عدد", 7_800_000m),
        ("کولر گازی ۱۲ هزار", "HOME-AC-12", "عدد", 22_500_000m),
        ("یخچال فریزر ۱۴ فوت", "HOME-RF-14", "عدد", 18_700_000m),
        ("ماشین لباسشویی ۷ کیلویی", "HOME-WM-07", "عدد", 16_200_000m),
        ("شیرآلات ظرفشویی اهرمی", "PLUMB-TP-01", "عدد", 1_150_000m),
        ("لوله PVC سایز ۲۰ (شاخه)", "PLUMB-PVC-20", "شاخه", 95_000m),
        ("رنگ روغنی سفید ۴ کیلویی", "PAINT-W-4K", "حلب", 640_000m),
        ("غلطک نقاشی ۳۰ سانتی", "PAINT-RL-30", "عدد", 85_000m),
        ("سیمان تیپ ۲ پاکت ۵۰ کیلویی", "BLD-CEM-50", "پاکت", 145_000m),
        ("گچ ساختمانی ۲۵ کیلویی", "BLD-PLS-25", "پاکت", 78_000m),
        ("آجر فشاری (هزار عدد)", "BLD-BRK-1K", "هزار", 2_800_000m),
        ("کابل برق ۲.۵ میلی‌متر (متر)", "ELEC-CBL-25", "متر", 28_000m),
        ("کلید و پریز توکار ست", "ELEC-SW-01", "ست", 210_000m),
        ("لامپ LED ۱۲ وات", "ELEC-LED-12", "عدد", 55_000m),
        ("پرینتر لیزری سیاه‌وسفید", "OFF-PRT-BW", "عدد", 6_900_000m),
        ("کاغذ A4 بسته ۵۰۰ برگی", "OFF-PPR-A4", "بسته", 185_000m),
        ("کارتریج تونر مشکی", "OFF-TNR-BK", "عدد", 1_250_000m),
        ("پوشه زونکن اداری", "OFF-FLD-01", "عدد", 95_000m),
        ("میز پذیرایی چوبی", "FURN-COF-01", "عدد", 3_400_000m),
        ("کمد رختکن دو درب", "FURN-WRD-02", "عدد", 5_100_000m),
        ("دوربین مدار بسته ۴ مگاپیکسل", "SEC-CAM-4M", "عدد", 1_850_000m),
        ("قفل دیجیتال اثرانگشتی", "SEC-LCK-01", "عدد", 4_200_000m),
        ("کپسول آتش‌نشانی ۶ کیلویی", "SEC-FE-06", "عدد", 980_000m)
    ];

    /// <summary>نام‌های کوتاه برای اسناد legacy (سازگاری با سید قدیمی).</summary>
    private static readonly string[] Products =
        ProductCatalog.Select(p => p.Name).ToArray();

    private readonly CrmDbContext _db;
    private readonly UserManager<CrmUser> _users;
    private readonly SalesModuleSeeder _modules;
    private readonly BusinessModuleSeeder _business;
    private readonly BrandingOptions _branding;

    public DemoTenantSeeder(
        CrmDbContext db,
        UserManager<CrmUser> users,
        SalesModuleSeeder modules,
        BusinessModuleSeeder business,
        IOptions<BrandingOptions> branding)
    {
        _db = db;
        _users = users;
        _modules = modules;
        _business = business;
        _branding = branding.Value;
    }

    public async Task EnsureSeededAsync()
    {
        var result = await CreateOrRefreshAsync();
        if (!result.Ok)
            throw new InvalidOperationException(result.Message);
    }

    /// <summary>
    /// ساخت مشتری دمو (اگر نیست) یا تکمیل دادهٔ نمونه تا حداقل ~۵۵ رکورد در هر صفحه.
    /// از پنل ادمین فراخوانی می‌شود.
    /// </summary>
    public async Task<(bool Ok, string Message, int? TenantId)> CreateOrRefreshAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var existed = await _db.Tenants.AnyAsync(t => t.Slug == DemoSlug);
            var (tenant, admin) = await EnsureTenantAsync(now);
            await SeedAllContentAsync(tenant.Id, admin.Id, now);

            var message = existed
                ? $"مشتری دمو از قبل بود؛ دادهٔ نمونه تا حداقل {TargetCount} رکورد در هر صفحه تکمیل شد."
                : $"مشتری دمو ساخته شد. ورود: {DemoEmail} / {DemoPassword}";

            return (true, message, tenant.Id);
        }
        catch (Exception ex)
        {
            return (false, $"ساخت مشتری دمو ناموفق بود: {ex.Message}", null);
        }
    }

    public async Task<bool> DemoExistsAsync() =>
        await _db.Tenants.AnyAsync(t => t.Slug == DemoSlug);

    private async Task SeedAllContentAsync(int tenantId, int adminId, DateTime now)
    {
        await SeedDynamicModuleAsync(tenantId, adminId, "organizations", TargetCount, (i, t) =>
        {
            var name = UniqueCompanyName(i);
            return (name, new Dictionary<string, string?>
            {
                ["name"] = name,
                ["phone"] = $"021{40000000 + i}",
                ["website"] = $"https://demo{i + 1}.example.ir",
                ["industry"] = WeightedPick(i, ("tech", 28), ("trade", 22), ("services", 18), ("manufacturing", 14), ("construction", 8), ("health", 6), ("other", 4)),
                ["city"] = WeightedPick(i + 3, ("تهران", 35), ("اصفهان", 14), ("مشهد", 12), ("شیراز", 10), ("تبریز", 8), ("کرج", 7), ("اهواز", 5), ("رشت", 4), ("یزد", 3), ("همدان", 2)),
                ["address"] = $"خیابان نمونه، پلاک {i + 1}",
                ["description"] = $"سازمان نمونه شماره {i + 1} — سید در {t:yyyy-MM-dd}"
            });
        });

        var orgIds = await ModuleRecordIdsAsync(tenantId, "organizations");

        await SeedDynamicModuleAsync(tenantId, adminId, "leads", TargetCount, (i, t) =>
        {
            var name = UniquePersonName(i);
            return (name, new Dictionary<string, string?>
            {
                ["name"] = name,
                ["company"] = UniqueCompanyName(i + 17),
                ["phone"] = $"0912{2000000 + i:0000000}",
                ["email"] = $"lead{i + 1:000}@demo.local",
                ["city"] = WeightedPick(i + 5, ("تهران", 32), ("اصفهان", 15), ("مشهد", 13), ("شیراز", 11), ("تبریز", 9), ("کرج", 8), ("اهواز", 5), ("قم", 4), ("کرمان", 3)),
                ["status"] = WeightedPick(i, ("warm", 32), ("cold", 24), ("qualified", 18), ("hot", 16), ("junk", 10)),
                ["source"] = WeightedPick(i + 11, ("website", 34), ("referral", 22), ("ads", 16), ("social", 14), ("call", 8), ("exhibition", 6)),
                ["description"] = $"سرنخ نمونه — ایجاد نسبی به {t:yyyy-MM-dd}"
            });
        });

        await SeedDynamicModuleAsync(tenantId, adminId, "contacts", TargetCount, (i, t) =>
        {
            var name = UniquePersonName(i + 211);
            return (name, new Dictionary<string, string?>
            {
                ["name"] = name,
                ["organization"] = orgIds.Count > 0 ? orgIds[i % orgIds.Count].ToString() : null,
                ["position"] = WeightedPick(i, ("کارشناس فروش", 30), ("مدیر فروش", 22), ("خرید", 16), ("مالی", 14), ("IT", 10), ("CEO", 8)),
                ["mobile"] = $"0935{3000000 + i:0000000}",
                ["phone"] = $"021{50000000 + i}",
                ["email"] = $"contact{i + 1:000}@demo.local",
                ["address"] = WeightedPick(i + 2, ("تهران", 30), ("اصفهان", 16), ("مشهد", 14), ("شیراز", 12), ("تبریز", 10), ("کرج", 8), ("اهواز", 6), ("رشت", 4)),
                ["description"] = $"مخاطب نمونه — {t:yyyy-MM-dd}"
            });
        });

        var contactIds = await ModuleRecordIdsAsync(tenantId, "contacts");

        await SeedDynamicModuleAsync(tenantId, adminId, "opportunities", TargetCount, (i, t) =>
        {
            var title = $"فرصت فروش {UniqueCompanyName(i)}";
            var close = t.AddDays(7 + (i % 60));
            return (title, new Dictionary<string, string?>
            {
                ["name"] = title,
                ["contact"] = contactIds.Count > 0 ? contactIds[i % contactIds.Count].ToString() : null,
                ["organization"] = orgIds.Count > 0 ? orgIds[i % orgIds.Count].ToString() : null,
                ["amount"] = ((i % 20 + 1) * 1_500_000m).ToString("0"),
                ["probability"] = WeightedPick(i, ("70", 20), ("50", 18), ("40", 16), ("30", 14), ("80", 12), ("20", 10), ("90", 6), ("10", 4)),
                ["stage"] = WeightedPick(i, ("qualified", 26), ("proposal", 22), ("new", 18), ("negotiation", 16), ("won", 12), ("lost", 6)),
                ["expectedCloseDate"] = close.ToString("yyyy-MM-dd"),
                ["description"] = $"فرصت نمونه با تاریخ نسبی {close:yyyy-MM-dd}"
            });
        });

        await SeedDynamicModuleAsync(tenantId, adminId, "tasks", TargetCount, (i, t) =>
        {
            var due = t.AddDays(-45 + (i % 90)).AddHours(8 + (i % 9));
            var title = $"وظیفه پیگیری #{i + 1:00}";
            return (title, new Dictionary<string, string?>
            {
                ["name"] = title,
                ["dueDate"] = due.ToString("yyyy-MM-dd'T'HH:mm"),
                ["priority"] = WeightedPick(i, ("normal", 42), ("high", 28), ("low", 18), ("urgent", 12)),
                ["status"] = WeightedPick(i + 7, ("todo", 38), ("inprogress", 30), ("done", 24), ("canceled", 8)),
                ["description"] = $"وظیفه نمونه — سررسید نسبی {due:yyyy-MM-dd HH:mm}"
            });
        });

        await SeedDynamicModuleAsync(tenantId, adminId, "events", TargetCount, (i, t) =>
        {
            var start = t.AddDays(-45 + (i % 90)).AddHours(9 + (i % 7));
            var end = start.AddHours(1);
            var title = $"{WeightedPick(i, ("جلسه", 45), ("بازدید", 30), ("دمو", 25))} #{i + 1:00}";
            return (title, new Dictionary<string, string?>
            {
                ["name"] = title,
                ["startAt"] = start.ToString("yyyy-MM-dd'T'HH:mm"),
                ["endAt"] = end.ToString("yyyy-MM-dd'T'HH:mm"),
                ["location"] = Cities[i % Cities.Length],
                ["type"] = WeightedPick(i, ("meeting", 48), ("visit", 28), ("demo", 16), ("other", 8)),
                ["description"] = $"رویداد نمونه — شروع نسبی {start:yyyy-MM-dd HH:mm}"
            });
        });

        await SeedDynamicModuleAsync(tenantId, adminId, "calls", TargetCount, (i, t) =>
        {
            var callAt = t.AddDays(-20 + (i % 40)).AddHours(10 + (i % 7));
            var title = $"تماس با {UniquePersonName(i)}";
            return (title, new Dictionary<string, string?>
            {
                ["name"] = title,
                ["contact"] = contactIds.Count > 0 ? contactIds[i % contactIds.Count].ToString() : null,
                ["direction"] = WeightedPick(i, ("outgoing", 62), ("incoming", 38)),
                ["callAt"] = callAt.ToString("yyyy-MM-dd'T'HH:mm"),
                ["durationMinutes"] = (5 + i % 40).ToString(),
                ["result"] = WeightedPick(i + 3, ("answered", 48), ("followup", 22), ("noanswer", 18), ("busy", 12)),
                ["description"] = $"تماس نمونه — زمان نسبی {callAt:yyyy-MM-dd HH:mm}"
            });
        });

        await SeedProductsAsync(tenantId, adminId, now);
        await SeedDynamicProductsModuleAsync(tenantId, adminId, now);

        var productIds = await _db.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && !p.IsDeleted)
            .OrderBy(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync();

        await SeedPriceBooksAsync(tenantId, adminId, productIds, now);

        var opportunityIdsEarly = await ModuleRecordIdsAsync(tenantId, "opportunities");

        await SeedDynamicModuleAsync(tenantId, adminId, "vendors", TargetCount, (i, t) =>
        {
            var name = $"تأمین‌کننده {UniqueCompanyName(i + 50)}";
            return (name, new Dictionary<string, string?>
            {
                ["name"] = name,
                ["phone"] = $"021{60000000 + i}",
                ["email"] = $"vendor{i + 1:000}@demo.local",
                ["city"] = Cities[i % Cities.Length],
                ["description"] = $"تأمین‌کننده نمونه — {t:yyyy-MM-dd}"
            });
        });
        var vendorIds = await ModuleRecordIdsAsync(tenantId, "vendors");

        await SeedAppDocumentModulesAsync(
            tenantId, adminId, now, productIds, orgIds, contactIds, opportunityIdsEarly, vendorIds);

        await SeedSalesDocumentsAsync(tenantId, adminId, SalesDocumentKind.Quote, productIds, contactIds, orgIds, now);
        await SeedSalesDocumentsAsync(tenantId, adminId, SalesDocumentKind.Order, productIds, contactIds, orgIds, now);
        await SeedSalesDocumentsAsync(tenantId, adminId, SalesDocumentKind.Invoice, productIds, contactIds, orgIds, now);
        await SeedCommissionRulesAsync(tenantId, adminId, productIds, now);
        await SeedProjectsAsync(tenantId, adminId, contactIds, now);
        await SeedVendorsAsync(tenantId, adminId, now);
        await SeedPurchaseOrdersAsync(tenantId, adminId, productIds, now);
        await SeedCampaignsAsync(tenantId, adminId, now);
        var leadIds = await ModuleRecordIdsAsync(tenantId, "leads");
        var opportunityIds = opportunityIdsEarly;
        await SeedCampaignMembersAsync(tenantId, adminId, leadIds, contactIds, opportunityIds, now);
        await SeedCommissionEntriesAsync(tenantId, adminId, now);
        await SeedWorkflowsAsync(tenantId, adminId, now);
        await SeedReportsAsync(tenantId, adminId, now);
        await SeedWebFormsAsync(tenantId, adminId, now);
        await SeedSurveysAsync(tenantId, adminId, now);
        await SeedTemplatesAsync(tenantId, adminId, now);
        await SeedTicketsAsync(tenantId, adminId, contactIds, now);
        await SeedContractsAsync(tenantId, adminId, contactIds, now);
        await SeedWarrantiesAsync(tenantId, adminId, productIds, contactIds, now);
        await SeedKbAsync(tenantId, adminId, now);
        await SeedPortalUsersAsync(tenantId, adminId, contactIds, now);
        await SeedLeavesAsync(tenantId, adminId, now);
        await ReshapeDemoDistributionsAsync(tenantId, now);
        await NormalizeDemoUniqueNamesAsync(tenantId);
        await SeedPrintTemplatesAsync(tenantId, adminId);
        await SeedDashboardWidgetsAsync(tenantId, adminId, now);

        // در پایان دوباره سطر اسناد را تضمین کن (اگر سید قبلی وسط کار قطع شده باشد)
        var productIdsFinal = await _db.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && !p.IsDeleted)
            .OrderBy(p => p.Id)
            .Select(p => new ProductSeedRow(p.Id, p.Name, p.SalePrice, p.TaxPercent))
            .ToListAsync();
        if (productIdsFinal.Count > 0)
        {
            await EnsureLinesForDocumentsAsync(tenantId, adminId, now, "quotes", "quote_lines", "quote", productIdsFinal);
            await EnsureLinesForDocumentsAsync(tenantId, adminId, now, "sales_orders", "sales_order_lines", "sales_order", productIdsFinal);
            await EnsureLinesForDocumentsAsync(tenantId, adminId, now, "invoices", "invoice_lines", "invoice", productIdsFinal);
            await EnsureLinesForDocumentsAsync(tenantId, adminId, now, "purchase_orders", "purchase_order_lines", "purchase_order", productIdsFinal);
        }
    }

    /// <summary>
    /// برای دموی از قبل ساخته‌شده: وضعیت‌ها و تاریخ ساخت را نابرابر و واقعی‌تر می‌کند.
    /// </summary>
    private async Task ReshapeDemoDistributionsAsync(int tenantId, DateTime now)
    {
        await ReshapeModuleAsync(tenantId, "leads", now, (i, data) =>
        {
            data["status"] = WeightedPick(i, ("warm", 32), ("cold", 24), ("qualified", 18), ("hot", 16), ("junk", 10));
            data["source"] = WeightedPick(i + 11, ("website", 34), ("referral", 22), ("ads", 16), ("social", 14), ("call", 8), ("exhibition", 6));
            data["city"] = WeightedPick(i + 5, ("تهران", 32), ("اصفهان", 15), ("مشهد", 13), ("شیراز", 11), ("تبریز", 9), ("کرج", 8), ("اهواز", 5), ("قم", 4), ("کرمان", 3));
        });

        await ReshapeModuleAsync(tenantId, "organizations", now, (i, data) =>
        {
            data["industry"] = WeightedPick(i, ("tech", 28), ("trade", 22), ("services", 18), ("manufacturing", 14), ("construction", 8), ("health", 6), ("other", 4));
            data["city"] = WeightedPick(i + 3, ("تهران", 35), ("اصفهان", 14), ("مشهد", 12), ("شیراز", 10), ("تبریز", 8), ("کرج", 7), ("اهواز", 5), ("رشت", 4), ("یزد", 3), ("همدان", 2));
        });

        await ReshapeModuleAsync(tenantId, "opportunities", now, (i, data) =>
        {
            data["stage"] = WeightedPick(i, ("qualified", 26), ("proposal", 22), ("new", 18), ("negotiation", 16), ("won", 12), ("lost", 6));
        });

        await ReshapeModuleAsync(tenantId, "tasks", now, (i, data) =>
        {
            data["priority"] = WeightedPick(i, ("normal", 42), ("high", 28), ("low", 18), ("urgent", 12));
            data["status"] = WeightedPick(i + 7, ("todo", 38), ("inprogress", 30), ("done", 24), ("canceled", 8));
        });

        await ReshapeModuleAsync(tenantId, "events", now, (i, data) =>
        {
            data["type"] = WeightedPick(i, ("meeting", 48), ("visit", 28), ("demo", 16), ("other", 8));
        });

        await ReshapeModuleAsync(tenantId, "calls", now, (i, data) =>
        {
            data["direction"] = WeightedPick(i, ("outgoing", 62), ("incoming", 38));
            data["result"] = WeightedPick(i + 3, ("answered", 48), ("followup", 22), ("noanswer", 18), ("busy", 12));
        });
    }

    /// <summary>
    /// عنوان‌های تکراری دموی قبلی را با نام‌های یکتا بازنویسی می‌کند (leads/contacts/organizations/opportunities).
    /// </summary>
    private async Task NormalizeDemoUniqueNamesAsync(int tenantId)
    {
        await RewriteModuleNamesAsync(tenantId, "leads", (i, data, rec) =>
        {
            var name = UniquePersonName(i);
            data["name"] = name;
            data["company"] = UniqueCompanyName(i + 17);
            data["email"] = $"lead{i + 1:000}@demo.local";
            data["phone"] = $"0912{2000000 + i:0000000}";
            rec.Title = name;
        });

        await RewriteModuleNamesAsync(tenantId, "contacts", (i, data, rec) =>
        {
            var name = UniquePersonName(i + 211);
            data["name"] = name;
            data["email"] = $"contact{i + 1:000}@demo.local";
            data["mobile"] = $"0935{3000000 + i:0000000}";
            rec.Title = name;
        });

        await RewriteModuleNamesAsync(tenantId, "organizations", (i, data, rec) =>
        {
            var name = UniqueCompanyName(i);
            data["name"] = name;
            data["phone"] = $"021{40000000 + i}";
            rec.Title = name;
        });

        await RewriteModuleNamesAsync(tenantId, "opportunities", (i, data, rec) =>
        {
            var title = $"فرصت فروش {UniqueCompanyName(i)}";
            data["name"] = title;
            rec.Title = title;
        });
    }

    private async Task RewriteModuleNamesAsync(
        int tenantId,
        string moduleName,
        Action<int, Dictionary<string, string?>, DynamicRecord> mutate)
    {
        var moduleId = await _db.Modules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Name == moduleName)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();
        if (moduleId == 0)
            return;

        var records = await _db.Records.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.ModuleId == moduleId && !r.IsDeleted)
            .OrderBy(r => r.Id)
            .ToListAsync();

        for (var i = 0; i < records.Count; i++)
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, string?>>(records[i].CustomData)
                       ?? new Dictionary<string, string?>();
            mutate(i, data, records[i]);
            records[i].CustomData = JsonSerializer.Serialize(data);
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>دو قالب A4 برای پیش‌فاکتور و دو قالب A4 برای فاکتور.</summary>
    private async Task SeedPrintTemplatesAsync(int tenantId, int userId)
    {
        var quoteModuleId = await _db.Modules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Name == "quotes")
            .Select(m => m.Id)
            .FirstOrDefaultAsync();
        var invoiceModuleId = await _db.Modules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Name == "invoices")
            .Select(m => m.Id)
            .FirstOrDefaultAsync();

        if (quoteModuleId != 0)
        {
            await EnsurePrintTemplateAsync(tenantId, userId, quoteModuleId,
                "پیش‌فاکتور رسمی A4", isDefault: true, classic: true, docLabel: "پیش‌فاکتور");
            await EnsurePrintTemplateAsync(tenantId, userId, quoteModuleId,
                "پیش‌فاکتور ساده A4", isDefault: false, classic: false, docLabel: "پیش‌فاکتور");
        }

        if (invoiceModuleId != 0)
        {
            await EnsurePrintTemplateAsync(tenantId, userId, invoiceModuleId,
                "فاکتور رسمی A4", isDefault: true, classic: true, docLabel: "فاکتور فروش");
            await EnsurePrintTemplateAsync(tenantId, userId, invoiceModuleId,
                "فاکتور ساده A4", isDefault: false, classic: false, docLabel: "فاکتور فروش");
        }

        var pricebookModuleId = await _db.Modules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Name == "pricebooks")
            .Select(m => m.Id)
            .FirstOrDefaultAsync();
        if (pricebookModuleId != 0)
        {
            await EnsurePrintTemplateAsync(tenantId, userId, pricebookModuleId,
                "دفترچه قیمت A4", isDefault: true, classic: false, docLabel: "دفترچه قیمت",
                bodyOverride: BuildPriceBookPrintBody());
            await EnsurePrintTemplateAsync(tenantId, userId, pricebookModuleId,
                "دفترچه قیمت رسمی A4", isDefault: false, classic: true, docLabel: "دفترچه قیمت",
                bodyOverride: BuildPriceBookPrintBodyClassic());
        }
    }

    private async Task EnsurePrintTemplateAsync(
        int tenantId,
        int userId,
        int moduleId,
        string name,
        bool isDefault,
        bool classic,
        string docLabel,
        string? bodyOverride = null)
    {
        var exists = await _db.PrintTemplates.IgnoreQueryFilters()
            .AnyAsync(t => t.TenantId == tenantId && t.ModuleId == moduleId && t.Name == name && !t.IsDeleted);
        if (exists)
            return;

        if (isDefault)
        {
            var others = await _db.PrintTemplates.IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId && t.ModuleId == moduleId && t.IsDefault && !t.IsDeleted)
                .ToListAsync();
            foreach (var o in others)
                o.IsDefault = false;
        }

        var (header, body, footer) = classic
            ? BuildClassicA4PrintHtml(docLabel)
            : BuildSimpleA4PrintHtml(docLabel);
        if (bodyOverride is not null)
            body = bodyOverride;

        _db.PrintTemplates.Add(new PrintTemplate
        {
            TenantId = tenantId,
            ModuleId = moduleId,
            Name = name,
            IsHtmlEditor = true,
            IsActive = true,
            IsDefault = isDefault,
            ServiceProvider = "browser",
            PageSize = "A4",
            Landscape = false,
            TextDirection = "rtl",
            FontFamily = classic ? "shabnam" : "vazir",
            FontSize = classic ? 11 : 12,
            MarginTop = 12,
            MarginRight = 12,
            MarginBottom = 14,
            MarginLeft = 12,
            RepeatHeaderEachPage = false,
            ShowPageNumbers = true,
            AllowPdf = true,
            AllowWord = true,
            ShareWithAllRoles = true,
            FileNamePattern = "{$FN.docTitle}-{$FN.docNumber}",
            HeaderHtml = header,
            BodyHtml = body,
            FooterHtml = footer,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }

    private static string BuildPriceBookPrintBody()
    {
        const string th = "padding:8px;background:#0f766e;color:#fff;font-size:10pt;text-align:center";
        const string td = "padding:8px;font-size:10pt;border-bottom:1px solid #e5e7eb";
        return
            "<p style=\"font-size:11pt;margin:0 0 12px\">نام دفترچه: <strong>{$RECORD.name}</strong></p>" +
            "<p style=\"font-size:10pt;color:#64748b;margin:0 0 12px\">{$RECORD.description}</p>" +
            "<table style=\"width:100%;border-collapse:collapse\" cellspacing=\"0\">" +
            $"<thead><tr><td style=\"{th};width:8%\">#</td><td style=\"{th}\">محصول</td>" +
            $"<td style=\"{th}\">قیمت</td></tr></thead>" +
            "<tbody>" +
            $"<tr data-repeat=\"LINEITEMS\"><td style=\"{td}\">{{$INDEX}}</td><td style=\"{td}\">{{$ITEM.title}}</td>" +
            $"<td style=\"{td}\">{{$ITEM.unitPrice|number}}</td></tr>" +
            "</tbody></table>";
    }

    private static string BuildPriceBookPrintBodyClassic()
    {
        const string th = "padding:6px;color:#fff;font-size:10pt;text-align:center";
        const string td = "padding:6px;font-size:10pt";
        const string table = "border-collapse:collapse;width:100%;border:1px solid #9ca3af";
        return
            "<table style=\"border:none;width:100%;margin-top:8px\" cellspacing=\"0\" cellpadding=\"2\"><tbody>" +
            "<tr><td style=\"border:none\">دفترچه: <strong>{$RECORD.name}</strong></td>" +
            "<td style=\"border:none;text-align:left\">تاریخ: <strong>{$FN.today}</strong></td></tr>" +
            "<tr><td style=\"border:none\" colspan=\"2\">{$RECORD.description}</td></tr>" +
            "</tbody></table>" +
            "<div style=\"height:12px\"></div>" +
            $"<table style=\"{table}\" border=\"1\" cellspacing=\"0\" width=\"100%\"><tbody>" +
            $"<tr style=\"background-color:#0f766e\"><td style=\"{th};width:8%\">ردیف</td>" +
            $"<td style=\"{th}\">شرح / محصول</td><td style=\"{th}\">قیمت</td></tr>" +
            $"<tr data-repeat=\"LINEITEMS\"><td style=\"{td}\"><strong>{{$INDEX}}</strong></td>" +
            $"<td style=\"{td}\">{{$ITEM.title}}</td><td style=\"{td}\">{{$ITEM.unitPrice|number}}</td></tr>" +
            "</tbody></table>";
    }

    private static (string Header, string Body, string Footer) BuildClassicA4PrintHtml(string docLabel)
    {
        const string th = "padding:6px;color:#fff;font-size:10pt;text-align:center";
        const string td = "padding:6px;font-size:10pt";
        const string table = "border-collapse:collapse;width:100%;border:1px solid #9ca3af";

        var header =
            "<table style=\"border:none;width:100%\" cellspacing=\"0\" cellpadding=\"6\"><tbody><tr>" +
            "<td style=\"border:none;width:24%;background-color:#f3f4f6\">{$COMPANY.logo}</td>" +
            "<td style=\"border:none;width:26%;background-color:#f3f4f6;color:#6b7280;font-size:10pt\">{$COMPANY.website}</td>" +
            "<td style=\"border:none;text-align:center\"><span style=\"font-size:18pt\"><strong>" + docLabel + "</strong></span></td>" +
            "<td style=\"border:none;width:12%;background-color:#f3f4f6\">&nbsp;</td>" +
            "</tr></tbody></table>";

        var body =
            "<table style=\"border:none;width:100%;margin-top:14px\" cellspacing=\"0\" cellpadding=\"2\"><tbody>" +
            "<tr><td style=\"border:none\">خریدار : <strong>{$RECORD.organization}</strong></td>" +
            "<td style=\"border:none;text-align:left\">تاریخ : <strong>{$FN.today}</strong></td></tr>" +
            "<tr><td style=\"border:none\">فروشنده : <strong>{$COMPANY.name}</strong></td>" +
            "<td style=\"border:none;text-align:left\">شماره : <strong>{$FN.docNumber}</strong></td></tr>" +
            "</tbody></table>" +
            "<div style=\"height:14px\"></div>" +
            $"<table style=\"{table}\" border=\"1\" cellspacing=\"0\" width=\"100%\"><tbody>" +
            $"<tr style=\"background-color:#6b7280\"><td style=\"{th};width:6%\">ردیف</td>" +
            $"<td style=\"{th}\">شرح</td><td style=\"{th}\">تعداد</td><td style=\"{th}\">قیمت واحد</td>" +
            $"<td style=\"{th}\">جمع سطر</td></tr>" +
            $"<tr data-repeat=\"LINEITEMS\"><td style=\"{td}\"><strong>{{$INDEX}}</strong></td>" +
            $"<td style=\"{td}\">{{$ITEM.title}}</td><td style=\"{td}\">{{$ITEM.quantity|number}}</td>" +
            $"<td style=\"{td}\">{{$ITEM.unitPrice|number}}</td><td style=\"{td}\">{{$ITEM.lineTotal|number}}</td></tr>" +
            "</tbody></table>" +
            $"<table style=\"{table};width:52%;margin-inline-end:auto;margin-top:6px\" border=\"1\" cellspacing=\"0\"><tbody>" +
            $"<tr><td style=\"{td}\">جمع جزء</td><td style=\"{td};text-align:left\">{{$TOTALS.subTotal|number}}</td></tr>" +
            $"<tr><td style=\"{td}\">مالیات</td><td style=\"{td};text-align:left\">{{$TOTALS.taxTotal|number}}</td></tr>" +
            $"<tr style=\"background-color:#e5e7eb\"><td style=\"{td}\"><strong>جمع کل</strong></td>" +
            $"<td style=\"{td};text-align:left\"><strong>{{$TOTALS.grandTotal|number}}</strong></td></tr>" +
            "</tbody></table>" +
            "<table style=\"border:none;width:100%;margin-top:34px\" cellspacing=\"0\" cellpadding=\"6\"><tbody><tr>" +
            "<td style=\"border:1px solid #d1d5db;height:70px;text-align:center\"><strong>امضا فروشنده</strong></td>" +
            "<td style=\"border:none;width:25%\">&nbsp;</td>" +
            "<td style=\"border:1px solid #d1d5db;height:70px;text-align:center\"><strong>امضا خریدار</strong></td>" +
            "</tr></tbody></table>";

        var footer =
            "<div style=\"text-align:center;font-size:8pt;color:#9ca3af\">{$COMPANY.name} — {$COMPANY.website}</div>";

        return (header, body, footer);
    }

    private static (string Header, string Body, string Footer) BuildSimpleA4PrintHtml(string docLabel)
    {
        const string th = "padding:8px;background:#2563eb;color:#fff;font-size:10pt;text-align:center";
        const string td = "padding:8px;font-size:10pt;border-bottom:1px solid #e5e7eb";

        var header =
            "<div style=\"border-bottom:3px solid #2563eb;padding-bottom:10px;margin-bottom:12px\">" +
            "<div style=\"font-size:20pt;font-weight:700;color:#1e3a8a\">" + docLabel + "</div>" +
            "<div style=\"color:#64748b;font-size:10pt;margin-top:4px\">{$COMPANY.name} · {$FN.today} · {$FN.docNumber}</div>" +
            "</div>";

        var body =
            "<p style=\"font-size:11pt;margin:0 0 12px\">خریدار: <strong>{$RECORD.organization}</strong></p>" +
            "<table style=\"width:100%;border-collapse:collapse\" cellspacing=\"0\">" +
            $"<thead><tr><td style=\"{th};width:8%\">#</td><td style=\"{th}\">محصول / شرح</td>" +
            $"<td style=\"{th}\">تعداد</td><td style=\"{th}\">قیمت</td><td style=\"{th}\">جمع</td></tr></thead>" +
            "<tbody>" +
            $"<tr data-repeat=\"LINEITEMS\"><td style=\"{td}\">{{$INDEX}}</td><td style=\"{td}\">{{$ITEM.title}}</td>" +
            $"<td style=\"{td}\">{{$ITEM.quantity|number}}</td><td style=\"{td}\">{{$ITEM.unitPrice|number}}</td>" +
            $"<td style=\"{td}\">{{$ITEM.lineTotal|number}}</td></tr>" +
            "</tbody></table>" +
            "<div style=\"margin-top:16px;text-align:left;font-size:12pt\">" +
            "<div>جمع جزء: {$TOTALS.subTotal|number}</div>" +
            "<div>مالیات: {$TOTALS.taxTotal|number}</div>" +
            "<div style=\"font-weight:700;color:#1e3a8a;margin-top:4px\">جمع کل: {$TOTALS.grandTotal|number}</div>" +
            "</div>" +
            "<p style=\"margin-top:18px;font-size:10pt;color:#64748b\"><strong>توضیحات:</strong> {$RECORD.description}</p>";

        var footer =
            "<div style=\"text-align:center;font-size:8pt;color:#94a3b8;border-top:1px solid #e2e8f0;padding-top:6px\">" +
            "با تشکر از انتخاب شما — {$COMPANY.name}</div>";

        return (header, body, footer);
    }

    private async Task ReshapeModuleAsync(
        int tenantId,
        string moduleName,
        DateTime now,
        Action<int, Dictionary<string, string?>> mutate)
    {
        var moduleId = await _db.Modules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Name == moduleName)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();
        if (moduleId == 0)
            return;

        var records = await _db.Records.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.ModuleId == moduleId && !r.IsDeleted)
            .OrderBy(r => r.Id)
            .ToListAsync();

        for (var i = 0; i < records.Count; i++)
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, string?>>(records[i].CustomData)
                       ?? new Dictionary<string, string?>();
            mutate(i, data);
            records[i].CustomData = JsonSerializer.Serialize(data);
            records[i].CreatedAtUtc = GrowthCreatedAt(now, i, records.Count);
            records[i].UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedDashboardWidgetsAsync(int tenantId, int userId, DateTime now)
    {
        var old = await _db.DashboardWidgets.IgnoreQueryFilters()
            .Where(w => w.TenantId == tenantId && w.UserId == userId)
            .ToListAsync();
        if (old.Count > 0)
        {
            _db.DashboardWidgets.RemoveRange(old);
            await _db.SaveChangesAsync();
        }

        var modules = await _db.Modules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && !m.IsDeleted)
            .Select(m => new { m.Id, m.Name, m.PluralLabel })
            .ToListAsync();

        int? IdOf(string name) => modules.FirstOrDefault(m => m.Name == name)?.Id;
        string LabelOf(string name) => modules.FirstOrDefault(m => m.Name == name)?.PluralLabel ?? name;

        var specs = new List<(string Type, string Module, string? Field, string Title)>
        {
            ("counter", "leads", null, $"تعداد {LabelOf("leads")}"),
            ("counter", "contacts", null, $"تعداد {LabelOf("contacts")}"),
            ("counter", "opportunities", null, "فرصت‌های فروش"),
            ("pie", "leads", "status", "قیف وضعیت سرنخ‌ها"),
            ("funnel", "opportunities", "stage", "قیف فروش"),
            ("bar", "tasks", "status", "وضعیت وظایف"),
            ("monthly", "leads", null, "روند جذب سرنخ"),
            ("monthly", "opportunities", null, "روند ثبت فرصت")
        };

        var order = 0;
        foreach (var (type, moduleName, field, title) in specs)
        {
            var moduleId = IdOf(moduleName);
            if (moduleId is null)
                continue;

            _db.DashboardWidgets.Add(new DashboardWidget
            {
                TenantId = tenantId,
                UserId = userId,
                Type = type,
                Title = title,
                ModuleId = moduleId.Value,
                FieldName = field,
                SortOrder = ++order,
                CreatedAtUtc = now,
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task<(Tenant Tenant, CrmUser Admin)> EnsureTenantAsync(DateTime now)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == DemoSlug);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Name = $"شرکت نمونه {_branding.DisplayName}",
                Slug = DemoSlug,
                Status = TenantStatus.Active,
                CreatedAtUtc = now,
                TrialEndsAtUtc = now.AddYears(10)
            };
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync();

            var ceo = new Role { TenantId = tenant.Id, Name = "مدیر عامل", IsAdmin = true };
            _db.CrmRoles.Add(ceo);
            await _db.SaveChangesAsync();

            var sales = new Role { TenantId = tenant.Id, Name = "کارشناس فروش", ParentRoleId = ceo.Id };
            var adminProfile = new Profile { TenantId = tenant.Id, Name = "مدیر سیستم", IsAdmin = true };
            var userProfile = new Profile { TenantId = tenant.Id, Name = "کاربر استاندارد" };
            _db.CrmRoles.Add(sales);
            _db.Profiles.AddRange(adminProfile, userProfile);
            await _db.SaveChangesAsync();

            var existingUser = await _users.FindByEmailAsync(DemoEmail);
            if (existingUser is not null)
            {
                await _users.DeleteAsync(existingUser);
            }

            var user = new CrmUser
            {
                UserName = DemoEmail,
                Email = DemoEmail,
                FullName = "مدیر نمونه",
                TenantId = tenant.Id,
                CrmRoleId = ceo.Id,
                ProfileId = adminProfile.Id,
                IsTenantAdmin = true,
                CreatedAtUtc = now
            };
            var result = await _users.CreateAsync(user, DemoPassword);
            if (!result.Succeeded)
                throw new InvalidOperationException("ساخت کاربر دمو ناموفق: " + string.Join(", ", result.Errors.Select(e => e.Description)));

            await _modules.SeedAsync(tenant.Id, adminProfile.Id, userProfile.Id);
            await _business.SeedAsync(tenant.Id, adminProfile.Id, userProfile.Id);
            await _business.EnsureDemoExtrasAsync(tenant.Id);
            return (tenant, user);
        }

        await _modules.EnsureSeededAsync(tenant.Id);
        await _business.EnsureSeededAsync(tenant.Id);

        var admin = await _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.IsTenantAdmin)
            ?? await _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id);

        if (admin is null)
        {
            var adminProfileId = await _db.Profiles
                .Where(p => p.TenantId == tenant.Id && p.IsAdmin)
                .Select(p => p.Id)
                .FirstAsync();
            var roleId = await _db.CrmRoles
                .Where(r => r.TenantId == tenant.Id)
                .OrderBy(r => r.Id)
                .Select(r => r.Id)
                .FirstAsync();

            var orphan = await _users.FindByEmailAsync(DemoEmail);
            if (orphan is not null)
                await _users.DeleteAsync(orphan);

            admin = new CrmUser
            {
                UserName = DemoEmail,
                Email = DemoEmail,
                FullName = "مدیر نمونه",
                TenantId = tenant.Id,
                CrmRoleId = roleId,
                ProfileId = adminProfileId,
                IsTenantAdmin = true,
                CreatedAtUtc = now
            };
            var recreate = await _users.CreateAsync(admin, DemoPassword);
            if (!recreate.Succeeded)
                throw new InvalidOperationException("بازسازی کاربر دمو ناموفق: " + string.Join(", ", recreate.Errors.Select(e => e.Description)));
        }

        if (tenant.Status != TenantStatus.Active)
        {
            tenant.Status = TenantStatus.Active;
            tenant.TrialEndsAtUtc = now.AddYears(10);
            await _db.SaveChangesAsync();
        }

        return (tenant, admin);
    }

    private async Task SeedDynamicModuleAsync(
        int tenantId,
        int ownerUserId,
        string moduleName,
        int target,
        Func<int, DateTime, (string Title, Dictionary<string, string?> Data)> factory)
    {
        var module = await _db.Modules.IgnoreQueryFilters()
            .FirstAsync(m => m.TenantId == tenantId && m.Name == moduleName);

        var existing = await _db.Records.IgnoreQueryFilters()
            .CountAsync(r => r.TenantId == tenantId && r.ModuleId == module.Id && !r.IsDeleted);
        if (existing >= target)
            return;

        var now = DateTime.UtcNow;
        var toAdd = target - existing;
        for (var n = 0; n < toAdd; n++)
        {
            var i = existing + n;
            var created = GrowthCreatedAt(now, i, target);
            var (title, data) = factory(i, now);
            _db.Records.Add(new DynamicRecord
            {
                TenantId = tenantId,
                ModuleId = module.Id,
                Title = title,
                OwnerUserId = ownerUserId,
                CreatedByUserId = ownerUserId,
                CreatedAtUtc = created,
                CustomData = JsonSerializer.Serialize(data)
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task<List<int>> ModuleRecordIdsAsync(int tenantId, string moduleName)
    {
        var moduleId = await _db.Modules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Name == moduleName)
            .Select(m => m.Id)
            .FirstAsync();

        return await _db.Records.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.ModuleId == moduleId && !r.IsDeleted)
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .ToListAsync();
    }

    private async Task SeedProductsAsync(int tenantId, int userId, DateTime now)
    {
        var existing = await _db.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && !p.IsDeleted)
            .OrderBy(p => p.Id)
            .ToListAsync();

        // بازنویسی محصولات قدیمی/نرم‌افزاری با کالای فیزیکی واقعی
        for (var i = 0; i < existing.Count; i++)
        {
            var spec = ProductCatalog[i % ProductCatalog.Length];
            var name = i < ProductCatalog.Length ? spec.Name : $"{spec.Name} ({i + 1})";
            existing[i].Name = name;
            existing[i].Sku = $"{spec.Sku}-{i + 1:000}";
            existing[i].Unit = spec.Unit;
            existing[i].SalePrice = spec.Price;
            existing[i].TaxPercent = 9;
            existing[i].IsService = false;
            existing[i].TrackInventory = true;
            existing[i].StockQty = 20 + (i % 80);
            existing[i].ReorderPoint = 5;
            existing[i].IsActive = true;
            existing[i].Description = $"کالای فیزیکی نمونه — {spec.Name}";
            existing[i].UpdatedAtUtc = now;
        }

        for (var i = existing.Count; i < TargetCount; i++)
        {
            var spec = ProductCatalog[i % ProductCatalog.Length];
            var name = i < ProductCatalog.Length ? spec.Name : $"{spec.Name} ({i + 1})";
            _db.Products.Add(new Product
            {
                TenantId = tenantId,
                Name = name,
                Sku = $"{spec.Sku}-{i + 1:000}",
                Unit = spec.Unit,
                SalePrice = spec.Price,
                TaxPercent = 9,
                IsService = false,
                TrackInventory = true,
                StockQty = 20 + (i % 80),
                ReorderPoint = 5,
                IsActive = true,
                Description = $"کالای فیزیکی نمونه — {spec.Name}",
                CreatedAtUtc = now.AddDays(-(i % 60)),
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedPriceBooksAsync(int tenantId, int userId, List<int> productIds, DateTime now)
    {
        if (productIds.Count == 0)
            return;

        await _business.EnsurePriceBooksStructureAsync(tenantId);

        var module = await _db.Modules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == "pricebooks");
        var lineModule = await _db.Modules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == "pricebook_entries");
        if (module is null || lineModule is null)
            return;

        var specs = new (string Name, decimal Factor, string Description)[]
        {
            ("قیمت مصرف‌کننده", 1.00m, "قیمت پایه فروش به مشتری نهایی"),
            ("قیمت همکار", 0.90m, "۱۰٪ تخفیف برای همکاران و نمایندگان"),
            ("قیمت عمده", 0.80m, "۲۰٪ تخفیف برای خرید عمده"),
            ("قیمت نمایشگاهی", 0.85m, "قیمت ویژه کمپین و نمایشگاه"),
            ("قیمت VIP", 1.10m, "لیست ویژه مشتریان سازمانی با خدمات اضافه")
        };

        var existing = await _db.Records.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.ModuleId == module.Id && !r.IsDeleted)
            .OrderBy(r => r.Id)
            .ToListAsync();

        for (var i = 0; i < specs.Length; i++)
        {
            var data = new Dictionary<string, string?>
            {
                ["name"] = specs[i].Name,
                ["currency"] = "IRR",
                ["isActive"] = "true",
                ["description"] = specs[i].Description
            };

            if (i < existing.Count)
            {
                existing[i].Title = specs[i].Name;
                existing[i].CustomData = JsonSerializer.Serialize(data);
                existing[i].UpdatedAtUtc = now;
            }
            else
            {
                _db.Records.Add(new DynamicRecord
                {
                    TenantId = tenantId,
                    ModuleId = module.Id,
                    Title = specs[i].Name,
                    OwnerUserId = userId,
                    CreatedByUserId = userId,
                    CreatedAtUtc = now.AddDays(-i),
                    CustomData = JsonSerializer.Serialize(data)
                });
            }
        }

        await _db.SaveChangesAsync();

        var bookIds = await _db.Records.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.ModuleId == module.Id && !r.IsDeleted)
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .Take(specs.Length)
            .ToListAsync();

        var products = await _db.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && !p.IsDeleted && productIds.Contains(p.Id))
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Name, p.SalePrice, p.TaxPercent })
            .Take(Math.Min(40, TargetCount))
            .ToListAsync();

        var lineJsons = await _db.Records.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ModuleId == lineModule.Id && !r.IsDeleted)
            .Select(r => r.CustomData)
            .ToListAsync();
        var hasLine = new HashSet<(int BookId, int ProductId)>();
        foreach (var json in lineJsons)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("priceBook", out var pbEl) || !root.TryGetProperty("product", out var prEl))
                    continue;
                var pbRaw = pbEl.ValueKind == JsonValueKind.Number ? pbEl.GetRawText() : pbEl.GetString();
                var prRaw = prEl.ValueKind == JsonValueKind.Number ? prEl.GetRawText() : prEl.GetString();
                if (int.TryParse(pbRaw, out var bid) && int.TryParse(prRaw, out var pid))
                    hasLine.Add((bid, pid));
            }
            catch
            {
                // ignore
            }
        }

        for (var bi = 0; bi < bookIds.Count && bi < specs.Length; bi++)
        {
            var bookId = bookIds[bi];
            var factor = specs[bi].Factor;
            var sort = 0;
            foreach (var p in products)
            {
                if (hasLine.Contains((bookId, p.Id)))
                    continue;

                var price = Math.Round(p.SalePrice * factor, MidpointRounding.AwayFromZero);
                var data = new Dictionary<string, string?>
                {
                    ["title"] = p.Name,
                    ["product"] = p.Id.ToString(),
                    ["quantity"] = "1",
                    ["unitPrice"] = price.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                    ["discountPercent"] = "0",
                    ["taxPercent"] = "0",
                    ["lineTotal"] = price.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                    ["sortOrder"] = (++sort).ToString(),
                    ["priceBook"] = bookId.ToString()
                };
                _db.Records.Add(new DynamicRecord
                {
                    TenantId = tenantId,
                    ModuleId = lineModule.Id,
                    Title = p.Name,
                    OwnerUserId = userId,
                    CreatedByUserId = userId,
                    CreatedAtUtc = now,
                    CustomData = JsonSerializer.Serialize(data)
                });
                hasLine.Add((bookId, p.Id));
            }
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedDynamicProductsModuleAsync(int tenantId, int userId, DateTime now)
    {
        var module = await _db.Modules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == "products");
        if (module is null)
            return;

        var existing = await _db.Records.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.ModuleId == module.Id && !r.IsDeleted)
            .OrderBy(r => r.Id)
            .ToListAsync();

        for (var i = 0; i < existing.Count; i++)
        {
            var spec = ProductCatalog[i % ProductCatalog.Length];
            var name = i < ProductCatalog.Length ? spec.Name : $"{spec.Name} ({i + 1})";
            var data = new Dictionary<string, string?>
            {
                ["name"] = name,
                ["sku"] = $"{spec.Sku}-{i + 1:000}",
                ["unit"] = spec.Unit,
                ["salePrice"] = spec.Price.ToString("0"),
                ["isService"] = "false",
                ["stockQty"] = (20 + i % 80).ToString(),
                ["description"] = $"کالای فیزیکی نمونه — {spec.Name}"
            };
            existing[i].Title = name;
            existing[i].CustomData = JsonSerializer.Serialize(data);
            existing[i].UpdatedAtUtc = now;
        }

        for (var i = existing.Count; i < TargetCount; i++)
        {
            var spec = ProductCatalog[i % ProductCatalog.Length];
            var name = i < ProductCatalog.Length ? spec.Name : $"{spec.Name} ({i + 1})";
            var data = new Dictionary<string, string?>
            {
                ["name"] = name,
                ["sku"] = $"{spec.Sku}-{i + 1:000}",
                ["unit"] = spec.Unit,
                ["salePrice"] = spec.Price.ToString("0"),
                ["isService"] = "false",
                ["stockQty"] = (20 + i % 80).ToString(),
                ["description"] = $"کالای فیزیکی نمونه — {spec.Name}"
            };
            _db.Records.Add(new DynamicRecord
            {
                TenantId = tenantId,
                ModuleId = module.Id,
                Title = name,
                OwnerUserId = userId,
                CreatedByUserId = userId,
                CreatedAtUtc = now.AddDays(-(i % 60)),
                CustomData = JsonSerializer.Serialize(data)
            });
        }

        await _db.SaveChangesAsync();
    }

    private sealed record ProductSeedRow(int Id, string Name, decimal SalePrice, decimal TaxPercent);

    /// <summary>
    /// سید ماژول‌های سند App (پیش‌فاکتور/سفارش/فاکتور/خرید) به‌همراه سطرهای محصول.
    /// </summary>
    private async Task SeedAppDocumentModulesAsync(
        int tenantId,
        int userId,
        DateTime now,
        IReadOnlyList<int> productIds,
        IReadOnlyList<int> orgIds,
        IReadOnlyList<int> contactIds,
        IReadOnlyList<int> opportunityIds,
        IReadOnlyList<int> vendorIds)
    {
        var productRows = await _db.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && !p.IsDeleted && productIds.Contains(p.Id))
            .OrderBy(p => p.Id)
            .Select(p => new ProductSeedRow(p.Id, p.Name, p.SalePrice, p.TaxPercent))
            .ToListAsync();
        if (productRows.Count == 0)
            return;

        await SeedDocumentParentsAsync(tenantId, userId, now, "quotes", "پیش‌فاکتور", "Q-",
            orgIds, contactIds, opportunityIds, vendorIds: null, isPurchase: false);
        await SeedDocumentParentsAsync(tenantId, userId, now, "sales_orders", "سفارش فروش", "SO-",
            orgIds, contactIds, opportunityIds, vendorIds: null, isPurchase: false);
        await SeedDocumentParentsAsync(tenantId, userId, now, "invoices", "فاکتور", "INV-",
            orgIds, contactIds, opportunityIds, vendorIds: null, isPurchase: false);
        await SeedDocumentParentsAsync(tenantId, userId, now, "purchase_orders", "سفارش خرید", "PO-",
            orgIds, contactIds, opportunityIds, vendorIds, isPurchase: true);

        await EnsureLinesForDocumentsAsync(tenantId, userId, now, "quotes", "quote_lines", "quote", productRows);
        await EnsureLinesForDocumentsAsync(tenantId, userId, now, "sales_orders", "sales_order_lines", "sales_order", productRows);
        await EnsureLinesForDocumentsAsync(tenantId, userId, now, "invoices", "invoice_lines", "invoice", productRows);
        await EnsureLinesForDocumentsAsync(tenantId, userId, now, "purchase_orders", "purchase_order_lines", "purchase_order", productRows);
    }

    private async Task SeedDocumentParentsAsync(
        int tenantId,
        int userId,
        DateTime now,
        string moduleName,
        string titlePrefix,
        string numberPrefix,
        IReadOnlyList<int> orgIds,
        IReadOnlyList<int> contactIds,
        IReadOnlyList<int> opportunityIds,
        IReadOnlyList<int>? vendorIds,
        bool isPurchase)
    {
        var module = await _db.Modules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == moduleName);
        if (module is null)
            return;

        var existingParents = await _db.Records.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.ModuleId == module.Id && !r.IsDeleted)
            .OrderBy(r => r.Id)
            .ToListAsync();

        for (var i = 0; i < existingParents.Count; i++)
        {
            var issue = now.AddDays(-(i % 75));
            var number = $"{numberPrefix}{1001 + i}";
            var title = $"{titlePrefix} {number}";
            var status = isPurchase
                ? WeightedPick(i, ("Draft", 30), ("Ordered", 35), ("Received", 25), ("Canceled", 10))
                : moduleName == "invoices"
                    ? WeightedPick(i, ("Draft", 20), ("Confirmed", 28), ("PartiallyPaid", 18), ("Paid", 24), ("Canceled", 10))
                    : WeightedPick(i, ("Draft", 28), ("Confirmed", 36), ("Converted", 24), ("Canceled", 12));

            var data = DynamicRecordService.ParseData(existingParents[i]);
            data["name"] = title;
            data["number"] = number;
            data["issueDate"] = issue.ToString("yyyy-MM-dd");
            data["status"] = status;
            data["printTitle"] = titlePrefix;
            data["description"] = $"{titlePrefix} نمونه با اقلام کالا — {issue:yyyy-MM-dd}";
            if (!data.ContainsKey("discountPercent") || string.IsNullOrWhiteSpace(data["discountPercent"]))
                data["discountPercent"] = (i % 5 == 0 ? 5 : 0).ToString();

            if (isPurchase)
            {
                if (vendorIds is { Count: > 0 })
                    data["vendor"] = vendorIds[i % vendorIds.Count].ToString();
                data["orderDate"] = issue.ToString("yyyy-MM-dd");
            }
            else
            {
                if (orgIds.Count > 0)
                    data["organization"] = orgIds[i % orgIds.Count].ToString();
                if (contactIds.Count > 0)
                    data["contact"] = contactIds[i % contactIds.Count].ToString();
                if (opportunityIds.Count > 0 && i % 3 != 0)
                    data["opportunity"] = opportunityIds[i % opportunityIds.Count].ToString();
                if (moduleName == "quotes")
                    data["validUntil"] = issue.AddDays(14).ToString("yyyy-MM-dd");
                if (moduleName == "invoices")
                    data["dueDate"] = issue.AddDays(30).ToString("yyyy-MM-dd");
            }

            existingParents[i].Title = title;
            existingParents[i].CustomData = JsonSerializer.Serialize(data);
            existingParents[i].UpdatedAtUtc = now;
        }

        if (existingParents.Count > 0)
            await _db.SaveChangesAsync();

        await SeedDynamicModuleAsync(tenantId, userId, moduleName, TargetCount, (i, t) =>
        {
            var issue = t.AddDays(-(i % 75));
            var number = $"{numberPrefix}{1001 + i}";
            var title = $"{titlePrefix} {number}";
            var status = isPurchase
                ? WeightedPick(i, ("Draft", 30), ("Ordered", 35), ("Received", 25), ("Canceled", 10))
                : moduleName == "invoices"
                    ? WeightedPick(i, ("Draft", 20), ("Confirmed", 28), ("PartiallyPaid", 18), ("Paid", 24), ("Canceled", 10))
                    : WeightedPick(i, ("Draft", 28), ("Confirmed", 36), ("Converted", 24), ("Canceled", 12));

            var data = new Dictionary<string, string?>
            {
                ["name"] = title,
                ["number"] = number,
                ["issueDate"] = issue.ToString("yyyy-MM-dd"),
                ["status"] = status,
                ["printTitle"] = titlePrefix,
                ["description"] = $"{titlePrefix} نمونه با اقلام کالا — {issue:yyyy-MM-dd}",
                ["discountPercent"] = (i % 5 == 0 ? 5 : 0).ToString()
            };

            if (isPurchase)
            {
                if (vendorIds is { Count: > 0 })
                    data["vendor"] = vendorIds[i % vendorIds.Count].ToString();
                data["orderDate"] = issue.ToString("yyyy-MM-dd");
            }
            else
            {
                if (orgIds.Count > 0)
                    data["organization"] = orgIds[i % orgIds.Count].ToString();
                if (contactIds.Count > 0)
                    data["contact"] = contactIds[i % contactIds.Count].ToString();
                if (opportunityIds.Count > 0 && i % 3 != 0)
                    data["opportunity"] = opportunityIds[i % opportunityIds.Count].ToString();
                if (moduleName == "quotes")
                    data["validUntil"] = issue.AddDays(14).ToString("yyyy-MM-dd");
                if (moduleName == "invoices")
                    data["dueDate"] = issue.AddDays(30).ToString("yyyy-MM-dd");
            }

            return (title, data);
        });
    }

    private async Task EnsureLinesForDocumentsAsync(
        int tenantId,
        int userId,
        DateTime now,
        string parentModuleName,
        string lineModuleName,
        string linkField,
        IReadOnlyList<ProductSeedRow> productRows)
    {
        var parentModule = await _db.Modules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == parentModuleName);
        var lineModule = await _db.Modules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Name == lineModuleName);
        if (parentModule is null || lineModule is null || productRows.Count == 0)
            return;

        // قبل از ساخت سطرها، tracker سنگین والدها را خالی کن تا SaveChanges وسط سید قطع نشود
        _db.ChangeTracker.Clear();

        var parents = await _db.Records.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ModuleId == parentModule.Id && !r.IsDeleted)
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.CustomData, r.CreatedAtUtc })
            .ToListAsync();
        if (parents.Count == 0)
            return;

        var lineJsons = await _db.Records.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ModuleId == lineModule.Id && !r.IsDeleted)
            .Select(r => r.CustomData)
            .ToListAsync();
        var hasLine = new HashSet<int>();
        foreach (var json in lineJsons)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty(linkField, out var el)) continue;
                var raw = el.ValueKind == JsonValueKind.Number ? el.GetRawText() : el.GetString();
                if (int.TryParse(raw, out var parentId) && parentId > 0)
                    hasLine.Add(parentId);
            }
            catch
            {
                // نادیده گرفتن JSON خراب در سید
            }
        }

        const int batchSize = 20;
        var pending = 0;
        for (var pi = 0; pi < parents.Count; pi++)
        {
            var parent = parents[pi];
            if (hasLine.Contains(parent.Id))
                continue;

            var lineCount = 2 + (pi % 4); // ۲ تا ۵ سطر
            decimal subTotal = 0, taxTotal = 0;

            for (var L = 0; L < lineCount; L++)
            {
                var prod = productRows[(pi * 3 + L) % productRows.Count];
                var taxPct = prod.TaxPercent > 0 ? prod.TaxPercent : 9m;
                var price = prod.SalePrice;
                var qty = 1 + ((pi + L) % 5);
                var disc = L == 0 && pi % 7 == 0 ? 5m : 0m;
                var net = qty * price * (1 - disc / 100m);
                var lineTax = net * (taxPct / 100m);
                var lineTotal = net + lineTax;
                subTotal += net;
                taxTotal += lineTax;

                var data = new Dictionary<string, string?>
                {
                    ["title"] = prod.Name,
                    ["product"] = prod.Id.ToString(),
                    ["quantity"] = qty.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["unitPrice"] = price.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    ["discountPercent"] = disc.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    ["taxPercent"] = taxPct.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    ["lineTotal"] = lineTotal.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    ["sortOrder"] = (L + 1).ToString(),
                    [linkField] = parent.Id.ToString()
                };

                _db.Records.Add(new DynamicRecord
                {
                    TenantId = tenantId,
                    ModuleId = lineModule.Id,
                    Title = prod.Name,
                    OwnerUserId = userId,
                    CreatedByUserId = userId,
                    CreatedAtUtc = parent.CreatedAtUtc == default ? now : parent.CreatedAtUtc,
                    CustomData = JsonSerializer.Serialize(data)
                });
            }

            Dictionary<string, string?> parentData;
            try
            {
                parentData = JsonSerializer.Deserialize<Dictionary<string, string?>>(parent.CustomData)
                             ?? new Dictionary<string, string?>();
            }
            catch
            {
                parentData = new Dictionary<string, string?>();
            }

            var discountPercent = 0m;
            if (decimal.TryParse(
                    parentData.GetValueOrDefault("discountPercent"),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var dp))
                discountPercent = dp;
            var discountAmount = subTotal * (discountPercent / 100m);
            var grand = subTotal - discountAmount + taxTotal;
            parentData["subTotal"] = subTotal.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            parentData["discountAmount"] = discountAmount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            parentData["taxTotal"] = taxTotal.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            parentData["grandTotal"] = grand.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            parentData["amount"] = grand.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            var tracked = new DynamicRecord { Id = parent.Id };
            _db.Records.Attach(tracked);
            tracked.CustomData = JsonSerializer.Serialize(parentData);
            tracked.UpdatedAtUtc = now;
            _db.Entry(tracked).Property(r => r.CustomData).IsModified = true;
            _db.Entry(tracked).Property(r => r.UpdatedAtUtc).IsModified = true;

            pending++;
            if (pending >= batchSize)
            {
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
                pending = 0;
            }
        }

        if (pending > 0)
        {
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }

        if (parents.Count > 0)
        {
            var mod = await _db.Modules.IgnoreQueryFilters()
                .FirstAsync(m => m.Id == parentModule.Id);
            mod.NextNumber = Math.Max(mod.NextNumber, 1001 + parents.Count);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }
    }

    private async Task SeedSalesDocumentsAsync(
        int tenantId, int userId, SalesDocumentKind kind,
        IReadOnlyList<int> productIds, IReadOnlyList<int> contactIds, IReadOnlyList<int> orgIds,
        DateTime now)
    {
        var count = await _db.SalesDocuments.IgnoreQueryFilters()
            .CountAsync(d => d.TenantId == tenantId && d.Kind == kind && !d.IsDeleted);
        if (count >= TargetCount)
            return;

        var maxNumber = await _db.SalesDocuments.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.Kind == kind)
            .MaxAsync(d => (int?)d.Number) ?? 1000;

        for (var i = count; i < TargetCount; i++)
        {
            maxNumber++;
            var issue = now.AddDays(-(i % 75));
            var lineCount = 2 + (i % 3);
            decimal subTotal = 0, taxTotal = 0;
            var lines = new List<SalesDocumentLine>();
            for (var L = 0; L < lineCount; L++)
            {
                var spec = ProductCatalog[(i * 2 + L) % ProductCatalog.Length];
                var qty = 1 + ((i + L) % 5);
                var price = spec.Price;
                var tax = Math.Round(qty * price * 0.09m, 0);
                var lineTotal = qty * price + tax;
                subTotal += qty * price;
                taxTotal += tax;
                lines.Add(new SalesDocumentLine
                {
                    TenantId = tenantId,
                    ProductId = productIds.Count > 0 ? productIds[(i + L) % productIds.Count] : null,
                    Title = spec.Name,
                    Quantity = qty,
                    UnitPrice = price,
                    TaxPercent = 9,
                    LineTotal = lineTotal,
                    SortOrder = L + 1,
                    CreatedAtUtc = issue,
                    CreatedByUserId = userId
                });
            }

            var customer = $"{Companies[i % Companies.Length]}";
            var doc = new SalesDocument
            {
                TenantId = tenantId,
                Kind = kind,
                Number = maxNumber,
                Status = PickStatus(kind, i),
                CustomerName = customer,
                ContactRecordId = contactIds.Count > 0 ? contactIds[i % contactIds.Count] : null,
                OrganizationRecordId = orgIds.Count > 0 ? orgIds[i % orgIds.Count] : null,
                IssueDateUtc = issue,
                ValidUntilUtc = kind == SalesDocumentKind.Quote ? issue.AddDays(14) : null,
                SubTotal = subTotal,
                TaxTotal = taxTotal,
                GrandTotal = subTotal + taxTotal,
                Note = $"{kind} نمونه — تاریخ نسبی {issue:yyyy-MM-dd}",
                CreatedAtUtc = issue,
                CreatedByUserId = userId
            };
            foreach (var line in lines)
                doc.Lines.Add(line);

            _db.SalesDocuments.Add(doc);
        }

        await _db.SaveChangesAsync();
    }

    private static SalesDocumentStatus PickStatus(SalesDocumentKind kind, int i) => kind switch
    {
        SalesDocumentKind.Invoice => (i % 5) switch
        {
            0 => SalesDocumentStatus.Paid,
            1 => SalesDocumentStatus.PartiallyPaid,
            2 => SalesDocumentStatus.Confirmed,
            3 => SalesDocumentStatus.Draft,
            _ => SalesDocumentStatus.Canceled
        },
        _ => (i % 4) switch
        {
            0 => SalesDocumentStatus.Draft,
            1 => SalesDocumentStatus.Confirmed,
            2 => SalesDocumentStatus.Converted,
            _ => SalesDocumentStatus.Canceled
        }
    };

    private async Task SeedCommissionRulesAsync(int tenantId, int userId, IReadOnlyList<int> productIds, DateTime now)
    {
        var count = await _db.CommissionRules.IgnoreQueryFilters()
            .CountAsync(r => r.TenantId == tenantId && !r.IsDeleted);
        if (count >= TargetCount)
            return;

        for (var i = count; i < TargetCount; i++)
        {
            _db.CommissionRules.Add(new CommissionRule
            {
                TenantId = tenantId,
                Name = $"قانون پورسانت #{i + 1:00}",
                ProductId = i % 3 == 0 && productIds.Count > 0 ? productIds[i % productIds.Count] : null,
                Percent = 1 + (i % 10),
                FixedAmount = i % 4 == 0 ? 50_000m : 0,
                MinInvoiceAmount = (i % 5) * 1_000_000m,
                IsActive = i % 7 != 0,
                CreatedAtUtc = now.AddDays(-(i % 40)),
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedProjectsAsync(int tenantId, int userId, IReadOnlyList<int> contactIds, DateTime now)
    {
        var count = await _db.Projects.IgnoreQueryFilters()
            .CountAsync(p => p.TenantId == tenantId && !p.IsDeleted);
        if (count >= TargetCount)
            return;

        for (var i = count; i < TargetCount; i++)
        {
            var start = now.AddDays(-60 + (i % 50));
            var end = start.AddDays(30 + (i % 60));
            _db.Projects.Add(new Project
            {
                TenantId = tenantId,
                Name = $"پروژه {Companies[i % Companies.Length]} #{i + 1:00}",
                Description = $"پروژه نمونه با بازه نسبی {start:yyyy-MM-dd} تا {end:yyyy-MM-dd}",
                Status = (ProjectStatus)(i % 4),
                StartUtc = start,
                EndUtc = end,
                Budget = (i % 20 + 1) * 5_000_000m,
                ContactRecordId = contactIds.Count > 0 ? contactIds[i % contactIds.Count] : null,
                CustomerName = Companies[i % Companies.Length],
                ShowInPortal = i % 2 == 0,
                CreatedAtUtc = start,
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedVendorsAsync(int tenantId, int userId, DateTime now)
    {
        var count = await _db.Vendors.IgnoreQueryFilters()
            .CountAsync(v => v.TenantId == tenantId && !v.IsDeleted);
        if (count >= TargetCount)
            return;

        for (var i = count; i < TargetCount; i++)
        {
            _db.Vendors.Add(new Vendor
            {
                TenantId = tenantId,
                Name = $"تأمین‌کننده {Companies[i % Companies.Length]} {i + 1:00}",
                Phone = $"0910{4000000 + i:0000000}",
                Email = $"vendor{i + 1:00}@demo.local",
                Address = Cities[i % Cities.Length],
                Notes = $"سید نسبی {now:yyyy-MM-dd}",
                IsActive = i % 8 != 0,
                CreatedAtUtc = now.AddDays(-(i % 50)),
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedPurchaseOrdersAsync(int tenantId, int userId, IReadOnlyList<int> productIds, DateTime now)
    {
        var count = await _db.PurchaseOrders.IgnoreQueryFilters()
            .CountAsync(p => p.TenantId == tenantId && !p.IsDeleted);
        if (count >= TargetCount)
            return;

        var vendors = await _db.Vendors.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId && !v.IsDeleted)
            .OrderBy(v => v.Id)
            .Select(v => v.Id)
            .ToListAsync();
        if (vendors.Count == 0)
            return;

        var maxNumber = await _db.PurchaseOrders.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId)
            .MaxAsync(p => (int?)p.Number) ?? 2000;

        for (var i = count; i < TargetCount; i++)
        {
            maxNumber++;
            var issue = now.AddDays(-(i % 70));
            var lineCount = 2 + (i % 3);
            decimal total = 0;
            var po = new PurchaseOrder
            {
                TenantId = tenantId,
                Number = maxNumber,
                VendorId = vendors[i % vendors.Count],
                Status = (PurchaseOrderStatus)(i % 4),
                IssueDateUtc = issue,
                ReceivedAtUtc = i % 4 == 2 ? issue.AddDays(3) : null,
                Note = $"سفارش خرید نمونه — {issue:yyyy-MM-dd}",
                CreatedAtUtc = issue,
                CreatedByUserId = userId
            };
            for (var L = 0; L < lineCount; L++)
            {
                var spec = ProductCatalog[(i * 3 + L) % ProductCatalog.Length];
                var qty = 2 + ((i + L) % 8);
                var cost = Math.Round(spec.Price * 0.72m, 0);
                var lineTotal = qty * cost;
                total += lineTotal;
                po.Lines.Add(new PurchaseOrderLine
                {
                    TenantId = tenantId,
                    ProductId = productIds.Count > 0 ? productIds[(i + L) % productIds.Count] : null,
                    Title = spec.Name,
                    Quantity = qty,
                    UnitCost = cost,
                    LineTotal = lineTotal,
                    SortOrder = L + 1,
                    CreatedAtUtc = issue,
                    CreatedByUserId = userId
                });
            }
            po.Total = total;
            _db.PurchaseOrders.Add(po);
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedCampaignsAsync(int tenantId, int userId, DateTime now)
    {
        var count = await _db.Campaigns.IgnoreQueryFilters()
            .CountAsync(c => c.TenantId == tenantId && !c.IsDeleted);
        if (count >= TargetCount)
            return;

        for (var i = count; i < TargetCount; i++)
        {
            var start = now.AddDays(-40 + (i % 35));
            var end = start.AddDays(14 + (i % 20));
            _db.Campaigns.Add(new Campaign
            {
                TenantId = tenantId,
                Name = $"کمپین {Pick(["اینستاگرام", "گوگل", "پیامک", "ایمیل", "نمایشگاه"], i)} #{i + 1:00}",
                Channel = Pick(["instagram", "google", "sms", "email", "offline"], i),
                Description = $"کمپین نمونه — {start:yyyy-MM-dd} تا {end:yyyy-MM-dd}",
                Status = (CampaignStatus)(i % 4),
                StartUtc = start,
                EndUtc = end,
                Budget = (i % 15 + 1) * 2_000_000m,
                ActualCost = (i % 12 + 1) * 1_200_000m,
                CreatedAtUtc = start,
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedCampaignMembersAsync(
        int tenantId, int userId,
        IReadOnlyList<int> leadIds, IReadOnlyList<int> contactIds, IReadOnlyList<int> opportunityIds,
        DateTime now)
    {
        var existing = await _db.CampaignMembers.IgnoreQueryFilters()
            .CountAsync(m => m.TenantId == tenantId && !m.IsDeleted);
        if (existing >= 30)
            return;

        var campaigns = await _db.Campaigns.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && !c.IsDeleted)
            .OrderBy(c => c.Id)
            .Take(15)
            .Select(c => c.Id)
            .ToListAsync();
        if (campaigns.Count == 0)
            return;

        var added = existing;
        for (var i = 0; i < campaigns.Count && added < 30; i++)
        {
            var campaignId = campaigns[i];
            void TryAdd(string module, IReadOnlyList<int> ids, int offset)
            {
                if (ids.Count == 0 || added >= 30) return;
                var recordId = ids[(i + offset) % ids.Count];
                _db.CampaignMembers.Add(new CampaignMember
                {
                    TenantId = tenantId,
                    CampaignId = campaignId,
                    ModuleName = module,
                    RecordId = recordId,
                    CreatedAtUtc = now.AddDays(-i),
                    CreatedByUserId = userId
                });
                added++;
            }

            TryAdd("leads", leadIds, 0);
            TryAdd("contacts", contactIds, 3);
            TryAdd("opportunities", opportunityIds, 7);
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedCommissionEntriesAsync(int tenantId, int userId, DateTime now)
    {
        var existing = await _db.CommissionEntries.IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == tenantId && !e.IsDeleted);
        if (existing >= 40)
            return;

        var rules = await _db.CommissionRules.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && !r.IsDeleted && r.IsActive)
            .OrderBy(r => r.Id)
            .Take(20)
            .Select(r => r.Id)
            .ToListAsync();
        if (rules.Count == 0)
            return;

        var invoices = await _db.SalesDocuments.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && !d.IsDeleted && d.Kind == SalesDocumentKind.Invoice)
            .OrderBy(d => d.Id)
            .Take(40)
            .Select(d => new { d.Id, d.GrandTotal, d.CreatedByUserId })
            .ToListAsync();
        if (invoices.Count == 0)
            return;

        var teamUsers = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId)
            .Select(u => u.Id)
            .ToListAsync();
        if (teamUsers.Count == 0)
            teamUsers = [userId];

        var need = 40 - existing;
        for (var i = 0; i < need && i < invoices.Count; i++)
        {
            var inv = invoices[i];
            var ruleId = rules[i % rules.Count];
            var amount = Math.Max(50_000m, Math.Round(inv.GrandTotal * (0.02m + (i % 5) * 0.01m), 0));
            _db.CommissionEntries.Add(new CommissionEntry
            {
                TenantId = tenantId,
                DocumentId = inv.Id,
                UserId = teamUsers[i % teamUsers.Count],
                RuleId = ruleId,
                Amount = amount,
                CreatedAtUtc = now.AddDays(-(i % 45)),
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedWorkflowsAsync(int tenantId, int userId, DateTime now)
    {
        if (await _db.WorkflowRules.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenantId && !r.IsDeleted))
            return;

        async Task<int?> ModuleId(string name) =>
            await _db.Modules.IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && m.Name == name && !m.IsDeleted)
                .Select(m => (int?)m.Id)
                .FirstOrDefaultAsync();

        var leadsId = await ModuleId("leads");
        var oppsId = await ModuleId("opportunities");
        var tasksId = await ModuleId("tasks");
        if (leadsId is null || oppsId is null)
            return;

        var specs = new List<(string Name, int ModuleId, WorkflowTrigger Trigger, WorkflowActionType Action, string Config)>
        {
            ("اعلان سرنخ داغ", leadsId.Value, WorkflowTrigger.RecordCreated, WorkflowActionType.Notify,
                """{"message":"سرنخ جدید ثبت شد: {name}"}"""),
            ("وظیفه پیگیری سرنخ", leadsId.Value, WorkflowTrigger.RecordUpdated, WorkflowActionType.CreateTask,
                """{"title":"پیگیری سرنخ {name}","dueInDays":2}"""),
            ("به‌روزرسانی مرحله فرصت", oppsId.Value, WorkflowTrigger.RecordUpdated, WorkflowActionType.UpdateField,
                """{"field":"probability","value":"70"}"""),
            ("اعلان فرصت برنده", oppsId.Value, WorkflowTrigger.RecordUpdated, WorkflowActionType.Notify,
                """{"message":"فرصت {name} به مرحله برنده رسید"}""")
        };

        if (tasksId is not null)
        {
            specs.Add(("یادآوری وظیفه روزانه", tasksId.Value, WorkflowTrigger.Scheduled, WorkflowActionType.Notify,
                """{"message":"مرور وظایف باز"}"""));
        }

        foreach (var spec in specs)
        {
            var rule = new WorkflowRule
            {
                TenantId = tenantId,
                Name = spec.Name,
                ModuleId = spec.ModuleId,
                Trigger = spec.Trigger,
                Schedule = spec.Trigger == WorkflowTrigger.Scheduled ? WorkflowSchedule.Daily : null,
                ConditionsJson = """{"logic":"and","items":[]}""",
                IsActive = true,
                CreatedAtUtc = now,
                CreatedByUserId = userId
            };
            _db.WorkflowRules.Add(rule);
            await _db.SaveChangesAsync();

            _db.WorkflowActions.Add(new WorkflowAction
            {
                TenantId = tenantId,
                RuleId = rule.Id,
                Type = spec.Action,
                ConfigJson = spec.Config,
                SortOrder = 1,
                CreatedAtUtc = now,
                CreatedByUserId = userId
            });

            _db.WorkflowLogs.Add(new WorkflowLog
            {
                TenantId = tenantId,
                RuleId = rule.Id,
                Success = true,
                Message = "اجرای نمونه دمو",
                CreatedAtUtc = now.AddHours(-spec.ModuleId % 24),
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedReportsAsync(int tenantId, int userId, DateTime now)
    {
        if (await _db.Reports.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenantId && !r.IsDeleted))
            return;

        async Task<int?> ModuleId(string name) =>
            await _db.Modules.IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && m.Name == name && !m.IsDeleted)
                .Select(m => (int?)m.Id)
                .FirstOrDefaultAsync();

        var leadsId = await ModuleId("leads");
        var oppsId = await ModuleId("opportunities");
        var tasksId = await ModuleId("tasks");
        var callsId = await ModuleId("calls");

        var reports = new List<ReportDef>();
        if (leadsId is not null)
        {
            reports.Add(new ReportDef
            {
                TenantId = tenantId,
                Name = "سرنخ‌ها بر اساس وضعیت",
                ModuleId = leadsId.Value,
                ColumnsJson = """["name","status","source","city"]""",
                GroupByField = "status",
                CreatedAtUtc = now,
                CreatedByUserId = userId
            });
            reports.Add(new ReportDef
            {
                TenantId = tenantId,
                Name = "کانال جذب سرنخ",
                ModuleId = leadsId.Value,
                ColumnsJson = """["name","source","status"]""",
                GroupByField = "source",
                CreatedAtUtc = now,
                CreatedByUserId = userId
            });
        }

        if (oppsId is not null)
        {
            reports.Add(new ReportDef
            {
                TenantId = tenantId,
                Name = "فرصت‌ها بر اساس مرحله",
                ModuleId = oppsId.Value,
                ColumnsJson = """["name","stage","amount","probability"]""",
                GroupByField = "stage",
                SumField = "amount",
                CreatedAtUtc = now,
                CreatedByUserId = userId
            });
        }

        if (tasksId is not null)
        {
            reports.Add(new ReportDef
            {
                TenantId = tenantId,
                Name = "وضعیت وظایف",
                ModuleId = tasksId.Value,
                ColumnsJson = """["name","status","priority","dueDate"]""",
                GroupByField = "status",
                CreatedAtUtc = now,
                CreatedByUserId = userId
            });
        }

        if (callsId is not null)
        {
            reports.Add(new ReportDef
            {
                TenantId = tenantId,
                Name = "نتیجه تماس‌ها",
                ModuleId = callsId.Value,
                ColumnsJson = """["name","result","direction"]""",
                GroupByField = "result",
                CreatedAtUtc = now,
                CreatedByUserId = userId
            });
        }

        if (reports.Count > 0)
        {
            _db.Reports.AddRange(reports);
            await _db.SaveChangesAsync();
        }
    }

    private async Task SeedWebFormsAsync(int tenantId, int userId, DateTime now)
    {
        var count = await _db.WebForms.IgnoreQueryFilters()
            .CountAsync(w => w.TenantId == tenantId && !w.IsDeleted);
        if (count >= TargetCount)
            return;

        var leadsModuleId = await _db.Modules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Name == "leads")
            .Select(m => m.Id)
            .FirstAsync();

        for (var i = count; i < TargetCount; i++)
        {
            _db.WebForms.Add(new WebForm
            {
                TenantId = tenantId,
                Name = $"فرم ثبت سرنخ #{i + 1:00}",
                PublicKey = Guid.NewGuid().ToString("N"),
                ModuleId = leadsModuleId,
                FieldsJson = """[{"name":"name"},{"name":"phone"},{"name":"email"}]""",
                SuccessMessage = "درخواست شما ثبت شد.",
                UseCaptcha = false,
                IsActive = i % 6 != 0,
                SubmissionCount = i % 25,
                CreatedAtUtc = now.AddDays(-(i % 55)),
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedSurveysAsync(int tenantId, int userId, DateTime now)
    {
        var count = await _db.Surveys.IgnoreQueryFilters()
            .CountAsync(s => s.TenantId == tenantId && !s.IsDeleted);
        if (count >= TargetCount)
            return;

        for (var i = count; i < TargetCount; i++)
        {
            var survey = new Survey
            {
                TenantId = tenantId,
                Title = $"نظرسنجی رضایت #{i + 1:00}",
                Description = $"نظرسنجی نمونه — سید {now:yyyy-MM-dd}",
                PublicKey = Guid.NewGuid().ToString("N"),
                IsActive = i % 5 != 0,
                ConvertToLead = i % 4 == 0,
                IsTicketSurvey = i % 7 == 0,
                CreatedAtUtc = now.AddDays(-(i % 45)),
                CreatedByUserId = userId
            };
            survey.Questions.Add(new SurveyQuestion
            {
                TenantId = tenantId,
                Text = "میزان رضایت شما چقدر است؟",
                Type = SurveyQuestionType.Scale,
                SortOrder = 1,
                CreatedAtUtc = now,
                CreatedByUserId = userId
            });
            survey.Questions.Add(new SurveyQuestion
            {
                TenantId = tenantId,
                Text = "پیشنهاد شما چیست؟",
                Type = SurveyQuestionType.Text,
                SortOrder = 2,
                CreatedAtUtc = now,
                CreatedByUserId = userId
            });
            _db.Surveys.Add(survey);
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedTemplatesAsync(int tenantId, int userId, DateTime now)
    {
        var count = await _db.MessageTemplates.IgnoreQueryFilters()
            .CountAsync(t => t.TenantId == tenantId && !t.IsDeleted);
        if (count >= TargetCount)
            return;

        for (var i = count; i < TargetCount; i++)
        {
            _db.MessageTemplates.Add(new MessageTemplate
            {
                TenantId = tenantId,
                Title = $"قالب پیام #{i + 1:00}",
                Body = $"سلام {{name}}، این یک پیام آماده نمونه است (سید {now:yyyy-MM-dd}). شماره {i + 1}.",
                IsPublic = i % 3 != 0,
                CreatedAtUtc = now.AddDays(-(i % 35)),
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedTicketsAsync(int tenantId, int userId, IReadOnlyList<int> contactIds, DateTime now)
    {
        var count = await _db.Tickets.IgnoreQueryFilters()
            .CountAsync(t => t.TenantId == tenantId && !t.IsDeleted);
        if (count >= TargetCount)
            return;

        if (!await _db.SlaPolicies.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenantId))
        {
            foreach (var (priority, hours) in new (TicketPriority, int)[]
                     {
                         (TicketPriority.Urgent, 2), (TicketPriority.High, 4),
                         (TicketPriority.Normal, 8), (TicketPriority.Low, 24)
                     })
            {
                _db.SlaPolicies.Add(new SlaPolicy
                {
                    TenantId = tenantId,
                    Priority = priority,
                    ResponseHours = hours,
                    CreatedAtUtc = now,
                    CreatedByUserId = userId
                });
            }

            await _db.SaveChangesAsync();
        }

        var maxNumber = await _db.Tickets.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId)
            .MaxAsync(t => (int?)t.Number) ?? 100;

        for (var i = count; i < TargetCount; i++)
        {
            maxNumber++;
            var created = now.AddDays(-(i % 40)).AddHours(-(i % 10));
            var priority = (TicketPriority)(i % 4);
            var ticket = new Ticket
            {
                TenantId = tenantId,
                Number = maxNumber,
                Subject = $"تیکت پشتیبانی #{i + 1:00} — {Pick(["ورود", "گزارش", "فاکتور", "API", "پورتال"], i)}",
                Category = Pick(["فنی", "مالی", "عمومی", "فروش"], i),
                Priority = priority,
                Status = (TicketStatus)(i % 5),
                AssignedUserId = userId,
                ContactRecordId = contactIds.Count > 0 ? contactIds[i % contactIds.Count] : null,
                DueAtUtc = created.AddHours(8),
                CreatedAtUtc = created,
                CreatedByUserId = userId
            };
            ticket.Messages.Add(new TicketMessage
            {
                TenantId = tenantId,
                Body = $"متن اولیه تیکت نمونه. ثبت نسبی در {created:yyyy-MM-dd HH:mm}.",
                IsFromCustomer = true,
                AuthorName = PersonName(i),
                CreatedAtUtc = created,
                CreatedByUserId = userId
            });
            _db.Tickets.Add(ticket);
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedContractsAsync(int tenantId, int userId, IReadOnlyList<int> contactIds, DateTime now)
    {
        var count = await _db.ServiceContracts.IgnoreQueryFilters()
            .CountAsync(c => c.TenantId == tenantId && !c.IsDeleted);
        if (count >= TargetCount)
            return;

        for (var i = count; i < TargetCount; i++)
        {
            var start = now.AddDays(-90 + (i % 60));
            var end = start.AddMonths(6 + (i % 6));
            _db.ServiceContracts.Add(new ServiceContract
            {
                TenantId = tenantId,
                Name = $"قرارداد خدمات #{i + 1:00}",
                ContactRecordId = contactIds.Count > 0 ? contactIds[i % contactIds.Count] : null,
                CustomerName = Companies[i % Companies.Length],
                StartUtc = start,
                EndUtc = end,
                MaxTickets = 20 + (i % 30),
                TicketsUsed = i % 10,
                IsActive = end > now,
                CreatedAtUtc = start,
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedWarrantiesAsync(
        int tenantId, int userId, IReadOnlyList<int> productIds, IReadOnlyList<int> contactIds, DateTime now)
    {
        var count = await _db.Warranties.IgnoreQueryFilters()
            .CountAsync(w => w.TenantId == tenantId && !w.IsDeleted);
        if (count >= TargetCount)
            return;

        for (var i = count; i < TargetCount; i++)
        {
            var start = now.AddDays(-120 + (i % 80));
            var end = start.AddYears(1);
            _db.Warranties.Add(new Warranty
            {
                TenantId = tenantId,
                SerialNumber = $"SN-DEMO-{i + 1:00000}",
                ProductId = productIds.Count > 0 ? productIds[i % productIds.Count] : null,
                CustomerName = PersonName(i),
                ContactRecordId = contactIds.Count > 0 ? contactIds[i % contactIds.Count] : null,
                StartUtc = start,
                EndUtc = end,
                Notes = $"گارانتی نمونه — شروع نسبی {start:yyyy-MM-dd}",
                CreatedAtUtc = start,
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedKbAsync(int tenantId, int userId, DateTime now)
    {
        var count = await _db.KbArticles.IgnoreQueryFilters()
            .CountAsync(a => a.TenantId == tenantId && !a.IsDeleted);
        if (count >= TargetCount)
            return;

        for (var i = count; i < TargetCount; i++)
        {
            _db.KbArticles.Add(new KbArticle
            {
                TenantId = tenantId,
                Title = $"مقاله دانش پایه #{i + 1:00}",
                Body = $"<p>محتوای آموزشی نمونه شماره {i + 1}. تاریخ سید: {now:yyyy-MM-dd}.</p>",
                Category = Pick(["شروع کار", "فروش", "مالی", "پشتیبانی", "API"], i),
                IsPublishedToPortal = i % 2 == 0,
                CreatedAtUtc = now.AddDays(-(i % 50)),
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedPortalUsersAsync(int tenantId, int userId, IReadOnlyList<int> contactIds, DateTime now)
    {
        var count = await _db.PortalUsers.IgnoreQueryFilters()
            .CountAsync(u => u.TenantId == tenantId && !u.IsDeleted);
        if (count >= TargetCount)
            return;

        var hasher = new PasswordHasher<PortalUser>();
        for (var i = count; i < TargetCount; i++)
        {
            var user = new PortalUser
            {
                TenantId = tenantId,
                Email = $"portal{i + 1:00}@demo.local",
                FullName = PersonName(i + 3),
                ContactRecordId = contactIds.Count > 0 ? contactIds[i % contactIds.Count] : null,
                IsActive = i % 9 != 0,
                CreatedAtUtc = now.AddDays(-(i % 40)),
                CreatedByUserId = userId
            };
            user.PasswordHash = hasher.HashPassword(user, "Portal@1405");
            _db.PortalUsers.Add(user);
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedLeavesAsync(int tenantId, int userId, DateTime now)
    {
        var count = await _db.LeaveRequests.IgnoreQueryFilters()
            .CountAsync(l => l.TenantId == tenantId && !l.IsDeleted);
        if (count >= TargetCount)
            return;

        for (var i = count; i < TargetCount; i++)
        {
            var from = now.AddDays(-30 + (i % 45));
            var to = from.AddDays(1 + (i % 5));
            _db.LeaveRequests.Add(new LeaveRequest
            {
                TenantId = tenantId,
                UserId = userId,
                Type = (LeaveType)(i % 2),
                FromUtc = from,
                ToUtc = to,
                Reason = $"درخواست نمونه #{i + 1:00}",
                Status = (LeaveStatus)(i % 3),
                ReviewedByUserId = i % 3 == 0 ? null : userId,
                ReviewNote = i % 3 == 0 ? null : "بررسی شد",
                CreatedAtUtc = from.AddDays(-2),
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    private static string PersonName(int i) => UniquePersonName(i);

    /// <summary>نام یکتا از ترکیب نام+نام‌خانوادگی؛ برای اندیس‌های بزرگ پسوند عددی.</summary>
    private static string UniquePersonName(int i)
    {
        var combo = FirstNames.Length * LastNames.Length;
        var first = FirstNames[i % FirstNames.Length];
        var last = LastNames[(i / FirstNames.Length) % LastNames.Length];
        if (i < combo)
            return $"{first} {last}";
        return $"{first} {last} ({i + 1})";
    }

    private static string UniqueCompanyName(int i)
    {
        var baseName = Companies[i % Companies.Length];
        return $"{baseName} {i + 1:000}";
    }

    private static string Pick(string[] items, int i) => items[i % items.Length];

    /// <summary>انتخاب وزن‌دار و قطعی — برای نمودارهای نابرابر و واقعی‌تر.</summary>
    private static string WeightedPick(int seed, params (string Value, int Weight)[] items)
    {
        var total = items.Sum(x => x.Weight);
        if (total <= 0)
            return items[0].Value;

        var n = (int)((uint)(seed * 1103515245 + 12345) % (uint)total);
        foreach (var (value, weight) in items)
        {
            if (n < weight)
                return value;
            n -= weight;
        }

        return items[^1].Value;
    }

    /// <summary>تاریخ ساخت با منحنی رشد (ماه‌های اخیر پرتراکم‌تر).</summary>
    private static DateTime GrowthCreatedAt(DateTime now, int index, int total)
    {
        // توزیع تقریبی روی ۶ ماه: ۱٪، ۵٪، ۱۰٪، ۱۸٪، ۲۸٪، ۳۸٪
        int[] monthWeights = [1, 5, 10, 18, 28, 38];
        var sum = monthWeights.Sum();
        var slot = total <= 1 ? 0 : (int)((long)index * sum / total);
        var monthOffset = 5;
        var acc = 0;
        for (var m = 0; m < monthWeights.Length; m++)
        {
            acc += monthWeights[m];
            if (slot < acc)
            {
                monthOffset = 5 - m;
                break;
            }
        }

        var day = 1 + (index * 7) % 27;
        var hour = 8 + (index % 10);
        var baseMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-monthOffset);
        var daysInMonth = DateTime.DaysInMonth(baseMonth.Year, baseMonth.Month);
        day = Math.Min(day, daysInMonth);
        return new DateTime(baseMonth.Year, baseMonth.Month, day, hour, (index * 13) % 60, 0, DateTimeKind.Utc);
    }
}
