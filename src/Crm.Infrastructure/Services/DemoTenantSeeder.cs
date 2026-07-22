using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;

namespace Crm.Infrastructure.Services;

/// <summary>
/// یک Tenant نمونه همیشه با حداقل ۲۰۰ رکورد در هر صفحهٔ پنل App.
/// تاریخ‌ها نسبت به زمان اجرای seed (UtcNow) ساخته می‌شوند. Idempotent است.
/// </summary>
public class DemoTenantSeeder
{
    public const string DemoSlug = "demo";
    public const string DemoEmail = "demo@bamacrm.local";
    public const string DemoPassword = "Demo@1405";
    public const int TargetCount = 200;

    private static readonly string[] FirstNames =
    [
        "علی", "محمد", "رضا", "حسین", "مهدی", "امیر", "سعید", "حامد", "پارسا", "آرین",
        "زهرا", "فاطمه", "مریم", "سارا", "نگار", "نیلوفر", "هستی", "آیدا", "یلدا", "لیلا"
    ];

    private static readonly string[] LastNames =
    [
        "محمدی", "رضایی", "حسینی", "کریمی", "موسوی", "نوری", "جعفری", "احمدی", "کاظمی", "صادقی",
        "اکبری", "باقری", "نجفی", "شریفی", "طاهری", "امینی", "رحیمی", "قاسمی", "مرادی", "یوسفی"
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
        "نوآوران فردا", "پیمان‌سازان"
    ];

    private static readonly string[] Products =
    [
        "لایسنس حرفه‌ای", "پشتیبانی ماهانه", "آموزش حضوری", "ماژول انبار", "ماژول فروش",
        "سرور ابری پایه", "سرور ابری پیشرفته", "پیامک انبوه", "درگاه پرداخت", "مشاوره پیاده‌سازی",
        "قالب گزارش", "داشبورد مدیریتی", "یکپارچه‌سازی API", "بکاپ روزانه", "مانیتورینگ"
    ];

    private readonly CrmDbContext _db;
    private readonly UserManager<CrmUser> _users;
    private readonly SalesModuleSeeder _modules;

    public DemoTenantSeeder(CrmDbContext db, UserManager<CrmUser> users, SalesModuleSeeder modules)
    {
        _db = db;
        _users = users;
        _modules = modules;
    }

    public async Task EnsureSeededAsync()
    {
        var result = await CreateOrRefreshAsync();
        if (!result.Ok)
            throw new InvalidOperationException(result.Message);
    }

