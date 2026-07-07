using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

public class ReportFormModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ModuleId { get; set; }
    public List<string> Columns { get; set; } = [];
    public string FiltersJson { get; set; } = """{"logic":"and","items":[]}""";
    public string? GroupByField { get; set; }
    public string? SumField { get; set; }

    public List<ModuleDef> Modules { get; set; } = [];
    public Dictionary<int, List<object>> ModuleFields { get; set; } = [];
}

public class ReportResultRow
{
    public Dictionary<string, string?> Values { get; set; } = [];
}

public class ReportRunViewModel
{
    public ReportDef Report { get; set; } = null!;
    public List<FieldDef> Columns { get; set; } = [];
    public List<ReportResultRow> Rows { get; set; } = [];

    /// <summary>نتیجه گروه‌بندی: برچسب گروه ← (تعداد، جمع).</summary>
    public List<(string Group, int Count, decimal Sum)> Groups { get; set; } = [];
    public int TotalCount { get; set; }
    public decimal TotalSum { get; set; }
}

/// <summary>گزارش‌ساز پویا: ماژول + ستون‌ها + فیلتر + گروه‌بندی/جمع + خروجی اکسل.</summary>
public class ReportsController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly MetadataService _metadata;

    public ReportsController(CrmDbContext db, MetadataService metadata)
    {
        _db = db;
        _metadata = metadata;
    }

    [HttpGet("/App/reports")]
    public async Task<IActionResult> Index()
    {
        var reports = await _db.Reports.AsNoTracking()
            .Include(r => r.Module)
            .OrderByDescending(r => r.Id)
            .ToListAsync();

        ViewData["Title"] = "گزارش‌ها";
        return View(reports);
    }

    [HttpGet("/App/reports/create")]
    public async Task<IActionResult> Create()
    {
        var model = new ReportFormModel();
        await FillListsAsync(model);
        ViewData["Title"] = "گزارش جدید";
        return View("Form", model);
    }

    [HttpGet("/App/reports/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var report = await _db.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (report is null)
            return NotFound();

        var model = new ReportFormModel
        {
            Id = report.Id,
            Name = report.Name,
            ModuleId = report.ModuleId,
            Columns = JsonSerializer.Deserialize<List<string>>(report.ColumnsJson) ?? [],
            FiltersJson = report.FiltersJson,
            GroupByField = report.GroupByField,
            SumField = report.SumField
        };
        await FillListsAsync(model);
        ViewData["Title"] = $"ویرایش {report.Name}";
        return View("Form", model);
    }

    [HttpPost("/App/reports/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ReportFormModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name) || model.ModuleId == 0 || model.Columns.Count == 0)
        {
            TempData["Error"] = "نام، ماژول و حداقل یک ستون الزامی است.";
            await FillListsAsync(model);
            return View("Form", model);
        }

        ReportDef report;
        if (model.Id == 0)
        {
            report = new ReportDef();
            _db.Reports.Add(report);
        }
        else
        {
            report = await _db.Reports.FirstAsync(r => r.Id == model.Id);
        }

        report.Name = model.Name.Trim();
        report.ModuleId = model.ModuleId;
        report.ColumnsJson = JsonSerializer.Serialize(model.Columns);
        report.FiltersJson = string.IsNullOrWhiteSpace(model.FiltersJson)
            ? """{"logic":"and","items":[]}""" : model.FiltersJson;
        report.GroupByField = string.IsNullOrWhiteSpace(model.GroupByField) ? null : model.GroupByField;
        report.SumField = string.IsNullOrWhiteSpace(model.SumField) ? null : model.SumField;

        await _db.SaveChangesAsync();
        TempData["Success"] = "گزارش ذخیره شد.";
        return RedirectToAction(nameof(Run), new { id = report.Id });
    }

    [HttpGet("/App/reports/{id:int}")]
    public async Task<IActionResult> Run(int id)
    {
        var model = await BuildRunAsync(id);
        if (model is null)
            return NotFound();

        ViewData["Title"] = model.Report.Name;
        return View(model);
    }

    [HttpGet("/App/reports/{id:int}/excel")]
    public async Task<IActionResult> Excel(int id)
    {
        var model = await BuildRunAsync(id);
        if (model is null)
            return NotFound();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("گزارش");
        sheet.RightToLeft = true;

        for (var c = 0; c < model.Columns.Count; c++)
            sheet.Cell(1, c + 1).Value = model.Columns[c].Label;
        sheet.Row(1).Style.Font.Bold = true;

        for (var r = 0; r < model.Rows.Count; r++)
            for (var c = 0; c < model.Columns.Count; c++)
                sheet.Cell(r + 2, c + 1).Value = model.Rows[r].Values.GetValueOrDefault(model.Columns[c].Name) ?? "";

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"report-{model.Report.Id}.xlsx");
    }

    [HttpPost("/App/reports/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var report = await _db.Reports.FindAsync(id);
        if (report is not null)
        {
            report.IsDeleted = true;
            report.DeletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "گزارش حذف شد.";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<ReportRunViewModel?> BuildRunAsync(int id)
    {
        var report = await _db.Reports.AsNoTracking()
            .Include(r => r.Module)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (report is null)
            return null;

        var fields = await _metadata.GetFieldsAsync(report.ModuleId);
        var columnNames = JsonSerializer.Deserialize<List<string>>(report.ColumnsJson) ?? [];
        var columns = columnNames
            .Select(name => fields.FirstOrDefault(f => f.Name == name))
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();

        var filters = WorkflowEngine.ParseConditions(report.FiltersJson);

        var records = await _db.Records.AsNoTracking()
            .Where(r => r.ModuleId == report.ModuleId)
            .OrderByDescending(r => r.Id)
            .Take(5000)
            .ToListAsync();

        var model = new ReportRunViewModel { Report = report, Columns = columns };

        var groupField = fields.FirstOrDefault(f => f.Name == report.GroupByField);
        var groupBuckets = new Dictionary<string, (int Count, decimal Sum)>();

        foreach (var record in records)
        {
            var data = DynamicRecordService.ParseData(record);
            data["__title"] = record.Title;

            if (!WorkflowEngine.Evaluate(filters, data))
                continue;

            var row = new ReportResultRow();
            foreach (var column in columns)
            {
                var value = data.GetValueOrDefault(column.Name);
                if (column.Type == FieldType.Picklist && value is not null)
                    value = column.PicklistValues.FirstOrDefault(p => p.Value == value)?.Label ?? value;
                row.Values[column.Name] = value;
            }
            model.Rows.Add(row);

            var sumValue = report.SumField is not null &&
                           decimal.TryParse(data.GetValueOrDefault(report.SumField), out var s) ? s : 0m;
            model.TotalSum += sumValue;

            if (report.GroupByField is not null)
            {
                var rawGroup = data.GetValueOrDefault(report.GroupByField) ?? "(خالی)";
                var label = groupField?.PicklistValues.FirstOrDefault(p => p.Value == rawGroup)?.Label ?? rawGroup;
                var bucket = groupBuckets.GetValueOrDefault(label);
                groupBuckets[label] = (bucket.Count + 1, bucket.Sum + sumValue);
            }
        }

        model.TotalCount = model.Rows.Count;
        model.Groups = groupBuckets
            .Select(kv => (kv.Key, kv.Value.Count, kv.Value.Sum))
            .OrderByDescending(g => g.Count)
            .ToList();

        return model;
    }

    private async Task FillListsAsync(ReportFormModel model)
    {
        model.Modules = (await _metadata.GetActiveModulesAsync()).ToList();
        foreach (var module in model.Modules)
        {
            var fields = await _metadata.GetFieldsAsync(module.Id);
            model.ModuleFields[module.Id] = fields
                .Select(f => (object)new { name = f.Name, label = f.Label, type = f.Type.ToString() })
                .ToList();
        }
    }
}
