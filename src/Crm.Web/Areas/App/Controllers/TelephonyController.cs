using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Security;
using Crm.Infrastructure.Services;
using Crm.Web.Services;

namespace Crm.Web.Areas.App.Controllers;

public class TelephonyController : AppControllerBase
{
    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TenantIntegrationService _integrations;
    private readonly TelefonchyClient _client;
    private readonly TelefonchyCallService _calls;
    private readonly DynamicRecordService _records;
    private readonly RecordAccessService _access;
    private readonly MetadataService _metadata;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TelephonyController> _logger;
    private readonly ICallRecordingStore _recordingStore;

    public TelephonyController(
        CrmDbContext db,
        ITenantContext tenant,
        TenantIntegrationService integrations,
        TelefonchyClient client,
        TelefonchyCallService calls,
        DynamicRecordService records,
        RecordAccessService access,
        MetadataService metadata,
        IWebHostEnvironment env,
        ILogger<TelephonyController> logger,
        ICallRecordingStore recordingStore)
    {
        _db = db;
        _tenant = tenant;
        _integrations = integrations;
        _client = client;
        _calls = calls;
        _records = records;
        _access = access;
        _metadata = metadata;
        _env = env;
        _logger = logger;
        _recordingStore = recordingStore;
    }

    [HttpPost("/App/telephony/call")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Call(string moduleName, int recordId, string? phone)
    {
        var config = await _integrations.GetAsync();
        if (!config.HasTelefonchy)
            return BadRequest(new { ok = false, message = "اتصال تلفنچی فعال نیست." });

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == _tenant.UserId);
        var exten = user?.TelefonchyExtension;
        if (string.IsNullOrWhiteSpace(exten))
            exten = config.TelefonchyDefaultExten;
        if (string.IsNullOrWhiteSpace(exten))
            return BadRequest(new { ok = false, message = "داخلی تلفنچی برای این کاربر تنظیم نشده است." });

        var module = await _metadata.GetModuleByNameAsync(moduleName);
        if (module is null)
            return NotFound(new { ok = false, message = "ماژول یافت نشد." });
        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid();

        var record = await _records.GetAsync(module.Id, recordId);
        if (record is null)
            return NotFound(new { ok = false, message = "رکورد یافت نشد." });

        var data = DynamicRecordService.ParseData(record);
        var number = phone;
        if (string.IsNullOrWhiteSpace(number))
            number = data.GetValueOrDefault("mobile") ?? data.GetValueOrDefault("phone");
        if (string.IsNullOrWhiteSpace(number))
            return BadRequest(new { ok = false, message = "شماره تماس روی این پرونده خالی است." });

        var result = await _client.SmartCallAsync(exten, number);
        await HttpContext.RequestServices.GetRequiredService<TelefonchyTraceService>().WriteAsync(new TelefonchyTraceEntry
        {
            Kind = "call",
            Action = "smartcall",
            Direction = "outgoing",
            Status = result?.CallStatus,
            Cuid = result?.Cuid,
            RequestId = result?.RequestId,
            Phone = number,
            CrmRecordId = recordId,
            Summary = result is { Ok: true }
                ? $"تماس سریع از داخلی {exten} به {number} — {result.CallStatus}"
                : $"تماس سریع رد شد: {result?.Message ?? result?.FailureReason}",
            Payload = TelefonchyTraceService.ToJson(result)
        });
        if (result is null || !result.Ok)
        {
            var raw = result?.Message ?? result?.FailureReason;
            var message = raw?.Contains("Active operator", StringComparison.OrdinalIgnoreCase) == true
                ? $"تلفنچی داخلی «{exten}» را به‌عنوان اپراتور فعال این سرویس نشناخت. "
                  + "یا شماره داخلی اشتباه است (در تنظیمات یکپارچگی، «نمایش داخلی‌های سرویس» را بزنید و از همان لیست مقدار بگذارید) "
                  + "یا سافت‌فون آن داخلی آنلاین نیست."
                : raw ?? "درخواست تماس از طرف تلفنچی پذیرفته نشد.";
            return BadRequest(new { ok = false, message });
        }

        var dto = new TelefonchyCallDto
        {
            Type = "outgoing",
            Event = "ringing",
            Dest = number,
            Src = exten,
            Extension = exten,
            RequestId = result.RequestId,
            Cuid = result.Cuid,
            StartedAt = DateTime.Now,
            Status = result.CallStatus
        };
        var callRecord = await _calls.UpsertCallAsync(dto);
        if (callRecord is not null && (module.Name is "contacts" or "leads"))
        {
            var values = DynamicRecordService.ParseData(callRecord);
            if (module.Name == "contacts")
                values["contact"] = record.Id.ToString();
            else
                values["lead"] = record.Id.ToString();
            values["phoneNumber"] = number;
            values["extension"] = exten;
            values["requestId"] = result.RequestId;
            values["name"] = $"تماس خروجی با {record.Title}";
            try
            {
                await _records.UpdateAsync(callRecord.ModuleId, callRecord.Id, values);
            }
            catch
            {
                // رکورد تماس ثبت شده؛ لینک مخاطب حیاتی نیست
            }
        }

        return Ok(new
        {
            ok = true,
            message = "درخواست تماس ثبت شد. ابتدا تلفن داخلی شما زنگ می‌خورد.",
            requestId = result.RequestId,
            callRecordId = callRecord?.Id
        });
    }

