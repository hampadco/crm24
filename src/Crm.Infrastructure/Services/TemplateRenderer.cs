using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Services;

/// <summary>
/// موتور جایگذاری قالب چاپ و پیام —
/// توکن‌ها: {$RECORD.*}، {$COMPANY.*}، {$USER.fullName}، {$FN.*}، {#LINEITEMS}…{/LINEITEMS}، {$TOTALS.*}.
/// </summary>
public class TemplateRenderer
{
    private static readonly Regex TokenRegex = new(
        @"\{\$(?<scope>RECORD|COMPANY|USER|FN|ITEM|LINEITEM|TOTALS|NOTE|COMMENT|ATTACHMENT)\.(?<expr>[^}]+)\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RepeatAttrRegex = new(
        @"\s*data-repeat\s*=\s*[""'][^""']*[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LineItemsRegex = new(
        @"\{#LINEITEMS\}(?<body>.*?)\{\/LINEITEMS\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex NotesRegex = new(
        @"\{#(?<tag>NOTES|COMMENTS)\}(?<body>.*?)\{\/\k<tag>\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex AttachmentsRegex = new(
        @"\{#ATTACHMENTS\}(?<body>.*?)\{\/ATTACHMENTS\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>
    /// شروع عنصر تکرارشونده: &lt;tr data-repeat="LINEITEMS"&gt;.
    /// چون متن خام داخل جدول توسط مرورگر بیرون انداخته می‌شود، حلقه‌ی جدول‌ها با صفت علامت‌گذاری می‌شود.
    /// </summary>
    private static readonly Regex RepeatOpenRegex = new(
        @"<(?<tag>[a-zA-Z][a-zA-Z0-9]*)\b[^>]*\bdata-repeat\s*=\s*[""'](?<src>LINEITEMS|ITEMS|NOTES|COMMENTS|ATTACHMENTS)[""'][^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AmountInWordsRegex = new(
        @"^amountInWords\((?<field>[a-zA-Z0-9_]+)\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly MetadataService _metadata;
    private readonly UserManager<CrmUser> _users;

    public TemplateRenderer(
        CrmDbContext db,
        ITenantContext tenant,
        MetadataService metadata,
        UserManager<CrmUser> users)
    {
        _db = db;
        _tenant = tenant;
        _metadata = metadata;
        _users = users;
    }

    /// <summary>رندر کامل قالب چاپ: سربرگ + بدنه + پاورقی داخل پوسته RTL قابل چاپ.</summary>
    public async Task<string> RenderAsync(
        PrintTemplate template,
        DynamicRecord record,
        CancellationToken ct = default) =>
        await RenderAsync(template, record, lineItems: null, totals: null, ct);

    public async Task<string> RenderAsync(
        PrintTemplate template,
        DynamicRecord record,
        IReadOnlyList<Dictionary<string, string?>>? lineItems,
        IReadOnlyDictionary<string, string?>? totals,
        CancellationToken ct = default)
    {
        var parts = await RenderPartsAsync(template, record, lineItems, totals, ct);
        return WrapPrintShell(parts.Header, parts.Body, parts.Footer, parts.Title, template);
    }

    /// <summary>جایگذاری سربرگ/بدنه/پاورقی بدون پوسته HTML (برای View چاپ).</summary>
    public async Task<(string Header, string Body, string Footer, string Title)> RenderPartsAsync(
        PrintTemplate template,
        DynamicRecord record,
        CancellationToken ct = default) =>
        await RenderPartsAsync(template, record, lineItems: null, totals: null, ct);

    public async Task<(string Header, string Body, string Footer, string Title)> RenderPartsAsync(
        PrintTemplate template,
        DynamicRecord record,
        IReadOnlyList<Dictionary<string, string?>>? lineItems,
        IReadOnlyDictionary<string, string?>? totals,
        CancellationToken ct = default)
    {
        var ctx = await BuildContextAsync(record, ct);
        if (lineItems is not null)
        {
            ctx.LineItems = lineItems
                .Select(l => new Dictionary<string, string?>(l, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var lineModuleId = await GetLineModuleIdAsync(record.ModuleId);
            if (lineModuleId is int lmid)
                await ResolveLookupsAsync(lmid, ctx.LineItems, ct);
            if (totals is null)
                ctx.Totals = BuildTotals(ctx.Record, ctx.LineItems);
        }

        if (totals is not null)
        {
            ctx.Totals = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in totals)
                ctx.Totals[k] = v;
            // کلیدهای رایج برای قالب
            if (!ctx.Totals.ContainsKey("discount") && totals.TryGetValue("discountAmount", out var da))
                ctx.Totals["discount"] = da;
        }

        var header = ReplaceAll(template.HeaderHtml ?? "", ctx);
        var body = ReplaceAll(template.BodyHtml ?? "", ctx);
        var footer = ReplaceAll(template.FooterHtml ?? "", ctx);
        var title = string.IsNullOrWhiteSpace(record.Title) ? template.Name : record.Title;
        return (header, body, footer, title);
    }

    public static string WrapPrintShell(
        string header, string body, string footer, string title, PrintTemplate template)
    {
        var safeTitle = System.Net.WebUtility.HtmlEncode(title);
        var dir = NormalizeDirection(template.TextDirection);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"fa\" dir=\"").Append(dir).Append("\"><head><meta charset=\"utf-8\" />");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        sb.Append("<title>").Append(safeTitle).Append("</title><style>");
        sb.Append(BuildPageCss(template, fontBaseUrl: null));
        sb.Append("</style></head><body>");
        sb.Append(BuildWatermarkHtml(template));
        if (!string.IsNullOrWhiteSpace(header))
            sb.Append("<div class=\"print-header\">").Append(header).Append("</div>");
        sb.Append("<div class=\"print-body\">").Append(body).Append("</div>");
        if (!string.IsNullOrWhiteSpace(footer))
            sb.Append("<div class=\"print-footer\">").Append(footer).Append("</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// CSS کامل صفحه چاپ بر اساس تنظیمات قالب (اندازه، جهت، حاشیه، فونت، واترمارک، استایل سفارشی).
    /// <paramref name="fontBaseUrl"/> ریشه فایل‌های فونت است؛ null یعنی مسیر مطلق پیش‌فرض.
    /// </summary>
    public static string BuildPageCss(PrintTemplate template, string? fontBaseUrl)
    {
        var size = PrintPageSizes.All.Any(s =>
            string.Equals(s.Name, template.PageSize, StringComparison.OrdinalIgnoreCase))
            ? template.PageSize.Trim()
            : "A4";
        var orientation = template.Landscape ? "landscape" : "portrait";
        var dir = NormalizeDirection(template.TextDirection);
        var fontSize = Math.Clamp(template.FontSize <= 0 ? 12 : template.FontSize, 6, 40);
        var baseUrl = string.IsNullOrWhiteSpace(fontBaseUrl)
            ? "/panel-assets/vendor/fonts/farsi-fonts-fa-num"
            : fontBaseUrl.TrimEnd('/');

        var sb = new StringBuilder();
        sb.Append(BuildFontFaceCss(template.FontFamily, baseUrl));
        sb.Append("@page{size:").Append(size).Append(' ').Append(orientation)
          .Append(";margin:")
          .Append(Clamp(template.MarginTop)).Append("mm ")
          .Append(Clamp(template.MarginRight)).Append("mm ")
          .Append(Clamp(template.MarginBottom)).Append("mm ")
          .Append(Clamp(template.MarginLeft)).Append("mm;}");
        sb.Append("html,body{direction:").Append(dir).Append(";}");
        sb.Append("body{font-family:'print-font',Tahoma,sans-serif;font-size:")
          .Append(fontSize).Append("pt;color:#222;line-height:1.7;margin:0;padding:0;position:relative;}");
        sb.Append(BuildContentCss(dir, scopeSelector: null));
        sb.Append(BuildWatermarkCss(template));
        // پاورقی را از جریان چاپ خارج می‌کنیم تا تنها به صفحهٔ بعد پرتاب نشود (مثل ورد پایین صفحه می‌ماند).
        // Chrome/Edge با position:fixed در چاپ، عنصر را پایین هر صفحه تکرار می‌کنند.
        sb.Append("@media print{");
        sb.Append(".print-footer{position:fixed!important;bottom:0;left:0;right:0;width:auto;");
        sb.Append("margin:0!important;padding:6px 0 0;background:#fff;z-index:5;");
        sb.Append("page-break-inside:avoid;break-inside:avoid;}");
        sb.Append(".print-body{padding-bottom:52px;}");
        sb.Append(".print-header{page-break-inside:avoid;break-inside:avoid;}");
        sb.Append("}");
        if (template.RepeatHeaderEachPage)
            sb.Append("@media print{.print-header{position:running(pageHeader);}thead{display:table-header-group;}}");
        if (template.ShowPageNumbers)
            sb.Append("@media print{.print-footer::after{content:' ';}}");
        sb.Append("@media print{.no-print{display:none!important;}}");
        if (!string.IsNullOrWhiteSpace(template.CustomCss))
            sb.Append('\n').Append(SanitizeCss(template.CustomCss!));
        return sb.ToString();

        static int Clamp(int mm) => Math.Clamp(mm, 0, 60);
    }

    /// <summary>
    /// قوانین ظاهر محتوا (جدول/figure/تراز) — مشترک بین چاپ و بوم Design.
    /// اگر <paramref name="scopeSelector"/> داده شود (مثلاً <c>.pt-designer .ck-content</c>) همهٔ سلکتورها زیر آن می‌آیند.
    /// </summary>
    public static string BuildContentCss(string textDirection, string? scopeSelector)
    {
        var dir = NormalizeDirection(textDirection);
        var align = dir == "rtl" ? "right" : "left";
        // در RTL شروع = راست → جدول نیم‌عرض با margin-inline-end:auto به راست می‌چسبد
        var halfAlign = "margin-inline-end:auto!important;margin-inline-start:0!important";

        var raw = new StringBuilder();
        raw.Append(".print-body,.print-header,.print-footer{text-align:").Append(align).Append(";}");
        raw.Append("table{width:100%;max-width:100%;border-collapse:collapse;margin:10px 0;box-sizing:border-box;}");
        // specificity بالاتر از .ck-content .table table td
        raw.Append("figure.table table td,figure.table table th,.table table td,.table table th{");
        raw.Append("border:1px solid #999!important;padding:5px 8px;text-align:").Append(align).Append(";}");
        // فقط وقتی پس‌زمینهٔ اینلاین ندارد — وگرنه رنگ سلول در ادیتور دیده نمی‌شود
        raw.Append("th:not([style*=\"background\"]),th:not([bgcolor]){background:#f2f2f2;}");
        raw.Append("td[style*=\"background-color\"],th[style*=\"background-color\"],");
        raw.Append("td[style*=\"background:\"],th[style*=\"background:\"]{background-clip:padding-box;}");
        raw.Append("img{max-width:100%;height:auto;}");
        raw.Append("p{margin:.35em 0;}");
        raw.Append(".print-header{margin-bottom:14px;}");
        raw.Append(".print-footer{margin-top:12px;border-top:1px solid #ddd;padding-top:6px;}");
        // جلوگیری از افتادن پاورقی تنها روی صفحهٔ بعد وقتی هنوز در جریان سند است (پیش‌نمایش صفحه)
        raw.Append(".print-footer{page-break-before:avoid;break-before:avoid;page-break-inside:avoid;break-inside:avoid;}");
        raw.Append("figure{margin:0;}");
        raw.Append("figure.table{margin:8px 0;float:none!important;clear:both;max-width:100%;}");
        raw.Append("figure.table table{margin:0;width:100%;max-width:100%;}");
        raw.Append("table[style*=\"border:none\"] td,table[style*=\"border: none\"] td,");
        raw.Append("table[style*=\"border:none\"] th,table[style*=\"border: none\"] th,");
        raw.Append("td[style*=\"border:none\"],td[style*=\"border: none\"],");
        raw.Append("th[style*=\"border:none\"],th[style*=\"border: none\"]{border:none!important;}");
        raw.Append("table[style*=\"border:none\"],table[style*=\"border: none\"]{border:none!important;}");
        raw.Append("figure.table[style*=\"width:50%\"],figure.table[style*=\"width: 50%\"],");
        raw.Append("figure.table[style*=\"width:52%\"],figure.table[style*=\"width: 52%\"]{width:50%!important;max-width:50%!important;")
          .Append(halfAlign).Append(";}");
        raw.Append("table[style*=\"width:50%\"],table[style*=\"width: 50%\"],");
        raw.Append("table[style*=\"width:52%\"],table[style*=\"width: 52%\"]{width:50%!important;max-width:50%!important;")
          .Append(halfAlign).Append(";}");
        raw.Append("figure.image{text-align:center;}figure figcaption{font-size:.85em;color:#777;}");
        raw.Append(".page-break{page-break-after:always;break-after:page;}");
        raw.Append(".text-tiny{font-size:.7em;}.text-small{font-size:.85em;}");
        raw.Append(".text-big{font-size:1.4em;}.text-huge{font-size:1.8em;}");
        raw.Append(".ck-align-left,.text-left{text-align:left!important;}");
        raw.Append(".ck-align-center,.text-center{text-align:center!important;}");
        raw.Append(".ck-align-right,.text-right{text-align:right!important;}");
        raw.Append(".ck-align-justify,.text-justify{text-align:justify!important;}");

        if (string.IsNullOrWhiteSpace(scopeSelector))
            return raw.ToString();

        return ScopeCss(raw.ToString(), scopeSelector.Trim());
    }

    /// <summary>هر rule را زیر <paramref name="scope"/> قرار می‌دهد (برای بوم CKEditor).</summary>
    public static string ScopeCss(string css, string scope)
    {
        var sb = new StringBuilder();
        foreach (var chunk in css.Split('}', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = chunk.Trim();
            if (part.Length == 0) continue;
            var brace = part.IndexOf('{');
            if (brace <= 0) continue;
            var selectors = part[..brace].Trim();
            var body = part[(brace + 1)..].Trim();
            if (selectors.Length == 0 || body.Length == 0) continue;

            var scoped = string.Join(',',
                selectors.Split(',').Select(s =>
                {
                    s = s.Trim();
                    return s.Length == 0 ? "" : scope + " " + s;
                }).Where(s => s.Length > 0));

            if (scoped.Length == 0) continue;
            sb.Append(scoped).Append('{').Append(body).Append('}');
        }
        return sb.ToString();
    }

    /// <summary>لایه واترمارک متنی یا تصویری روی صفحه چاپ.</summary>
    public static string BuildWatermarkHtml(PrintTemplate template)
    {
        if (!template.WatermarkEnabled)
            return "";

        if (string.Equals(template.WatermarkType, "image", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(template.WatermarkImagePath))
                return "";
            return "<div class=\"print-watermark\"><img src=\""
                   + System.Net.WebUtility.HtmlEncode(template.WatermarkImagePath)
                   + "\" alt=\"\" /></div>";
        }

        if (string.IsNullOrWhiteSpace(template.WatermarkText))
            return "";
        return "<div class=\"print-watermark\"><span>"
               + System.Net.WebUtility.HtmlEncode(template.WatermarkText)
               + "</span></div>";
    }

    private static string BuildWatermarkCss(PrintTemplate template)
    {
        if (!template.WatermarkEnabled)
            return "";

        var opacity = Math.Clamp(template.WatermarkOpacity <= 0 ? 12 : template.WatermarkOpacity, 1, 100) / 100m;
        var rotation = Math.Clamp(template.WatermarkRotation, -180, 180);
        var wmFontSize = Math.Clamp(template.WatermarkFontSize <= 0 ? 72 : template.WatermarkFontSize, 8, 300);
        var color = SanitizeColor(template.WatermarkColor) ?? "#9e9e9e";

        return ".print-watermark{position:fixed;inset:0;display:flex;align-items:center;justify-content:center;"
             + "pointer-events:none;z-index:0;opacity:" + opacity.ToString("0.##", CultureInfo.InvariantCulture) + ";}"
             + ".print-watermark span{transform:rotate(" + rotation.ToString(CultureInfo.InvariantCulture) + "deg);"
             + "font-size:" + wmFontSize.ToString(CultureInfo.InvariantCulture) + "pt;font-weight:700;white-space:nowrap;color:" + color + ";}"
             + ".print-watermark img{transform:rotate(" + rotation.ToString(CultureInfo.InvariantCulture) + "deg);max-width:60%;max-height:60%;}"
             + ".print-header,.print-body{position:relative;z-index:1;}"
             + ".print-footer{z-index:2;}";
    }

    private static string BuildFontFaceCss(string? slug, string baseUrl)
    {
        if (!PrintFonts.IsKnown(slug))
            slug = "shabnam";
        var sb = new StringBuilder();
        foreach (var weight in new[] { 300, 400, 500, 700 })
        {
            sb.Append("@font-face{font-family:'print-font';src:url('")
              .Append(baseUrl).Append('/').Append(slug).Append('-').Append(weight)
              .Append(".woff2') format('woff2');font-weight:").Append(weight).Append(";font-display:swap;}");
        }
        return sb.ToString();
    }

    /// <summary>نام فایل خروجی بر اساس الگوی قالب (با جایگذاری توکن‌های رکورد).</summary>
    public string ResolveFileName(PrintTemplate template, DynamicRecord record, string fallback)
    {
        var pattern = template.FileNamePattern;
        string name;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            name = fallback;
        }
        else
        {
            var data = DynamicRecordService.ParseData(record);
            data["title"] = record.Title;
            var ctx = new RenderContext { Record = data };
            name = ReplaceTokens(pattern, ctx, item: null);
        }

        name = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static string NormalizeDirection(string? dir) =>
        string.Equals(dir, "ltr", StringComparison.OrdinalIgnoreCase) ? "ltr" : "rtl";

    private static string? SanitizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return null;
        color = color.Trim();
        return Regex.IsMatch(color, @"^#[0-9a-fA-F]{3,8}$|^[a-zA-Z]{3,20}$") ? color : null;
    }

    /// <summary>حذف ساختارهای خطرناک از CSS سفارشی کاربر.</summary>
    private static string SanitizeCss(string css) =>
        Regex.Replace(css, @"</\s*style|<\s*script|javascript\s*:|expression\s*\(",
            "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>جایگذاری ساده برای قالب‌های پیام (بدون پوسته چاپ).</summary>
    public string Interpolate(string? template, IReadOnlyDictionary<string, string?> data)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        var ctx = new RenderContext
        {
            Record = new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase),
            CompanyName = data.GetValueOrDefault("company_name") ?? data.GetValueOrDefault("COMPANY.name") ?? "",
            CompanyLogo = data.GetValueOrDefault("company_logo") ?? "",
            UserFullName = data.GetValueOrDefault("user_fullName") ?? data.GetValueOrDefault("USER.fullName") ?? ""
        };
        return ReplaceAll(template, ctx);
    }

    /// <summary>جایگذاری ناهمگام با بارگذاری Tenant و کاربر جاری.</summary>
    public async Task<string> InterpolateAsync(
        string? template,
        DynamicRecord? record = null,
        IReadOnlyDictionary<string, string?>? extra = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        RenderContext ctx;
        if (record is not null)
            ctx = await BuildContextAsync(record, ct);
        else
            ctx = await BuildEmptyContextAsync(ct);

        if (extra is not null)
        {
            foreach (var (k, v) in extra)
                ctx.Record[k] = v;
        }

        return ReplaceAll(template, ctx);
    }

    private async Task<RenderContext> BuildEmptyContextAsync(CancellationToken ct)
    {
        var tenant = _tenant.TenantId is int tid
            ? await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid, ct)
            : null;
        string? userName = null;
        if (_tenant.UserId is int uid)
            userName = await _users.Users.AsNoTracking()
                .Where(u => u.Id == uid)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct);

        return new RenderContext
        {
            CompanyName = tenant?.Name ?? "",
            CompanyLogo = tenant?.LogoPath ?? "",
            CompanyExtra = ParseCompanyExtra(tenant),
            UserFullName = userName ?? ""
        };
    }

    /// <summary>مقادیر رشته‌ای سطح‌اول jsonb تنظیمات Tenant به‌عنوان فیلدهای شرکت.</summary>
    private static Dictionary<string, string?> ParseCompanyExtra(Tenant? tenant)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (tenant is null)
            return result;

        result["slug"] = tenant.Slug;
        if (string.IsNullOrWhiteSpace(tenant.Settings))
            return result;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(tenant.Settings);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return result;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind is System.Text.Json.JsonValueKind.String
                    or System.Text.Json.JsonValueKind.Number)
                    result[prop.Name] = prop.Value.ToString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // تنظیمات نامعتبر — فیلدهای اضافه شرکت نادیده گرفته می‌شود
        }

        return result;
    }

    private async Task<RenderContext> BuildContextAsync(DynamicRecord record, CancellationToken ct)
    {
        var data = DynamicRecordService.ParseData(record);
        data["title"] = record.Title;
        data["id"] = record.Id.ToString(CultureInfo.InvariantCulture);

        var tenant = _tenant.TenantId is int tid
            ? await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid, ct)
            : null;

        string? userName = null;
        if (_tenant.UserId is int uid)
            userName = await _users.Users.AsNoTracking()
                .Where(u => u.Id == uid)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct);

        var lineItems = await LoadLineItemsAsync(record, ct);
        if (await GetLineModuleIdAsync(record.ModuleId) is int lineModuleId)
            await ResolveLookupsAsync(lineModuleId, lineItems, ct);

        await ResolveLookupsAsync(record.ModuleId, [data], ct);

        var totals = BuildTotals(data, lineItems);
        var (notes, attachments) = await LoadRelatedBlocksAsync(record, ct);

        return new RenderContext
        {
            Record = data,
            LineItems = lineItems,
            Notes = notes,
            Attachments = attachments,
            Totals = totals,
            CompanyName = tenant?.Name ?? "",
            CompanyLogo = tenant?.LogoPath ?? "",
            CompanyExtra = ParseCompanyExtra(tenant),
            UserFullName = userName ?? ""
        };
    }

    /// <summary>۵ یادداشت و ۵ پیوست آخر رکورد برای بلاک‌های مرتبط قالب.</summary>
    private async Task<(List<Dictionary<string, string?>> Notes, List<Dictionary<string, string?>> Attachments)>
        LoadRelatedBlocksAsync(DynamicRecord record, CancellationToken ct)
    {
        var modules = await _metadata.GetActiveModulesAsync();
        var moduleName = modules.FirstOrDefault(m => m.Id == record.ModuleId)?.Name;
        if (string.IsNullOrWhiteSpace(moduleName))
            return ([], []);

        var notes = await _db.Notes.AsNoTracking()
            .Where(n => n.ModuleName == moduleName && n.RecordId == record.Id)
            .OrderByDescending(n => n.Id)
            .Take(5)
            .Select(n => new { n.Body, n.CreatedAtUtc, n.CreatedByUserId })
            .ToListAsync(ct);

        var files = await _db.Attachments.AsNoTracking()
            .Where(a => a.ModuleName == moduleName && a.RecordId == record.Id)
            .OrderByDescending(a => a.Id)
            .Take(10)
            .Select(a => new { a.FileName, a.SizeBytes, a.CreatedAtUtc })
            .ToListAsync(ct);

        var authorIds = notes.Where(n => n.CreatedByUserId != null)
            .Select(n => n.CreatedByUserId!.Value).Distinct().ToList();
        var authors = authorIds.Count == 0
            ? new Dictionary<int, string>()
            : await _users.Users.AsNoTracking()
                .Where(u => authorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName ?? "", ct);

        var noteRows = notes.Select(n => new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["body"] = n.Body,
            ["content"] = n.Body,
            ["comment_content"] = n.Body,
            ["commenter"] = n.CreatedByUserId is int uid ? authors.GetValueOrDefault(uid, "") : "",
            ["author"] = n.CreatedByUserId is int uid2 ? authors.GetValueOrDefault(uid2, "") : "",
            ["date"] = n.CreatedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture),
            ["comment_time"] = n.CreatedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)
        }).ToList();

        var fileRows = files.Select(a => new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["fileName"] = a.FileName,
            ["size"] = FormatNumber(a.SizeBytes / 1024m) + " KB",
            ["date"] = a.CreatedAtUtc.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
        }).ToList();

        return (noteRows, fileRows);
    }

    /// <summary>شناسه ماژول خطوط سند برای ماژول والد (اگر بلاک آیتم‌ها تعریف شده باشد).</summary>
    private async Task<int?> GetLineModuleIdAsync(int parentModuleId)
    {
        var blocks = await _metadata.GetBlocksAsync(parentModuleId);
        var name = blocks.FirstOrDefault(b => b.Kind == BlockKind.LineItems)?.LineModuleName;
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return (await _metadata.GetModuleByNameAsync(name))?.Id;
    }

    /// <summary>
    /// جایگزینی شناسه فیلدهای Lookup با عنوان رکورد مقصد؛ شناسه خام زیر کلید «name_id» می‌ماند.
    /// </summary>
    private async Task ResolveLookupsAsync(
        int moduleId, List<Dictionary<string, string?>> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        var lookups = (await _metadata.GetFieldsAsync(moduleId))
            .Where(f => f.Type == FieldType.Lookup)
            .ToList();
        if (lookups.Count == 0)
            return;

        var ids = new HashSet<int>();
        foreach (var row in rows)
            foreach (var f in lookups)
                if (int.TryParse(row.GetValueOrDefault(f.Name), out var id))
                    ids.Add(id);
        if (ids.Count == 0)
            return;

        var titles = await _db.Records.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Title, ct);

        foreach (var row in rows)
            foreach (var f in lookups)
            {
                var raw = row.GetValueOrDefault(f.Name);
                if (!int.TryParse(raw, out var id))
                    continue;
                row[f.Name + "_id"] = raw;
                // شناسه‌ای که رکورد مقصدش پیدا نشود در سند چاپی بی‌معناست.
                row[f.Name] = titles.TryGetValue(id, out var title) && !string.IsNullOrWhiteSpace(title)
                    ? title
                    : "";
            }
    }

    private async Task<List<Dictionary<string, string?>>> LoadLineItemsAsync(
        DynamicRecord record, CancellationToken ct)
    {
        var blocks = await _metadata.GetBlocksAsync(record.ModuleId);
        var lineBlock = blocks.FirstOrDefault(b =>
            b.Kind == BlockKind.LineItems
            && !string.IsNullOrWhiteSpace(b.LineModuleName)
            && !string.IsNullOrWhiteSpace(b.LineLinkField));
        if (lineBlock is null)
            return [];

        var childModule = await _metadata.GetModuleByNameAsync(lineBlock.LineModuleName!);
        if (childModule is null)
            return [];

        var linkField = lineBlock.LineLinkField!;
        if (linkField.Length > 64 || !linkField.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            return [];

        var parentId = record.Id.ToString();
        var children = await _db.Records.AsNoTracking()
            .Where(r => r.ModuleId == childModule.Id)
            .OrderBy(r => r.Id)
            .Take(500)
            .ToListAsync(ct);

        var result = new List<Dictionary<string, string?>>();
        foreach (var child in children)
        {
            var row = DynamicRecordService.ParseData(child);
            if (!row.TryGetValue(linkField, out var link) ||
                !string.Equals(link, parentId, StringComparison.Ordinal))
                continue;

            row["title"] = child.Title;
            result.Add(row);
        }

        return result;
    }

    private static Dictionary<string, string?> BuildTotals(
        Dictionary<string, string?> record,
        IReadOnlyList<Dictionary<string, string?>> lines)
    {
        decimal Sum(string key) => lines
            .Select(l => ParseDecimal(l.GetValueOrDefault(key)))
            .Sum();

        var subTotal = ParseDecimal(record.GetValueOrDefault("sub_total")
            ?? record.GetValueOrDefault("subTotal")) is var st and > 0
            ? st
            : Sum("line_total") is var lt and > 0 ? lt : Sum("amount");

        var discount = ParseDecimal(record.GetValueOrDefault("discount_amount")
            ?? record.GetValueOrDefault("discountAmount"));
        var tax = ParseDecimal(record.GetValueOrDefault("tax_total")
            ?? record.GetValueOrDefault("taxTotal"));
        var grand = ParseDecimal(record.GetValueOrDefault("grand_total")
            ?? record.GetValueOrDefault("grandTotal"));
        if (grand <= 0)
            grand = subTotal - discount + tax;

        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["subTotal"] = FormatNumber(subTotal),
            ["discount"] = FormatNumber(discount),
            ["taxTotal"] = FormatNumber(tax),
            ["grandTotal"] = FormatNumber(grand)
        };
    }

    private static string ReplaceAll(string template, RenderContext ctx)
    {
        var expanded = ExpandRepeatElements(template, ctx);

        expanded = LineItemsRegex.Replace(expanded, m =>
            Repeat(m.Groups["body"].Value, ctx, ctx.LineItems));

        expanded = NotesRegex.Replace(expanded, m =>
            Repeat(m.Groups["body"].Value, ctx, ctx.Notes));

        expanded = AttachmentsRegex.Replace(expanded, m =>
            Repeat(m.Groups["body"].Value, ctx, ctx.Attachments));

        return ReplaceTokens(expanded, ctx, item: null);
    }

    private static string Repeat(
        string bodyTpl, RenderContext ctx, List<Dictionary<string, string?>> rows)
    {
        if (rows.Count == 0)
            return string.Empty;
        var sb = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
            sb.Append(ReplaceTokens(bodyTpl, ctx, rows[i], i + 1));
        return sb.ToString();
    }

    /// <summary>تکرار عنصرهای دارای data-repeat به تعداد سطرهای منبع (سازگار با ساختار جدول).</summary>
    private static string ExpandRepeatElements(string template, RenderContext ctx)
    {
        var result = new StringBuilder();
        var cursor = 0;

        while (true)
        {
            var open = RepeatOpenRegex.Match(template, cursor);
            if (!open.Success)
                break;

            var tag = open.Groups["tag"].Value;
            var source = open.Groups["src"].Value.ToUpperInvariant();
            var end = FindElementEnd(template, tag, open.Index + open.Length);
            if (end < 0)
                break;

            var element = RepeatAttrRegex.Replace(template[open.Index..end], "", 1);
            var rows = source switch
            {
                "NOTES" or "COMMENTS" => ctx.Notes,
                "ATTACHMENTS" => ctx.Attachments,
                _ => ctx.LineItems
            };

            result.Append(template, cursor, open.Index - cursor);
            result.Append(Repeat(element, ctx, rows));
            cursor = end;
        }

        result.Append(template, cursor, template.Length - cursor);
        return result.ToString();
    }

    /// <summary>مکان پایان تگ بسته متناظر (با شمارش تگ‌های تودرتوی هم‌نام).</summary>
    private static int FindElementEnd(string html, string tag, int from)
    {
        var openPattern = new Regex($@"<{Regex.Escape(tag)}\b", RegexOptions.IgnoreCase);
        var closePattern = new Regex($@"</{Regex.Escape(tag)}\s*>", RegexOptions.IgnoreCase);
        var depth = 1;
        var pos = from;

        while (pos < html.Length)
        {
            var nextOpen = openPattern.Match(html, pos);
            var nextClose = closePattern.Match(html, pos);
            if (!nextClose.Success)
                return -1;

            if (nextOpen.Success && nextOpen.Index < nextClose.Index)
            {
                depth++;
                pos = nextOpen.Index + nextOpen.Length;
                continue;
            }

            depth--;
            pos = nextClose.Index + nextClose.Length;
            if (depth == 0)
                return pos;
        }

        return -1;
    }

    private static string ReplaceTokens(
        string template, RenderContext ctx, Dictionary<string, string?>? item, int rowIndex = 0)
    {
        if (rowIndex > 0)
            template = template.Replace("{$INDEX}", rowIndex.ToString(CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase);

        return TokenRegex.Replace(template, m =>
        {
            var scope = m.Groups["scope"].Value.ToUpperInvariant();
            var expr = m.Groups["expr"].Value.Trim();
            string? format = null;

            var pipe = expr.LastIndexOf('|');
            if (pipe > 0)
            {
                format = expr[(pipe + 1)..].Trim();
                expr = expr[..pipe].Trim();
            }

            var value = scope switch
            {
                "RECORD" => ResolveRecord(ctx.Record, expr),
                "COMPANY" => ResolveCompany(ctx, expr),
                "USER" => ResolveUser(ctx, expr),
                "FN" => ResolveFn(ctx, expr),
                "ITEM" or "LINEITEM" or "NOTE" or "COMMENT" or "ATTACHMENT" =>
                    item is null ? "" : ResolveRecord(item, expr),
                "TOTALS" => ctx.Totals.GetValueOrDefault(expr) ?? "",
                _ => m.Value
            };

            return ApplyFormat(value, format);
        });
    }

    /// <summary>قالب‌بندی اختیاری مقدار توکن: {$ITEM.unitPrice|number}</summary>
    private static string ApplyFormat(string value, string? format)
    {
        if (string.IsNullOrEmpty(format) || string.IsNullOrWhiteSpace(value))
            return value;

        return format.ToLowerInvariant() switch
        {
            "number" or "money" => FormatNumber(ParseDecimal(value)),
            "int" => ParseDecimal(value).ToString("#,0", CultureInfo.InvariantCulture),
            "words" => AmountToPersianWords(ParseDecimal(value)),
            _ => value
        };
    }

    private static string ResolveRecord(Dictionary<string, string?> data, string field) =>
        data.GetValueOrDefault(field) ?? "";

    private static string ResolveCompany(RenderContext ctx, string expr)
    {
        if (expr.Equals("name", StringComparison.OrdinalIgnoreCase))
            return ctx.CompanyName;

        if (expr.Equals("logo", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(ctx.CompanyLogo)
                ? ""
                : $"<img src=\"{System.Net.WebUtility.HtmlEncode(ctx.CompanyLogo)}\" alt=\"\" style=\"max-height:64px\" />";

        if (expr.Equals("logoUrl", StringComparison.OrdinalIgnoreCase))
            return ctx.CompanyLogo;

        return ctx.CompanyExtra.GetValueOrDefault(expr) ?? "";
    }

    private static string ResolveUser(RenderContext ctx, string expr) =>
        expr.Equals("fullName", StringComparison.OrdinalIgnoreCase) ? ctx.UserFullName ?? "" : "";

    private static string ResolveFn(RenderContext ctx, string expr)
    {
        var now = DateTime.Now;
        switch (expr.ToLowerInvariant())
        {
            case "today":
                return ToJalali(now, withTime: false);
            case "now":
                return ToJalali(now, withTime: true);
            case "todaygregorian":
                return now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            case "time":
                return now.ToString("HH:mm", CultureInfo.InvariantCulture);
            case "pagenumber":
                return "<span class=\"print-page-number\"></span>";
            case "pagecount":
                return "<span class=\"print-page-count\"></span>";
            case "doctitle":
                return FirstNonEmpty(ctx.Record, "printTitle", "print_title", "name", "title");
            case "docnumber":
                return FirstNonEmpty(ctx.Record, "number", "documentNumber", "code", "id");
        }

        var amountMatch = AmountInWordsRegex.Match(expr);
        if (amountMatch.Success)
        {
            var field = amountMatch.Groups["field"].Value;
            var raw = ctx.Record.GetValueOrDefault(field)
                      ?? ctx.Totals.GetValueOrDefault(field);
            return AmountToPersianWords(ParseDecimal(raw));
        }

        return "";
    }

    private static string FirstNonEmpty(Dictionary<string, string?> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = data.GetValueOrDefault(key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return "";
    }

    private static string ToJalali(DateTime value, bool withTime)
    {
        var pc = new PersianCalendar();
        var text = $"{pc.GetYear(value):0000}/{pc.GetMonth(value):00}/{pc.GetDayOfMonth(value):00}";
        return withTime ? $"{text} {value:HH:mm}" : text;
    }

    private static decimal ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;
        raw = raw.Replace(",", "").Replace("٬", "").Trim();
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static string FormatNumber(decimal value) =>
        value.ToString("#,0.##", CultureInfo.InvariantCulture);

    /// <summary>تبدیل ساده مبلغ به حروف فارسی (تا میلیارد).</summary>
    public static string AmountToPersianWords(decimal amount)
    {
        if (amount < 0)
            return "منفی " + AmountToPersianWords(-amount);
        var n = (long)Math.Floor(Math.Abs(amount));
        if (n == 0)
            return "صفر";

        string[] ones =
        [
            "", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه",
            "ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده"
        ];
        string[] tens = ["", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"];
        string[] hundreds = ["", "صد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد"];
        string[] scales = ["", "هزار", "میلیون", "میلیارد"];

        string Triple(int x)
        {
            var parts = new List<string>();
            var h = x / 100;
            var r = x % 100;
            if (h > 0) parts.Add(hundreds[h]);
            if (r > 0)
            {
                if (r < 20) parts.Add(ones[r]);
                else
                {
                    parts.Add(tens[r / 10]);
                    if (r % 10 > 0) parts.Add(ones[r % 10]);
                }
            }
            return string.Join(" و ", parts.Where(p => p.Length > 0));
        }

        var chunks = new List<string>();
        var scale = 0;
        while (n > 0 && scale < scales.Length)
        {
            var chunk = (int)(n % 1000);
            if (chunk > 0)
            {
                var words = Triple(chunk);
                if (scales[scale].Length > 0)
                    words += " " + scales[scale];
                chunks.Insert(0, words.Trim());
            }
            n /= 1000;
            scale++;
        }

        return string.Join(" و ", chunks);
    }

    private sealed class RenderContext
    {
        public Dictionary<string, string?> Record { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Dictionary<string, string?>> LineItems { get; set; } = [];
        public List<Dictionary<string, string?>> Notes { get; set; } = [];
        public List<Dictionary<string, string?>> Attachments { get; set; } = [];
        public Dictionary<string, string?> Totals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string CompanyName { get; set; } = "";
        public string CompanyLogo { get; set; } = "";
        public Dictionary<string, string?> CompanyExtra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? UserFullName { get; set; }
    }
}
