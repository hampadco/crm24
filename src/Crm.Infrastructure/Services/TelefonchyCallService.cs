using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

public sealed class PhoneMatch
{
    public DynamicRecord Record { get; init; } = null!;
    public string ModuleName { get; init; } = "";
}

public class TelefonchyCallService
{
    private readonly CrmDbContext _db;
    private readonly TenantContext _tenant;
    private readonly TenantIntegrationService _integrations;
    private readonly DynamicRecordService _records;
    private readonly TelefonchyClient _client;
    private readonly ICallRecordingStore _recordings;
    private readonly ILogger<TelefonchyCallService> _logger;
    private readonly ICallPopupNotifier _notifier;

    public TelefonchyCallService(
        CrmDbContext db,
        TenantContext tenant,
        TenantIntegrationService integrations,
        DynamicRecordService records,
        TelefonchyClient client,
        ICallRecordingStore recordings,
        ILogger<TelefonchyCallService> logger,
        ICallPopupNotifier notifier)
    {
        _db = db;
        _tenant = tenant;
        _integrations = integrations;
        _records = records;
        _client = client;
        _recordings = recordings;
        _logger = logger;
        _notifier = notifier;
    }

    public void BindTenant(int tenantId)
    {
        _tenant.TenantId = tenantId;
        _tenant.IsTenantAdmin = true;
    }

    /// <summary>
    /// آیا شناسه فایل ضبط قابل استفاده است. رکوردهای قدیمی مقدار "0" دارند که
    /// یعنی تماس ضبط نشده، ولی مقدار خالی نیست و نباید پخش‌کننده نمایش داده شود.
    /// </summary>
    public static bool HasUsableRecordingId(string? fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return false;

        var value = fileId.Trim();
        if (value is "0" or "-" or "false" or "null")
            return false;
        // شناسه واقعی فایل ضبط خط تیره دارد؛ ObjectId تماس این شکل را ندارد.
        return value.Contains('-');
    }

    public static bool HasRecording(IReadOnlyDictionary<string, string?> data) =>
        !string.IsNullOrWhiteSpace(data.GetValueOrDefault("recordingFile"))
        || HasUsableRecordingId(data.GetValueOrDefault("recordingFileId"));

    /// <summary>
    /// تماس مکالمه داشته یا نام فایل ضبط آمده، ولی فایل هنوز در CRM ذخیره نشده.
    /// </summary>
    public static bool IsRecordingPending(IReadOnlyDictionary<string, string?> data)
    {
        if (HasRecording(data))
            return false;
        if (!string.IsNullOrWhiteSpace(data.GetValueOrDefault("recordingFileName")))
            return true;
        if (data.GetValueOrDefault("hasAudio") is "true" or "1")
            return true;
        if (string.Equals(data.GetValueOrDefault("recordingStatus"), "processing", StringComparison.OrdinalIgnoreCase))
            return true;
        return int.TryParse(data.GetValueOrDefault("talkSeconds"), out var talk) && talk > 0;
    }