    [HttpGet("/App/telephony/call-status")]
    public async Task<IActionResult> CallStatus(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return BadRequest(new { ok = false });

        var result = await _client.GetSmartCallStatusAsync(requestId);
        await HttpContext.RequestServices.GetRequiredService<TelefonchyTraceService>().WriteAsync(new TelefonchyTraceEntry
        {
            Kind = "call",
            Action = "smartcall-status",
            Status = result?.CallStatus,
            Cuid = result?.Cuid,
            RequestId = requestId,
            Summary = $"وضعیت تماس سریع: {result?.CallStatus ?? "unknown"}",
            Payload = TelefonchyTraceService.ToJson(result)
        });
        if (result is null)
            return Ok(new { ok = false, status = "unknown" });

        if (!string.IsNullOrWhiteSpace(result.Cuid) || !string.IsNullOrWhiteSpace(result.RequestId))
        {
            await _calls.UpsertCallAsync(new TelefonchyCallDto
            {
                Cuid = result.Cuid,
                RequestId = requestId,
                Type = "outgoing",
                Status = result.CallStatus
            });
        }

        return Ok(new
        {
            ok = result.Ok,
            status = result.CallStatus,
            cuid = result.Cuid,
            message = result.FailureReason ?? result.Message
        });
    }

    /// <summary>۴۰۴ با دلیل مشخص، تا عیب‌یابی پخش صدا حدسی نباشد.</summary>
    private IActionResult RecordingUnavailable(int callRecordId, string reason)
    {
        _logger.LogWarning("Telefonchy recording unavailable for call {CallRecordId}: {Reason}", callRecordId, reason);
        Response.Headers["X-Recording-Error"] = Uri.EscapeDataString(reason);
        return NotFound(new { ok = false, message = reason });
    }

    /// <summary>جزئیات تماس را از تلفنچی می‌گیرد تا cuid و شناسه فایل ضبط تکمیل شود.</summary>
    private async Task<string?> RefreshCallIdentifiersAsync(
        int callsModuleId, int callRecordId, Dictionary<string, string?> data)
    {
        var requestId = data.GetValueOrDefault("requestId");
        if (string.IsNullOrWhiteSpace(requestId))
            return null;

        var status = await _client.GetSmartCallStatusAsync(requestId);
        if (status is null || string.IsNullOrWhiteSpace(status.Cuid))
            return null;

        data["cuid"] = status.Cuid;
        var detailed = await _client.FindCallAsync(
            status.Cuid, data.GetValueOrDefault("callId"), data.GetValueOrDefault("phoneNumber"));
        if (detailed is null)
            detailed = await _client.GetCallAsync(status.Cuid);
        if (!string.IsNullOrWhiteSpace(detailed?.FileId))
            data["recordingFileId"] = detailed.FileId;
        if (!string.IsNullOrWhiteSpace(detailed?.FileRecord))
            data["recordingFileName"] = detailed.FileRecord;
        await _records.UpdateAsync(callsModuleId, callRecordId, data);
        return status.Cuid;
    }

