using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

public class TelefonchyWebhookJob
{
    private readonly TelefonchyCallService _calls;
    private readonly TenantIntegrationService _integrations;
    private readonly ILogger<TelefonchyWebhookJob> _logger;

    public TelefonchyWebhookJob(
        TelefonchyCallService calls,
        TenantIntegrationService integrations,
        ILogger<TelefonchyWebhookJob> logger)
    {
        _calls = calls;
        _integrations = integrations;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 1)]
    public async Task ProcessEventAsync(int tenantId, string payloadJson)
    {
        _calls.BindTenant(tenantId);
        var call = TelefonchyClient.ParseWebhookPayload(payloadJson);
        if (call is null)
        {
            _logger.LogWarning("Telefonchy event payload unreadable for tenant {TenantId}.", tenantId);
            return;
        }

        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasTelefonchy)
            return;
        if (!string.IsNullOrWhiteSpace(call.WebserviceToken)
            && !string.Equals(call.WebserviceToken, config.TelefonchyToken, StringComparison.Ordinal))
        {
            _logger.LogWarning("Telefonchy event token mismatch for tenant {TenantId}.", tenantId);
            return;
        }

        var ev = (call.Event ?? "").Trim().ToLowerInvariant();
        if (ev == "recording.merged.ready")
        {
            BackgroundJob.Enqueue<TelefonchyCallSyncJob>(
                j => j.FetchMergedRecordingAsync(tenantId, call.Cuid, call.RecordId ?? call.CallId, 0));
            return;
        }

        if (ev == "ringing")
        {
            await _calls.HandleRingingAsync(call);
            return;
        }

        await _calls.HandleHangupOrCdrAsync(call, fetchDetails: true);
        if (LooksAnswered(call))
            ScheduleMergedFetch(tenantId, call, TimeSpan.FromSeconds(20));
    }

    [AutomaticRetry(Attempts = 2)]
    public async Task ProcessCdrAsync(int tenantId, string payloadJson)
    {
        _calls.BindTenant(tenantId);
        var call = TelefonchyClient.ParseWebhookPayload(payloadJson);
        if (call is null)
        {
            _logger.LogWarning("Telefonchy CDR payload unreadable for tenant {TenantId}.", tenantId);
            return;
        }

        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasTelefonchy)
            return;
        if (!string.IsNullOrWhiteSpace(call.WebserviceToken)
            && !string.Equals(call.WebserviceToken, config.TelefonchyToken, StringComparison.Ordinal))
        {
            _logger.LogWarning("Telefonchy CDR token mismatch for tenant {TenantId}.", tenantId);
            return;
        }

        if (string.Equals(call.Event, "recording.merged.ready", StringComparison.OrdinalIgnoreCase))
        {
            BackgroundJob.Enqueue<TelefonchyCallSyncJob>(
                j => j.FetchMergedRecordingAsync(tenantId, call.Cuid, call.RecordId ?? call.CallId, 0));
            return;
        }

        await _calls.HandleHangupOrCdrAsync(call, fetchDetails: string.IsNullOrWhiteSpace(call.FileId));
        if (LooksAnswered(call))
            ScheduleMergedFetch(tenantId, call, TimeSpan.FromSeconds(20));
    }

    internal static void ScheduleMergedFetch(int tenantId, TelefonchyCallDto call, TimeSpan delay)
    {
        BackgroundJob.Schedule<TelefonchyCallSyncJob>(
            j => j.FetchMergedRecordingAsync(tenantId, call.Cuid, call.RecordId ?? call.CallId, 0),
            delay);
    }

    private static bool LooksAnswered(TelefonchyCallDto call) =>
        call.TalkSeconds is > 0
        || call.HasAudio
        || !string.IsNullOrWhiteSpace(call.FileRecord)
        || (call.Status ?? "").Contains("ANSWER", StringComparison.OrdinalIgnoreCase);
}

