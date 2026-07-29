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
    private readonly CrmDbContext _db;

    public RecordsController(
        MetadataService metadata,
        DynamicRecordService records,
        RecordAccessService access,
        RecordImportExportService importExport,
        ListColumnService listColumns,
        CrmDbContext db)
    {
        _metadata = metadata;
        _records = records;
        _access = access;
        _importExport = importExport;
        _listColumns = listColumns;
        _db = db;
    }

    [HttpGet("/App/m/{moduleName}")]
    public async Task<IActionResult> Index(
        string moduleName, string? q, int page = 1, string? sort = null, string? dir = null)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var fields = await _metadata.GetFieldsAsync(module.Id);
        var allVisible = fields.Where(f => f.IsVisible).ToList();
        var listFields = (await _listColumns.GetListFieldsAsync(module.Id)).ToList();
        var blocks = await _metadata.GetBlocksAsync(module.Id);
        var filters = ParseColumnFilters(Request.Query, listFields);

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
            HasKanban = fields.Any(f => f.Name == "stage" && f.Type == FieldType.Picklist)
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
        ViewBag.PagingRoutes = pagingRoutes;

        return View(model);
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

    [HttpPost("/App/m/{moduleName}/bulk-delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDelete(string moduleName, int[]? ids)
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
        return RedirectToAction(nameof(Index), new { moduleName });
    }

    [HttpGet("/App/m/{moduleName}/{id:int}")]
    public async Task<IActionResult> Details(string moduleName, int id)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

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

        var model = new RecordDetailViewModel
        {
            Module = module,
            Record = record,
            Fields = fields,
            Blocks = blocks,
            Values = values,
            LookupTitles = await ResolveLookupTitlesAsync(fields, [values]),
            CanEdit = canEditModule && await _access.CanModifyRecordAsync(record),
            CanDelete = canDeleteModule && await _access.CanModifyRecordAsync(record),
            Notes = await _db.Notes.AsNoTracking()
                .Where(n => n.ModuleName == module.Name && n.RecordId == id)
                .OrderByDescending(n => n.CreatedAtUtc)
                .Take(100)
                .ToListAsync(),
            AuditLogs = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.ModuleName == module.Name && a.RecordId == id)
                .OrderByDescending(a => a.AtUtc)
                .Take(50)
                .ToListAsync(),
            Attachments = await _db.Attachments.AsNoTracking()
                .Where(a => a.ModuleName == module.Name && a.RecordId == id)
                .OrderByDescending(a => a.CreatedAtUtc)
                .Take(50)
                .ToListAsync(),
            Tags = await _db.TagLinks.AsNoTracking()
                .Where(t => t.ModuleName == module.Name && t.RecordId == id)
                .Include(t => t.Tag)
                .Select(t => t.Tag)
                .ToListAsync()
        };

        var inbound = await LoadInboundRelatedAsync(module, id);
        model.Activities = inbound
            .Where(r => ActivityModuleNames.Contains(r.ModuleName))
            .ToList();
        model.Relations = await BuildRelationGroupsAsync(module, id, values, fields, inbound);

        ViewData["Title"] = record.Title;
        ViewData["PanelTitle"] = module.PluralLabel;
        return View(model);
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

    private static bool IsSafeJsonKey(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 64
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>رکوردهایی که Lookup آن‌ها به این رکورد اشاره می‌کند.</summary>
    private async Task<List<RelatedRecordItem>> LoadInboundRelatedAsync(ModuleDef module, int recordId)
    {
        var lookupFields = await _db.Fields.AsNoTracking()
            .Include(f => f.Module)
            .Where(f => f.Type == FieldType.Lookup
                        && f.LookupModule == module.Name
                        && f.ModuleId != module.Id)
            .ToListAsync();

        var results = new List<RelatedRecordItem>();
        var idStr = recordId.ToString();

        foreach (var field in lookupFields)
        {
            if (!IsSafeJsonKey(field.Name) || field.Module is null)
                continue;

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
                    FieldLabel = field.Label
                });
            }
        }

        return results
            .GroupBy(r => (r.ModuleName, r.RecordId))
            .Select(g => g.First())
            .ToList();
    }

    private async Task<List<RelatedRecordGroup>> BuildRelationGroupsAsync(
        ModuleDef module,
        int recordId,
        Dictionary<string, string?> values,
        IReadOnlyList<FieldDef> fields,
        IReadOnlyList<RelatedRecordItem> inbound)
    {
        var groups = new List<RelatedRecordGroup>();

        // Outbound: lookup values روی همین رکورد
        var outboundItems = new List<RelatedRecordItem>();
        foreach (var field in fields.Where(f => f.Type == FieldType.Lookup && !string.IsNullOrWhiteSpace(f.LookupModule)))
        {
            if (!values.TryGetValue(field.Name, out var raw) || !int.TryParse(raw, out var relatedId))
                continue;

            var related = await _db.Records.AsNoTracking()
                .Where(r => r.Id == relatedId)
                .Select(r => new { r.Id, r.Title, ModuleName = r.Module.Name, ModuleLabel = r.Module.SingularLabel })
                .FirstOrDefaultAsync();
            if (related is null)
                continue;

            outboundItems.Add(new RelatedRecordItem
            {
                ModuleName = related.ModuleName,
                ModuleLabel = related.ModuleLabel,
                RecordId = related.Id,
                Title = related.Title,
                FieldLabel = field.Label
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
                    Records = g.ToList()
                });
            }
        }

        // Inbound (غیر فعالیت) به‌صورت گروه
        foreach (var g in inbound.Where(r => !ActivityModuleNames.Contains(r.ModuleName)).GroupBy(r => r.ModuleName))
        {
            groups.Add(new RelatedRecordGroup
            {
                Label = g.First().ModuleLabel,
                ModuleName = g.Key,
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

                List<RelatedRecordItem> matched;

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
                        FieldLabel = rel.Label
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
                }

                var already = groups.FirstOrDefault(g =>
                    string.Equals(g.ModuleName, other.Name, StringComparison.OrdinalIgnoreCase));
                if (already is not null)
                {
                    if (!string.IsNullOrWhiteSpace(rel.Label))
                        already.Label = rel.Label;
                    if (matched.Count > 0 && already.Records.Count == 0)
                        already.Records = matched;
                    continue;
                }

                groups.Add(new RelatedRecordGroup
                {
                    Label = string.IsNullOrWhiteSpace(rel.Label) ? other.PluralLabel : rel.Label,
                    ModuleName = other.Name,
                    Records = matched
                });
            }
        }

        return groups;
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
        var blocks = await _metadata.GetBlocksAsync(module.Id);

        var lookupOptions = new Dictionary<string, List<(int, string)>>();
        foreach (var field in fields.Where(f => f.Type == FieldType.Lookup && f.LookupModule is not null))
        {
            var target = await _metadata.GetModuleByNameAsync(field.LookupModule!);
            if (target is null)
                continue;

            var (items, _) = await _records.ListAsync(target.Id, search: null, page: 1, pageSize: 40, includeTotal: false);
            lookupOptions[field.Name] = items.Select(r => (r.Id, r.Title)).ToList();
        }

        return new RecordFormViewModel
        {
            Module = module,
            Fields = fields,
            Blocks = blocks,
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
