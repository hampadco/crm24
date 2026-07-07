using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>مدیریت پروژه: پروژه ← فاز ← وظیفه + نمای گانت + تبدیل فرصت برنده.</summary>
public class ProjectsController : AppControllerBase
{
    public static string StatusLabel(ProjectStatus status) => status switch
    {
        ProjectStatus.Active => "فعال",
        ProjectStatus.OnHold => "متوقف",
        ProjectStatus.Completed => "تکمیل‌شده",
        _ => "لغوشده"
    };

    private readonly CrmDbContext _db;
    private readonly MetadataService _metadata;

    public ProjectsController(CrmDbContext db, MetadataService metadata)
    {
        _db = db;
        _metadata = metadata;
    }

    [HttpGet("/App/projects")]
    public async Task<IActionResult> Index()
    {
        var projects = await _db.Projects.AsNoTracking()
            .Include(p => p.Tasks)
            .OrderByDescending(p => p.Id).Take(300).ToListAsync();

        // فرصت‌های برنده که هنوز به پروژه تبدیل نشده‌اند
        var opportunitiesModule = await _metadata.GetModuleByNameAsync("opportunities");
        var wonOpportunities = new Dictionary<int, string>();
        if (opportunitiesModule is not null)
        {
            var convertedIds = await _db.Projects.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.OpportunityRecordId != null)
                .Select(p => p.OpportunityRecordId!.Value).ToListAsync();

            var candidates = await _db.Records.AsNoTracking()
                .Where(r => r.ModuleId == opportunitiesModule.Id && !convertedIds.Contains(r.Id))
                .OrderByDescending(r => r.Id).Take(200).ToListAsync();

            foreach (var record in candidates)
            {
                var data = DynamicRecordService.ParseData(record);
                if (data.GetValueOrDefault("stage") == "won")
                    wonOpportunities[record.Id] = record.Title;
            }
        }
        ViewBag.WonOpportunities = wonOpportunities;

        ViewData["Title"] = "پروژه‌ها";
        return View(projects);
    }

    [HttpGet("/App/projects/create")]
    public IActionResult Create()
    {
        ViewData["Title"] = "پروژه جدید";
        return View("Form", new Project { StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow.AddMonths(3) });
    }

    [HttpGet("/App/projects/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (project is null)
            return NotFound();
        ViewData["Title"] = $"ویرایش {project.Name}";
        return View("Form", project);
    }

    [HttpPost("/App/projects/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string name, string? description, string? customerName,
        DateTime startUtc, DateTime endUtc, decimal budget, ProjectStatus status, bool showInPortal, int? contactRecordId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "نام پروژه الزامی است.";
            return RedirectToAction(nameof(Index));
        }

        Project project;
        if (id == 0)
        {
            project = new Project();
            _db.Projects.Add(project);
        }
        else
        {
            project = await _db.Projects.FirstAsync(p => p.Id == id);
        }

        project.Name = name.Trim();
        project.Description = description?.Trim();
        project.CustomerName = customerName?.Trim();
        project.StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        project.EndUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);
        project.Budget = budget;
        project.Status = status;
        project.ShowInPortal = showInPortal;
        project.ContactRecordId = contactRecordId;

        await _db.SaveChangesAsync();
        TempData["Success"] = "پروژه ذخیره شد.";
        return RedirectToAction(nameof(Details), new { id = project.Id });
    }

    /// <summary>تبدیل فرصت برنده به پروژه با معادل‌سازی فیلدها.</summary>
    [HttpPost("/App/projects/from-opportunity/{recordId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FromOpportunity(int recordId)
    {
        var opportunitiesModule = await _metadata.GetModuleByNameAsync("opportunities");
        if (opportunitiesModule is null)
            return NotFound();

        var record = await _db.Records.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ModuleId == opportunitiesModule.Id && r.Id == recordId);
        if (record is null)
            return NotFound();

        if (await _db.Projects.AnyAsync(p => p.OpportunityRecordId == recordId))
        {
            TempData["Error"] = "این فرصت قبلاً به پروژه تبدیل شده است.";
            return RedirectToAction(nameof(Index));
        }

        var data = DynamicRecordService.ParseData(record);
        decimal.TryParse(data.GetValueOrDefault("amount"), out var amount);
        int? contactId = int.TryParse(data.GetValueOrDefault("contact"), out var cid) ? cid : null;

        string? customerName = null;
        if (contactId is int contactRecordId)
            customerName = await _db.Records.AsNoTracking()
                .Where(r => r.Id == contactRecordId).Select(r => r.Title).FirstOrDefaultAsync();

        var project = new Project
        {
            Name = record.Title,
            Budget = amount,
            OpportunityRecordId = recordId,
            ContactRecordId = contactId,
            CustomerName = customerName,
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow.AddMonths(3)
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"پروژه «{project.Name}» از فرصت برنده ساخته شد.";
        return RedirectToAction(nameof(Details), new { id = project.Id });
    }

    [HttpGet("/App/projects/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var project = await _db.Projects.AsNoTracking()
            .Include(p => p.Phases.OrderBy(f => f.SortOrder))
            .Include(p => p.Tasks.OrderBy(t => t.SortOrder).ThenBy(t => t.StartUtc))
            .FirstOrDefaultAsync(p => p.Id == id);
        if (project is null)
            return NotFound();

        ViewBag.Users = await _db.Users.AsNoTracking()
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        ViewData["Title"] = project.Name;
        return View(project);
    }

    [HttpPost("/App/projects/{id:int}/phases")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPhase(int id, string name)
    {
        var project = await _db.Projects.Include(p => p.Phases).FirstOrDefaultAsync(p => p.Id == id);
        if (project is not null && !string.IsNullOrWhiteSpace(name))
        {
            project.Phases.Add(new ProjectPhase
            {
                Name = name.Trim(),
                SortOrder = project.Phases.Count + 1
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = "فاز افزوده شد.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/App/projects/{id:int}/tasks")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTask(int id, string name, int? phaseId, int? assignedUserId,
        DateTime startUtc, DateTime endUtc)
    {
        var project = await _db.Projects.Include(p => p.Tasks).FirstOrDefaultAsync(p => p.Id == id);
        if (project is not null && !string.IsNullOrWhiteSpace(name) && endUtc >= startUtc)
        {
            project.Tasks.Add(new ProjectTask
            {
                Name = name.Trim(),
                PhaseId = phaseId,
                AssignedUserId = assignedUserId,
                StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
                EndUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc),
                SortOrder = project.Tasks.Count + 1
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = "وظیفه افزوده شد.";
        }
        else
        {
            TempData["Error"] = "نام و بازه زمانی معتبر الزامی است.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/App/projects/tasks/{taskId:int}/progress")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetTaskProgress(int taskId, int percent)
    {
        var task = await _db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null)
            return NotFound();

        task.ProgressPercent = Math.Clamp(percent, 0, 100);
        task.Status = task.ProgressPercent switch
        {
            0 => ProjectTaskStatus.Todo,
            100 => ProjectTaskStatus.Done,
            _ => ProjectTaskStatus.InProgress
        };
        await _db.SaveChangesAsync();

        TempData["Success"] = "پیشرفت بروزرسانی شد.";
        return RedirectToAction(nameof(Details), new { id = task.ProjectId });
    }
}
