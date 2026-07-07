# ElementorBuilder

ماژول drag-and-drop page builder (الهام‌گرفته از Elementor) برای ASP.NET Core MVC.

## نصب

### Project Reference

```bash
dotnet add reference ../ElementorBuilder/ElementorBuilder.csproj
```

### NuGet (پس از pack)

```bash
dotnet pack -c Release
dotnet add package ElementorBuilder --source ./nupkg
```

## راه‌اندازی

در `Program.cs`:

```csharp
using ElementorBuilder.Extensions;

builder.Services.AddControllersWithViews()
    .AddElementorBuilder(options =>
    {
        options.UploadFolder = "uploads/my-content";
        options.ContentFieldId = "Content";
    });
```

## استفاده — ادیتور

```cshtml
@using ElementorBuilder.Models

<form method="post" id="myForm">
    <input asp-for="Content" type="hidden" id="Content" />

    @await Component.InvokeAsync("ElementorEditor", new ElementorEditorViewModel
    {
        Content = Model.Content,
        CancelUrl = "/Admin/Posts",
        ExtraToolbarButtons = new List<ToolbarButton>
        {
            new() { Label = "ذخیره", Icon = "fas fa-save", OnClick = "submitForm()", Primary = true }
        }
    })
</form>
```

## استفاده — رندر عمومی

```cshtml
@await Component.InvokeAsync("ElementorContent", new { html = Model.Content })
```

## API رسانه

```
POST /Elementor/Media/Upload
Content-Type: multipart/form-data
Body: file=<image-or-audio>

Response: { "success": true, "url": "/uploads/elementor/abc.jpg" }
```

```
POST /Elementor/Media/Delete
Body: url=/uploads/elementor/abc.jpg

Response: { "success": true, "message": "فایل حذف شد." }
```

حذف فقط برای فایل‌های داخل `UploadFolder` (و پوشه‌های اضافی) انجام می‌شود. قبل از حذف، `IElementorMediaUsageChecker` بررسی می‌کند URL جای دیگری استفاده نشده باشد.

### پاک‌سازی هنگام ذخیره (پروژه میزبان)

```csharp
builder.Services.AddElementorBuilder(options => { options.UploadFolder = "uploads/my-content"; });
builder.Services.AddScoped<IElementorMediaUsageChecker, MyMediaUsageChecker>();
```

## پیکربندی

| Option | پیش‌فرض | توضیح |
|--------|---------|-------|
| `ContentFieldId` | `Content` | id فیلد hidden برای HTML |
| `UploadUrl` | `/Elementor/Media/Upload` | endpoint آپلود |
| `DeleteUrl` | `/Elementor/Media/Delete` | endpoint حذف |
| `UploadFolder` | `uploads/elementor` | پوشه ذخیره در wwwroot |
| `AdditionalManagedUploadFolders` | `[]` | پوشه‌های legacy قابل حذف |
| `DraftStorageKey` | `elementor-content-draft` | کلید localStorage |
| `CssPath` | `/_content/ElementorBuilder/css/elementor-builder.css` | مسیر CSS |
| `MaxUploadSizeMb` | `5` | حداکثر حجم آپلود |

## وابستگی‌ها

- ASP.NET Core 10+
- Font Awesome 6 (CDN — در ViewComponent لود می‌شود)

## ویجت‌های موجود

عنوان، متن، تصویر، ویدیو، دکمه، جداکننده، فاصله، آیکون، لیست، نقل‌قول، HTML، هشدار

## JavaScript API

```javascript
elementorBuilder.save();           // sync به hidden field
elementorBuilder.getContent();     // HTML خروجی
elementorBuilder.setContent(html); // بارگذاری HTML
```

Config سفارشی:

```javascript
window.ElementorBuilderConfig = {
    contentFieldId: 'Body',
    uploadUrl: '/Elementor/Media/Upload',
    draftKey: 'my-app-draft',
    cssPath: '/_content/ElementorBuilder/css/elementor-builder.css'
};
```
