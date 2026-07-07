using ElementorBuilder.Extensions;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Security;
using Crm.Infrastructure.Services;
using Crm.Web.Data;
using ElementorBuilder.Abstractions;
using Crm.Web.Middleware;
using Crm.Web.Services;
using Crm.Web.Validation;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// ---------- سایت عمومی و ادمین محتوا ----------
builder.Services.Configure<AdminSettings>(builder.Configuration.GetSection(AdminSettings.SectionName));
builder.Services.AddScoped<AdminAuthService>();
builder.Services.AddScoped<PlatformAdminService>();
builder.Services.AddScoped<MediaUploadService>();
builder.Services.AddScoped<ContentMediaService>();
builder.Services.AddScoped<IElementorMediaUsageChecker, SiteElementorMediaUsageChecker>();
builder.Services.AddScoped<ContentTaxonomyService>();

builder.Services.AddDbContext<SiteDbContext>(options => options.UseNpgsql(connectionString));

// ---------- هسته CRM ----------
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

builder.Services.AddDbContext<CrmDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Crm.Infrastructure")));

builder.Services.AddMemoryCache();
builder.Services.AddScoped<MetadataService>();
builder.Services.AddScoped<RecordAccessService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<DynamicRecordService>();
builder.Services.AddScoped<RecordImportExportService>();
builder.Services.AddScoped<TenantProvisioningService>();
builder.Services.AddScoped<TenantLifecycleService>();
builder.Services.AddScoped<TenantQuotaService>();
builder.Services.AddScoped<SalesModuleSeeder>();
builder.Services.AddScoped<LeadConversionService>();
builder.Services.AddScoped<FinanceService>();
builder.Services.AddScoped<WorkflowEngine>();
builder.Services.AddScoped<SupportService>();
builder.Services.AddScoped<PurchasingService>();
builder.Services.AddScoped<TenantIntegrationService>();
builder.Services.AddScoped<AccountingPushJob>();
builder.Services.AddScoped<Crm.Core.Abstractions.IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<Crm.Core.Abstractions.ISmsSender, HttpSmsSender>();
builder.Services.AddScoped<Crm.Core.Abstractions.IMessengerSender, BaleMessengerSender>();
builder.Services.AddScoped<Crm.Core.Abstractions.IAccountingGateway, QueuedAccountingGateway>();
builder.Services.AddScoped<Crm.Core.Abstractions.IPaymentGateway, SandboxPaymentGateway>();
builder.Services.AddHttpClient();

builder.Services.AddIdentityCore<CrmUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<CrmDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<CrmUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

// ---------- احراز هویت: کوکی جدا برای ادمین سایت و کاربران CRM ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Admin/Account/Login";
        options.AccessDeniedPath = "/Admin/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    })
    .AddCookie("Portal", options =>
    {
        options.Cookie.Name = "Crm.Portal";
        options.LoginPath = "/Portal/Account/Login";
        options.AccessDeniedPath = "/Portal/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    })
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "Crm.App";
    options.LoginPath = "/App/Account/Login";
    options.AccessDeniedPath = "/App/Account/Login";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
});

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// ---------- Hangfire (جاب‌های پس‌زمینه) ----------
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

// ---------- SignalR (اعلان لحظه‌ای) ----------
builder.Services.AddSignalR();

// ---------- OpenAPI برای REST API عمومی ----------
builder.Services.AddOpenApi();

builder.Services.AddControllersWithViews()
    .AddPersianValidation()
    .AddElementorBuilder(options =>
    {
        options.UploadFolder = "uploads/site";
        options.ContentFieldId = "Content";
        options.FontAwesomePath = "/lib/fontawesome/6.4.0/css/all.min.css";
        options.ContentCssPath = "/css/taben-elementor-content.css";
        options.MaxUploadSizeMb = 20;
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DbInitializer.InitializeAsync(scope.ServiceProvider);
    var crmDb = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
    await crmDb.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<TenantLifecycleService>().SeedDefaultPlansAsync();
}

// جاب روزانه: انقضای تریال/اشتراک و قطع دسترسی خودکار
app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<TenantLifecycleService>(
    "tenant-expiration-check",
    service => service.CheckExpirationsAsync(),
    Cron.Daily(3));

// جاب روزانه: یادآوری اقساط سررسیدشده
app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<FinanceService>(
    "installment-reminders",
    service => service.RemindDueInstallmentsAsync(),
    Cron.Daily(4));

// جاب ساعتی: قوانین گردش‌کار زمان‌بندی‌شده
app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<WorkflowEngine>(
    "scheduled-workflows",
    engine => engine.RunScheduledRulesAsync(),
    Cron.Hourly());

// جاب ساعتی: بررسی نقض SLA تیکت‌ها و Escalation
app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<SupportService>(
    "ticket-sla-check",
    service => service.CheckSlaBreachesAsync(),
    Cron.Hourly(30));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();

app.MapHub<Crm.Web.Hubs.NotificationHub>("/hubs/notifications");
app.MapOpenApi();

app.MapControllerRoute(
    name: "articles-detail",
    pattern: "articles/{slug}",
    defaults: new { controller = "Articles", action = "Detail" });

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
