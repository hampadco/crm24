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
    private readonly CrmDbContext _db;

    public RecordsController(
        MetadataService metadata,
        DynamicRecordService records,
        RecordAccessService access,
        RecordImportExportService importExport,
        CrmDbContext db)
    {
        _metadata = metadata;
        _records = records;
        _access = access;
        _importExport = importExport;
        _db = db;
    }

    [HttpGet("/App/m/{moduleName}")]
    public async Task<IActionResult> Index(string moduleName, string? q, int page = 1)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        const int pageSize = 20;
        var (items, total) = await _records.ListAsync(module.Id, q, Math.Max(1, page), pageSize);
        var fields = await _metadata.GetFieldsAsync(module.Id);

        var recordData = items.ToDictionary(r => r.Id, DynamicRecordService.ParseData);

        var model = new RecordListViewModel
        {
            Module = module,
            Fields = fields.Where(f => f.ShowInList).ToList(),
            Records = items,
            RecordData = recordData,
            Search = q,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            CanCreate = await _access.CanCreateAsync(module.Id),
            CanEdit = await _access.CanEditAsync(module.Id),
            CanDelete = await _access.CanDeleteAsync(module.Id),
            LookupTitles = await ResolveLookupTitlesAsync(fields, recordData.Values),
            HasKanban = fields.Any(f => f.Name == "stage" && f.Type == FieldType.Picklist)
        };

        ViewData["Title"] = module.PluralLabel;
        return View(model);
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

        var model = await BuildFormModelAsync(module, recordId: null, values: null);
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
            TempData["Success"] = $"{module.SingularLabel} «{record.Title}» ثبت شد.";
            return RedirectToAction(nameof(Index), new { moduleName });
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
            TempData["Success"] = "تغییرات ذخیره شد.";
            return RedirectToAction(nameof(Index), new { moduleName });
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
            return RedirectToAction(nameof(Index), new { moduleName = "leads" });
        }

        TempData["Success"] = "سرنخ با موفقیت به مخاطب" +
            (result.OrganizationId is not null ? " + سازمان" : "") +
            (result.OpportunityId is not null ? " + فرصت فروش" : "") + " تبدیل شد.";
        return RedirectToAction(nameof(Index), new { moduleName = "opportunities" });
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

        var lookupOptions = new Dictionary<string, List<(int, string)>>();
        foreach (var field in fields.Where(f => f.Type == FieldType.Lookup && f.LookupModule is not null))
        {
            var target = await _metadata.GetModuleByNameAsync(field.LookupModule!);
            if (target is null)
                continue;

            var (items, _) = await _records.ListAsync(target.Id, search: null, page: 1, pageSize: 500);
            lookupOptions[field.Name] = items.Select(r => (r.Id, r.Title)).ToList();
        }

        return new RecordFormViewModel
        {
            Module = module,
            Fields = fields,
            FieldAccessMap = await _access.GetFieldAccessMapAsync(module.Id),
            RecordId = recordId,
            Values = values ?? new Dictionary<string, string?>(),
            LookupOptions = lookupOptions
        };
    }

    /// <summary>مقادیر فیلدها از فرم — با پیشوند f_ تا با توکن‌های فرم قاطی نشود.</summary>
    private static Dictionary<string, string?> ExtractFieldValues(IFormCollection form)
    {
        var values = new Dictionary<string, string?>();
        foreach (var key in form.Keys.Where(k => k.StartsWith("f_")))
            values[key[2..]] = form[key].ToString();
        return values;
    }
}
