using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Crm.Infrastructure.Services;

/// <summary>
/// ماژول‌های سیستمی فروش (پلن ۴) را برای یک Tenant می‌سازد — همه metadata-driven:
/// سرنخ، مخاطب، سازمان، فرصت فروش، وظیفه، رویداد و تماس تلفنی.
/// Idempotent است: ماژول موجود دوباره ساخته نمی‌شود.
/// </summary>
public class SalesModuleSeeder
{
    private readonly CrmDbContext _db;
    private readonly IMemoryCache _cache;

    public SalesModuleSeeder(CrmDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task SeedAsync(int tenantId, int adminProfileId, int userProfileId)
    {
        var sortOrder = 0;

        await EnsureModuleAsync(tenantId, adminProfileId, userProfileId,
            "leads", "سرنخ", "سرنخ‌ها", "bx-user-plus", ++sortOrder,
            [
                F("name", "نام و نام خانوادگی", FieldType.Text, required: true),
                F("company", "شرکت", FieldType.Text),
                F("phone", "تلفن", FieldType.Phone, unique: true),
                F("email", "ایمیل", FieldType.Email),
                F("city", "شهر", FieldType.Text),
                F("status", "وضعیت", FieldType.Picklist, defaultValue: "cold",
                    picklist: [P("cold", "سرد", "#69809a"), P("warm", "گرم", "#fdac41"), P("hot", "مشتاق", "#ff5b5c"), P("qualified", "واجد شرایط", "#39da8a"), P("junk", "بی‌ارزش", "#495563")]),
                F("source", "منبع آشنایی", FieldType.Picklist,
                    picklist: [P("website", "وب‌سایت"), P("referral", "معرفی"), P("ads", "تبلیغات"), P("social", "شبکه اجتماعی"), P("exhibition", "نمایشگاه"), P("call", "تماس ورودی")]),
                F("doNotMessage", "عدم ارسال پیام", FieldType.Checkbox, showInList: false),
                F("description", "توضیحات", FieldType.MultilineText, showInList: false)
            ]);

        await EnsureModuleAsync(tenantId, adminProfileId, userProfileId,
            "organizations", "سازمان", "سازمان‌ها", "bx-buildings", ++sortOrder,
            [
                F("name", "نام سازمان", FieldType.Text, required: true, unique: true),
                F("phone", "تلفن", FieldType.Phone),
                F("website", "وب‌سایت", FieldType.Url, showInList: false),
                F("economicCode", "کد اقتصادی", FieldType.Text, showInList: false),
                F("nationalId", "شناسه ملی", FieldType.Text, showInList: false),
                F("industry", "صنعت", FieldType.Picklist,
                    picklist: [P("tech", "فناوری اطلاعات"), P("manufacturing", "تولیدی"), P("trade", "بازرگانی"), P("services", "خدماتی"), P("construction", "ساختمان"), P("health", "سلامت"), P("other", "سایر")]),
                F("city", "شهر", FieldType.Text),
                F("address", "نشانی", FieldType.MultilineText, showInList: false),
                F("description", "توضیحات", FieldType.MultilineText, showInList: false)
            ]);

        await EnsureModuleAsync(tenantId, adminProfileId, userProfileId,
            "contacts", "مخاطب", "مخاطبین", "bx-user", ++sortOrder,
            [
                F("name", "نام و نام خانوادگی", FieldType.Text, required: true),
                F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
                F("position", "سمت", FieldType.Text),
                F("mobile", "موبایل", FieldType.Phone, unique: true),
                F("phone", "تلفن ثابت", FieldType.Phone, showInList: false),
                F("email", "ایمیل", FieldType.Email),
                F("address", "نشانی", FieldType.MultilineText, showInList: false),
                F("description", "توضیحات", FieldType.MultilineText, showInList: false)
            ]);

        await EnsureModuleAsync(tenantId, adminProfileId, userProfileId,
            "opportunities", "فرصت فروش", "فرصت‌های فروش", "bx-target-lock", ++sortOrder,
            [
                F("name", "عنوان فرصت", FieldType.Text, required: true),
                F("contact", "مخاطب", FieldType.Lookup, lookupModule: "contacts"),
                F("organization", "سازمان", FieldType.Lookup, lookupModule: "organizations"),
                F("amount", "مبلغ (تومان)", FieldType.Currency),
                F("probability", "درصد موفقیت", FieldType.Number, showInList: false),
                F("stage", "مرحله", FieldType.Picklist, defaultValue: "new",
                    picklist: [P("new", "جدید", "#696cff"), P("qualified", "ارزیابی‌شده", "#03c3ec"), P("proposal", "ارسال پیشنهاد", "#fdac41"), P("negotiation", "مذاکره", "#ff5b5c"), P("won", "برنده", "#39da8a"), P("lost", "بازنده", "#495563")]),
                F("expectedCloseDate", "تاریخ پیش‌بینی بستن", FieldType.Date),
                F("description", "توضیحات", FieldType.MultilineText, showInList: false)
            ]);

        await EnsureModuleAsync(tenantId, adminProfileId, userProfileId,
            "tasks", "وظیفه", "وظایف", "bx-task", ++sortOrder,
            [
                F("name", "عنوان وظیفه", FieldType.Text, required: true),
                F("dueDate", "سررسید", FieldType.DateTime, required: true),
                F("priority", "اولویت", FieldType.Picklist, defaultValue: "normal",
                    picklist: [P("low", "کم", "#69809a"), P("normal", "عادی", "#03c3ec"), P("high", "زیاد", "#fdac41"), P("urgent", "فوری", "#ff5b5c")]),
                F("status", "وضعیت", FieldType.Picklist, defaultValue: "todo",
                    picklist: [P("todo", "در انتظار", "#69809a"), P("inprogress", "در حال انجام", "#fdac41"), P("done", "انجام شد", "#39da8a"), P("canceled", "لغو شد", "#495563")]),
                F("description", "توضیحات", FieldType.MultilineText, showInList: false)
            ]);

        await EnsureModuleAsync(tenantId, adminProfileId, userProfileId,
            "events", "رویداد", "رویدادها", "bx-calendar-event", ++sortOrder,
            [
                F("name", "عنوان رویداد", FieldType.Text, required: true),
                F("startAt", "شروع", FieldType.DateTime, required: true),
                F("endAt", "پایان", FieldType.DateTime),
                F("location", "مکان", FieldType.Text),
                F("type", "نوع", FieldType.Picklist, defaultValue: "meeting",
                    picklist: [P("meeting", "جلسه", "#696cff"), P("visit", "بازدید", "#03c3ec"), P("demo", "دمو محصول", "#fdac41"), P("other", "سایر", "#69809a")]),
                F("description", "توضیحات", FieldType.MultilineText, showInList: false)
            ]);

        await EnsureModuleAsync(tenantId, adminProfileId, userProfileId,
            "calls", "تماس", "تماس‌های تلفنی", "bx-phone-call", ++sortOrder,
            [
                F("name", "موضوع تماس", FieldType.Text, required: true),
                F("contact", "مخاطب", FieldType.Lookup, lookupModule: "contacts"),
                F("direction", "نوع تماس", FieldType.Picklist, defaultValue: "outgoing",
                    picklist: [P("incoming", "ورودی", "#03c3ec"), P("outgoing", "خروجی", "#696cff")]),
                F("callAt", "زمان تماس", FieldType.DateTime, required: true),
                F("durationMinutes", "مدت (دقیقه)", FieldType.Number, showInList: false),
                F("result", "نتیجه", FieldType.Picklist,
                    picklist: [P("answered", "پاسخ داد", "#39da8a"), P("noanswer", "پاسخ نداد", "#fdac41"), P("busy", "مشغول", "#ff5b5c"), P("followup", "نیاز به پیگیری", "#03c3ec")]),
                F("description", "توضیحات", FieldType.MultilineText, showInList: false)
            ]);
    }

    /// <summary>برای Tenant های موجود که همه ماژول‌های فروش را ندارند (ارتقا).</summary>
    public async Task EnsureSeededAsync(int tenantId)
    {
        var cacheKey = $"sales-modules-ok:{tenantId}";
        if (_cache.TryGetValue(cacheKey, out bool ok) && ok)
            return;

        var hasAll = await _db.Modules.CountAsync(m =>
            m.TenantId == tenantId &&
            new[] { "leads", "organizations", "contacts", "opportunities", "tasks", "events", "calls" }.Contains(m.Name)) == 7;
        if (hasAll)
        {
            _cache.Set(cacheKey, true, TimeSpan.FromHours(6));
            return;
        }

        var adminProfile = await _db.Profiles.Where(p => p.TenantId == tenantId && p.IsAdmin).Select(p => p.Id).FirstOrDefaultAsync();
        var userProfile = await _db.Profiles.Where(p => p.TenantId == tenantId && !p.IsAdmin).Select(p => p.Id).FirstOrDefaultAsync();
        await SeedAsync(tenantId, adminProfile, userProfile);
        _cache.Remove($"modules:{tenantId}");
        _cache.Set(cacheKey, true, TimeSpan.FromHours(6));
    }

    private async Task EnsureModuleAsync(
        int tenantId, int adminProfileId, int userProfileId,
        string name, string singular, string plural, string icon, int sortOrder, FieldSpec[] specs)
    {
        if (await _db.Modules.AnyAsync(m => m.TenantId == tenantId && m.Name == name))
            return;

        var module = new ModuleDef
        {
            TenantId = tenantId,
            Name = name,
            SingularLabel = singular,
            PluralLabel = plural,
            Icon = icon,
            IsSystem = true,
            SortOrder = sortOrder
        };
        _db.Modules.Add(module);
        await _db.SaveChangesAsync();

        var order = 0;
        var fields = specs.Select(s => new FieldDef
        {
            TenantId = tenantId,
            ModuleId = module.Id,
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
                    SortOrder = ++pOrder
                });
            }
        }

