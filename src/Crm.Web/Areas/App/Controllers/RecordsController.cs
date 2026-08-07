using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Security;
using Crm.Infrastructure.Services;
using Crm.Web.Areas.App.Models;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>
/// CRUD عمومی ماژول‌های metadata-driven: لیست/فرم از روی FieldDef ها ساخته می‌شود
/// و دسترسی سه‌لایه در سرویس‌ها اعمال می‌گردد.
/// </summary>
public class RecordsController : AppControllerBase
{
    private readonly MetadataService _metadata;
    private readonly DynamicRecordService _records;
    private readonly RecordAccessService _access;
    private readonly RecordImportExportService _importExport;
    private readonly ListColumnService _listColumns;
    private readonly TemplateRenderer _templates;
    private readonly WordExportService _wordExport;
    private readonly LineItemsService _lineItems;
    private readonly SalesDocumentService _docs;
    private readonly CrmDbContext _db;
    private readonly BusinessModuleSeeder _business;

    public RecordsController(
        MetadataService metadata,
        DynamicRecordService records,
        RecordAccessService access,
        RecordImportExportService importExport,
        ListColumnService listColumns,
        TemplateRenderer templates,
        WordExportService wordExport,
        LineItemsService lineItems,
        SalesDocumentService docs,
        CrmDbContext db,
        BusinessModuleSeeder business)
    {
        _metadata = metadata;
        _records = records;
        _access = access;
        _importExport = importExport;
        _listColumns = listColumns;
        _templates = templates;
        _wordExport = wordExport;
        _lineItems = lineItems;
        _docs = docs;
        _db = db;
        _business = business;
    }

    [HttpGet("/App/m/{moduleName}")]
    public async Task<IActionResult> Index(
        string moduleName, string? q, int page = 1, string? sort = null, string? dir = null, int? view = null)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var tenant = HttpContext.RequestServices.GetRequiredService<Crm.Core.Abstractions.ITenantContext>();
        var savedViews = await _db.SavedViews.AsNoTracking()
            .Where(v => v.ModuleId == module.Id
                        && (v.IsShared || v.OwnerUserId == tenant.UserId))
            .OrderBy(v => v.Name)
            .ToListAsync();

        SavedView? activeView = null;
        if (view is int viewId)
            activeView = savedViews.FirstOrDefault(v => v.Id == viewId);

        var fields = await _metadata.GetFieldsAsync(module.Id);
        var allVisible = fields.Where(f => f.IsVisible).ToList();
        var listFields = (await _listColumns.GetListFieldsAsync(module.Id)).ToList();
        var blocks = await _metadata.GetBlocksAsync(module.Id);

        // ستون‌های ذخیره در نما
        if (activeView is not null && !string.IsNullOrWhiteSpace(activeView.ColumnIdsJson))
        {
            try
            {
                var ids = System.Text.Json.JsonSerializer.Deserialize<List<int>>(activeView.ColumnIdsJson!) ?? [];
                if (ids.Count > 0)
                {
                    var byId = allVisible.ToDictionary(f => f.Id);
                    listFields = ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
                }
            }
            catch (System.Text.Json.JsonException) { /* ignore bad json */ }
        }

        var filters = ParseColumnFilters(Request.Query, listFields);

        // فیلترهای نما اگر در query فیلتر دستی نباشد
        if (activeView is not null && filters.Count == 0 && !string.IsNullOrWhiteSpace(activeView.FiltersJson))
        {
            try
            {
                var fromView = System.Text.Json.JsonSerializer.Deserialize<List<ColumnFilter>>(
                                   activeView.FiltersJson!,
                                   new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                               ?? [];
                filters = fromView;
            }
            catch (System.Text.Json.JsonException) { /* ignore */ }
        }

        if (activeView is not null)
        {
            if (string.IsNullOrWhiteSpace(sort) && !string.IsNullOrWhiteSpace(activeView.SortField))
                sort = activeView.SortField;
            if (string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(activeView.SortDir))
                dir = activeView.SortDir;
        }

        const int pageSize = 20;
        var listQuery = new RecordListQuery
        {
            Search = q,
            Page = Math.Max(1, page),
            PageSize = pageSize,
            SortField = sort,
            SortDir = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc",
            Filters = filters
        };

        var (items, total) = await _records.ListAsync(module.Id, listQuery);
        var recordData = items.ToDictionary(r => r.Id, DynamicRecordService.ParseData);

        var model = new RecordListViewModel
        {
            Module = module,
            Fields = listFields,
            AllFields = allVisible,
            Blocks = blocks,
            SelectedColumnIds = listFields.Select(f => f.Id).ToList(),
            Records = items,
            RecordData = recordData,
            Search = q,
            Page = listQuery.Page,
            PageSize = pageSize,
            TotalCount = total,
            SortField = listQuery.SortField,
            SortDir = listQuery.SortDir,
            Filters = filters,
            CanCreate = await _access.CanCreateAsync(module.Id),
            CanEdit = await _access.CanEditAsync(module.Id),
            CanDelete = await _access.CanDeleteAsync(module.Id),
            LookupTitles = await ResolveLookupTitlesAsync(fields, recordData.Values),
            HasKanban = KanbanController.ModuleSupportsKanban(fields),
            SavedViews = savedViews,
            ActiveViewId = activeView?.Id
        };

        ViewData["Title"] = module.PluralLabel;
        ViewBag.TotalCount = total;
        ViewBag.Page = listQuery.Page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var pagingRoutes = model.RouteValues(page: null)
            .Where(kv => kv.Key != "page")
            .ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
        pagingRoutes["moduleName"] = module.Name;
        if (activeView is not null)
            pagingRoutes["view"] = activeView.Id.ToString();
        ViewBag.PagingRoutes = pagingRoutes;

        return View(model);
    }

    [HttpPost("/App/m/{moduleName}/views")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveView(
        string moduleName,
        string name,
        bool isShared,
        string? filtersJson,
        string? columnIdsJson,
        string? sortField,
        string? sortDir,
        string? viewMode)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "نام نما الزامی است.";
            return RedirectToAction(nameof(Index), new { moduleName });
        }

        var tenant = HttpContext.RequestServices.GetRequiredService<Crm.Core.Abstractions.ITenantContext>();
        var view = new SavedView
        {
            ModuleId = module.Id,
            Name = name.Trim(),
            OwnerUserId = tenant.UserId,
            IsShared = isShared,
            FiltersJson = string.IsNullOrWhiteSpace(filtersJson) ? "[]" : filtersJson.Trim(),
            ColumnIdsJson = string.IsNullOrWhiteSpace(columnIdsJson) ? "[]" : columnIdsJson.Trim(),
            SortField = string.IsNullOrWhiteSpace(sortField) ? null : sortField.Trim(),
            SortDir = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc",
            ViewMode = string.IsNullOrWhiteSpace(viewMode) ? "list" : viewMode.Trim()
        };
        _db.SavedViews.Add(view);
        await _db.SaveChangesAsync();