    /// <summary>
    /// ساخت مشتری دمو (اگر نیست) یا تکمیل دادهٔ نمونه تا حداقل ۲۰۰ رکورد در هر صفحه.
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
            var name = $"{Companies[i % Companies.Length]} {i + 1:00}";
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
            var name = PersonName(i);
            return (name, new Dictionary<string, string?>
            {
                ["name"] = name,
                ["company"] = Companies[i % Companies.Length],
                ["phone"] = $"0912{2000000 + i:0000000}",
                ["email"] = $"lead{i + 1:00}@demo.local",
                ["city"] = WeightedPick(i + 5, ("تهران", 32), ("اصفهان", 15), ("مشهد", 13), ("شیراز", 11), ("تبریز", 9), ("کرج", 8), ("اهواز", 5), ("قم", 4), ("کرمان", 3)),
                ["status"] = WeightedPick(i, ("warm", 32), ("cold", 24), ("qualified", 18), ("hot", 16), ("junk", 10)),
                ["source"] = WeightedPick(i + 11, ("website", 34), ("referral", 22), ("ads", 16), ("social", 14), ("call", 8), ("exhibition", 6)),
                ["description"] = $"سرنخ نمونه — ایجاد نسبی به {t:yyyy-MM-dd}"
            });
        });

        await SeedDynamicModuleAsync(tenantId, adminId, "contacts", TargetCount, (i, t) =>
        {
            var name = PersonName(i + 7);
            return (name, new Dictionary<string, string?>
            {
                ["name"] = name,
                ["organization"] = orgIds.Count > 0 ? orgIds[i % orgIds.Count].ToString() : null,
                ["position"] = WeightedPick(i, ("کارشناس فروش", 30), ("مدیر فروش", 22), ("خرید", 16), ("مالی", 14), ("IT", 10), ("CEO", 8)),
                ["mobile"] = $"0935{3000000 + i:0000000}",
                ["phone"] = $"021{50000000 + i}",
                ["email"] = $"contact{i + 1:00}@demo.local",
                ["address"] = WeightedPick(i + 2, ("تهران", 30), ("اصفهان", 16), ("مشهد", 14), ("شیراز", 12), ("تبریز", 10), ("کرج", 8), ("اهواز", 6), ("رشت", 4)),
                ["description"] = $"مخاطب نمونه — {t:yyyy-MM-dd}"
            });
        });

        var contactIds = await ModuleRecordIdsAsync(tenantId, "contacts");

        await SeedDynamicModuleAsync(tenantId, adminId, "opportunities", TargetCount, (i, t) =>
        {
            var title = $"فرصت فروش {Companies[i % Companies.Length]} #{i + 1:00}";
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
            var title = $"تماس با {PersonName(i)} #{i + 1:00}";
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
        var productIds = await _db.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && !p.IsDeleted)
            .OrderBy(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync();

        await SeedSalesDocumentsAsync(tenantId, adminId, SalesDocumentKind.Quote, productIds, contactIds, orgIds, now);
        await SeedSalesDocumentsAsync(tenantId, adminId, SalesDocumentKind.Order, productIds, contactIds, orgIds, now);
        await SeedSalesDocumentsAsync(tenantId, adminId, SalesDocumentKind.Invoice, productIds, contactIds, orgIds, now);
        await SeedCommissionRulesAsync(tenantId, adminId, productIds, now);
        await SeedProjectsAsync(tenantId, adminId, contactIds, now);
        await SeedVendorsAsync(tenantId, adminId, now);
        await SeedPurchaseOrdersAsync(tenantId, adminId, productIds, now);
        await SeedCampaignsAsync(tenantId, adminId, now);
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
        await SeedDashboardWidgetsAsync(tenantId, adminId, now);
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
            ("pie", "opportunities", "stage", "مراحل فروش"),
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
                Name = "شرکت نمونه BamaCRM",
                Slug = DemoSlug,
                Status = TenantStatus.Active,
                CreatedAtUtc = now,
                TrialEndsAtUtc = now.AddYears(10)
            };
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync();

            var ceo = new Role { TenantId = tenant.Id, Name = "مدیر عامل" };
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
            return (tenant, user);
        }

        await _modules.EnsureSeededAsync(tenant.Id);

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
        var count = await _db.Products.IgnoreQueryFilters()
            .CountAsync(p => p.TenantId == tenantId && !p.IsDeleted);
        if (count >= TargetCount)
            return;

        for (var i = count; i < TargetCount; i++)
        {
            var name = $"{Products[i % Products.Length]} {i + 1:00}";
            _db.Products.Add(new Product
            {
                TenantId = tenantId,
                Name = name,
                Sku = $"SKU-{i + 1:000}",
                Unit = i % 5 == 0 ? "ماه" : "عدد",
                SalePrice = (i % 15 + 1) * 250_000m,
                TaxPercent = 9,
                IsService = i % 3 == 0,
                TrackInventory = i % 3 != 0,
                StockQty = 10 + (i % 40),
                ReorderPoint = 5,
                IsActive = true,
                Description = $"محصول نمونه — سید {now:yyyy-MM-dd}",
                CreatedAtUtc = now.AddDays(-(i % 60)),
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
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
            var qty = 1 + (i % 5);
            var price = (i % 12 + 1) * 300_000m;
            var tax = Math.Round(qty * price * 0.09m, 0);
            var lineTotal = qty * price + tax;
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
                SubTotal = qty * price,
                TaxTotal = tax,
                GrandTotal = lineTotal,
                Note = $"{kind} نمونه — تاریخ نسبی {issue:yyyy-MM-dd}",
                CreatedAtUtc = issue,
                CreatedByUserId = userId
            };

            doc.Lines.Add(new SalesDocumentLine
            {
                TenantId = tenantId,
                ProductId = productIds.Count > 0 ? productIds[i % productIds.Count] : null,
                Title = Products[i % Products.Length],
                Quantity = qty,
                UnitPrice = price,
                TaxPercent = 9,
                LineTotal = lineTotal,
                SortOrder = 1,
                CreatedAtUtc = issue,
                CreatedByUserId = userId
            });

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
            var qty = 2 + (i % 8);
            var cost = (i % 10 + 1) * 180_000m;
            var total = qty * cost;

            var po = new PurchaseOrder
            {
                TenantId = tenantId,
                Number = maxNumber,
                VendorId = vendors[i % vendors.Count],
                Status = (PurchaseOrderStatus)(i % 4),
                IssueDateUtc = issue,
                ReceivedAtUtc = i % 4 == 2 ? issue.AddDays(3) : null,
                Total = total,
                Note = $"سفارش خرید نمونه — {issue:yyyy-MM-dd}",
                CreatedAtUtc = issue,
                CreatedByUserId = userId
            };
            po.Lines.Add(new PurchaseOrderLine
            {
                TenantId = tenantId,
                ProductId = productIds.Count > 0 ? productIds[i % productIds.Count] : null,
                Title = Products[i % Products.Length],
                Quantity = qty,
                UnitCost = cost,
                LineTotal = total,
                SortOrder = 1,
                CreatedAtUtc = issue,
                CreatedByUserId = userId
            });
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

    private static string PersonName(int i) =>
        $"{FirstNames[i % FirstNames.Length]} {LastNames[(i * 3) % LastNames.Length]}";

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
