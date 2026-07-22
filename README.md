# BamaCRM — پلتفرم مدیریت ارتباط با مشتری

پلتفرم SaaS چند-مستاجری (Multi-Tenant) برای مدیریت فروش، پشتیبانی و ارتباط با مشتری — با ‎.NET 10 MVC.

## ساختار سالوشن

| پروژه | نقش |
|---|---|
| `src/Crm.Web` | وب‌اپ اصلی: سایت عمومی (لندینگ، وبلاگ، FAQ) + پنل‌ها در قالب Area |
| `src/Crm.Core` | دامنه و سرویس‌های هسته (متادیتا، RBAC، Workflow — از پلن ۲) |
| `src/Crm.Infrastructure` | EF Core + Npgsql و یکپارچگی‌ها |
| `src/ElementorBuilder` | صفحه‌ساز drag & drop (کتابخانه آماده) |

### Area های `Crm.Web`

- **Root** — سایت عمومی (پورت‌شده از TabenLife، بدون فروشگاه)
- **`Areas/Admin`** — ادمین محتوای سایت (وبلاگ/صفحات/FAQ) — موقت؛ در پلن ۳ زیر پنل Owner می‌رود
- **`Areas/Owner`** — پنل مالک مجموعه (مدیریت Tenant ها، پلن‌ها، اشتراک)
- **`Areas/App`** — پنل CRM هر مشتری (Tenant)
- **`Areas/Portal`** — پورتال مشتریان نهایی هر Tenant

قالب پنل‌ها: **Frest RTL** — assets در `wwwroot/panel-assets`، Layout ها: `Views/Shared/_PanelLayout.cshtml` و `_AuthLayout.cshtml`.

## اجرا (Development)

پیش‌نیازها: ‎.NET 10 SDK، Docker (برای PostgreSQL)

```bash
# 1) دیتابیس
docker compose up -d

# 2) اجرای وب‌اپ
dotnet run --project src/Crm.Web
```

- سایت عمومی: `/`
- ادمین محتوای سایت: `/Admin` (کاربر پیش‌فرض در `appsettings.json` بخش `Admin`)
- پنل مالک: `/Owner` — پنل CRM: `/App` — پورتال مشتری: `/Portal`

دیتابیس: PostgreSQL 16 (`bamacrm` / `bamacrm` / `bamacrm_dev` روی پورت 5432) — اسکیما و داده اولیه به‌صورت خودکار هنگام اجرا ساخته می‌شود.

### Tenant نمونه (دمو)

فقط در **Development** از پنل ادمین ← **مشتریان** ← دکمه «ساخت مشتری دمو» (در Production نمایش داده نمی‌شود):

- ورود پنل CRM: `/App/Account/Login`
- ایمیل: `demo@bamacrm.local`
- رمز: `Demo@1405`

جزئیات کامل (دادهٔ دمو، نمودارها، تقویم شمسی، پورسانت، بهینه‌سازی لود): [`docs/DEMO-TENANT.md`](docs/DEMO-TENANT.md)

## استقرار (Production — Cloud یا On-Premise)

```bash
# متغیر رمز دیتابیس را ست کنید (فایل .env یا محیط)
echo "POSTGRES_PASSWORD=a-strong-password" > .env

docker compose -f docker-compose.deploy.yml up -d --build
```

- وب‌اپ روی پورت `8080` — دیتابیس و آپلودها در ولوم Docker
- **Backup خودکار:** سرویس `backup` هر ۲۴ ساعت `pg_dump` می‌گیرد (۱۴ نسخه آخر در ولوم `bamacrm_backups`)
- Migration ها هنگام بالا آمدن وب‌اپ خودکار اجرا می‌شوند

## REST API عمومی

- Base URL: `/api/v1` — احراز با هدر `X-Api-Key` (ساخت کلید: پنل CRM ← «یکپارچگی‌ها و API»)
- الگوی CRUD یکسان برای همه ماژول‌ها: `GET|POST /api/v1/{module}`، `GET|PUT|DELETE /api/v1/{module}/{id}`
- لیست ماژول‌ها: `GET /api/v1/modules` — مستندات OpenAPI: `/openapi/v1.json`
- Webhook تماس ورودی سانترال: `POST /api/v1/voip/incoming` با بدنه `{ "caller": "0912...", "called": "..." }`
- Rate limit: ۱۲۰ درخواست در دقیقه به‌ازای هر کلید

## تست‌های E2E

اسکریپت‌های دود (نیاز به اپ در حال اجرا روی `http://localhost:5000`):

```powershell
./tools/smoke-e2e.ps1           # هسته و متادیتا
./tools/smoke-owner.ps1         # پنل مالک و اشتراک
./tools/smoke-sales.ps1         # فروش پایه
./tools/smoke-finance.ps1       # چرخه مالی
./tools/smoke-workflow.ps1      # گردش‌کار و گزارش
./tools/smoke-support.ps1       # تیکتینگ و پورتال
./tools/smoke-plan8.ps1         # پروژه/خرید/بازاریابی
./tools/smoke-integrations.ps1  # API عمومی، درگاه پرداخت، VoIP
```

## نقشه راه

اجرا در ۹ پلن (سند مرجع: پلن «نقشه راه کامل ساخت پلتفرم CRM»):

1. ✅ اسکلت سالوشن + سایت عمومی + پوسته پنل‌ها
2. ✅ هسته CRM: Multi-Tenancy، موتور متادیتا، RBAC، Identity
3. ✅ پنل مالک و سیستم اشتراک
4. ✅ فروش پایه (سرنخ، مخاطب، فرصت، کاریز)
5. ✅ محصول، انبار و چرخه مالی
6. ✅ موتور گردش‌کار + داشبورد و گزارش‌ساز
7. ✅ پشتیبانی و پورتال مشتریان نهایی
8. ✅ پروژه، تأمین و خرید، بازاریابی
9. ✅ یکپارچگی‌ها، API عمومی و استقرار