public class TelefonchyCallSyncJob
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15)
    ];

    private readonly CrmDbContext _db;
    private readonly TenantIntegrationService _integrations;
    private readonly TelefonchyClient _client;
    private readonly TelefonchyCallService _calls;
    private readonly ILogger<TelefonchyCallSyncJob> _logger;

    public TelefonchyCallSyncJob(
        CrmDbContext db,
        TenantIntegrationService integrations,
        TelefonchyClient client,
        TelefonchyCallService calls,
        ILogger<TelefonchyCallSyncJob> logger)
    {
        _db = db;
        _integrations = integrations;
        _client = client;
        _calls = calls;
        _logger = logger;
    }

    public async Task SyncAllTenantsAsync()
    {
        var tenants = await _db.Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial)
            .Select(t => t.Id)
            .ToListAsync();

        foreach (var tenantId in tenants)
        {
            try
            {
                await SyncTenantAsync(tenantId, initial: false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telefonchy sync failed for tenant {TenantId}.", tenantId);
            }
        }
    }

    [AutomaticRetry(Attempts = 1)]
    public async Task SyncTenantAsync(int tenantId, bool initial = false)
    {
        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasTelefonchy || !config.TelefonchySyncEnabled)
            return;

        _calls.BindTenant(tenantId);

        var from = initial || config.TelefonchyLastSyncUtc is null
            ? DateTime.UtcNow.AddDays(-30)
            : config.TelefonchyLastSyncUtc.Value.AddHours(-1);

        var page = 1;
        var imported = 0;
        while (page <= 50)
        {
            var result = await _client.GetCallsAsync(tenantId, page, from, DateTime.UtcNow.AddDays(1));
            if (result is null || result.Calls.Count == 0)
                break;

            foreach (var call in result.Calls)
            {
                var record = await _calls.UpsertCallAsync(call);
                if (record is not null)
                    imported++;
            }

            if (result.CurrentPage >= result.LastPage)
                break;
            page++;
        }

        config.TelefonchyLastSyncUtc = DateTime.UtcNow;
        await _integrations.SaveAsync(config, tenantId);
        _logger.LogInformation("Telefonchy sync tenant {TenantId}: {Count} upserts.", tenantId, imported);
        await BackfillMissingRecordingsAsync(tenantId);
    }

    /// <summary>تماس‌های پاسخ‌داده‌شدهٔ اخیر که هنوز فایل ضبط ندارند را صف می‌کند.</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task BackfillMissingRecordingsAsync(int tenantId)
    {
        _calls.BindTenant(tenantId);
        var module = await _db.Modules.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Name == "calls" && m.IsActive);
        if (module is null)
            return;

        var since = DateTime.UtcNow.AddHours(-36);
        var records = await _db.Records.AsNoTracking()
            .Where(r => r.ModuleId == module.Id && r.CreatedAtUtc >= since)
            .ToListAsync();

        var queued = 0;
        foreach (var record in records)
        {
            var data = DynamicRecordService.ParseData(record);
            if (TelefonchyCallService.HasRecording(data) || !TelefonchyCallService.IsRecordingPending(data))
                continue;

            BackgroundJob.Enqueue<TelefonchyCallSyncJob>(
                j => j.FetchMergedRecordingAsync(
                    tenantId, data.GetValueOrDefault("cuid"), data.GetValueOrDefault("callId"), 0));
            queued++;
        }

        if (queued > 0)
            _logger.LogInformation("Telefonchy queued {Count} missing recordings for tenant {TenantId}.", queued, tenantId);
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task FetchMergedRecordingAsync(int tenantId, string? cuid, string? recordId, int attempt)
    {
        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasTelefonchy || !config.TelefonchyRecordingEnabled)
            return;

        _calls.BindTenant(tenantId);
        var record = await _calls.FindCallRecordAsync(cuid, recordId);
        if (record is null)
        {
            _logger.LogInformation(
                "Telefonchy recording fetch: CRM row not found tenant {TenantId} cuid {Cuid}.", tenantId, cuid);
            Reschedule(tenantId, cuid, recordId, attempt);
            return;
        }

        var data = DynamicRecordService.ParseData(record);
        if (TelefonchyCallService.HasRecording(data)
            && !string.IsNullOrWhiteSpace(data.GetValueOrDefault("recordingFile")))
            return;

        var state = await _calls.EnsureRecordingAsync(record.ModuleId, record);
        if (state == TelefonchyCallService.CallRecordingState.Ready)
        {
            _logger.LogInformation("Telefonchy merged recording saved tenant {TenantId} cuid {Cuid}.", tenantId, cuid);
            return;
        }

        _logger.LogInformation(
            "Telefonchy merged recording {State} tenant {TenantId} cuid {Cuid} (attempt {Attempt}).",
            state, tenantId, cuid, attempt + 1);
        Reschedule(tenantId, cuid, recordId ?? data.GetValueOrDefault("callId"), attempt);
    }

    private static void Reschedule(int tenantId, string? cuid, string? recordId, int attempt)
    {
        if (attempt < 0 || attempt >= RetryDelays.Length)
            return;

        BackgroundJob.Schedule<TelefonchyCallSyncJob>(
            j => j.FetchMergedRecordingAsync(tenantId, cuid, recordId, attempt + 1),
            RetryDelays[attempt]);
    }

    /// <summary>جاب‌های زمان‌بندی‌شدهٔ قبلی؛ به مسیر merged هدایت می‌شود.</summary>
    [Obsolete]
    [AutomaticRetry(Attempts = 0)]
    public Task RefreshRecordingAsync(int tenantId, string? cuid, string? callId, string? phone) =>
        FetchMergedRecordingAsync(tenantId, cuid, callId, 0);

    [Obsolete]
    [AutomaticRetry(Attempts = 0)]
    public Task RefreshRecordingAsync(int tenantId, string? cuid, string? callId, string? phone, int attempt) =>
        FetchMergedRecordingAsync(tenantId, cuid, callId, 0);
}