        TempData["Success"] = "نما ذخیره شد.";
        return RedirectToAction(nameof(Index), new { moduleName, view = view.Id });
    }

    [HttpPost("/App/m/{moduleName}/views/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteView(string moduleName, int id)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        var tenant = HttpContext.RequestServices.GetRequiredService<Crm.Core.Abstractions.ITenantContext>();
        var view = await _db.SavedViews.FirstOrDefaultAsync(v => v.Id == id && v.ModuleId == module.Id);
        if (view is null)
            return NotFound();

        if (view.OwnerUserId != tenant.UserId && !tenant.IsTenantAdmin)
            return Forbid("Identity.Application");

        _db.SavedViews.Remove(view);
        await _db.SaveChangesAsync();
        TempData["Success"] = "نما حذف شد.";
        return RedirectToAction(nameof(Index), new { moduleName });
    }

    [HttpPost("/App/m/{moduleName}/columns")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveColumns(string moduleName, [FromForm] int[]? fieldIds)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        try
        {
            await _listColumns.SaveListColumnsAsync(module.Id, fieldIds ?? []);
            TempData["Success"] = "ستون‌های لیست ذخیره شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { moduleName });
    }

    private static List<ColumnFilter> ParseColumnFilters(
        IQueryCollection query, IReadOnlyList<FieldDef> listFields)
    {
        var byName = listFields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var allowed = byName.Keys.Append("title").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filters = new List<ColumnFilter>();

        foreach (var key in query.Keys)
        {
            if (!key.StartsWith("cf_", StringComparison.OrdinalIgnoreCase))
                continue;

            var field = key[3..];
            if (!allowed.Contains(field))
                continue;

            var isPick = byName.TryGetValue(field, out var def)
                         && def.Type is FieldType.Picklist or FieldType.MultiPicklist;
            var defaultOp = isPick ? "equals" : "contains";
            var op = query.TryGetValue($"op_{field}", out var opVal) && !string.IsNullOrWhiteSpace(opVal)
                ? opVal.ToString()!
                : defaultOp;
            var value = query[key].ToString() ?? "";
            var needsValue = !string.Equals(op, "isempty", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(op, "isnotempty", StringComparison.OrdinalIgnoreCase);
            if (needsValue && string.IsNullOrWhiteSpace(value))
                continue;

            filters.Add(new ColumnFilter
            {
                Field = field,
                Op = op,
                Value = value.Trim()
            });
        }

        return filters;
    }

    /// <summary>عنوان رکوردهای مقصد فیلدهای Lookup را برای نمایش در لیست برمی‌گرداند.</summary>
    private async Task<Dictionary<string, Dictionary<string, string>>> ResolveLookupTitlesAsync(
        IReadOnlyList<FieldDef> fields, IEnumerable<Dictionary<string, string?>> rows)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        var lookupFields = fields.Where(f => f.Type == FieldType.Lookup).ToList();
        if (lookupFields.Count == 0)
            return result;

        var allIds = new HashSet<int>();
        foreach (var row in rows)
            foreach (var field in lookupFields)
                if (row.TryGetValue(field.Name, out var v) && int.TryParse(v, out var id))
                    allIds.Add(id);

        if (allIds.Count == 0)
            return result;

        var titles = await _db.Records.AsNoTracking()
            .Where(r => allIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id.ToString(), r => r.Title);

        foreach (var field in lookupFields)
            result[field.Name] = titles;

        return result;
    }

    [HttpGet("/App/m/{moduleName}/create")]
    public async Task<IActionResult> Create(string moduleName)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanCreateAsync(module.Id))
            return Forbid("Identity.Application");

        if (string.Equals(moduleName, "pricebooks", StringComparison.OrdinalIgnoreCase)
            && HttpContext.RequestServices.GetRequiredService<Crm.Core.Abstractions.ITenantContext>().TenantId is int tid)
        {
            await _business.EnsurePriceBooksStructureAsync(tid);
            module = await _metadata.GetModuleByNameAsync(moduleName) ?? module;
        }
        else if (moduleName is "quotes" or "sales_orders" or "invoices"
            && HttpContext.RequestServices.GetRequiredService<Crm.Core.Abstractions.ITenantContext>().TenantId is int docTid)
        {
            await _business.EnsureDocumentPriceBookFieldAsync(docTid);
            module = await _metadata.GetModuleByNameAsync(moduleName) ?? module;
        }

        // Prefill از query: ?f_organization=12&f_contact=34
        var prefill = new Dictionary<string, string?>();
        foreach (var key in Request.Query.Keys)
        {
            if (!key.StartsWith("f_", StringComparison.OrdinalIgnoreCase))
                continue;
            var fieldName = key[2..];
            var val = Request.Query[key].ToString();
            if (!string.IsNullOrWhiteSpace(fieldName) && !string.IsNullOrWhiteSpace(val))
                prefill[fieldName] = val;
        }

        var model = await BuildFormModelAsync(module, recordId: null, values: prefill.Count > 0 ? prefill : null);
        ViewData["Title"] = $"{module.SingularLabel} جدید";
        return View("Form", model);
    }

    [HttpPost("/App/m/{moduleName}/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string moduleName, IFormCollection form)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanCreateAsync(module.Id))
            return Forbid("Identity.Application");

        var values = ExtractFieldValues(form);
        try
        {
            var record = await _records.CreateAsync(module.Id, values);
            var tracked = await _db.Records.FirstAsync(r => r.Id == record.Id);
            await _docs.AssignNumberIfNeededAsync(module, tracked);
            await _lineItems.SaveFromFormAsync(module.Id, tracked.Id, form);
            TempData["Success"] = $"{module.SingularLabel} «{tracked.Title}» ثبت شد.";
            return RedirectToAction(nameof(Details), new { moduleName, id = tracked.Id });
        }
        catch (RecordValidationException ex)
        {
            var model = await BuildFormModelAsync(module, recordId: null, values);
            model.Errors = new Dictionary<string, string>(ex.Errors);
            ViewData["Title"] = $"{module.SingularLabel} جدید";
            return View("Form", model);
        }
    }

    [HttpGet("/App/m/{moduleName}/{id:int}/edit")]
    public async Task<IActionResult> Edit(string moduleName, int id)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanEditAsync(module.Id))
            return Forbid("Identity.Application");

        if (string.Equals(moduleName, "pricebooks", StringComparison.OrdinalIgnoreCase)
            && HttpContext.RequestServices.GetRequiredService<Crm.Core.Abstractions.ITenantContext>().TenantId is int tid)
        {
            await _business.EnsurePriceBooksStructureAsync(tid);
            module = await _metadata.GetModuleByNameAsync(moduleName) ?? module;
        }
        else if (moduleName is "quotes" or "sales_orders" or "invoices"
            && HttpContext.RequestServices.GetRequiredService<Crm.Core.Abstractions.ITenantContext>().TenantId is int docTid)
        {
            await _business.EnsureDocumentPriceBookFieldAsync(docTid);
            module = await _metadata.GetModuleByNameAsync(moduleName) ?? module;
        }

        var record = await _records.GetAsync(module.Id, id);
        if (record is null)
            return NotFound();

        var model = await BuildFormModelAsync(module, record.Id, DynamicRecordService.ParseData(record));
        ViewData["Title"] = $"ویرایش {module.SingularLabel}";
        return View("Form", model);
    }

    [HttpPost("/App/m/{moduleName}/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string moduleName, int id, IFormCollection form)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanEditAsync(module.Id))
            return Forbid("Identity.Application");

        var values = ExtractFieldValues(form);
        try
        {
            await _records.UpdateAsync(module.Id, id, values);
            await _lineItems.SaveFromFormAsync(module.Id, id, form);
            TempData["Success"] = "تغییرات ذخیره شد.";
            return RedirectToAction(nameof(Details), new { moduleName, id });
        }
        catch (RecordValidationException ex)
        {
            var model = await BuildFormModelAsync(module, id, values);
            model.Errors = new Dictionary<string, string>(ex.Errors);
            ViewData["Title"] = $"ویرایش {module.SingularLabel}";
            return View("Form", model);
        }
        catch (UnauthorizedAccessException)
        {
            TempData["Error"] = "شما اجازه ویرایش این رکورد را ندارید.";
            return RedirectToAction(nameof(Index), new { moduleName });
        }
    }

    [HttpPost("/App/m/{moduleName}/{id:int}/clone")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clone(string moduleName, int id)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanCreateAsync(module.Id))
            return Forbid("Identity.Application");

        try
        {
            var clone = await _records.CloneAsync(module.Id, id);
            TempData["Success"] = $"کپی «{clone.Title}» ایجاد شد.";
            return RedirectToAction(nameof(Edit), new { moduleName, id = clone.Id });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid("Identity.Application");
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("/App/m/{moduleName}/{id:int}/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDocument(string moduleName, int id)
    {
        try
        {
            await _docs.ConfirmAsync(moduleName, id);
            TempData["Success"] = "سند تأیید شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { moduleName, id });
    }

    [HttpPost("/App/m/{moduleName}/{id:int}/convert")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConvertDocument(string moduleName, int id)
    {
        try
        {
            var created = await _docs.ConvertAsync(moduleName, id);
            var target = await _db.Modules.AsNoTracking().FirstAsync(m => m.Id == created.ModuleId);
            TempData["Success"] = $"سند به «{target.SingularLabel}» تبدیل شد.";
            return RedirectToAction(nameof(Details), new { moduleName = target.Name, id = created.Id });
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { moduleName, id });
        }
    }

    [HttpPost("/App/m/{moduleName}/{id:int}/pay")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPayment(string moduleName, int id, decimal amount, string method = "transfer", string? reference = null, string? note = null)
    {
        if (!string.Equals(moduleName, "invoices", StringComparison.OrdinalIgnoreCase))
            return BadRequest();
        try
        {
            await _docs.AddPaymentAsync(id, amount, method, reference, note);
            TempData["Success"] = "پرداخت ثبت شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { moduleName, id });
    }

    [HttpPost("/App/m/{moduleName}/{id:int}/installments")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInstallments(string moduleName, int id, int count = 3, DateTime? firstDue = null)
    {
        if (!string.Equals(moduleName, "invoices", StringComparison.OrdinalIgnoreCase))
            return BadRequest();
        try
        {
            await _docs.CreateInstallmentsAsync(id, count, firstDue ?? DateTime.UtcNow.Date);
            TempData["Success"] = "اقساط ایجاد شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { moduleName, id });
    }

    [HttpPost("/App/m/{moduleName}/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string moduleName, int id)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanDeleteAsync(module.Id))
            return Forbid("Identity.Application");

        try
        {
            await _records.DeleteAsync(module.Id, id);
            TempData["Success"] = "رکورد به سطل بازیابی منتقل شد.";
        }
        catch (UnauthorizedAccessException)
        {
            TempData["Error"] = "شما اجازه حذف این رکورد را ندارید.";
        }

        return RedirectToAction(nameof(Index), new { moduleName });
    }

    [HttpPost("/App/m/{moduleName}/bulk-delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDelete(string moduleName, int[]? ids, string? returnUrl = null)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanDeleteAsync(module.Id))
            return Forbid("Identity.Application");

        if (ids is null || ids.Length == 0)
        {
            TempData["Error"] = "موردی انتخاب نشده است.";
            return RedirectToAction(nameof(Index), new { moduleName });
        }

        var deleted = 0;
        foreach (var id in ids.Distinct().Take(200))
        {
            try
            {
                await _records.DeleteAsync(module.Id, id);
                deleted++;
            }
            catch (UnauthorizedAccessException)
            {
                // skip unauthorized rows
            }
        }

        TempData["Success"] = deleted > 0
            ? $"{deleted} رکورد به سطل بازیابی منتقل شد."
            : "هیچ رکوردی حذف نشد.";
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction(nameof(Index), new { moduleName });
    }

    /// <summary>چاپ رکورد با قالب انتخابی (یا پیش‌فرض ماژول).</summary>
    [HttpGet("/App/m/{moduleName}/{id:int}/print")]
    public async Task<IActionResult> Print(string moduleName, int id, int? templateId = null)
    {
        var (module, record, template, error) = await ResolvePrintAsync(moduleName, id, templateId);
        if (error is not null)
            return error;
        if (module is null || record is null || template is null)
            return NotFound();

        if (!template.AllowPdf)
        {
            TempData["Error"] = "خروجی چاپ برای این قالب غیرفعال است.";
            return RedirectToAction(nameof(Details), new { moduleName, id });
        }

        var (lines, totals) = await BuildPrintLinesAndTotalsAsync(module.Id, record);
        var parts = await _templates.RenderPartsAsync(template, record, lineItems: lines, totals: totals);
        ViewBag.PrintTitle = parts.Title;
        ViewBag.PageCss = TemplateRenderer.BuildPageCss(template, fontBaseUrl: null);
        ViewBag.WatermarkHtml = TemplateRenderer.BuildWatermarkHtml(template);
        ViewBag.TextDirection = template.TextDirection == "ltr" ? "ltr" : "rtl";
        ViewBag.HeaderHtml = parts.Header;
        ViewBag.BodyHtml = parts.Body;
        ViewBag.FooterHtml = parts.Footer;
        return View("Print");
    }

    /// <summary>خروجی Word (DOCX) از قالب چاپ.</summary>
    [HttpGet("/App/m/{moduleName}/{id:int}/word")]
    public async Task<IActionResult> ExportWord(string moduleName, int id, int? templateId = null)
    {
        var (module, record, template, error) = await ResolvePrintAsync(moduleName, id, templateId);
        if (error is not null)
            return error;
        if (module is null || record is null || template is null)
            return NotFound();

        if (!template.AllowWord)
        {
            TempData["Error"] = "خروجی Word برای این قالب غیرفعال است.";
            return RedirectToAction(nameof(Details), new { moduleName, id });
        }

        var (lines, totals) = await BuildPrintLinesAndTotalsAsync(module.Id, record);
        var html = await _templates.RenderAsync(template, record, lines, totals);
        var bytes = _wordExport.HtmlToDocx(html, record.Title);
        var fallback = record.Title.Length == 0 ? module.Name : record.Title;
        var safeName = _templates.ResolveFileName(template, record, fallback);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            $"{safeName}-{DateTime.Now:yyyyMMdd-HHmm}.docx");
    }

    private async Task<(IReadOnlyList<Dictionary<string, string?>> Lines, Dictionary<string, string?> Totals)>
        BuildPrintLinesAndTotalsAsync(int moduleId, DynamicRecord record)
    {
        var lines = await _lineItems.LoadLinesAsync(moduleId, record.Id);
        var data = DynamicRecordService.ParseData(record);
        string Pick(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (data.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                    return v!;
            }
            return "";
        }

        var totals = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["subTotal"] = Pick("subTotal", "sub_total"),
            ["taxTotal"] = Pick("taxTotal", "tax_total"),
            ["grandTotal"] = Pick("grandTotal", "grand_total"),
            ["discountAmount"] = Pick("discountAmount", "discount_amount"),
            ["discount"] = Pick("discountAmount", "discount_amount", "discount")
        };
        return (lines, totals);
    }

    private async Task<(ModuleDef? Module, DynamicRecord? Record, PrintTemplate? Template, IActionResult? Error)>
        ResolvePrintAsync(string moduleName, int id, int? templateId)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return (null, null, null, NotFound());

        if (!await _access.CanViewModuleAsync(module.Id))
            return (null, null, null, Forbid("Identity.Application"));

        var record = await _records.GetAsync(module.Id, id);
        if (record is null)
            return (null, null, null, NotFound());

        var templatesQuery = AccessiblePrintTemplatesQuery(module.Id);

        PrintTemplate? template;
        if (templateId is int tid)
        {
            template = await templatesQuery.FirstOrDefaultAsync(t => t.Id == tid);
        }
        else
        {
            template = await templatesQuery
                .OrderByDescending(t => t.IsDefault)
                .ThenBy(t => t.Id)
                .FirstOrDefaultAsync();
        }

        if (template is null)
        {
            TempData["Error"] = "قالب چاپی برای این ماژول تعریف نشده است.";
            return (module, record, null,
                RedirectToAction(nameof(Details), new { moduleName, id }));
        }

        return (module, record, template, null);
    }

    /// <summary>قالب‌های فعال ماژول که با نقش جاری اشتراک شده‌اند (ادمین Tenant همه را می‌بیند).</summary>
    private IQueryable<PrintTemplate> AccessiblePrintTemplatesQuery(int moduleId)
    {
        var query = _db.PrintTemplates.AsNoTracking()
            .Where(t => t.ModuleId == moduleId && t.IsActive);

        var tenant = HttpContext.RequestServices.GetRequiredService<Crm.Core.Abstractions.ITenantContext>();
        if (!tenant.IsTenantAdmin)
        {
            var roleId = tenant.RoleId;
            query = query.Where(t =>
                t.ShareWithAllRoles
                || (roleId != null && _db.PrintTemplateRoles.Any(r =>
                    r.PrintTemplateId == t.Id && r.RoleId == roleId)));
        }

        return query;
    }

    private async Task<List<PrintTemplateOption>> ListAccessiblePrintTemplatesAsync(int moduleId)
    {
        return await AccessiblePrintTemplatesQuery(moduleId)
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Name)
            .Select(t => new PrintTemplateOption
            {
                Id = t.Id,
                Name = t.Name,
                IsDefault = t.IsDefault,
                AllowPdf = t.AllowPdf,
                AllowWord = t.AllowWord,
                PageSize = t.PageSize,
                Landscape = t.Landscape
            })
            .ToListAsync();
    }

    [HttpGet("/App/m/{moduleName}/{id:int}")]
    public async Task<IActionResult> Details(string moduleName, int id)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        if (string.Equals(moduleName, "pricebooks", StringComparison.OrdinalIgnoreCase)
            && HttpContext.RequestServices.GetRequiredService<Crm.Core.Abstractions.ITenantContext>().TenantId is int tid)
        {
            await _business.EnsurePriceBooksStructureAsync(tid);
            module = await _metadata.GetModuleByNameAsync(moduleName) ?? module;
        }

        var record = await _records.GetAsync(module.Id, id);
        if (record is null)
            return NotFound();

        var fields = (await _metadata.GetFieldsAsync(module.Id))
            .Where(f => f.IsVisible)
            .ToList();
        var fieldAccess = await _access.GetFieldAccessMapAsync(module.Id);
        if (fieldAccess.Count > 0)
            fields = fields.Where(f => !fieldAccess.TryGetValue(f.Id, out var a) || a != FieldAccess.Hidden).ToList();

        var blocks = await _metadata.GetBlocksAsync(module.Id);
        var values = DynamicRecordService.ParseData(record);
        var canEditModule = await _access.CanEditAsync(module.Id);
        var canDeleteModule = await _access.CanDeleteAsync(module.Id);
        var canModifyRecord = await _access.CanModifyRecordAsync(record);
        var lookupTitles = await ResolveLookupTitlesAsync(fields, [values]);

        var auditLogs = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.ModuleName == module.Name && a.RecordId == id)
            .OrderByDescending(a => a.AtUtc)
            .Take(50)
            .ToListAsync();

        var userIds = new HashSet<int>();
        if (record.CreatedByUserId is int cby) userIds.Add(cby);
        if (record.UpdatedByUserId is int uby) userIds.Add(uby);
        foreach (var a in auditLogs)
            if (a.UserId is int uid) userIds.Add(uid);

        var userNames = userIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(
                    u => u.Id,
                    u => !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName!
                        : (!string.IsNullOrWhiteSpace(u.UserName) ? u.UserName! : $"کاربر #{u.Id}"));

        var model = new RecordDetailViewModel
        {
            Module = module,
            Record = record,
            Fields = fields,
            Blocks = blocks,
            Values = values,
            LookupTitles = lookupTitles,
            CanEdit = canEditModule && canModifyRecord,
            CanDelete = canDeleteModule && canModifyRecord,
            Notes = await _db.Notes.AsNoTracking()
                .Where(n => n.ModuleName == module.Name && n.RecordId == id)
                .OrderByDescending(n => n.CreatedAtUtc)
                .Take(100)
                .ToListAsync(),
            AuditLogs = auditLogs,
            AuditUserNames = userNames,
            CreatedByName = record.CreatedByUserId is int cb && userNames.TryGetValue(cb, out var cbn) ? cbn : null,
            UpdatedByName = record.UpdatedByUserId is int ub && userNames.TryGetValue(ub, out var ubn) ? ubn : null,
            Attachments = await _db.Attachments.AsNoTracking()
                .Where(a => a.ModuleName == module.Name && a.RecordId == id)
                .OrderByDescending(a => a.CreatedAtUtc)
                .Take(50)
                .ToListAsync(),
            Tags = await _db.TagLinks.AsNoTracking()
                .Where(t => t.ModuleName == module.Name && t.RecordId == id)
                .Include(t => t.Tag)
                .Select(t => t.Tag)
                .ToListAsync(),
            PrintTemplates = await ListAccessiblePrintTemplatesAsync(module.Id)
        };

        var (lineBlock, lineModule, lineFields) = await _lineItems.GetLineBlockAsync(module.Id);
        if (lineBlock is not null && lineModule is not null && !string.IsNullOrWhiteSpace(lineBlock.LineLinkField))
        {
            model.LineBlock = lineBlock;
            var isPriceBook = string.Equals(module.Name, "pricebooks", StringComparison.OrdinalIgnoreCase);
            var visibleLineNames = isPriceBook
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "product", "title", "unitPrice" }
                : null;
            model.LineFields = lineFields
                .Where(f => !string.Equals(f.Name, "sortOrder", StringComparison.OrdinalIgnoreCase))
                .Where(f => visibleLineNames is null || visibleLineNames.Contains(f.Name))
                .ToList();
            model.LineItems = await _lineItems.LoadLinesAsync(lineModule.Id, lineBlock.LineLinkField!, id);

            var lineLookups = lineFields
                .Where(f => f.Type == FieldType.Lookup
                            && !string.Equals(f.Name, "product", StringComparison.OrdinalIgnoreCase))
                .ToList();
            model.LineLookupTitles = await ResolveLookupTitlesAsync(lineLookups, model.LineItems);

            var productIds = model.LineItems
                .Select(r => r.GetValueOrDefault("product"))
                .Where(v => !string.IsNullOrWhiteSpace(v) && int.TryParse(v, out _))
                .Select(v => int.Parse(v!))
                .Distinct()
                .ToList();
            if (productIds.Count > 0)
            {
                var productTitles = await _db.Products.AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id.ToString(), p => p.Name);
                if (productTitles.Count > 0)
                    model.LineLookupTitles["product"] = productTitles;
            }
        }

        if (string.Equals(module.Name, "invoices", StringComparison.OrdinalIgnoreCase))
        {
            model.InvoicePayments = await _docs.LoadPaymentsAsync(id);
            model.InvoiceGrandTotal = ParseMoney(values, "grandTotal", ParseMoney(values, "amount", 0));
            model.InvoicePaidAmount = ParseMoney(values, "paidAmount", 0);
            if (model.InvoicePaidAmount <= 0 && model.InvoicePayments.Count > 0)
            {
                model.InvoicePaidAmount = model.InvoicePayments.Sum(p =>
                    ParseMoney(p, "amount", 0));
            }
            model.InvoiceRemainingAmount = ParseMoney(values, "remainingAmount",
                Math.Max(0, model.InvoiceGrandTotal - model.InvoicePaidAmount));
        }

        if (module.Name is "quotes" or "sales_orders" or "invoices")
        {
            var pbRaw = values.GetValueOrDefault("priceBook");
            if (int.TryParse(pbRaw, out var pbId) && pbId > 0)
            {
                model.PriceBookId = pbId;
                if (lookupTitles.TryGetValue("priceBook", out var pbTitles)
                    && pbTitles.TryGetValue(pbRaw!, out var pbName))
                    model.PriceBookName = pbName;
                else
                {
                    var title = await _db.Records.AsNoTracking()
                        .Where(r => r.Id == pbId)
                        .Select(r => r.Title)
                        .FirstOrDefaultAsync();
                    model.PriceBookName = title;
                }
            }
        }

        // فقط ماژول‌های سطر سند (*_lines) از مرتبط‌ها حذف شوند — نه payments/installments
        var skipModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (lineModule is not null)
            skipModules.Add(lineModule.Name);

        var inbound = await LoadInboundRelatedAsync(module, id, skipModules);
        model.Activities = inbound
            .Where(r => ActivityModuleNames.Contains(r.ModuleName))
            .ToList();
        model.Relations = await BuildRelationGroupsAsync(
            module, id, values, fields, inbound, lookupTitles, skipModules, loadCandidates: false);
        if (string.Equals(module.Name, "invoices", StringComparison.OrdinalIgnoreCase))
        {
            model.Relations = model.Relations
                .Where(g => !string.Equals(g.ModuleName, "payments", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        ViewData["Title"] = record.Title;
        ViewData["PanelTitle"] = module.PluralLabel;
        return View(model);
    }

    /// <summary>قیمت محصول از سطرهای دفترچه قیمت داینامیک؛ در صورت نبود، SalePrice.</summary>
    [HttpGet("/App/m/pricebooks/price")]
    public async Task<IActionResult> PriceBookUnitPrice(int priceBookId, int productId)
    {
        if (productId <= 0)
            return BadRequest();

        var product = await _db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.SalePrice, p.TaxPercent })
            .FirstOrDefaultAsync();
        if (product is null)
            return NotFound();

        if (priceBookId > 0)
        {
            var pbModule = await _metadata.GetModuleByNameAsync("pricebooks");
            if (pbModule is not null && await _access.CanViewModuleAsync(pbModule.Id))
            {
                var lines = await _lineItems.LoadLinesAsync(pbModule.Id, priceBookId);
                var match = lines.FirstOrDefault(l =>
                    string.Equals(l.GetValueOrDefault("product"), productId.ToString(), StringComparison.Ordinal));
                if (match is not null)
                {
                    var raw = match.GetValueOrDefault("unitPrice") ?? match.GetValueOrDefault("price");
                    if (decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var bookPrice)
                        || decimal.TryParse(raw, out bookPrice))
                    {
                        var taxRaw = match.GetValueOrDefault("taxPercent");
                        var tax = product.TaxPercent;
                        if (decimal.TryParse(taxRaw, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var t)
                            || decimal.TryParse(taxRaw, out t))
                            tax = t;
                        return Json(new { price = bookPrice, tax, fromBook = true });
                    }
                }
            }
        }

        return Json(new { price = product.SalePrice, tax = product.TaxPercent, fromBook = false });
    }

    /// <summary>جستجوی Ajax برای فیلد Lookup فرم (Select2).</summary>
    [HttpGet("/App/m/{moduleName}/lookup/{fieldName}")]
    public async Task<IActionResult> LookupSearch(
        string moduleName,
        string fieldName,
        string? q = null,
        int page = 1)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();
        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var fields = await _metadata.GetFieldsAsync(module.Id);
        var field = fields.FirstOrDefault(f =>
            string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase)
            && f.Type == FieldType.Lookup
            && !string.IsNullOrWhiteSpace(f.LookupModule));
        if (field is null || !field.IsVisible)
            return NotFound();

        var fieldAccess = await _access.GetFieldAccessMapAsync(module.Id);
        if (fieldAccess.TryGetValue(field.Id, out var fa) && fa == FieldAccess.Hidden)
            return Forbid("Identity.Application");

        var target = await _metadata.GetModuleByNameAsync(field.LookupModule!);
        if (target is null)
            return NotFound();
        if (!await _access.CanViewModuleAsync(target.Id))
            return Forbid("Identity.Application");

        // ReadOnly: فقط برای نمایش مقدار فعلی، جستجو خالی
        if (fieldAccess.TryGetValue(field.Id, out var accessLevel) && accessLevel == FieldAccess.ReadOnly)
            return Json(new { results = Array.Empty<object>(), pagination = new { more = false } });

        var term = (q ?? string.Empty).Trim();
        if (term.Length > 80) term = term[..80];
        page = Math.Max(1, page);
        const int pageSize = 30;

        var (items, _) = await _records.ListAsync(
            target.Id,
            search: string.IsNullOrWhiteSpace(term) ? null : term,
            page: page,
            pageSize: pageSize + 1,
            includeTotal: false);

        var more = items.Count > pageSize;
        var pageItems = more ? items.Take(pageSize) : items;

        return Json(new
        {
            results = pageItems.Select(r => new { id = r.Id, text = string.IsNullOrWhiteSpace(r.Title) ? $"#{r.Id}" : r.Title }),
            pagination = new { more }
        });
    }

    /// <summary>جستجوی Ajax محصولات typed برای خط‌اقلام / فرم‌های مالی.</summary>
    [HttpGet("/App/lookup/products")]
    public async Task<IActionResult> ProductLookupSearch(string? q = null, int page = 1)
    {
        var productsModule = await _metadata.GetModuleByNameAsync("products");
        if (productsModule is not null && !await _access.CanViewModuleAsync(productsModule.Id))
            return Forbid("Identity.Application");

        var term = (q ?? string.Empty).Trim();
        if (term.Length > 80) term = term[..80];
        page = Math.Max(1, page);
        const int pageSize = 30;

        var query = _db.Products.AsNoTracking().Where(p => p.IsActive && !p.IsDeleted);
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(p => EF.Functions.ILike(p.Name, "%" + term + "%")
                                     || (p.Sku != null && EF.Functions.ILike(p.Sku, "%" + term + "%")));

        var rows = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .Select(p => new { p.Id, p.Name, p.SalePrice, p.TaxPercent })
            .ToListAsync();

        var more = rows.Count > pageSize;
        var pageItems = more ? rows.Take(pageSize) : rows;

        return Json(new
        {
            results = pageItems.Select(p => new
            {
                id = p.Id,
                text = p.Name,
                price = p.SalePrice,
                tax = p.TaxPercent
            }),
            pagination = new { more }
        });
    }

    /// <summary>جستجوی Ajax تأمین‌کنندگان typed.</summary>
    [HttpGet("/App/lookup/vendors")]
    public async Task<IActionResult> VendorLookupSearch(string? q = null, int page = 1)
    {
        var vendorsModule = await _metadata.GetModuleByNameAsync("vendors");
        if (vendorsModule is not null && !await _access.CanViewModuleAsync(vendorsModule.Id))
            return Forbid("Identity.Application");

        var term = (q ?? string.Empty).Trim();
        if (term.Length > 80) term = term[..80];
        page = Math.Max(1, page);
        const int pageSize = 30;

        var query = _db.Vendors.AsNoTracking().Where(v => !v.IsDeleted);
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(v => EF.Functions.ILike(v.Name, "%" + term + "%"));

        var rows = await query
            .OrderBy(v => v.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .Select(v => new { v.Id, v.Name })
            .ToListAsync();

        var more = rows.Count > pageSize;
        var pageItems = more ? rows.Take(pageSize) : rows;

        return Json(new
        {
            results = pageItems.Select(v => new { id = v.Id, text = v.Name }),
            pagination = new { more }
        });
    }

    /// <summary>جستجوی Ajax مستقیم روی یک ماژول داینامیک (برای فرم‌های typed مثل مخاطب).</summary>
    [HttpGet("/App/lookup/module/{targetModule}")]
    public async Task<IActionResult> ModuleLookupSearch(string targetModule, string? q = null, int page = 1)
    {
        var target = await _metadata.GetModuleByNameAsync(targetModule);
        if (target is null)
            return NotFound();
        if (!await _access.CanViewModuleAsync(target.Id))
            return Forbid("Identity.Application");

        var term = (q ?? string.Empty).Trim();
        if (term.Length > 80) term = term[..80];
        page = Math.Max(1, page);
        const int pageSize = 30;

        var (items, _) = await _records.ListAsync(
            target.Id,
            search: string.IsNullOrWhiteSpace(term) ? null : term,
            page: page,
            pageSize: pageSize + 1,
            includeTotal: false);

        var more = items.Count > pageSize;
        var pageItems = more ? items.Take(pageSize) : items;

        return Json(new
        {
            results = pageItems.Select(r => new { id = r.Id, text = string.IsNullOrWhiteSpace(r.Title) ? $"#{r.Id}" : r.Title }),
            pagination = new { more }
        });
    }

    /// <summary>کاندیداهای اتصال مرتبط برای Select2 آژاکس (بدون لود سنگین در Details).</summary>
    [HttpGet("/App/m/{moduleName}/{id:int}/link-candidates")]
    public async Task<IActionResult> LinkCandidates(
        string moduleName,
        int id,
        string relatedModule,
        string? linkField = null,
        int? relationId = null,
        string? q = null,
        int page = 1)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();
        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var parent = await _records.GetAsync(module.Id, id);
        if (parent is null)
            return NotFound();

        var related = await _metadata.GetModuleByNameAsync(relatedModule);
        if (related is null)
            return NotFound();
        if (!await _access.CanViewModuleAsync(related.Id))
            return Forbid("Identity.Application");

        List<(int Id, string Title)> candidates;
        if (relationId is int rid)
        {
            var existing = await _db.RecordLinks.AsNoTracking()
                .Where(l => l.RelationId == rid && (l.LeftRecordId == id || l.RightRecordId == id))
                .Select(l => l.LeftRecordId == id ? l.RightRecordId : l.LeftRecordId)
                .ToListAsync();
            candidates = await LoadM2MCandidatesAsync(relatedModule, existing.ToHashSet(), q, page);
        }
        else if (!string.IsNullOrWhiteSpace(linkField))
        {
            candidates = await LoadLinkCandidatesAsync(relatedModule, linkField, id, [], q, page);
        }
        else
        {
            candidates = [];
        }

        return Json(new
        {
            results = candidates.Select(c => new { id = c.Id, text = c.Title }),
            pagination = new { more = candidates.Count >= 30 }
        });
    }

    [HttpPost("/App/m/{moduleName}/{id:int}/notes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNote(string moduleName, int id, string text)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var record = await _records.GetAsync(module.Id, id);
        if (record is null)
            return NotFound();

        var body = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            TempData["Error"] = "متن یادداشت خالی است.";
            return RedirectToAction(nameof(Details), new { moduleName, id });
        }

        if (body.Length > 4000)
            body = body[..4000];

        _db.Notes.Add(new Note
        {
            ModuleName = module.Name,
            RecordId = id,
            Body = body
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "یادداشت ثبت شد.";
        return Redirect($"/App/m/{module.Name}/{id}#notes");
    }

    [HttpPost("/App/m/{moduleName}/bulk-notes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAddNotes(string moduleName, int[]? ids, string text, string? returnUrl = null)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        if (ids is null || ids.Length == 0)
        {
            TempData["Error"] = "موردی انتخاب نشده است.";
            return LocalOrList(moduleName, returnUrl);
        }

        var body = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            TempData["Error"] = "متن یادداشت خالی است.";
            return LocalOrList(moduleName, returnUrl);
        }

        if (body.Length > 4000)
            body = body[..4000];

        var added = 0;
        foreach (var id in ids.Distinct().Take(200))
        {
            var record = await _records.GetAsync(module.Id, id);
            if (record is null)
                continue;
            _db.Notes.Add(new Note
            {
                ModuleName = module.Name,
                RecordId = id,
                Body = body
            });
            added++;
        }

        if (added > 0)
            await _db.SaveChangesAsync();

        TempData["Success"] = added > 0
            ? $"یادداشت برای {added} رکورد ثبت شد."
            : "هیچ یادداشتی ثبت نشد.";
        return LocalOrList(moduleName, returnUrl);
    }

    [HttpPost("/App/m/{moduleName}/tags/assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignTags(string moduleName, int[]? ids, string tagName, string? color = null, string? returnUrl = null)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanEditAsync(module.Id))
            return Forbid("Identity.Application");

        var name = (tagName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || ids is null || ids.Length == 0)
        {
            TempData["Error"] = "برچسب یا رکورد مشخص نیست.";
            return LocalOrList(moduleName, returnUrl);
        }

        if (name.Length > 64)
            name = name[..64];

        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Name == name);
        if (tag is null)
        {
            tag = new Tag
            {
                Name = name,
                Color = string.IsNullOrWhiteSpace(color) ? "#696cff" : color.Trim()
            };
            _db.Tags.Add(tag);
            await _db.SaveChangesAsync();
        }
        else if (!string.IsNullOrWhiteSpace(color) && tag.Color != color)
        {
            tag.Color = color.Trim();
        }

        var linked = 0;
        foreach (var id in ids.Distinct().Take(200))
        {
            var record = await _records.GetAsync(module.Id, id);
            if (record is null)
                continue;

            var exists = await _db.TagLinks.AnyAsync(l =>
                l.TagId == tag.Id && l.ModuleName == module.Name && l.RecordId == id);
            if (exists)
                continue;

            _db.TagLinks.Add(new TagLink
            {
                TagId = tag.Id,
                ModuleName = module.Name,
                RecordId = id
            });
            linked++;
        }

        if (linked > 0)
            await _db.SaveChangesAsync();

        TempData["Success"] = linked > 0
            ? $"برچسب «{tag.Name}» به {linked} رکورد اضافه شد."
            : "برچسب قبلاً روی رکوردهای انتخابی بود.";
        return LocalOrList(moduleName, returnUrl);
    }

    [HttpPost("/App/m/{moduleName}/tags/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTags(string moduleName, int[]? ids, int tagId, string? returnUrl = null)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanEditAsync(module.Id))
            return Forbid("Identity.Application");

        if (ids is null || ids.Length == 0 || tagId <= 0)
        {
            TempData["Error"] = "برچسب یا رکورد مشخص نیست.";
            return LocalOrList(moduleName, returnUrl);
        }

        var idSet = ids.Distinct().Take(200).ToHashSet();
        var links = await _db.TagLinks
            .Where(l => l.TagId == tagId && l.ModuleName == module.Name && idSet.Contains(l.RecordId))
            .ToListAsync();
        if (links.Count > 0)
        {
            _db.TagLinks.RemoveRange(links);
            await _db.SaveChangesAsync();
        }

        TempData["Success"] = links.Count > 0
            ? $"برچسب از {links.Count} رکورد برداشته شد."
            : "برچسبی برای حذف یافت نشد.";
        return LocalOrList(moduleName, returnUrl);
    }

    private IActionResult LocalOrList(string moduleName, string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction(nameof(Index), new { moduleName });
    }

    private static readonly HashSet<string> ActivityModuleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "tasks", "events", "calls", "activities", "activity"
    };

    private sealed class RelatedSqlRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string ModuleLabel { get; set; } = string.Empty;
    }

    private static decimal ParseMoney(Dictionary<string, string?> data, string key, decimal fallback)
    {
        if (!data.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;
        return decimal.TryParse(raw, out v) ? v : fallback;
    }

    private static bool IsSafeJsonKey(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 64
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>رکوردهایی که Lookup آن‌ها به این رکورد اشاره می‌کند.</summary>
    private async Task<List<RelatedRecordItem>> LoadInboundRelatedAsync(
        ModuleDef module, int recordId, HashSet<string>? skipModules = null)
    {
        var lookupFields = await _db.Fields.AsNoTracking()
            .Include(f => f.Module)
            .Where(f => f.Type == FieldType.Lookup
                        && f.LookupModule == module.Name
                        && f.ModuleId != module.Id
                        && (f.Module == null || !f.Module.Name.EndsWith("_lines")))
            .ToListAsync();

        if (skipModules is { Count: > 0 })
        {
            lookupFields = lookupFields
                .Where(f => f.Module is null || !skipModules.Contains(f.Module.Name))
                .ToList();
        }

        var idStr = recordId.ToString();
        var results = new List<RelatedRecordItem>();
        foreach (var field in lookupFields.Where(f => IsSafeJsonKey(f.Name) && f.Module is not null))
        {
            var rows = await _db.Database
                .SqlQuery<RelatedSqlRow>($"""
                    SELECT r."Id" AS "Id",
                           r."Title" AS "Title",
                           m."Name" AS "ModuleName",
                           m."SingularLabel" AS "ModuleLabel"
                    FROM "Records" r
                    INNER JOIN "Modules" m ON m."Id" = r."ModuleId"
                    WHERE r."ModuleId" = {field.ModuleId}
                      AND r."IsDeleted" = FALSE
                      AND r."CustomData" ->> {field.Name} = {idStr}
                    ORDER BY r."Id" DESC
                    LIMIT 40
                    """)
                .ToListAsync();

            foreach (var row in rows)
            {
                results.Add(new RelatedRecordItem
                {
                    ModuleName = row.ModuleName,
                    ModuleLabel = row.ModuleLabel,
                    RecordId = row.Id,
                    Title = row.Title,
                    FieldLabel = field.Label,
                    LinkFieldName = field.Name
                });
            }
        }

        return results
            .GroupBy(r => (r.ModuleName, r.RecordId))
            .Select(g => g.First())
            .ToList();
    }

    private async Task<List<(int Id, string Title)>> LoadLinkCandidatesAsync(
        string relatedModuleName,
        string linkFieldName,
        int parentRecordId,
        HashSet<int> alreadyLinked,
        string? q = null,
        int page = 1)
    {
        if (!IsSafeJsonKey(linkFieldName))
            return [];

        var relatedModule = await _metadata.GetModuleByNameAsync(relatedModuleName);
        if (relatedModule is null)
            return [];
        if (!await _access.CanViewModuleAsync(relatedModule.Id))
            return [];

        var parentStr = parentRecordId.ToString();
        var term = (q ?? string.Empty).Trim();
        if (term.Length > 80) term = term[..80];
        page = Math.Max(1, page);
        const int pageSize = 30;

        // چند صفحهٔ کوچک می‌گیریم تا بعد از فیلتر لینک، به اندازهٔ کافی بماند
        var (items, _) = await _records.ListAsync(
            relatedModule.Id,
            search: string.IsNullOrWhiteSpace(term) ? null : term,
            page: page,
            pageSize: pageSize * 3,
            includeTotal: false);

        var result = new List<(int Id, string Title)>();
        foreach (var r in items)
        {
            if (alreadyLinked.Contains(r.Id)) continue;
            var data = DynamicRecordService.ParseData(r);
            var linked = data.GetValueOrDefault(linkFieldName);
            if (string.Equals(linked, parentStr, StringComparison.Ordinal))
                continue;
            result.Add((r.Id, string.IsNullOrWhiteSpace(r.Title) ? $"#{r.Id}" : r.Title));
            if (result.Count >= pageSize)
                break;
        }

        return result;
    }

    private async Task EnrichRelationGroupsAsync(List<RelatedRecordGroup> groups, int parentRecordId)
    {
        foreach (var g in groups)
        {
            g.ParentRecordId = parentRecordId;
            var linked = g.Records.Select(r => r.RecordId).ToHashSet();

            if (g.IsManyToMany && g.RelationId is int)
            {
                g.LinkCandidates = await LoadM2MCandidatesAsync(g.ModuleName, linked);
                continue;
            }

            if (string.IsNullOrWhiteSpace(g.LinkFieldName))
                continue;
            g.LinkCandidates = await LoadLinkCandidatesAsync(g.ModuleName, g.LinkFieldName, parentRecordId, linked);
        }
    }

    private async Task<List<(int Id, string Title)>> LoadM2MCandidatesAsync(
        string relatedModuleName, HashSet<int> alreadyLinked, string? q = null, int page = 1)
    {
        var relatedModule = await _metadata.GetModuleByNameAsync(relatedModuleName);
        if (relatedModule is null)
            return [];
        if (!await _access.CanViewModuleAsync(relatedModule.Id))
            return [];

        var term = (q ?? string.Empty).Trim();
        if (term.Length > 80) term = term[..80];
        page = Math.Max(1, page);
        const int pageSize = 30;

        var (items, _) = await _records.ListAsync(
            relatedModule.Id,
            search: string.IsNullOrWhiteSpace(term) ? null : term,
            page: page,
            pageSize: pageSize + alreadyLinked.Count + 5,
            includeTotal: false);

        return items
            .Where(r => !alreadyLinked.Contains(r.Id))
            .Take(pageSize)
            .Select(r => (r.Id, string.IsNullOrWhiteSpace(r.Title) ? $"#{r.Id}" : r.Title))
            .ToList();
    }

    private async Task<List<RelatedRecordGroup>> BuildRelationGroupsAsync(
        ModuleDef module,
        int recordId,
        Dictionary<string, string?> values,
        IReadOnlyList<FieldDef> fields,
        IReadOnlyList<RelatedRecordItem> inbound,
        Dictionary<string, Dictionary<string, string>>? lookupTitles = null,
        HashSet<string>? skipModules = null,
        bool loadCandidates = true)
    {
        var groups = new List<RelatedRecordGroup>();

        // Outbound: از LookupTitles از قبل resolve‌شده (بدون N+1)
        var outboundItems = new List<RelatedRecordItem>();
        foreach (var field in fields.Where(f => f.Type == FieldType.Lookup && !string.IsNullOrWhiteSpace(f.LookupModule)))
        {
            if (!values.TryGetValue(field.Name, out var raw) || !int.TryParse(raw, out var relatedId))
                continue;

            string? title = null;
            if (lookupTitles is not null
                && lookupTitles.TryGetValue(field.Name, out var map)
                && map.TryGetValue(relatedId.ToString(), out var t))
                title = t;

            if (string.IsNullOrWhiteSpace(title))
            {
                title = await _db.Records.AsNoTracking()
                    .Where(r => r.Id == relatedId)
                    .Select(r => r.Title)
                    .FirstOrDefaultAsync();
            }

            if (string.IsNullOrWhiteSpace(title))
                continue;

            var relatedModule = await _metadata.GetModuleByNameAsync(field.LookupModule!);
            outboundItems.Add(new RelatedRecordItem
            {
                ModuleName = relatedModule?.Name ?? field.LookupModule!,
                ModuleLabel = relatedModule?.SingularLabel ?? field.LookupModule!,
                RecordId = relatedId,
                Title = title,
                FieldLabel = field.Label,
                LinkFieldName = field.Name
            });
        }

        if (outboundItems.Count > 0)
        {
            foreach (var g in outboundItems.GroupBy(x => x.ModuleName))
            {
                groups.Add(new RelatedRecordGroup
                {
                    Label = g.First().FieldLabel ?? g.First().ModuleLabel,
                    ModuleName = g.Key,
                    TabKey = $"rel-{g.Key}",
                    LinkFieldName = null,
                    ParentRecordId = recordId,
                    Records = g.ToList()
                });
            }
        }

        // Inbound (غیر فعالیت) به‌صورت گروه
        foreach (var g in inbound.Where(r => !ActivityModuleNames.Contains(r.ModuleName)).GroupBy(r => r.ModuleName))
        {
            if (skipModules is not null && skipModules.Contains(g.Key))
                continue;

            groups.Add(new RelatedRecordGroup
            {
                Label = g.First().ModuleLabel,
                ModuleName = g.Key,
                TabKey = $"rel-{g.Key}",
                LinkFieldName = g.First().LinkFieldName,
                ParentRecordId = recordId,
                Records = g.ToList()
            });
        }

        // RelationDef: برچسب رابطه + پر کردن از lookup بین دو ماژول (با LinkFieldName در صورت وجود)
        var relations = await _db.Relations.AsNoTracking()
            .Where(r => r.SourceModuleId == module.Id || r.TargetModuleId == module.Id)
            .ToListAsync();

        if (relations.Count > 0)
        {
            var moduleIds = relations
                .SelectMany(r => new[] { r.SourceModuleId, r.TargetModuleId })
                .Distinct()
                .ToList();
            var moduleMap = await _db.Modules.AsNoTracking()
                .Where(m => moduleIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id);

            var idStr = recordId.ToString();

            foreach (var rel in relations)
            {
                var otherId = rel.SourceModuleId == module.Id ? rel.TargetModuleId : rel.SourceModuleId;
                if (!moduleMap.TryGetValue(otherId, out var other))
                    continue;
                if (other.IsChildModule && other.Name.EndsWith("_lines", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (skipModules is not null && skipModules.Contains(other.Name))
                    continue;

                var isM2M = rel.IsManyToMany || rel.Kind == RelationKind.ManyToMany;
                List<RelatedRecordItem> matched;
                string? linkField = rel.LinkFieldName;

                if (isM2M)
                {
                    var linkRows = await _db.RecordLinks.AsNoTracking()
                        .Where(l => l.RelationId == rel.Id
                                    && (l.LeftRecordId == recordId || l.RightRecordId == recordId))
                        .ToListAsync();
                    var otherRecordIds = linkRows
                        .Select(l => l.LeftRecordId == recordId ? l.RightRecordId : l.LeftRecordId)
                        .Distinct()
                        .ToList();

                    matched = [];
                    if (otherRecordIds.Count > 0)
                    {
                        var rows = await _db.Records.AsNoTracking()
                            .Where(r => r.ModuleId == other.Id && otherRecordIds.Contains(r.Id))
                            .Select(r => new { r.Id, r.Title })
                            .ToListAsync();
                        matched = rows.Select(row => new RelatedRecordItem
                        {
                            ModuleName = other.Name,
                            ModuleLabel = other.SingularLabel,
                            RecordId = row.Id,
                            Title = row.Title,
                            FieldLabel = rel.Label
                        }).ToList();
                    }

                    var alreadyM2M = groups.FirstOrDefault(g =>
                        g.RelationId == rel.Id
                        || (g.IsManyToMany
                            && string.Equals(g.ModuleName, other.Name, StringComparison.OrdinalIgnoreCase)));
                    if (alreadyM2M is not null)
                    {
                        alreadyM2M.Label = string.IsNullOrWhiteSpace(rel.Label) ? other.PluralLabel : rel.Label;
                        alreadyM2M.RelationId = rel.Id;
                        alreadyM2M.IsManyToMany = true;
                        if (matched.Count > 0)
                            alreadyM2M.Records = matched;
                        continue;
                    }

                    groups.Add(new RelatedRecordGroup
                    {
                        Label = string.IsNullOrWhiteSpace(rel.Label) ? other.PluralLabel : rel.Label,
                        ModuleName = other.Name,
                        TabKey = $"rel-m2m-{rel.Id}",
                        RelationId = rel.Id,
                        IsManyToMany = true,
                        ParentRecordId = recordId,
                        Records = matched
                    });
                    continue;
                }

                // اگر inbound قبلاً این ماژول را پر کرده، دوباره JSONB اسکن نکن
                var alreadyInbound = groups.FirstOrDefault(g =>
                    !g.IsManyToMany
                    && string.Equals(g.ModuleName, other.Name, StringComparison.OrdinalIgnoreCase));
                if (alreadyInbound is not null
                    && alreadyInbound.Records.Count > 0
                    && !string.IsNullOrWhiteSpace(rel.LinkFieldName)
                    && rel.SourceModuleId == module.Id)
                {
                    if (!string.IsNullOrWhiteSpace(rel.Label))
                        alreadyInbound.Label = rel.Label;
                    if (!string.IsNullOrWhiteSpace(linkField))
                        alreadyInbound.LinkFieldName = linkField;
                    continue;
                }

                // LinkFieldName: Lookup روی ماژول مقصد که به مبدأ اشاره می‌کند
                if (!string.IsNullOrWhiteSpace(rel.LinkFieldName)
                    && IsSafeJsonKey(rel.LinkFieldName)
                    && rel.SourceModuleId == module.Id)
                {
                    var linkName = rel.LinkFieldName!;
                    var rows = await _db.Database
                        .SqlQuery<RelatedSqlRow>($"""
                            SELECT r."Id" AS "Id",
                                   r."Title" AS "Title",
                                   m."Name" AS "ModuleName",
                                   m."SingularLabel" AS "ModuleLabel"
                            FROM "Records" r
                            INNER JOIN "Modules" m ON m."Id" = r."ModuleId"
                            WHERE r."ModuleId" = {rel.TargetModuleId}
                              AND r."IsDeleted" = FALSE
                              AND r."CustomData" ->> {linkName} = {idStr}
                            ORDER BY r."Id" DESC
                            LIMIT 40
                            """)
                        .ToListAsync();

                    matched = rows.Select(row => new RelatedRecordItem
                    {
                        ModuleName = row.ModuleName,
                        ModuleLabel = row.ModuleLabel,
                        RecordId = row.Id,
                        Title = row.Title,
                        FieldLabel = rel.Label,
                        LinkFieldName = linkName
                    }).ToList();
                }
                else
                {
                    matched = inbound
                        .Where(i => string.Equals(i.ModuleName, other.Name, StringComparison.OrdinalIgnoreCase))
                        .Concat(outboundItems.Where(o =>
                            string.Equals(o.ModuleName, other.Name, StringComparison.OrdinalIgnoreCase)))
                        .GroupBy(x => x.RecordId)
                        .Select(x => x.First())
                        .ToList();
                    linkField ??= matched.FirstOrDefault()?.LinkFieldName;
                }

                var already = groups.FirstOrDefault(g =>
                    !g.IsManyToMany
                    && string.Equals(g.ModuleName, other.Name, StringComparison.OrdinalIgnoreCase));
                if (already is not null)
                {
                    if (!string.IsNullOrWhiteSpace(rel.Label))
                        already.Label = rel.Label;
                    if (!string.IsNullOrWhiteSpace(linkField))
                        already.LinkFieldName = linkField;
                    if (matched.Count > 0 && already.Records.Count == 0)
                        already.Records = matched;
                    continue;
                }

                groups.Add(new RelatedRecordGroup
                {
                    Label = string.IsNullOrWhiteSpace(rel.Label) ? other.PluralLabel : rel.Label,
                    ModuleName = other.Name,
                    TabKey = $"rel-{other.Name}",
                    LinkFieldName = linkField,
                    ParentRecordId = recordId,
                    Records = matched
                });
            }
        }

        // یکتا کردن TabKey در صورت تکرار ماژول
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            if (string.IsNullOrWhiteSpace(g.TabKey))
                g.TabKey = $"rel-{g.ModuleName}";
            var baseKey = g.TabKey;
            var n = 2;
            while (!seen.Add(g.TabKey))
                g.TabKey = $"{baseKey}-{n++}";
        }

        if (loadCandidates)
            await EnrichRelationGroupsAsync(groups, recordId);
        return groups;
    }

    [HttpPost("/App/m/{moduleName}/{id:int}/link-related")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkRelated(
        string moduleName,
        int id,
        string relatedModule,
        int relatedRecordId,
        string? linkField = null,
        int? relationId = null)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        var related = await _metadata.GetModuleByNameAsync(relatedModule);
        if (module is null || related is null)
            return NotFound();

        // Many-to-many via RecordLink
        if (relationId is int rid)
        {
            if (!await _access.CanEditAsync(module.Id))
                return Forbid("Identity.Application");

            var rel = await _db.Relations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == rid
                    && (r.SourceModuleId == module.Id || r.TargetModuleId == module.Id)
                    && (r.SourceModuleId == related.Id || r.TargetModuleId == related.Id));
            if (rel is null || (!rel.IsManyToMany && rel.Kind != RelationKind.ManyToMany))
            {
                TempData["Error"] = "رابطه چندبه‌چند معتبر نیست.";
                return Redirect($"/App/m/{module.Name}/{id}");
            }

            var parent = await _records.GetAsync(module.Id, id);
            var child = await _records.GetAsync(related.Id, relatedRecordId);
            if (parent is null || child is null)
                return NotFound();

            var left = Math.Min(id, relatedRecordId);
            var right = Math.Max(id, relatedRecordId);
            var exists = await _db.RecordLinks.AnyAsync(l =>
                l.RelationId == rid && l.LeftRecordId == left && l.RightRecordId == right);
            if (!exists)
            {
                _db.RecordLinks.Add(new RecordLink
                {
                    RelationId = rid,
                    LeftRecordId = left,
                    RightRecordId = right
                });
                await _db.SaveChangesAsync();
            }

            TempData["Success"] = $"«{child.Title}» متصل شد.";
            return Redirect($"/App/m/{module.Name}/{id}#rel-m2m-{rid}");
        }

        if (!await _access.CanEditAsync(related.Id))
            return Forbid("Identity.Application");

        if (string.IsNullOrWhiteSpace(linkField) || !IsSafeJsonKey(linkField))
        {
            TempData["Error"] = "فیلد ارتباط نامعتبر است.";
            return Redirect($"/App/m/{module.Name}/{id}");
        }

        var linkOk = await _db.Fields.AsNoTracking().AnyAsync(f =>
            f.ModuleId == related.Id
            && f.Name == linkField
            && f.Type == FieldType.Lookup
            && f.LookupModule == module.Name);
        if (!linkOk)
        {
            TempData["Error"] = "این ارتباط برای اتصال پشتیبانی نمی‌شود.";
            return Redirect($"/App/m/{module.Name}/{id}");
        }

        var parentRec = await _records.GetAsync(module.Id, id);
        var childRec = await _records.GetAsync(related.Id, relatedRecordId);
        if (parentRec is null || childRec is null)
            return NotFound();

        var data = DynamicRecordService.ParseData(childRec);
        data[linkField] = id.ToString();
        await _records.UpdateAsync(related.Id, relatedRecordId, data);

        TempData["Success"] = $"«{childRec.Title}» به این {module.SingularLabel} متصل شد.";
        return Redirect($"/App/m/{module.Name}/{id}#rel-{related.Name}");
    }

    [HttpPost("/App/m/{moduleName}/{id:int}/unlink-related")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlinkRelated(
        string moduleName, int id, int relationId, int relatedRecordId)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanEditAsync(module.Id))
            return Forbid("Identity.Application");

        var rel = await _db.Relations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == relationId
                && (r.SourceModuleId == module.Id || r.TargetModuleId == module.Id));
        if (rel is null)
        {
            TempData["Error"] = "رابطه یافت نشد.";
            return Redirect($"/App/m/{module.Name}/{id}");
        }

        var left = Math.Min(id, relatedRecordId);
        var right = Math.Max(id, relatedRecordId);
        var link = await _db.RecordLinks.FirstOrDefaultAsync(l =>
            l.RelationId == relationId && l.LeftRecordId == left && l.RightRecordId == right);
        if (link is not null)
        {
            _db.RecordLinks.Remove(link);
            await _db.SaveChangesAsync();
            TempData["Success"] = "اتصال حذف شد.";
        }

        return Redirect($"/App/m/{module.Name}/{id}#rel-m2m-{relationId}");
    }

    [HttpGet("/App/m/{moduleName}/export")]
    public async Task<IActionResult> Export(string moduleName, string? q)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var (items, _) = await _records.ListAsync(module.Id, q, page: 1, pageSize: 10_000);
        var bytes = await _importExport.ExportToExcelAsync(module, items);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{module.Name}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    [HttpPost("/App/m/{moduleName}/import")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(string moduleName, IFormFile? file)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanCreateAsync(module.Id))
            return Forbid("Identity.Application");

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "فایل CSV انتخاب نشده است.";
            return RedirectToAction(nameof(Index), new { moduleName });
        }

        await using var stream = file.OpenReadStream();
        var (imported, errors) = await _importExport.ImportCsvAsync(module.Id, stream);

        TempData["Success"] = $"{imported} رکورد وارد شد.";
        if (errors.Count > 0)
            TempData["Error"] = string.Join("\n", errors.Take(10));

        return RedirectToAction(nameof(Index), new { moduleName });
    }

    /// <summary>تبدیل یک‌کلیکی سرنخ به مخاطب + سازمان + فرصت فروش.</summary>
    [HttpPost("/App/m/leads/{id:int}/convert")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Convert(int id, [FromServices] LeadConversionService conversion)
    {
        var leads = await _metadata.GetModuleByNameAsync("leads");
        if (leads is null || !await _access.CanEditAsync(leads.Id))
            return Forbid("Identity.Application");

        var result = await conversion.ConvertAsync(id);
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Details), new { moduleName = "leads", id });
        }

        TempData["Success"] = "سرنخ با موفقیت به مخاطب" +
            (result.OrganizationId is not null ? " + سازمان" : "") +
            (result.OpportunityId is not null ? " + فرصت فروش" : "") + " تبدیل شد.";
        return RedirectToAction(nameof(Index), new { moduleName = "opportunities" });
    }

    [HttpPost("/App/m/{moduleName}/{id:int}/attachments")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> UploadAttachment(
        string moduleName,
        int id,
        IFormFile? file,
        [FromServices] IWebHostEnvironment env,
        [FromServices] Crm.Core.Abstractions.ITenantContext tenant)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var record = await _records.GetAsync(module.Id, id);
        if (record is null)
            return NotFound();

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "فایلی انتخاب نشده است.";
            return Redirect($"/App/m/{module.Name}/{id}#attachments");
        }

        if (file.Length > 20 * 1024 * 1024)
        {
            TempData["Error"] = "حداکثر حجم فایل ۲۰ مگابایت است.";
            return Redirect($"/App/m/{module.Name}/{id}#attachments");
        }

        var safeName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "file";

        var ext = Path.GetExtension(safeName);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        var tenantKey = tenant.TenantId?.ToString() ?? "0";
        var relDir = Path.Combine("uploads", "records", tenantKey, module.Name, id.ToString());
        var absDir = Path.Combine(env.WebRootPath ?? Path.GetTempPath(), relDir);
        Directory.CreateDirectory(absDir);
        var absPath = Path.Combine(absDir, storedName);

        await using (var stream = System.IO.File.Create(absPath))
            await file.CopyToAsync(stream);

        _db.Attachments.Add(new Attachment
        {
            ModuleName = module.Name,
            RecordId = id,
            FileName = safeName,
            StoredPath = "/" + relDir.Replace('\\', '/') + "/" + storedName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "پیوست ذخیره شد.";
        return Redirect($"/App/m/{module.Name}/{id}#attachments");
    }

    [HttpGet("/App/m/{moduleName}/{id:int}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> DownloadAttachment(
        string moduleName,
        int id,
        int attachmentId,
        [FromServices] IWebHostEnvironment env)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var att = await _db.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.ModuleName == module.Name && a.RecordId == id);
        if (att is null)
            return NotFound();

        var relative = att.StoredPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var abs = Path.Combine(env.WebRootPath, relative);
        if (!System.IO.File.Exists(abs))
            return NotFound();

        return PhysicalFile(abs, att.ContentType, att.FileName);
    }

    [HttpPost("/App/m/{moduleName}/{id:int}/attachments/{attachmentId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAttachment(
        string moduleName,
        int id,
        int attachmentId,
        [FromServices] IWebHostEnvironment env)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanEditAsync(module.Id))
            return Forbid("Identity.Application");

        var att = await _db.Attachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.ModuleName == module.Name && a.RecordId == id);
        if (att is null)
            return NotFound();

        var relative = att.StoredPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var abs = Path.Combine(env.WebRootPath, relative);
        if (System.IO.File.Exists(abs))
        {
            try { System.IO.File.Delete(abs); } catch { /* ignore */ }
        }

        att.IsDeleted = true;
        att.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "پیوست حذف شد.";
        return Redirect($"/App/m/{module.Name}/{id}#attachments");
    }

    [HttpGet("/App/recycle-bin")]
    public async Task<IActionResult> RecycleBin()
    {
        var deleted = await _records.ListDeletedAsync();
        ViewData["Title"] = "سطل بازیابی";
        return View(new RecycleBinViewModel { Records = deleted });
    }

    [HttpPost("/App/recycle-bin/{id:int}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        try
        {
            await _records.RestoreAsync(id);
            TempData["Success"] = "رکورد بازیابی شد.";
        }
        catch (InvalidOperationException)
        {
            TempData["Error"] = "رکورد یافت نشد.";
        }

        return RedirectToAction(nameof(RecycleBin));
    }

    private async Task<RecordFormViewModel> BuildFormModelAsync(
        ModuleDef module, int? recordId, Dictionary<string, string?>? values)
    {
        var fields = await _metadata.GetFieldsAsync(module.Id);
        var blocks = await _metadata.GetBlocksAsync(module.Id);
        values ??= new Dictionary<string, string?>();

        // فقط عنوان مقدار فعلی Lookup (برای Select2 Ajax) — نه لیست کامل
        var lookupOptions = new Dictionary<string, List<(int, string)>>();
        foreach (var field in fields.Where(f => f.Type == FieldType.Lookup && f.LookupModule is not null))
        {
            if (!values.TryGetValue(field.Name, out var selectedRaw)
                || string.IsNullOrWhiteSpace(selectedRaw)
                || !int.TryParse(selectedRaw.Trim(), out var selectedId)
                || selectedId <= 0)
            {
                lookupOptions[field.Name] = [];
                continue;
            }

            var target = await _metadata.GetModuleByNameAsync(field.LookupModule!);
            if (target is null || !await _access.CanViewModuleAsync(target.Id))
            {
                lookupOptions[field.Name] = [(selectedId, $"#{selectedId}")];
                continue;
            }

            var selected = await _records.GetAsync(target.Id, selectedId);
            lookupOptions[field.Name] =
            [
                (selectedId, selected is null
                    ? $"#{selectedId}"
                    : (string.IsNullOrWhiteSpace(selected.Title) ? $"#{selectedId}" : selected.Title))
            ];
        }

        // دفترچه قیمت روی اسناد فروش (اگر فیلد در متادیتا باشد یا فقط در خط‌اقلام)
        if (module.Name is "quotes" or "sales_orders" or "invoices"
            && !lookupOptions.ContainsKey("priceBook")
            && values.TryGetValue("priceBook", out var pbRaw)
            && int.TryParse(pbRaw?.Trim(), out var pbId) && pbId > 0)
        {
            var pbModule = await _metadata.GetModuleByNameAsync("pricebooks");
            if (pbModule is not null && await _access.CanViewModuleAsync(pbModule.Id))
            {
                var selected = await _records.GetAsync(pbModule.Id, pbId);
                lookupOptions["priceBook"] =
                [
                    (pbId, selected is null || string.IsNullOrWhiteSpace(selected.Title)
                        ? $"#{pbId}"
                        : selected.Title)
                ];
            }
            else
            {
                lookupOptions["priceBook"] = [(pbId, $"#{pbId}")];
            }
        }

        var (_, _, lineFields) = await _lineItems.GetLineBlockAsync(module.Id);
        var lineItems = recordId is int rid
            ? await _lineItems.LoadLinesAsync(module.Id, rid)
            : [];

        // فقط محصولات انتخاب‌شده در سطرها (Ajax بقیه را می‌آورد)
        var selectedProductIds = lineItems
            .Select(r => r.GetValueOrDefault("product"))
            .Where(v => int.TryParse(v, out _))
            .Select(v => int.Parse(v!))
            .Distinct()
            .ToList();

        var products = selectedProductIds.Count == 0
            ? new List<(int, string, decimal, decimal)>()
            : await _db.Products.AsNoTracking()
                .Where(p => p.IsActive && selectedProductIds.Contains(p.Id))
                .OrderBy(p => p.Name)
                .Select(p => new ValueTuple<int, string, decimal, decimal>(p.Id, p.Name, p.SalePrice, p.TaxPercent))
                .ToListAsync();

        return new RecordFormViewModel
        {
            Module = module,
            Fields = fields,
            Blocks = blocks,
            FieldAccessMap = await _access.GetFieldAccessMapAsync(module.Id),
            RecordId = recordId,
            Values = values,
            LookupOptions = lookupOptions,
            LineFields = lineFields,
            LineItems = lineItems,
            ProductOptions = products
        };
    }

    /// <summary>مقادیر فیلدها از فرم — با پیشوند f_ تا با توکن‌های فرم قاطی نشود.</summary>
    private static Dictionary<string, string?> ExtractFieldValues(IFormCollection form)
    {
        var values = new Dictionary<string, string?>();
        foreach (var key in form.Keys.Where(k => k.StartsWith("f_")))
        {
            var fieldName = key[2..];
            var entries = form[key];
            // MultiPicklist: چند مقدار با یک نام → کاما-جدا
            if (entries.Count > 1)
                values[fieldName] = string.Join(",", entries.Where(v => !string.IsNullOrWhiteSpace(v)));
            else
                values[fieldName] = entries.ToString();
        }
        return values;
    }
}