    [HttpGet("/App/telephony/recording/{callRecordId:int}")]
    public async Task<IActionResult> Recording(int callRecordId)
    {
        var config = await _integrations.GetAsync();
        if (!config.HasTelefonchy || !config.TelefonchyRecordingEnabled)
            return RecordingUnavailable(callRecordId, "اتصال تلفنچی یا گزینه ضبط مکالمه فعال نیست.");

        var module = await _metadata.GetModuleByNameAsync("calls");
        if (module is null || !await _access.CanViewModuleAsync(module.Id))
            return Forbid();

        var record = await _records.GetAsync(module.Id, callRecordId);
        if (record is null)
            return RecordingUnavailable(callRecordId, "رکورد تماس یافت نشد.");

        var data = DynamicRecordService.ParseData(record);
        var tenantId = _tenant.TenantId ?? 0;
        await HttpContext.RequestServices.GetRequiredService<TelefonchyTraceService>().WriteAsync(new TelefonchyTraceEntry
        {
            Kind = "recording",
            Action = "play",
            Cuid = data.GetValueOrDefault("cuid"),
            CallId = data.GetValueOrDefault("callId"),
            CrmRecordId = callRecordId,
            Summary = $"درخواست پخش ضبط رکورد #{callRecordId}"
        });

        if (!TelefonchyCallService.HasRecording(data)
            || string.IsNullOrWhiteSpace(data.GetValueOrDefault("recordingFile"))
            || _recordingStore.TryGetAbsolutePath(tenantId, callRecordId, data.GetValueOrDefault("recordingFile")) is null)
        {
            var state = await _calls.EnsureRecordingAsync(
                module.Id, record, lookupMissingIds: false, fallbackFileId: false);
            record = await _records.GetAsync(module.Id, callRecordId) ?? record;
            data = DynamicRecordService.ParseData(record);
            if (state != TelefonchyCallService.CallRecordingState.Ready
                && !TelefonchyCallService.HasRecording(data))
            {
                var reason = state == TelefonchyCallService.CallRecordingState.Processing
                    || TelefonchyCallService.IsRecordingPending(data)
                    ? "ضبط مکالمه هنوز در تلفنچی آماده نشده است. چند لحظه بعد دوباره تلاش کنید."
                    : "این تماس در تلفنچی ضبط نشده است.";
                return RecordingUnavailable(callRecordId, reason);
            }
        }

        var cached = _recordingStore.TryGetAbsolutePath(tenantId, callRecordId, data.GetValueOrDefault("recordingFile"));
        if (cached is null)
        {
            var fileId = data.GetValueOrDefault("recordingFileId");
            var relativeDir = Path.Combine("uploads", "records", tenantId.ToString(), "calls", callRecordId.ToString());
            var absDir = Path.Combine(_env.WebRootPath, relativeDir);
            if (Directory.Exists(absDir) && TelefonchyCallService.HasUsableRecordingId(fileId))
                cached = Directory.GetFiles(absDir, $"{fileId}.*").FirstOrDefault();
        }

        if (cached is null || new FileInfo(cached).Length < 4096)
            return RecordingUnavailable(callRecordId, "فایل ضبط روی سرور ذخیره نشده است.");

        var contentType = Path.GetExtension(cached).Equals(".wav", StringComparison.OrdinalIgnoreCase)
            ? "audio/wav"
            : "audio/mpeg";
        return PhysicalFile(cached, contentType, enableRangeProcessing: true);
    }

    [HttpGet("/App/telephony/trace")]
    public async Task<IActionResult> Trace(string? kind, string? q, bool live = false)
    {
        var module = await _metadata.GetModuleByNameAsync("calls");
        if (module is null || !await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var query = _db.TelefonchyTraces.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(kind))
            query = query.Where(t => t.Kind == kind);
        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(t =>
                (t.Summary != null && t.Summary.Contains(q))
                || (t.Action != null && t.Action.Contains(q))
                || (t.Cuid != null && t.Cuid.Contains(q))
                || (t.Phone != null && t.Phone.Contains(q))
                || (t.Payload != null && t.Payload.Contains(q)));
        }

