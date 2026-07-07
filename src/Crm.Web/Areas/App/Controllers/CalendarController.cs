using Microsoft.AspNetCore.Mvc;
using Crm.Infrastructure.Services;
using Crm.Infrastructure.Security;

namespace Crm.Web.Areas.App.Controllers;

/// <summary>تقویم وظایف و رویدادها (FullCalendar با تاریخ شمسی).</summary>
public class CalendarController : AppControllerBase
{
    private readonly MetadataService _metadata;
    private readonly DynamicRecordService _records;
    private readonly RecordAccessService _access;

    public CalendarController(MetadataService metadata, DynamicRecordService records, RecordAccessService access)
    {
        _metadata = metadata;
        _records = records;
        _access = access;
    }

    [HttpGet("/App/calendar")]
    public IActionResult Index()
    {
        ViewData["Title"] = "تقویم";
        return View();
    }

    /// <summary>خوراک JSON برای FullCalendar: وظایف (سررسید) + رویدادها (شروع/پایان).</summary>
    [HttpGet("/App/calendar/feed")]
    public async Task<IActionResult> Feed()
    {
        var events = new List<object>();

        var tasksModule = await _metadata.GetModuleByNameAsync("tasks");
        if (tasksModule is not null && await _access.CanViewModuleAsync(tasksModule.Id))
        {
            var (items, _) = await _records.ListAsync(tasksModule.Id, null, 1, 500);
            foreach (var record in items)
            {
                var data = DynamicRecordService.ParseData(record);
                var due = data.GetValueOrDefault("dueDate");
                if (string.IsNullOrEmpty(due))
                    continue;

                var priority = data.GetValueOrDefault("priority");
                events.Add(new
                {
                    id = $"task-{record.Id}",
                    title = "وظیفه: " + record.Title,
                    start = due,
                    url = $"/App/m/tasks/{record.Id}/edit",
                    color = priority switch { "urgent" => "#ff5b5c", "high" => "#fdac41", _ => "#03c3ec" }
                });
            }
        }

        var eventsModule = await _metadata.GetModuleByNameAsync("events");
        if (eventsModule is not null && await _access.CanViewModuleAsync(eventsModule.Id))
        {
            var (items, _) = await _records.ListAsync(eventsModule.Id, null, 1, 500);
            foreach (var record in items)
            {
                var data = DynamicRecordService.ParseData(record);
                var start = data.GetValueOrDefault("startAt");
                if (string.IsNullOrEmpty(start))
                    continue;

                events.Add(new
                {
                    id = $"event-{record.Id}",
                    title = record.Title,
                    start,
                    end = data.GetValueOrDefault("endAt"),
                    url = $"/App/m/events/{record.Id}/edit",
                    color = "#696cff"
                });
            }
        }

        return Json(events);
    }
}
