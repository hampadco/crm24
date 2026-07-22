# مشتری دمو، داشبورد، تقویم و بهینه‌سازی‌ها

مستند کارهای انجام‌شده برای دمو به مشتری، دادهٔ پر، نمودارها، تقویم شمسی، پورسانت و بهینه‌سازی لود صفحات App.

## ساخت / بازسازی دمو

> **فقط در Development:** دکمهٔ ساخت/بازسازی و اکشن `CreateDemo` تنها وقتی `ASPNETCORE_ENVIRONMENT=Development` باشد نمایش و اجرا می‌شوند. در Staging/Production دکمه دیده نمی‌شود و POST هم `404` برمی‌گرداند.

از پنل ادمین (حالت توسعه):

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
- قوانین پورسانت، کاربران، و دادهٔ مالی نمونه هم پر می‌شود

> اگر داشبورد دمو هنوز کند است یا ویجت‌های قدیمی (~۲۰ تایی) مانده، یک‌بار از ادمین «تکمیل / بازسازی دمو» بزنید.

## داشبورد و نمودارها (`/App`)

ویجت‌های seedشده برای ادمین tenant دمو:

- شمارنده‌ها (counter)
- نمودار دایره‌ای (pie)
- نمودار میله‌ای (bar)
- نمودار ماهانه (monthly)

**کنترلر:** `Areas/App/Controllers/DashboardController.cs`  
**ویو:** `Areas/App/Views/Dashboard/Index.cshtml` (ApexCharts فقط در همین صفحه لود می‌شود)

### بهینه‌سازی لود داشبورد

| قبل | بعد |
|-----|-----|
| هر ویجت pie/bar تا ۵۰۰۰ رکورد را به حافظه می‌کشید و JSON را در C# گروه‌بندی می‌کرد | تجمیع با SQL روی `CustomData` jsonb (`DynamicRecordService.AggregateFieldAsync`) |
| `CountAsync` جدا برای هر ماژول | یک `GroupBy(ModuleId)` برای همهٔ شمارش‌ها؛ counterها از همان دیکشنری |
| `EnsureSeededAsync` در هر بازدید داشبورد | نتیجه در `IMemoryCache` حدود ۶ ساعت |
| لیست ماژول‌ها بدون کش (منو + داشبورد دوباره می‌زد) | `MetadataService.GetActiveModulesAsync` کش ۵ دقیقه‌ای |
| ~۲۰ ویجت دمو | ۸ ویجت سبک |

## تقویم شمسی (`/App/calendar`)

### رفتار نماها

- **ماه:** گرید هفتگی ۷ستونه (`duration: { weeks: 6 }` + `dateAlignment: 'week'`) با لنگر اول ماه جلالی؛ روزهای خارج از ماه جاری کم‌رنگ
- جلوگیری از سر خوردن پنجره روی «امروز» وسط ماه (اگر `info.start` بعد از اول ماه باشد، به `jalaliAnchor` برمی‌گردد)
- **هفته:** `timeGridWeek` با عنوان بازه شمسی
- **لیست:** فقط همان ماه شمسی + هدر روز شمسی
- خوراک: `GET /App/calendar/feed` — فیلتر بازهٔ تاریخ در SQL روی jsonb (`ListByJsonDateRangeAsync`) به‌جای لود ۵۰۰+۵۰۰ رکورد و فیلتر در حافظه

### ظاهر و کنتراست

- رنگ‌های پس‌زمینهٔ **تیره/اشباع** برای وظیفه و رویداد (نه خاکستری روشن)
- متن رویداد **سفید** روی بلوک رنگی؛ در popover هم همان رنگ‌ها اجباری می‌شوند
- کلاس‌های fallback: `crm-cal-prio-*` و `crm-cal-type-*` در `wwwroot/css/app-calendar.css`
- `eventDisplay: 'block'` تا رویداد بدون پس‌زمینه نماند

**فایل‌ها:** `Areas/App/Views/Calendar/Index.cshtml`، `CalendarController.cs`، `wwwroot/js/panel-jalali.js`، `wwwroot/css/app-calendar.css`

## مشارکت در فروش (`/App/commissions`)

- جدول قوانین با **صفحه‌بندی ۲۰تایی**، شماره ردیف، جستجو، وضعیت خالی
- گزارش کارشناسان در کنار جدول
- تجمیع گزارش با SQL `GroupBy(UserId)` (دیگر همهٔ `CommissionEntries` به حافظه نمی‌آید)

## بهینه‌سازی سراسری پنل App

| مورد | تغییر |
|------|--------|
| ApexCharts | از `_PanelLayout` برداشته شد؛ فقط در داشبورد لود می‌شود |
| SignalR | لود تأخیری با `requestIdleCallback` / `setTimeout` تا TTFB صفحه را بند نکند |
| `ListAsync` | پارامتر اختیاری `includeTotal` برای وقتی شمارش لازم نیست |

## کارهای بعدی پیشنهادی (هنوز انجام نشده)

1. Kanban — کاهش/فیلتر SQL به‌جای Take(۵۰۰)
2. گزارش‌ها — حذف `Take(۵۰۰۰)` و تجمیع در SQL
3. فرم رکورد — lookup سبک‌تر یا typeahead به‌جای ۵۰۰ گزینه در هر فیلد
4. Records Index — یک کوئری permission به‌جای چهار کوئری جدا

## فایل‌های مرتبط

| موضوع | مسیر |
|--------|------|
| Seeder دمو | `src/Crm.Infrastructure/Services/DemoTenantSeeder.cs` |
| دکمه ادمین | `Areas/Admin/Controllers/TenantsController.cs`، `Views/Tenants/Index.cshtml` |
| داشبورد | `Areas/App/Controllers/DashboardController.cs`، `Views/Dashboard/Index.cshtml` |
| تجمیع jsonb / بازه تاریخ | `src/Crm.Infrastructure/Services/DynamicRecordService.cs` |
| کش ماژول‌ها | `src/Crm.Infrastructure/Services/MetadataService.cs` |
| EnsureSeeded کش‌شده | `src/Crm.Infrastructure/Services/SalesModuleSeeder.cs` |
| تقویم | `Areas/App/Controllers/CalendarController.cs`، `Views/Calendar/Index.cshtml`، `wwwroot/css/app-calendar.css`، `wwwroot/js/panel-jalali.js` |
| پورسانت | `Areas/App/Controllers/CommissionsController.cs` |
| Layout / SignalR / Apex | `Views/Shared/_PanelLayout.cshtml` |