        var rows = await query.OrderByDescending(t => t.Id).Take(400).ToListAsync();
        ViewData["Title"] = "لاگ تلفنچی";
        return View(new TelefonchyTraceViewModel
        {
            Kind = kind,
            Query = q,
            Live = live,
            Rows = rows
        });
    }

    [HttpPost("/App/telephony/trace/clear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearTrace()
    {
        var module = await _metadata.GetModuleByNameAsync("calls");
        if (module is null || !await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        var tid = _tenant.TenantId ?? 0;
        var deleted = await _db.TelefonchyTraces.IgnoreQueryFilters()
            .Where(t => t.TenantId == tid)
            .ExecuteDeleteAsync();
        TempData["Success"] = deleted == 0 ? "لاگی برای پاک‌کردن نبود." : $"{deleted} ردیف لاگ پاک شد.";
        return RedirectToAction(nameof(Trace));
    }

    [HttpGet("/App/telephony/report")]
    public async Task<IActionResult> Report(string? from, string? to, string? direction, string? result, string? format)
    {
        var module = await _metadata.GetModuleByNameAsync("calls");
        if (module is null)
            return NotFound();
        if (!await _access.CanViewModuleAsync(module.Id))
            return Forbid("Identity.Application");

        DateTime? fromUtc = string.IsNullOrWhiteSpace(from) ? DateTime.UtcNow.AddDays(-7) : PersianDateHelper.FromJalaliDateTime(from);
        DateTime? toUtc = string.IsNullOrWhiteSpace(to) ? DateTime.UtcNow : PersianDateHelper.FromJalaliDateTime(to + " 23:59");

        var records = await _db.Records.AsNoTracking()
            .Where(r => r.ModuleId == module.Id && r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc <= toUtc.Value.AddDays(1))
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(2000)
            .ToListAsync();

        var rows = records.Select(r =>
        {
            var d = DynamicRecordService.ParseData(r);
            return new CallReportRow
            {
                Id = r.Id,
                Title = r.Title,
                Direction = d.GetValueOrDefault("direction"),
                Result = d.GetValueOrDefault("result"),
                Phone = d.GetValueOrDefault("phoneNumber"),
                Extension = d.GetValueOrDefault("extension"),
                TalkSeconds = int.TryParse(d.GetValueOrDefault("talkSeconds"), out var ts) ? ts : 0,
                CallAt = d.GetValueOrDefault("callAt"),
                HasRecording = TelefonchyCallService.HasRecording(d)
            };
        }).Where(r =>
            (string.IsNullOrWhiteSpace(direction) || r.Direction == direction)
            && (string.IsNullOrWhiteSpace(result) || r.Result == result))
            .ToList();

        if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("موضوع,نوع,نتیجه,شماره,داخلی,مکالمه,زمان");
            foreach (var row in rows)
            {
                static string Csv(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
                sb.AppendLine(string.Join(",",
                    Csv(row.Title), Csv(row.Direction), Csv(row.Result),
                    Csv(row.Phone), Csv(row.Extension), row.TalkSeconds, Csv(row.CallAt)));
            }
            var bytes = System.Text.Encoding.UTF8.GetPreamble()
                .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "application/vnd.ms-excel", "calls.csv");
        }

        ViewData["Title"] = "گزارش تماس";
        return View(new CallReportViewModel
        {
            From = from,
            To = to,
            Direction = direction,
            Result = result,
            Rows = rows,
            IncomingCount = rows.Count(r => r.Direction == "incoming"),
            OutgoingCount = rows.Count(r => r.Direction == "outgoing"),
            NoAnswerCount = rows.Count(r => r.Result == "noanswer"),
            AvgTalkSeconds = rows.Count == 0 ? 0 : (int)rows.Average(r => r.TalkSeconds)
        });
    }
}

public class CallReportRow
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Direction { get; set; }
    public string? Result { get; set; }
    public string? Phone { get; set; }
    public string? Extension { get; set; }
    public int TalkSeconds { get; set; }
    public string? CallAt { get; set; }
    public bool HasRecording { get; set; }
}

public class CallReportViewModel
{
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Direction { get; set; }
    public string? Result { get; set; }
    public List<CallReportRow> Rows { get; set; } = [];
    public int IncomingCount { get; set; }
    public int OutgoingCount { get; set; }
    public int NoAnswerCount { get; set; }
    public int AvgTalkSeconds { get; set; }
}

public class TelefonchyTraceViewModel
{
    public string? Kind { get; set; }
    public string? Query { get; set; }
    public bool Live { get; set; }
    public List<TelefonchyTrace> Rows { get; set; } = [];
}
