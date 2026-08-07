using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;
using Crm.Web.Models;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>
/// طراح قالب چاپ در دو مرحله: ۱) تنظیمات صفحه/فونت/واترمارک/فایل ۲) ویرایشگر سرفصل/بدنه/پاورقی.
/// فقط ادمین Tenant.
/// </summary>
public class PrintTemplatesController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly MetadataService _metadata;
    private readonly TemplateRenderer _renderer;

    public PrintTemplatesController(
        CrmDbContext db,
        ITenantContext tenant,
        MetadataService metadata,
        TemplateRenderer renderer)
    {
        _db = db;
        _tenant = tenant;
        _metadata = metadata;
        _renderer = renderer;
    }

    // ── فهرست ────────────────────────────────────────────────────────

    [HttpGet("/App/print-templates")]
    public async Task<IActionResult> Index(int page = 1, int? moduleId = null)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var query = _db.PrintTemplates.AsNoTracking().AsQueryable();
        if (moduleId is int mid && mid > 0)
            query = query.Where(t => t.ModuleId == mid);

        var (templates, total, p, pageSize) = await AppPaging.ToPageAsync(
            query.OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name), page);

        var moduleIds = templates.Select(t => t.ModuleId).Distinct().ToList();
        var modules = await _db.Modules.AsNoTracking()
            .Where(m => moduleIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.PluralLabel);

        ViewBag.ModuleLabels = modules;
        ViewBag.Modules = await _metadata.GetActiveModulesAsync();
        ViewBag.FilterModuleId = moduleId;
        ViewData["Title"] = "قالب‌های چاپ";
        ViewData["PanelTitle"] = "تنظیمات";
        AppPaging.SetViewBag(ViewBag, total, p, pageSize);
        return View(templates);
    }

    // ── مرحله ۱: تنظیمات ─────────────────────────────────────────────

    [HttpGet("/App/print-templates/create")]
    public async Task<IActionResult> Create(int? moduleId = null)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        await FillSettingsLookupsAsync();
        ViewData["Title"] = "قالب چاپ جدید";
        ViewData["PanelTitle"] = "تنظیمات";
        ViewBag.Step = 1;
        return View("Settings", new PrintTemplate
        {
            ModuleId = moduleId ?? 0,
            ShareWithAllRoles = true,
            IsActive = true,
            IsHtmlEditor = false,
            PageSize = "A4",
            TextDirection = "rtl",
            FontFamily = "shabnam",
            FontSize = 12,
            ServiceProvider = "browser"
        });
    }

    [HttpGet("/App/print-templates/{id:int}/settings")]
    public async Task<IActionResult> Settings(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var template = await _db.PrintTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (template is null)
            return NotFound();

        await FillSettingsLookupsAsync();
        ViewBag.SelectedRoleIds = await LoadRoleIdsAsync(id);
        ViewData["Title"] = $"تنظیمات «{template.Name}»";
        ViewData["PanelTitle"] = "تنظیمات";
        ViewBag.Step = 1;
        return View("Settings", template);
    }

    /// <summary>مسیر قدیمی ویرایش — به مرحله تنظیمات هدایت می‌شود.</summary>
    [HttpGet("/App/print-templates/{id:int}/edit")]
    public IActionResult Edit(int id) => RedirectToAction(nameof(Settings), new { id });

    [HttpPost("/App/print-templates/save-settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(PrintTemplateSettingsInput input, int[]? roleIds)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        if (string.IsNullOrWhiteSpace(input.Name) || input.ModuleId <= 0)
        {
            TempData["Error"] = "نام قالب و ماژول الزامی است.";
            return RedirectToAction(nameof(Index));
        }

        if (!await _db.Modules.AnyAsync(m => m.Id == input.ModuleId))
        {
            TempData["Error"] = "ماژول انتخاب‌شده معتبر نیست.";
            return RedirectToAction(nameof(Index));
        }

        PrintTemplate template;
        var isNew = input.Id == 0;
        if (isNew)
        {
            template = new PrintTemplate();
            _db.PrintTemplates.Add(template);
        }
        else
        {
            var existing = await _db.PrintTemplates.FirstOrDefaultAsync(t => t.Id == input.Id);
            if (existing is null)
                return NotFound();
            template = existing;
        }

        ApplySettings(template, input);

        if (isNew)
        {
            var defaults = await DefaultDesignHtmlAsync(input.ModuleId);
            template.HeaderHtml = defaults.Header;
            template.BodyHtml = defaults.Body;
            template.FooterHtml = defaults.Footer;
        }

        await _db.SaveChangesAsync();

        if (template.IsDefault)
        {
            var others = await _db.PrintTemplates
                .Where(t => t.ModuleId == template.ModuleId && t.Id != template.Id && t.IsDefault)
                .ToListAsync();
            foreach (var other in others)
                other.IsDefault = false;
            await _db.SaveChangesAsync();
        }

        await SyncRolesAsync(template, roleIds);

        TempData["Success"] = "تنظیمات قالب ذخیره شد. حالا محتوای قالب را طراحی کنید.";
        return RedirectToAction(nameof(Design), new { id = template.Id });
    }

    // ── مرحله ۲: طراحی محتوا ─────────────────────────────────────────

    [HttpGet("/App/print-templates/{id:int}/design")]
    public async Task<IActionResult> Design(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var template = await _db.PrintTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (template is null)
            return NotFound();

        var modules = await _metadata.GetActiveModulesAsync();
        ViewBag.Module = modules.FirstOrDefault(m => m.Id == template.ModuleId);
        ViewBag.Catalog = await BuildCatalogAsync(template.ModuleId);
        ViewData["Title"] = $"طراحی «{template.Name}»";
        ViewData["PanelTitle"] = "تنظیمات";
        ViewBag.Step = 2;

        // float ذخیره‌شده ویجت‌های جدول را خراب می‌کند؛ برای نمایش پاک می‌کنیم
        template.HeaderHtml = SanitizePrintHtml(template.HeaderHtml);
        template.BodyHtml = SanitizePrintHtml(template.BodyHtml);
        template.FooterHtml = SanitizePrintHtml(template.FooterHtml);
        return View("Design", template);
    }

    [HttpPost("/App/print-templates/{id:int}/save-design")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDesign(
        int id,
        string? headerHtml,
        string? bodyHtml,
        string? footerHtml,
        string? textDirection,
        bool landscape,
        int marginTop,
        int marginRight,
        int marginBottom,
        int marginLeft,
        string? next)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var template = await _db.PrintTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (template is null)
            return NotFound();

        template.HeaderHtml = SanitizePrintHtml(headerHtml);
        template.BodyHtml = SanitizePrintHtml(bodyHtml);
        template.FooterHtml = SanitizePrintHtml(footerHtml);
        template.TextDirection = string.Equals(textDirection, "ltr", StringComparison.OrdinalIgnoreCase)
            ? "ltr" : "rtl";
        template.Landscape = landscape;
        template.MarginTop = Math.Clamp(marginTop, 0, 60);
        template.MarginRight = Math.Clamp(marginRight, 0, 60);
        template.MarginBottom = Math.Clamp(marginBottom, 0, 60);
        template.MarginLeft = Math.Clamp(marginLeft, 0, 60);
        await _db.SaveChangesAsync();

        TempData["Success"] = "قالب چاپ ذخیره شد.";
        return string.Equals(next, "stay", StringComparison.OrdinalIgnoreCase)
            ? RedirectToAction(nameof(Design), new { id })
            : RedirectToAction(nameof(Index), new { moduleId = template.ModuleId });
    }

    /// <summary>بازنشانی محتوای قالب به طرح آمادهٔ پیش‌فرض ماژول.</summary>
    [HttpPost("/App/print-templates/{id:int}/reset-design")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetDesign(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var template = await _db.PrintTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (template is null)
            return NotFound();

        var defaults = await DefaultDesignHtmlAsync(template.ModuleId);
        template.HeaderHtml = defaults.Header;
        template.BodyHtml = defaults.Body;
        template.FooterHtml = defaults.Footer;
        await _db.SaveChangesAsync();

        TempData["Success"] = "قالب به طرح آماده بازنشانی شد.";
        return RedirectToAction(nameof(Design), new { id });
    }

    /// <summary>پیش‌نمایش قالب با آخرین رکورد واقعی ماژول (یا نمونه خالی).</summary>
    [HttpGet("/App/print-templates/{id:int}/preview")]
    public async Task<IActionResult> Preview(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var template = await _db.PrintTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (template is null)
            return NotFound();

        var record = await _db.Records.AsNoTracking()
            .Where(r => r.ModuleId == template.ModuleId)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync()
            ?? new DynamicRecord { ModuleId = template.ModuleId, Title = "نمونه" };

        var parts = await _renderer.RenderPartsAsync(template, record);
        var html = TemplateRenderer.WrapPrintShell(parts.Header, parts.Body, parts.Footer, parts.Title, template);
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>کاتالوگ فیلد/تابع/بلاک یک ماژول برای مودال‌های طراح.</summary>
    [HttpGet("/App/print-templates/catalog")]
    public async Task<IActionResult> Catalog(int moduleId)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        return Json(await BuildCatalogAsync(moduleId));
    }

    // ── عملیات ───────────────────────────────────────────────────────

    [HttpPost("/App/print-templates/{id:int}/duplicate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var source = await _db.PrintTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (source is null)
            return NotFound();

        var copy = new PrintTemplate
        {
            ModuleId = source.ModuleId,
            Name = $"{source.Name} (کپی)",
            IsHtmlEditor = source.IsHtmlEditor,
            IsActive = source.IsActive,
            IsDefault = false,
            ServiceProvider = source.ServiceProvider,
            PageSize = source.PageSize,
            Landscape = source.Landscape,
            TextDirection = source.TextDirection,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            CustomCss = source.CustomCss,
            MarginTop = source.MarginTop,
            MarginRight = source.MarginRight,
            MarginBottom = source.MarginBottom,
            MarginLeft = source.MarginLeft,
            RepeatHeaderEachPage = source.RepeatHeaderEachPage,
            ShowPageNumbers = source.ShowPageNumbers,
            WatermarkEnabled = source.WatermarkEnabled,
            WatermarkType = source.WatermarkType,
            WatermarkText = source.WatermarkText,
            WatermarkImagePath = source.WatermarkImagePath,
            WatermarkOpacity = source.WatermarkOpacity,
            WatermarkRotation = source.WatermarkRotation,
            WatermarkFontSize = source.WatermarkFontSize,
            WatermarkColor = source.WatermarkColor,
            FileNamePattern = source.FileNamePattern,
            AllowPdf = source.AllowPdf,
            AllowWord = source.AllowWord,
            HeaderHtml = source.HeaderHtml,
            BodyHtml = source.BodyHtml,
            FooterHtml = source.FooterHtml,
            ShareWithAllRoles = source.ShareWithAllRoles
        };
        _db.PrintTemplates.Add(copy);
        await _db.SaveChangesAsync();

        var roleIds = await LoadRoleIdsAsync(id);
        await SyncRolesAsync(copy, roleIds.ToArray());

        TempData["Success"] = "یک کپی از قالب ساخته شد.";
        return RedirectToAction(nameof(Design), new { id = copy.Id });
    }

    [HttpPost("/App/print-templates/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!_tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        var template = await _db.PrintTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (template is not null)
        {
            await _db.PrintTemplateRoles
                .IgnoreQueryFilters()
                .Where(r => r.PrintTemplateId == id)
                .ExecuteDeleteAsync();
            template.IsDeleted = true;
            template.DeletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "قالب چاپ حذف شد.";
        }

        return RedirectToAction(nameof(Index));
    }

    // ── کمکی‌ها ──────────────────────────────────────────────────────

    private static void ApplySettings(PrintTemplate template, PrintTemplateSettingsInput input)
    {
        template.ModuleId = input.ModuleId;
        template.Name = input.Name.Trim();
        template.IsHtmlEditor = input.IsHtmlEditor;
        template.IsActive = input.IsActive;
        template.IsDefault = input.IsDefault;
        template.ServiceProvider = input.ServiceProvider is "gutenberg" ? "gutenberg" : "browser";

        template.PageSize = PrintPageSizes.All.Any(s =>
            string.Equals(s.Name, input.PageSize, StringComparison.OrdinalIgnoreCase))
            ? input.PageSize!.Trim()
            : "A4";
        template.Landscape = input.Landscape;
        template.TextDirection = string.Equals(input.TextDirection, "ltr", StringComparison.OrdinalIgnoreCase)
            ? "ltr" : "rtl";
        template.FontFamily = PrintFonts.IsKnown(input.FontFamily) ? input.FontFamily!.ToLowerInvariant() : "shabnam";
        template.FontSize = Math.Clamp(input.FontSize, 6, 40);
        template.CustomCss = string.IsNullOrWhiteSpace(input.CustomCss) ? null : input.CustomCss;

        template.MarginTop = Math.Clamp(input.MarginTop, 0, 60);
        template.MarginRight = Math.Clamp(input.MarginRight, 0, 60);
        template.MarginBottom = Math.Clamp(input.MarginBottom, 0, 60);
        template.MarginLeft = Math.Clamp(input.MarginLeft, 0, 60);
        template.RepeatHeaderEachPage = input.RepeatHeaderEachPage;
        template.ShowPageNumbers = input.ShowPageNumbers;

        template.WatermarkEnabled = input.WatermarkEnabled;
        template.WatermarkType = input.WatermarkType is "image" ? "image" : "text";
        template.WatermarkText = string.IsNullOrWhiteSpace(input.WatermarkText) ? null : input.WatermarkText.Trim();
        template.WatermarkImagePath = string.IsNullOrWhiteSpace(input.WatermarkImagePath)
            ? null : input.WatermarkImagePath.Trim();
        template.WatermarkOpacity = Math.Clamp(input.WatermarkOpacity, 1, 100);
        template.WatermarkRotation = Math.Clamp(input.WatermarkRotation, -180, 180);
        template.WatermarkFontSize = Math.Clamp(input.WatermarkFontSize, 8, 300);
        template.WatermarkColor = string.IsNullOrWhiteSpace(input.WatermarkColor) ? "#9e9e9e" : input.WatermarkColor;

        template.FileNamePattern = string.IsNullOrWhiteSpace(input.FileNamePattern)
            ? null : input.FileNamePattern.Trim();
        template.AllowPdf = input.AllowPdf;
        template.AllowWord = input.AllowWord;

        template.ShareWithAllRoles = input.ShareWithAllRoles;
    }

    private async Task SyncRolesAsync(PrintTemplate template, int[]? roleIds)
    {
        await _db.PrintTemplateRoles
            .IgnoreQueryFilters()
            .Where(r => r.PrintTemplateId == template.Id)
            .ExecuteDeleteAsync();

        if (template.ShareWithAllRoles || roleIds is not { Length: > 0 })
            return;

        var validRoleIds = await _db.CrmRoles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync();

        foreach (var roleId in validRoleIds.Distinct())
            _db.PrintTemplateRoles.Add(new PrintTemplateRole
            {
                PrintTemplateId = template.Id,
                RoleId = roleId
            });

        await _db.SaveChangesAsync();
    }

    private Task<List<int>> LoadRoleIdsAsync(int templateId) =>
        _db.PrintTemplateRoles.AsNoTracking()
            .Where(r => r.PrintTemplateId == templateId)
            .Select(r => r.RoleId)
            .ToListAsync();

    private async Task FillSettingsLookupsAsync()
    {
        ViewBag.Modules = (await _metadata.GetActiveModulesAsync())
            .OrderBy(m => m.IsChildModule)
            .ThenBy(m => m.SortOrder)
            .ToList();
        ViewBag.Roles = await _db.CrmRoles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleOption(r.Id, r.Name))
            .ToListAsync();
        ViewBag.SelectedRoleIds ??= new List<int>();
    }

    /// <summary>ساخت کاتالوگ فیلدها، توابع و بلاک‌های آماده برای ماژول انتخابی.</summary>
    private async Task<PrintCatalog> BuildCatalogAsync(int moduleId)
    {
        var modules = await _metadata.GetActiveModulesAsync();
        var module = modules.FirstOrDefault(m => m.Id == moduleId);
        if (module is null)
            return new PrintCatalog();

        var fields = await _metadata.GetFieldsAsync(moduleId);
        var blocks = await _metadata.GetBlocksAsync(moduleId);

        var recordFields = fields
            .Where(f => !f.IsCustom)
            .Select(f => new PrintToken(f.Label, $"{{$RECORD.{f.Name}}}"))
            .Prepend(new PrintToken("عنوان رکورد", "{$RECORD.title}"))
            .ToList();

        var customFields = fields
            .Where(f => f.IsCustom)
            .Select(f => new PrintToken(f.Label, $"{{$RECORD.{f.Name}}}"))
            .ToList();

        // فیلدهای موجودی = ستون‌های ماژول خطوط سند + جمع‌ها
        var inventory = new List<PrintToken>();
        var lineBlock = blocks.FirstOrDefault(b => b.Kind == BlockKind.LineItems);
        var lineModule = lineBlock?.LineModuleName is { Length: > 0 } lm
            ? modules.FirstOrDefault(m => m.Name == lm)
            : null;
        if (lineModule is not null)
        {
            var lineFields = await _metadata.GetFieldsAsync(lineModule.Id);
            inventory.AddRange(lineFields.Select(f => new PrintToken(
                f.Label, $"{{$ITEM.{f.Name}{(IsNumeric(f.Type) ? "|number" : "")}}}")));
            inventory.Add(new PrintToken("شماره ردیف", "{$INDEX}"));
        }
        inventory.AddRange(
        [
            new PrintToken("جمع جزء", "{$TOTALS.subTotal|number}"),
            new PrintToken("تخفیف", "{$TOTALS.discount|number}"),
            new PrintToken("مالیات", "{$TOTALS.taxTotal|number}"),
            new PrintToken("جمع کل", "{$TOTALS.grandTotal|number}"),
            new PrintToken("جمع کل به حروف", "{$FN.amountInWords(grandTotal)}")
        ]);

        // فیلدهای بلاک‌های مرتبط: هر ماژول مرتبط با فیلدهایش
        var relations = await _metadata.GetRelationsForModuleAsync(moduleId);
        var relatedGroups = new List<PrintTokenGroup>();
        foreach (var rel in relations)
        {
            var otherId = rel.SourceModuleId == moduleId ? rel.TargetModuleId : rel.SourceModuleId;
            var other = modules.FirstOrDefault(m => m.Id == otherId);
            if (other is null || relatedGroups.Any(g => g.Key == other.Name))
                continue;

            var otherFields = await _metadata.GetFieldsAsync(other.Id);
            relatedGroups.Add(new PrintTokenGroup(
                other.Name,
                string.IsNullOrWhiteSpace(rel.Label) ? other.PluralLabel : rel.Label,
                otherFields
                    .Where(f => f.IsVisible)
                    .Select(f => new PrintToken(f.Label, $"{{$RECORD.{other.Name}_{f.Name}}}"))
                    .ToList()));
        }

        var company = new List<PrintToken>
        {
            new("نام شرکت", "{$COMPANY.name}"),
            new("لوگوی شرکت", "{$COMPANY.logo}"),
            new("آدرس تصویر لوگو", "{$COMPANY.logoUrl}"),
            new("شناسه شرکت", "{$COMPANY.slug}"),
            new("وب‌سایت", "{$COMPANY.website}"),
            new("تلفن", "{$COMPANY.phone}"),
            new("آدرس", "{$COMPANY.address}"),
            new("کد اقتصادی", "{$COMPANY.economic_code}"),
            new("شماره ثبت", "{$COMPANY.registration_number}"),
            new("شناسه ملی", "{$COMPANY.national_id}"),
            new("کاربر جاری", "{$USER.fullName}")
        };

        var functions = new List<PrintToken>
        {
            new("تاریخ امروز (شمسی)", "{$FN.today}"),
            new("تاریخ و ساعت", "{$FN.now}"),
            new("تاریخ میلادی", "{$FN.todayGregorian}"),
            new("ساعت", "{$FN.time}"),
            new("مبلغ به حروف (جمع کل)", "{$FN.amountInWords(grandTotal)}"),
            new("مبلغ به حروف (فیلد رکورد)", "{$FN.amountInWords(amount)}"),
            new("عنوان سند (با جایگزین)", "{$FN.docTitle}"),
            new("شماره سند (با جایگزین)", "{$FN.docNumber}"),
            new("شماره صفحه", "{$FN.pageNumber}"),
            new("تعداد صفحات", "{$FN.pageCount}")
        };

        var lineColumns = lineModule is not null
            ? PickLineColumns(await _metadata.GetFieldsAsync(lineModule.Id), lineBlock?.LineLinkField)
            : [];

        return new PrintCatalog
        {
            ModuleId = moduleId,
            ModuleLabel = module.PluralLabel,
            Company = company,
            Record = recordFields,
            Custom = customFields,
            Inventory = inventory,
            Functions = functions,
            Related = relatedGroups,
            Blocks = BuildBlocks(lineColumns),
            RelatedBlocks = BuildRelatedBlocks()
        };
    }

    /// <summary>ستون‌های جدول آیتم — بدون فیلد پیوند والد و ترتیب، با جمع سطر در انتها.</summary>
    private static List<LineColumn> PickLineColumns(
        IReadOnlyList<FieldDef> fields, string? linkField)
    {
        // فیلد پیوند والد، ترتیب و مرجع‌ها کنار می‌روند؛ عنوان سطر خودش شرح کالا را دارد.
        bool IsExcluded(FieldDef f) =>
            string.Equals(f.Name, linkField, StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.Name, "sortOrder", StringComparison.OrdinalIgnoreCase)
            || f.Type == FieldType.Lookup;

        var usable = fields.Where(f => f.IsVisible && !IsExcluded(f)).ToList();
        var total = usable.FirstOrDefault(f =>
            string.Equals(f.Name, "lineTotal", StringComparison.OrdinalIgnoreCase));

        var columns = usable
            .Where(f => f != total)
            .Take(total is null ? 6 : 5)
            .ToList();
        if (total is not null)
            columns.Add(total);

        return columns.Select(f => new LineColumn(f.Label, f.Name, IsNumeric(f.Type))).ToList();
    }

    private static bool IsNumeric(FieldType type) =>
        type is FieldType.Number or FieldType.Decimal or FieldType.Currency or FieldType.Percent;

    private static List<PrintBlock> BuildBlocks(List<LineColumn> lineColumns)
    {
        var columns = lineColumns.Count > 0
            ? lineColumns
            :
            [
                new LineColumn("شرح", "title", false),
                new LineColumn("تعداد", "quantity", true),
                new LineColumn("قیمت واحد", "unitPrice", true),
                new LineColumn("جمع سطر", "lineTotal", true)
            ];

        var head = string.Join("", columns.Select(c =>
            $"<td style=\"{ThStyle}\">{c.Label}</td>"));

        var row = string.Join("", columns.Select(c =>
            $"<td style=\"{TdStyle}\">{{$ITEM.{c.Name}{(c.Numeric ? "|number" : "")}}}</td>"));

        var itemsTable =
            $"<table style=\"{TableStyle}\" border=\"1\" cellspacing=\"0\" width=\"100%\"><tbody>" +
            $"<tr style=\"background-color:#6b7280\"><td style=\"{ThStyle};width:6%\">ردیف</td>{head}</tr>" +
            $"<tr data-repeat=\"LINEITEMS\"><td style=\"{TdStyle}\"><strong>{{$INDEX}}</strong></td>{row}</tr>" +
            "</tbody></table>";

        var totals =
            $"<table style=\"{TableStyle};width:52%;margin-inline-end:auto;margin-top:6px\" " +
            "border=\"1\" cellspacing=\"0\">" +
            $"<tbody><tr><td style=\"{TdLabelStyle}\">جمع جزء</td>" +
            $"<td style=\"{TdMoneyStyle}\">{{$TOTALS.subTotal|number}}</td></tr>" +
            $"<tr><td style=\"{TdLabelStyle}\">تخفیف کل</td>" +
            $"<td style=\"{TdMoneyStyle}\">{{$TOTALS.discount|number}}</td></tr>" +
            $"<tr><td style=\"{TdLabelStyle}\">مالیات بر ارزش افزوده</td>" +
            $"<td style=\"{TdMoneyStyle}\">{{$TOTALS.taxTotal|number}}</td></tr>" +
            "<tr style=\"background-color:#e5e7eb\">" +
            $"<td style=\"{TdLabelStyle}\"><strong>جمع کل</strong></td>" +
            $"<td style=\"{TdMoneyStyle}\"><strong>{{$TOTALS.grandTotal|number}}</strong></td></tr>" +
            "</tbody></table>";

        var companyHeader =
            "<table style=\"border:none;width:100%\" cellspacing=\"0\" cellpadding=\"6\"><tbody><tr>" +
            "<td style=\"border:none;width:24%;background-color:#f3f4f6\">{$COMPANY.logo}</td>" +
            "<td style=\"border:none;width:26%;background-color:#f3f4f6;color:#6b7280;font-size:10pt\">" +
            "{$COMPANY.website}</td>" +
            "<td style=\"border:none;text-align:center\">" +
            "<span style=\"font-size:18pt\"><strong>{$FN.docTitle}</strong></span></td>" +
            "<td style=\"border:none;width:12%;background-color:#f3f4f6\">&nbsp;</td>" +
            "</tr></tbody></table>";

        var parties =
            "<table style=\"border:none;width:100%;margin-top:14px\" cellspacing=\"0\" cellpadding=\"2\"><tbody>" +
            "<tr><td style=\"border:none\">خریدار : <strong>{$RECORD.organization}</strong></td>" +
            "<td style=\"border:none;text-align:left\">تاریخ : <strong>{$FN.today}</strong></td></tr>" +
            "<tr><td style=\"border:none\">فروشنده : <strong>{$COMPANY.name}</strong></td>" +
            "<td style=\"border:none;text-align:left\">شماره : <strong>{$FN.docNumber}</strong></td></tr>" +
            "<tr><td style=\"border:none\">شناسه ملی : {$COMPANY.national_id}</td>" +
            "<td style=\"border:none;text-align:left\">شماره ثبت : {$COMPANY.registration_number}</td></tr>" +
            "</tbody></table>";

        var signature =
            "<table style=\"border:none;width:100%;margin-top:34px\" cellspacing=\"0\" cellpadding=\"6\"><tbody><tr>" +
            $"<td style=\"{SignStyle}\"><strong>امضا فروشنده</strong></td>" +
            $"<td style=\"{SignStyle};width:25%\">&nbsp;</td>" +
            $"<td style=\"{SignStyle}\"><strong>امضا خریدار</strong></td>" +
            "</tr></tbody></table>";

        var amountWords =
            $"<table style=\"{TableStyle};margin-top:6px\" border=\"1\" cellspacing=\"0\" width=\"100%\"><tbody><tr>" +
            $"<td style=\"{TdStyle};text-align:right\"><strong>جمع کل به حروف :</strong> " +
            "{$FN.amountInWords(grandTotal)} ریال</td>" +
            "<td style=\"background-color:#e5e7eb;padding:6px;width:26%;text-align:center\">" +
            "<strong>جمع کل به عدد: {$TOTALS.grandTotal|number}</strong></td>" +
            "</tr></tbody></table>";

        return
        [
            new PrintBlock("items", "جدول آیتم‌ها", "ستون‌های ماژول خطوط سند با حلقه تکرار", itemsTable),
            new PrintBlock("totals", "جمع مبالغ", "جمع جزء، تخفیف، مالیات و جمع کل", totals),
            new PrintBlock("company-header", "سربرگ شرکت", "لوگو، وب‌سایت و عنوان سند", companyHeader),
            new PrintBlock("parties", "خریدار و فروشنده", "نام طرفین، تاریخ و شماره سند", parties),
            new PrintBlock("amount-words", "مبلغ به حروف", "تبدیل جمع کل به حروف فارسی", amountWords),
            new PrintBlock("signature", "مهر و امضا", "امضای فروشنده و خریدار", signature)
        ];
    }

    private static List<PrintBlock> BuildRelatedBlocks()
    {
        var notes =
            "<h4>۵ یادداشت آخر</h4>" +
            $"<table style=\"{TableStyle}\" border=\"1\" cellspacing=\"0\" width=\"100%\"><tbody>" +
            $"<tr style=\"background-color:#6b7280\"><td style=\"{ThStyle}\">نویسنده</td>" +
            $"<td style=\"{ThStyle}\">تاریخ</td><td style=\"{ThStyle}\">متن</td></tr>" +
            $"<tr data-repeat=\"NOTES\"><td style=\"{TdStyle}\">{{$NOTE.author}}</td>" +
            $"<td style=\"{TdStyle}\">{{$NOTE.date}}</td><td style=\"{TdStyle}\">{{$NOTE.body}}</td></tr>" +
            "</tbody></table>";

        var comments =
            "<h4>۵ نظر آخر</h4>" +
            "<div data-repeat=\"COMMENTS\" style=\"border-bottom:1px solid #e5e7eb;padding:4px 0\">" +
            "<strong>{$COMMENT.author}</strong> <small style=\"color:#6b7280\">{$COMMENT.date}</small>" +
            "<div>{$COMMENT.body}</div></div>";

        var attachments =
            "<h4>پیوست‌ها</h4>" +
            $"<table style=\"{TableStyle}\" border=\"1\" cellspacing=\"0\" width=\"100%\"><tbody>" +
            $"<tr style=\"background-color:#6b7280\"><td style=\"{ThStyle}\">نام فایل</td>" +
            $"<td style=\"{ThStyle}\">حجم</td><td style=\"{ThStyle}\">تاریخ</td></tr>" +
            $"<tr data-repeat=\"ATTACHMENTS\"><td style=\"{TdStyle}\">{{$ATTACHMENT.fileName}}</td>" +
            $"<td style=\"{TdStyle}\">{{$ATTACHMENT.size}}</td>" +
            $"<td style=\"{TdStyle}\">{{$ATTACHMENT.date}}</td></tr>" +
            "</tbody></table>";

        return
        [
            new PrintBlock("notes", "۵ یادداشت آخر", "یادداشت‌های ثبت‌شده روی رکورد", notes),
            new PrintBlock("comments", "۵ نظر آخر", "آخرین نظرات با نام و زمان", comments),
            new PrintBlock("attachments", "پیوست‌ها", "فهرست فایل‌های ضمیمه رکورد", attachments)
        ];
    }

    private const string TableStyle = "border-collapse:collapse;border-color:#111827";
    private const string ThStyle = "padding:6px;text-align:center;color:#ffffff;font-size:9pt";
    private const string TdStyle = "padding:5px;text-align:center;font-size:9pt";
    private const string TdLabelStyle = "padding:5px;font-size:10pt";
    private const string TdMoneyStyle = "padding:5px;text-align:center;font-size:10pt";
    private const string SignStyle =
        "border:none;border-top:4px solid #ececec;text-align:center;width:25%";

    /// <summary>
    /// float را حذف می‌کند؛ عرض پیکسلی را به 100٪ محدود می‌کند؛ colgroup خراب را پاک می‌کند.
    /// جدول نیم‌عرض با margin-inline-end:auto به شروع RTL (راست) می‌چسبد.
    /// </summary>
    private static string SanitizePrintHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html ?? "";

        html = Regex.Replace(
            html,
            @"margin-inline-start\s*:\s*auto",
            "margin-inline-end:auto",
            RegexOptions.IgnoreCase);

        html = Regex.Replace(html, @"<colgroup\b[^>]*>[\s\S]*?</colgroup>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"\s*ck-table-resized\b", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"\sclass=([""'])\s*\1", "", RegexOptions.IgnoreCase);

        return Regex.Replace(
            html,
            @"style\s*=\s*([""'])(.*?)\1",
            m =>
            {
                var quote = m.Groups[1].Value;
                var style = m.Groups[2].Value;
                var touched = false;

                if (Regex.IsMatch(style, @"float\s*:", RegexOptions.IgnoreCase))
                {
                    var floated = Regex.IsMatch(style, @"float\s*:\s*(left|right)", RegexOptions.IgnoreCase);
                    var widthMatch = Regex.Match(style, @"width\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                    style = Regex.Replace(style, @"float\s*:\s*[^;]+;?", "", RegexOptions.IgnoreCase);
                    touched = true;

                    if (floated && widthMatch.Success)
                    {
                        var w = widthMatch.Groups[1].Value.Trim();
                        if (!string.Equals(w, "100%", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(w, "100", StringComparison.OrdinalIgnoreCase)
                            && !Regex.IsMatch(style, @"margin-inline-end\s*:\s*auto", RegexOptions.IgnoreCase))
                        {
                            style = string.IsNullOrEmpty(style)
                                ? "margin-inline-end:auto"
                                : style + ";margin-inline-end:auto";
                        }
                    }
                }

                var wm = Regex.Match(style, @"width\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                if (wm.Success)
                {
                    var w = wm.Groups[1].Value.Trim();
                    if (Regex.IsMatch(w, @"^\d+(?:\.\d+)?px$", RegexOptions.IgnoreCase)
                        || (Regex.IsMatch(w, @"^\d+(?:\.\d+)?%$")
                            && double.TryParse(w.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var pct) && pct > 100))
                    {
                        style = Regex.Replace(style, @"width\s*:\s*[^;]+;?", "width:100%;", RegexOptions.IgnoreCase);
                        touched = true;
                    }
                }

                if (!touched)
                    return m.Value;

                style = Regex.Replace(style, @";{2,}", ";").Trim().Trim(';').Trim();
                return string.IsNullOrEmpty(style)
                    ? ""
                    : $"style={quote}{style}{quote}";
            },
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private async Task<(string Header, string Body, string Footer)> DefaultDesignHtmlAsync(int moduleId)
    {
        var catalog = await BuildCatalogAsync(moduleId);

        string Block(string key) => catalog.Blocks.FirstOrDefault(b => b.Key == key)?.Html ?? "";

        var header = Block("company-header");
        var body = Block("parties")
                   + "\n<div style=\"height:14px\"></div>\n" + Block("items")
                   + "\n" + Block("totals")
                   + "\n" + Block("amount-words")
                   + "\n<p style=\"margin-top:10px;font-size:10pt\"><strong>توضیحات :</strong> "
                   + "{$RECORD.description}</p>"
                   + "\n<p style=\"font-size:10pt\"><strong>نشانی :</strong> {$COMPANY.address}</p>"
                   + "\n" + Block("signature");
        var footer =
            "<div style=\"text-align:center;font-size:8pt;color:#9ca3af\">"
            + "{$COMPANY.name} — {$COMPANY.website}</div>";

        return (header, body, footer);
    }

    public record LineColumn(string Label, string Name, bool Numeric);

    // ── مدل‌های ورودی/خروجی ──────────────────────────────────────────

    public record RoleOption(int Id, string Name);

    public record PrintToken(string Label, string Token);

    public record PrintTokenGroup(string Key, string Label, List<PrintToken> Tokens);

    public record PrintBlock(string Key, string Label, string Description, string Html);

    public class PrintCatalog
    {
        public int ModuleId { get; set; }
        public string ModuleLabel { get; set; } = "";
        public List<PrintToken> Company { get; set; } = [];
        public List<PrintToken> Record { get; set; } = [];
        public List<PrintToken> Custom { get; set; } = [];
        public List<PrintToken> Inventory { get; set; } = [];
        public List<PrintToken> Functions { get; set; } = [];
        public List<PrintTokenGroup> Related { get; set; } = [];
        public List<PrintBlock> Blocks { get; set; } = [];
        public List<PrintBlock> RelatedBlocks { get; set; } = [];
    }

    /// <summary>ورودی فرم مرحله تنظیمات.</summary>
    public class PrintTemplateSettingsInput
    {
        public int Id { get; set; }
        public int ModuleId { get; set; }
        public string Name { get; set; } = "";
        public bool IsHtmlEditor { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsDefault { get; set; }
        public string? ServiceProvider { get; set; }

        public string? PageSize { get; set; }
        public bool Landscape { get; set; }
        public string? TextDirection { get; set; }
        public string? FontFamily { get; set; }
        public int FontSize { get; set; } = 12;
        public string? CustomCss { get; set; }

        public int MarginTop { get; set; } = 12;
        public int MarginRight { get; set; } = 12;
        public int MarginBottom { get; set; } = 12;
        public int MarginLeft { get; set; } = 12;
        public bool RepeatHeaderEachPage { get; set; }
        public bool ShowPageNumbers { get; set; }

        public bool WatermarkEnabled { get; set; }
        public string? WatermarkType { get; set; }
        public string? WatermarkText { get; set; }
        public string? WatermarkImagePath { get; set; }
        public int WatermarkOpacity { get; set; } = 12;
        public int WatermarkRotation { get; set; } = -30;
        public int WatermarkFontSize { get; set; } = 72;
        public string? WatermarkColor { get; set; }

        public string? FileNamePattern { get; set; }
        public bool AllowPdf { get; set; } = true;
        public bool AllowWord { get; set; } = true;

        public bool ShareWithAllRoles { get; set; } = true;
    }
}
