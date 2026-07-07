using Microsoft.AspNetCore.Mvc;
using Crm.Core.Entities;
using Crm.Infrastructure.Security;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

public class KanbanViewModel
{
    public ModuleDef Module { get; set; } = null!;
    public FieldDef StageField { get; set; } = null!;
    public List<(PicklistValue Stage, List<KanbanCard> Cards)> Columns { get; set; } = [];
    public bool CanEdit { get; set; }
}

public class KanbanCard
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Amount { get; set; }
    public string? Subtitle { get; set; }
}

/// <summary>نمای کاریز (Kanban) با drag & drop برای هر ماژول دارای فیلد stage.</summary>
public class KanbanController : AppControllerBase
{
    private readonly MetadataService _metadata;
    private readonly DynamicRecordService _records;
    private readonly RecordAccessService _access;

    public KanbanController(MetadataService metadata, DynamicRecordService records, RecordAccessService access)
    {
        _metadata = metadata;
        _records = records;
        _access = access;
    }

    [HttpGet("/App/kanban/{moduleName}")]
    public async Task<IActionResult> Index(string moduleName)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var fields = await _metadata.GetFieldsAsync(module.Id);
        var stageField = fields.FirstOrDefault(f => f.Name == "stage" && f.Type == FieldType.Picklist);
        if (stageField is null)
            return RedirectToAction("Index", "Records", new { moduleName });

        var (items, _) = await _records.ListAsync(module.Id, search: null, page: 1, pageSize: 500);

        var columns = new List<(PicklistValue, List<KanbanCard>)>();
        foreach (var stage in stageField.PicklistValues.OrderBy(p => p.SortOrder))
        {
            var cards = new List<KanbanCard>();
            foreach (var record in items)
            {
                var data = DynamicRecordService.ParseData(record);
                if ((data.GetValueOrDefault("stage") ?? stageField.DefaultValue) != stage.Value)
                    continue;

                cards.Add(new KanbanCard
                {
                    Id = record.Id,
                    Title = record.Title,
                    Amount = data.TryGetValue("amount", out var amount) && decimal.TryParse(amount, out var a)
                        ? a.ToString("N0") + " تومان" : null,
                    Subtitle = data.GetValueOrDefault("expectedCloseDate")
                });
            }
            columns.Add((stage, cards));
        }

        var model = new KanbanViewModel
        {
            Module = module,
            StageField = stageField,
            Columns = columns,
            CanEdit = await _access.CanEditAsync(module.Id)
        };

        ViewData["Title"] = $"کاریز {module.PluralLabel}";
        return View(model);
    }

    /// <summary>جابجایی کارت بین ستون‌ها (AJAX).</summary>
    [HttpPost("/App/kanban/{moduleName}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(string moduleName, [FromForm] int recordId, [FromForm] string stage)
    {
        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound();

        if (!await _access.CanEditAsync(module.Id))
            return Forbid("Identity.Application");

        try
        {
            await _records.UpdateFieldAsync(module.Id, recordId, "stage", stage);
            return Ok(new { ok = true });
        }
        catch (RecordValidationException)
        {
            return BadRequest(new { ok = false, error = "مقدار مرحله مجاز نیست." });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, new { ok = false, error = "دسترسی ندارید." });
        }
    }
}
