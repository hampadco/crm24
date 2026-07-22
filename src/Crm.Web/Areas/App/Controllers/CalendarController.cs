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
    public async Task<IActionResult> Feed(DateTime? start, DateTime? end)
    {
        var rangeStart = start?.ToUniversalTime() ?? DateTime.UtcNow.AddMonths(-1);
        var rangeEnd = end?.ToUniversalTime() ?? DateTime.UtcNow.AddMonths(2);
        // حاشیه برای رویدادهای چندروزه
        var queryFrom = rangeStart.AddDays(-7);
        var queryTo = rangeEnd.AddDays(7);
        var events = new List<object>();

        var tasksModule = await _metadata.GetModuleByNameAsync("tasks");
        if (tasksModule is not null && await _access.CanViewModuleAsync(tasksModule.Id))
        {
            var items = await _records.ListByJsonDateRangeAsync(tasksModule.Id, "dueDate", queryFrom, queryTo);
            foreach (var record in items)
            {
                var data = DynamicRecordService.ParseData(record);
                var dueRaw = data.GetValueOrDefault("dueDate");
                if (string.IsNullOrEmpty(dueRaw) || !DateTime.TryParse(dueRaw, out var due))
                    continue;

                var dueUtc = DateTime.SpecifyKind(due, DateTimeKind.Utc);
                if (dueUtc < rangeStart || dueUtc > rangeEnd)
                    continue;

                var priority = data.GetValueOrDefault("priority") ?? "normal";
                var (bg, text) = TaskColors(priority);

                events.Add(new
                {
                    id = $"task-{record.Id}",
                    title = Truncate(record.Title, 28),
                    start = dueRaw,
                    url = $"/App/m/tasks/{record.Id}/edit",
                    backgroundColor = bg,
                    borderColor = bg,
                    textColor = text,
                    display = "block",
                    classNames = new[] { "crm-cal-task", $"crm-cal-prio-{priority}" },
                    extendedProps = new { kind = "task", fullTitle = record.Title, bg, text }
                });
            }
        }

        var eventsModule = await _metadata.GetModuleByNameAsync("events");
        if (eventsModule is not null && await _access.CanViewModuleAsync(eventsModule.Id))
        {
            var items = await _records.ListByJsonDateRangeAsync(eventsModule.Id, "startAt", queryFrom, queryTo);
            foreach (var record in items)
            {
                var data = DynamicRecordService.ParseData(record);
                var startRaw = data.GetValueOrDefault("startAt");
                if (string.IsNullOrEmpty(startRaw) || !DateTime.TryParse(startRaw, out var startAt))
                    continue;

                var startUtc = DateTime.SpecifyKind(startAt, DateTimeKind.Utc);
                if (startUtc > rangeEnd)
                    continue;

                var endRaw = data.GetValueOrDefault("endAt");
                if (!string.IsNullOrEmpty(endRaw) && DateTime.TryParse(endRaw, out var endAt))
                {
                    var endUtc = DateTime.SpecifyKind(endAt, DateTimeKind.Utc);
                    if (endUtc < rangeStart)
                        continue;
                }
                else if (startUtc < rangeStart)
                {
                    continue;
                }

                var type = data.GetValueOrDefault("type") ?? "meeting";
                var (bg, text) = EventColors(type);

                events.Add(new
                {
                    id = $"event-{record.Id}",
                    title = Truncate(record.Title, 28),
                    start = startRaw,
                    end = endRaw,
                    url = $"/App/m/events/{record.Id}/edit",
                    backgroundColor = bg,
                    borderColor = bg,
                    textColor = text,
                    display = "block",
                    classNames = new[] { "crm-cal-event", $"crm-cal-type-{type}" },
                    extendedProps = new { kind = "event", fullTitle = record.Title, bg, text }
                });
            }
        }

        return Json(events);
    }

    /// <summary>پس‌زمینه‌های اشباع‌شده + متن سفید؛ خاکستری‌های روشن استفاده نمی‌شوند.</summary>
    private static (string Bg, string Text) TaskColors(string priority) => priority switch
    {
        "urgent" => ("#e4282b", "#ffffff"),
        "high" => ("#e08a00", "#ffffff"),
        "low" => ("#4b5675", "#ffffff"),
        _ => ("#0097a7", "#ffffff")
    };

    private static (string Bg, string Text) EventColors(string type) => type switch
    {
        "visit" => ("#1f9d57", "#ffffff"),
        "demo" => ("#e07b00", "#ffffff"),
        "other" => ("#4b5675", "#ffffff"),
        _ => ("#5a5fea", "#ffffff")
    };

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "بدون عنوان";
        value = value.Trim();
        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }
}