    public static string? CanonicalPhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains('*'))
            return null;

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return null;
        if (digits.StartsWith("0098", StringComparison.Ordinal))
            digits = digits[4..];
        else if (digits.StartsWith("98", StringComparison.Ordinal) && digits.Length > 10)
            digits = digits[2..];
        if (digits.StartsWith('0'))
            digits = digits[1..];
        return digits.Length >= 8 ? digits : null;
    }

    public static bool IsMasked(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && raw.Contains('*');

    public async Task<IReadOnlyList<PhoneMatch>> MatchAsync(string? phone, CancellationToken ct = default)
    {
        var canonical = CanonicalPhone(phone);
        if (canonical is null)
            return [];

        return await MatchByCanonicalOrMaskAsync(canonical, maskRegex: null, ct);
    }

    public async Task<IReadOnlyList<PhoneMatch>> MatchMaskedAsync(string? masked, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(masked))
            return [];
        if (!IsMasked(masked))
            return await MatchAsync(masked, ct);

        var maskCanon = new string(masked.Where(c => char.IsDigit(c) || c == '*').ToArray());
        if (maskCanon.StartsWith("0098", StringComparison.Ordinal))
            maskCanon = maskCanon[4..];
        else if (maskCanon.StartsWith("98") && maskCanon.Length > 10)
            maskCanon = maskCanon[2..];
        if (maskCanon.StartsWith('0'))
            maskCanon = maskCanon[1..];
        var canonPattern = "^" + Regex.Escape(maskCanon).Replace("\\*", ".", StringComparison.Ordinal) + "$";
        return await MatchByCanonicalOrMaskAsync(null, canonPattern, ct);
    }

    public async Task<DynamicRecord?> UpsertCallAsync(TelefonchyCallDto call, CancellationToken ct = default)
    {
        var config = await _integrations.GetAsync();
        if (!config.TelefonchyAutoLogCalls)
            return null;

        var callsModule = await _db.Modules.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Name == "calls" && m.IsActive, ct);
        if (callsModule is null)
        {
            _logger.LogWarning("Calls module missing for tenant {TenantId}.", _tenant.TenantId);
            return null;
        }

        if (IsInternalLeg(call))
        {
            _logger.LogDebug("Telefonchy: skipped internal leg to {Number}.", call.ExternalNumber);
            return null;
        }

        var matches = await ResolveMatchesAsync(call, ct);
        var contact = matches.FirstOrDefault(m => m.ModuleName == "contacts");
        var lead = matches.FirstOrDefault(m => m.ModuleName == "leads");

        var values = BuildCallValues(call, contact?.Record, lead?.Record);
        var existing = await FindExistingCallAsync(callsModule.Id, call, ct);

        if (existing is null && string.IsNullOrWhiteSpace(values.GetValueOrDefault("phoneNumber")))
            return null;

        if (existing is not null)
        {
            var merged = Merge(DynamicRecordService.ParseData(existing), values);
            await _records.UpdateAsync(callsModule.Id, existing.Id, merged);
            existing.Title = merged.GetValueOrDefault("name") ?? existing.Title;
            existing.CustomData = JsonSerializer.Serialize(merged);
            return existing;
        }

        return await _records.CreateAsync(callsModule.Id, values);
    }

    public enum CallRecordingState
    {
        Ready,
        Processing,
        None
    }

    public async Task<DynamicRecord?> FindCallRecordAsync(string? cuid, string? callId, CancellationToken ct = default)
    {
        var callsModule = await _db.Modules.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Name == "calls" && m.IsActive, ct);
        if (callsModule is null)
            return null;

        return await FindExistingCallAsync(callsModule.Id, new TelefonchyCallDto
        {
            Cuid = cuid,
            CallId = callId,
            RecordId = callId
        }, ct);
    }

    /// <summary>
    /// فایل ضبط را از مسیر merged تلفنچی می‌گیرد و روی رکورد ذخیره می‌کند.
    /// اگر آماده نباشد Processing برمی‌گرداند تا UI و جاب بعداً تکرار کنند.
    /// </summary>
    public async Task<CallRecordingState> EnsureRecordingAsync(
        int callsModuleId, DynamicRecord record, CancellationToken ct = default)
    {
        var data = DynamicRecordService.ParseData(record);
        var tenantId = _tenant.TenantId ?? 0;
        if (!string.IsNullOrWhiteSpace(data.GetValueOrDefault("recordingFile"))
            && _recordings.TryGetAbsolutePath(tenantId, record.Id, data.GetValueOrDefault("recordingFile")) is not null)
            return CallRecordingState.Ready;

        var cuid = data.GetValueOrDefault("cuid");
        var recordId = MongoCallId(data.GetValueOrDefault("callId"));

        if (string.IsNullOrWhiteSpace(cuid) || string.IsNullOrWhiteSpace(recordId))
        {
            var found = await _client.FindCallAsync(
                cuid, data.GetValueOrDefault("callId"), data.GetValueOrDefault("phoneNumber"), tenantId, ct);
            if (found is not null)
            {
                await UpsertCallAsync(found, ct);
                record = await _records.GetAsync(callsModuleId, record.Id) ?? record;
                data = DynamicRecordService.ParseData(record);
                cuid = data.GetValueOrDefault("cuid") ?? found.Cuid;
                recordId = MongoCallId(data.GetValueOrDefault("callId")) ?? found.RecordId ?? MongoCallId(found.CallId);
            }
        }

        if (!string.IsNullOrWhiteSpace(cuid) && !string.IsNullOrWhiteSpace(recordId))
        {
            var merged = await _client.GetMergedRecordingAsync(cuid, recordId, tenantId, ct: ct);
            if (merged.IsReady)
            {
                await PersistRecordingAsync(callsModuleId, record, data, merged.Content!, merged.ContentType, merged.FileName, "merged", ct);
                return CallRecordingState.Ready;
            }

            if (merged.State == TelefonchyMergedRecordingState.Processing)
            {
                data["recordingStatus"] = "processing";
                await _records.UpdateAsync(callsModuleId, record.Id, data);
                return CallRecordingState.Processing;
            }
        }

        if (HasUsableRecordingId(data.GetValueOrDefault("recordingFileId")) && !string.IsNullOrWhiteSpace(cuid))
        {
            var download = await _client.GetRecordingAsync(data.GetValueOrDefault("recordingFileId")!, cuid, tenantId, ct);
            if (download.Ok)
            {
                await PersistRecordingAsync(
                    callsModuleId, record, data, download.Content!, download.ContentType, download.FileName, "file_id", ct);
                return CallRecordingState.Ready;
            }
        }

        return CallRecordingState.None;
    }

    private async Task PersistRecordingAsync(
        int callsModuleId,
        DynamicRecord record,
        Dictionary<string, string?> data,
        byte[] content,
        string contentType,
        string fileName,
        string source,
        CancellationToken ct)
    {
        var ext = Path.GetExtension(fileName);
        var stored = await _recordings.SaveAsync(
            _tenant.TenantId ?? 0, record.Id, content, ext, contentType, fileName, ct);
        data["recordingFile"] = stored;
        data["recordingSource"] = source;
        data["recordingStatus"] = "ready";
        await _records.UpdateAsync(callsModuleId, record.Id, data);
        record.CustomData = JsonSerializer.Serialize(data);
    }

    private static string? MongoCallId(string? value) =>
        string.IsNullOrWhiteSpace(value) || TelefonchyClient.IsAsteriskUniqueId(value) ? null : value.Trim();

    /// <summary>
    /// هنگام باز کردن تماس یا پخش صدا، لیست زنده GET /calls را می‌خواند و
    /// file_id را در صورت وجود روی رکورد ذخیره می‌کند.
    /// </summary>
    public async Task<bool> TryAttachRecordingAsync(int callsModuleId, DynamicRecord record, CancellationToken ct = default)
    {
        var data = DynamicRecordService.ParseData(record);
        if (HasUsableRecordingId(data.GetValueOrDefault("recordingFileId")))
            return true;

        var found = await _client.FindCallAsync(
            data.GetValueOrDefault("cuid"),
            data.GetValueOrDefault("callId"),
            data.GetValueOrDefault("phoneNumber"),
            _tenant.TenantId,
            ct);
        if (found is null)
            return false;

        await UpsertCallAsync(found, ct);
        return HasUsableRecordingId(found.FileId);
    }

    /// <summary>
    /// پای داخلی تماس سریع: تلفنچی اول خود داخلی اپراتور را می‌گیرد و همان را هم
    /// به‌عنوان یک تماس گزارش می‌کند. این پا نباید رکورد جداگانه بسازد.
    /// </summary>
    private static bool IsInternalLeg(TelefonchyCallDto call)
    {
        if (string.Equals(call.Type, "local", StringComparison.OrdinalIgnoreCase))
            return true;

        // اگر هیچ طرف تماس شماره بیرونی نباشد، این پای داخلی است.
        // وقتی هر دو طرف خالی‌اند چیزی برای قضاوت نداریم، پس رکورد را نگه می‌داریم.
        if (string.IsNullOrWhiteSpace(call.Src) && string.IsNullOrWhiteSpace(call.Dest))
            return false;

        return call.HasNoExternalParty;
    }

    /// <summary>
    /// رکورد تماس موجود را پیدا می‌کند. پاسخ اولیه تماس سریع cuid ندارد، پس
    /// requestId هم به‌عنوان کلید تطبیق استفاده می‌شود تا رکورد دوباره ساخته نشود.
    /// </summary>
    private async Task<DynamicRecord?> FindExistingCallAsync(
        int callsModuleId, TelefonchyCallDto call, CancellationToken ct)
    {
        var tenantId = _tenant.TenantId ?? 0;
        var identity = FirstNonEmpty(call.Cuid, call.CallId);
        if (!string.IsNullOrWhiteSpace(identity))
        {
            var byIdentity = await _db.Records
                .FromSqlInterpolated($"""
                    SELECT * FROM "Records"
                    WHERE "ModuleId" = {callsModuleId}
                      AND "TenantId" = {tenantId}
                      AND (
                            "CustomData" ->> 'cuid' = {identity}
                         OR "CustomData" ->> 'callId' = {identity}
                      )
                    LIMIT 1
                    """)
                .FirstOrDefaultAsync(ct);
            if (byIdentity is not null)
                return byIdentity;
        }

        if (!string.IsNullOrWhiteSpace(call.RequestId))
        {
            var byRequest = await _db.Records
                .FromSqlInterpolated($"""
                    SELECT * FROM "Records"
                    WHERE "ModuleId" = {callsModuleId}
                      AND "TenantId" = {tenantId}
                      AND "CustomData" ->> 'requestId' = {call.RequestId}
                    LIMIT 1
                    """)
                .FirstOrDefaultAsync(ct);
            if (byRequest is not null)
                return byRequest;
        }

        if (!string.IsNullOrWhiteSpace(call.CallId)
            && !string.Equals(call.CallId, identity, StringComparison.Ordinal))
        {
            var byCallId = await _db.Records
                .FromSqlInterpolated($"""
                    SELECT * FROM "Records"
                    WHERE "ModuleId" = {callsModuleId}
                      AND "TenantId" = {tenantId}
                      AND (
                            "CustomData" ->> 'callId' = {call.CallId}
                         OR "CustomData" ->> 'cuid' = {call.CallId}
                      )
                    LIMIT 1
                    """)
                .FirstOrDefaultAsync(ct);
            if (byCallId is not null)
                return byCallId;
        }

        if (HasUsableRecordingId(call.FileId))
        {
            var fileId = call.FileId!;
            var byFile = await _db.Records
                .FromSqlInterpolated($"""
                    SELECT * FROM "Records"
                    WHERE "ModuleId" = {callsModuleId}
                      AND "TenantId" = {tenantId}
                      AND "CustomData" ->> 'recordingFileId' = {fileId}
                    LIMIT 1
                    """)
                .FirstOrDefaultAsync(ct);
            if (byFile is not null)
                return byFile;
        }

        return await FindPendingOutgoingCallAsync(callsModuleId, call, ct);
    }

    /// <summary>
    /// CDR وب‌هوک اغلب cuid و requestId ندارد. رکورد لحظهٔ کلیک را با همان شماره
    /// در چند دقیقهٔ اخیر پیدا می‌کنیم تا ردیف دوم ساخته نشود.
    /// </summary>
    private async Task<DynamicRecord?> FindPendingOutgoingCallAsync(
        int callsModuleId, TelefonchyCallDto call, CancellationToken ct)
    {
        if (call.Direction != "outgoing")
            return null;

        var canonical = CanonicalPhone(call.ExternalNumber);
        if (canonical is null)
            return null;

        var since = DateTime.UtcNow.AddMinutes(-20);
        var tenantId = _tenant.TenantId ?? 0;
        var candidates = await _db.Records
            .FromSqlInterpolated($"""
                SELECT * FROM "Records"
                WHERE "ModuleId" = {callsModuleId}
                  AND "TenantId" = {tenantId}
                  AND "CreatedAtUtc" >= {since}
                  AND COALESCE("CustomData" ->> 'direction', '') = 'outgoing'
                ORDER BY "CreatedAtUtc" DESC
                LIMIT 20
                """)
            .ToListAsync(ct);

        var samePhone = candidates
            .Select(r => (Record: r, Data: DynamicRecordService.ParseData(r)))
            .Where(x => CanonicalPhone(x.Data.GetValueOrDefault("phoneNumber")) == canonical)
            .ToList();
        if (samePhone.Count == 0)
            return null;

        // اگر ورودی هویت ندارد (CDR)، یا رکورد موجود هنوز ناقص است، ادغام کن.
        var incomingHasId = !string.IsNullOrWhiteSpace(call.Cuid)
            || !string.IsNullOrWhiteSpace(call.RequestId)
            || (!string.IsNullOrWhiteSpace(call.CallId) && !TelefonchyClient.IsAsteriskUniqueId(call.CallId));

        var incomplete = samePhone.FirstOrDefault(x => IsIncompleteCallRecord(x.Data));
        if (incomplete.Record is not null)
            return incomplete.Record;

        if (!incomingHasId)
            return samePhone[0].Record;

        return null;
    }

    private static bool IsIncompleteCallRecord(Dictionary<string, string?> data)
    {
        var hasTalk = int.TryParse(data.GetValueOrDefault("talkSeconds"), out var talk) && talk > 0;
        var hasCuid = !string.IsNullOrWhiteSpace(data.GetValueOrDefault("cuid"));
        return !hasTalk || !hasCuid;
    }

    public async Task NotifyIncomingAsync(
        TelefonchyCallDto call,
        DynamicRecord? callRecord,
        IReadOnlyList<PhoneMatch> matches,
        CancellationToken ct = default)
    {
        if (_tenant.TenantId is not int tenantId)
            return;

        var contact = matches.FirstOrDefault(m => m.ModuleName == "contacts");
        var lead = matches.FirstOrDefault(m => m.ModuleName == "leads");
        var title = contact?.Record.Title ?? lead?.Record.Title
            ?? call.ExternalNumber ?? call.Src ?? "شماره ناشناس";
        var phone = call.ExternalNumber ?? call.Src ?? "";
        var link = callRecord is not null
            ? $"/App/m/calls/{callRecord.Id}"
            : contact is not null ? $"/App/m/contacts/{contact.Record.Id}"
            : lead is not null ? $"/App/m/leads/{lead.Record.Id}"
            : "/App/m/calls";

        var userIds = await ResolveNotifyUserIdsAsync(tenantId, call, ct);
        foreach (var userId in userIds)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = userId,
                Title = call.Direction == "incoming" ? "تماس ورودی" : "تماس خروجی",
                Body = title,
                LinkUrl = link
            });
            await _notifier.NotifyIncomingAsync(userId, new
            {
                caller = phone,
                contactId = contact?.Record.Id,
                contactTitle = contact?.Record.Title,
                leadId = lead?.Record.Id,
                leadTitle = lead?.Record.Title,
                callRecordId = callRecord?.Id,
                direction = call.Direction,
                eventName = call.Event,
                ambiguous = matches.Count > 1
            }, ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task NotifyCallStatusAsync(IEnumerable<int> userIds, object payload, CancellationToken ct = default)
    {
        foreach (var userId in userIds)
            await _notifier.NotifyStatusAsync(userId, payload, ct);
    }

    public async Task HandleRingingAsync(TelefonchyCallDto call, CancellationToken ct = default)
    {
        var config = await _integrations.GetAsync();
        var matches = await ResolveMatchesAsync(call, ct);
        DynamicRecord? record = null;
        if (config.TelefonchyAutoLogCalls)
            record = await UpsertCallAsync(call, ct);
        await NotifyIncomingAsync(call, record, matches, ct);
    }

    public async Task HandleHangupOrCdrAsync(TelefonchyCallDto call, bool fetchDetails, CancellationToken ct = default)
    {
        var config = await _integrations.GetAsync();
        // مسیر جزئیات تماس فقط call_id (شناسه رکورد) را می‌پذیرد و با cuid ریدایرکت می‌دهد.
        var lookupId = !string.IsNullOrWhiteSpace(call.CallId)
            && !TelefonchyClient.IsAsteriskUniqueId(call.CallId)
                ? call.CallId
                : call.Cuid;
        if (fetchDetails && !string.IsNullOrWhiteSpace(lookupId))
        {
            var detailed = await _client.GetCallAsync(lookupId, _tenant.TenantId, ct);
            if (detailed is not null)
            {
                detailed.Event ??= call.Event;
                detailed.WebserviceToken ??= call.WebserviceToken;
                detailed.Cuid ??= call.Cuid;
                detailed.CallId ??= call.CallId;
                detailed.RequestId ??= call.RequestId;
                detailed.Extension ??= call.Extension;
                call = detailed;
            }
        }

        if (!config.TelefonchyAutoLogCalls)
            return;

        await UpsertCallAsync(call, ct);
    }

    private async Task<IReadOnlyList<PhoneMatch>> ResolveMatchesAsync(TelefonchyCallDto call, CancellationToken ct)
    {
        var number = call.ExternalNumber;
        if (IsMasked(number))
            return await MatchMaskedAsync(number, ct);
        return await MatchAsync(number, ct);
    }

    private async Task<List<PhoneMatch>> MatchByCanonicalOrMaskAsync(
        string? canonical, string? maskRegex, CancellationToken ct)
    {
        var modules = await _db.Modules.AsNoTracking()
            .Where(m => m.Name == "contacts" || m.Name == "leads")
            .Select(m => new { m.Id, m.Name })
            .ToListAsync(ct);
        if (modules.Count == 0)
            return [];

        var ids = modules.Select(m => m.Id).ToArray();
        var records = await _db.Records.AsNoTracking()
            .Where(r => ids.Contains(r.ModuleId))
            .ToListAsync(ct);

        var results = new List<PhoneMatch>();
        Regex? rx = maskRegex is null ? null : new Regex(maskRegex, RegexOptions.CultureInvariant);
        foreach (var record in records)
        {
            var data = DynamicRecordService.ParseData(record);
            foreach (var key in new[] { "mobile", "phone" })
            {
                if (!data.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                    continue;
                var canon = CanonicalPhone(raw);
                if (canon is null)
                    continue;
                var hit = canonical is not null
                    ? string.Equals(canon, canonical, StringComparison.Ordinal)
                    : rx is not null && rx.IsMatch(canon);
                if (!hit)
                    continue;
                var moduleName = modules.First(m => m.Id == record.ModuleId).Name;
                results.Add(new PhoneMatch { Record = record, ModuleName = moduleName });
                break;
            }
        }

        return results;
    }

    private async Task<List<int>> ResolveNotifyUserIdsAsync(int tenantId, TelefonchyCallDto call, CancellationToken ct)
    {
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .Select(u => new { u.Id, u.TelefonchyExtension })
            .ToListAsync(ct);

        var dest = call.Direction == "incoming" ? call.Dest : call.Extension;
        if (!string.IsNullOrWhiteSpace(dest))
        {
            var destCanon = dest.Trim();
            var matched = users
                .Where(u => !string.IsNullOrWhiteSpace(u.TelefonchyExtension)
                            && string.Equals(u.TelefonchyExtension.Trim(), destCanon, StringComparison.Ordinal))
                .Select(u => u.Id)
                .ToList();
            if (matched.Count > 0)
                return matched;
        }

        return users.Select(u => u.Id).ToList();
    }

    private static Dictionary<string, string?> BuildCallValues(
        TelefonchyCallDto call, DynamicRecord? contact, DynamicRecord? lead)
    {
        var phone = CanonicalPhone(call.ExternalNumber) is string canon ? "0" + canon : call.ExternalNumber;
        var party = FirstMeaningful(contact?.Title, lead?.Title, phone, call.ExternalNumber);
        var incoming = call.Direction == "incoming";
        var prefix = incoming ? "تماس ورودی از " : "تماس خروجی با ";
        var fallback = incoming ? "تماس ورودی" : "تماس خروجی";
        var title = string.IsNullOrWhiteSpace(party) || party == fallback ? fallback : prefix + party;
        if (title.StartsWith(prefix + prefix, StringComparison.Ordinal)
            || title == prefix + fallback)
            title = string.IsNullOrWhiteSpace(phone) ? fallback : prefix + phone;

        var result = MapResult(call.Status, call.TalkSeconds);
        var talk = call.TalkSeconds;
        var wait = call.WaitSeconds;
        var started = call.StartedAt ?? DateTime.Now;

        return new Dictionary<string, string?>
        {
            ["name"] = title,
            ["contact"] = contact?.Id.ToString(),
            ["lead"] = lead?.Id.ToString(),
            ["direction"] = call.Direction,
            ["callAt"] = started.ToString("yyyy-MM-ddTHH:mm"),
            ["durationMinutes"] = talk is int t ? Math.Max(0, (int)Math.Ceiling(t / 60d)).ToString() : null,
            ["result"] = result,
            ["cuid"] = call.Cuid,
            ["callId"] = call.CallId,
            ["requestId"] = call.RequestId,
            ["phoneNumber"] = phone,
            ["extension"] = call.Extension,
            ["waitSeconds"] = wait?.ToString(),
            ["talkSeconds"] = talk?.ToString(),
            ["recordingFileId"] = call.FileId,
            ["recordingFileName"] = call.FileRecord,
            ["hasAudio"] = call.HasAudio ? "true" : null,
            ["fileStatus"] = call.FileStatus
        };
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? FirstMeaningful(params string?[] values) =>
        values.FirstOrDefault(v =>
            !string.IsNullOrWhiteSpace(v)
            && v is not ("تماس خروجی" or "تماس ورودی"));

    private static string MapResult(string? status, int? talkSeconds)
    {
        var s = (status ?? "").Replace(" ", "", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
        if (s.Contains("ANSWER"))
            return "answered";
        if (s.Contains("NOANSWER") || s.Contains("NO-ANSWER"))
            return "noanswer";
        if (s.Contains("BUSY"))
            return "busy";
        if (talkSeconds is > 0)
            return "answered";
        return "followup";
    }

    private static Dictionary<string, string?> Merge(
        Dictionary<string, string?> existing, Dictionary<string, string?> incoming)
    {
        var merged = new Dictionary<string, string?>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in incoming)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (key.Equals("recordingFileId", StringComparison.OrdinalIgnoreCase)
                && !HasUsableRecordingId(value))
                continue;
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase)
                && IsGenericCallTitle(value)
                && !IsGenericCallTitle(merged.GetValueOrDefault("name")))
                continue;
            if (key.Equals("result", StringComparison.OrdinalIgnoreCase)
                && value == "followup"
                && merged.GetValueOrDefault("result") == "answered")
                continue;
            merged[key] = value;
        }
        return merged;
    }

    private static bool IsGenericCallTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return true;
        return title is "تماس خروجی" or "تماس ورودی"
            or "تماس خروجی با تماس خروجی" or "تماس ورودی از تماس ورودی";
    }
}
