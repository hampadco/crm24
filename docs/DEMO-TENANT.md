# مشتری دمو (Demo Tenant) و داشبورد

مستند کارهای مربوط به tenant نمونه برای دمو به مشتری، دادهٔ پر، نمودارها و تقویم شمسی.

## ساخت / بازسازی دمو

از پنل ادمین:

1. ورود به `/Admin`
2. منو **مشتریان** (`/Admin/Tenants`)
3. دکمه **ساخت مشتری دمو** (یا **تکمیل / بازسازی دمو** اگر از قبل وجود دارد)

درخواست به `POST /Admin/Tenants/CreateDemo` می‌رود و `DemoTenantSeeder.CreateOrRefreshAsync()` اجرا می‌شود. روی صفحه یک overlay لودینگ نشان داده می‌شود تا کار تمام شود.

> دمو دیگر هنگام استارت اپ به‌صورت خودکار ساخته نمی‌شود؛ فقط از همین دکمه.

### ورود به پنل CRM دمو

| مورد | مقدار |
|------|--------|
| آدرس | `/App/Account/Login` |
| ایمیل | `demo@bamacrm.local` |
| رمز | `Demo@1405` |
| اسلاگ tenant | `demo` |

## چه داده‌ای ساخته می‌شود؟

فایل اصلی: `src/Crm.Infrastructure/Services/DemoTenantSeeder.cs`

- حدود **۲۰۰ رکورد** در هر ماژول/صفحهٔ مهم (سرنخ، مخاطب، فرصت، وظیفه، رویداد، فاکتور، …)
- توزیع **وزن‌دار** picklistها (`WeightedPick`) تا نمودارهای دایره‌ای تقریباً یکسان نباشند
- تاریخ ساخت با **منحنی رشد** (`GrowthCreatedAt`) برای نمودار ماهانه
- در صورت بازسازی، `ReshapeDemoDistributionsAsync` توزیع‌های قبلی را هم اصلاح می‌کند
- حدود **۸ ویجت** داشبورد (شمارنده + pie/bar + ماهانه) — نه ده‌ها ویجت سنگین
- در صورت بازسازی، `ReshapeDemoDistributionsAsync` توزیع‌های قبلی را هم اصلاح می‌کند
- قوانین پورسانت، کاربران، و دادهٔ مالی نمونه هم پر می‌شود

> اگر داشبورد دمو هنوز کند است، یک‌بار از ادمین «تکمیل / بازسازی دمو» بزنید تا ویجت‌های قدیمی (~۲۰ تایی) با مجموعهٔ سبک‌تر جایگزین شوند.

## داشبورد و نمودارها

برای ادمین tenant دمو، ویجت‌های داشبورد seed می‌شوند:

- شمارنده‌ها (counter)
- نمودار دایره‌ای (pie)
- نمودار میله‌ای (bar)
- نمودار ماهانه (monthly)

کنترلر: `Areas/App/Controllers/DashboardController.cs`  
ویو: `Areas/App/Views/Dashboard/Index.cshtml` (ApexCharts)

## تقویم شمسی (`/App/calendar`)

- نمای **ماه**: گرید هفتگی ۷ستونه (`duration: { weeks: 6 }`) با لنگر ماه جلالی — روزهای خارج از ماه جاری کم‌رنگ
- ناوبری prev/next/امروز روی ماه شمسی جابه‌جا می‌شود
- نمای **هفته**: `timeGridWeek` با عنوان بازه شمسی
- نمای **لیست**: فقط همان ماه شمسی + هدر روز شمسی
- رویدادها و وظایف از `/App/calendar/feed`
- متن رویدادهای رنگی سفید (`app-calendar.css`)

## مشارکت در فروش (`/App/commissions`)

- جدول قوانین پورسانت با **صفحه‌بندی** (۲۰ تایی)، شماره ردیف، جستجو، و وضعیت خالی
- ستون گزارش کارشناسان در کنار جدول قوانین

## فایل‌های مرتبط

| موضوع | مسیر |
|--------|------|
| Seeder دمو | `src/Crm.Infrastructure/Services/DemoTenantSeeder.cs` |
| دکمه ادمین | `Areas/Admin/Controllers/TenantsController.cs`، `Areas/Admin/Views/Tenants/Index.cshtml` |
| داشبورد | `Areas/App/Controllers/DashboardController.cs` |
| تقویم | `Areas/App/Views/Calendar/Index.cshtml`، `wwwroot/css/app-calendar.css` |
| پورسانت | `Areas/App/Controllers/CommissionsController.cs` |