        _db.SharingRules.Add(new SharingRule { TenantId = tenantId, ModuleId = module.Id, DefaultLevel = SharingLevel.Private });
        _db.ProfileModulePermissions.AddRange(
            new ProfileModulePermission { TenantId = tenantId, ProfileId = adminProfileId, ModuleId = module.Id, CanView = true, CanCreate = true, CanEdit = true, CanDelete = true },
            new ProfileModulePermission { TenantId = tenantId, ProfileId = userProfileId, ModuleId = module.Id, CanView = true, CanCreate = true, CanEdit = true, CanDelete = false });

        await _db.SaveChangesAsync();
    }

    private record FieldSpec(
        string Name, string Label, FieldType Type, bool Required, bool Unique, bool ShowInList,
        string? DefaultValue, string? LookupModule, (string Value, string Label, string? Color)[] Picklist);

    private static FieldSpec F(
        string name, string label, FieldType type,
        bool required = false, bool unique = false, bool showInList = true,
        string? defaultValue = null, string? lookupModule = null,
        (string Value, string Label, string? Color)[]? picklist = null) =>
        new(name, label, type, required, unique, showInList, defaultValue, lookupModule, picklist ?? []);

    private static (string, string, string?) P(string value, string label, string? color = null) => (value, label, color);
}
