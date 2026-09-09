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
    private readonly TelefonchyTraceService _trace;

    public TelefonchyWebhookJob(
        TelefonchyCallService calls,
        TenantIntegrationService integrations,
        ILogger<TelefonchyWebhookJob> logger,
        TelefonchyTraceService trace)
    {
        _calls = calls;
        _integrations = integrations;
        _logger = logger;
        _trace = trace;
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
        await _trace.WriteAsync(tenantId, TelefonchyTraceService.FromCall(
            "job", "process-event", call, $"جاب رویداد: {ev}", payloadJson));
        if (ev == "recording.merged.ready")
        {
            await _calls.HandleHangupOrCdrAsync(call, fetchDetails: false);
            await _calls.TryFetchReadyRecordingAsync(call);
            return;
        }

        if (ev == "ringing")
        {
            await _calls.HandleRingingAsync(call);
            return;
        }

        await _calls.HandleHangupOrCdrAsync(call, fetchDetails: false);
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
            await _trace.WriteAsync(tenantId, TelefonchyTraceService.FromCall(
                "job", "recording-merged-ready", call, "وب‌هوک ضبط merged آماده شد — دریافت فایل", payloadJson));
            await _calls.HandleHangupOrCdrAsync(call, fetchDetails: false);
            await _calls.TryFetchReadyRecordingAsync(call);
            return;
        }

        await _trace.WriteAsync(tenantId, TelefonchyTraceService.FromCall(
            "job", "process-cdr", call,
            $"جاب CDR: status={call.Status} talk={call.TalkSeconds} file_id={call.FileId}",
            payloadJson));
        await _calls.HandleHangupOrCdrAsync(call, fetchDetails: false);
        if (TelefonchyCallService.HasUsableRecordingId(call.FileId)
            || !string.IsNullOrWhiteSpace(call.DownloadUrl))
            await _calls.TryFetchReadyRecordingAsync(call);
    }
}

public class TelefonchyCallSyncJob
{
    private readonly CrmDbContext _db;
    private readonly TenantIntegrationService _integrations;
    private readonly TelefonchyClient _client;
    private readonly TelefonchyCallService _calls;
    private readonly TelefonchyApiGate _gate;
    private readonly ILogger<TelefonchyCallSyncJob> _logger;
    private readonly TelefonchyTraceService _trace;

    public TelefonchyCallSyncJob(
        CrmDbContext db,
        TenantIntegrationService integrations,
        TelefonchyClient client,
        TelefonchyCallService calls,
        TelefonchyApiGate gate,
        ILogger<TelefonchyCallSyncJob> logger,
        TelefonchyTraceService trace)
    {
        _db = db;
        _integrations = integrations;
        _client = client;
        _calls = calls;
        _gate = gate;
        _logger = logger;
        _trace = trace;
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
        if (_gate.IsPaused)
            return;

        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasTelefonchy || !config.TelefonchySyncEnabled)
            return;

        _calls.BindTenant(tenantId);

        var from = initial || config.TelefonchyLastSyncUtc is null
            ? DateTime.UtcNow.AddDays(-2)
            : config.TelefonchyLastSyncUtc.Value.AddHours(-1);

        var maxPages = initial ? 3 : 1;
        var page = 1;
        var imported = 0;
        while (page <= maxPages)
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
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task DrainAllTenantsAsync()
    {
        if (_gate.IsPaused)
            return;

        var tenants = await _db.Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial)
            .Select(t => t.Id)
            .ToListAsync();

        foreach (var tenantId in tenants)
        {
            try
            {
                await DrainOneRecordingAsync(tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telefonchy recording drain failed for tenant {TenantId}.", tenantId);
            }
        }
    }

    [DisableConcurrentExecution(180)]
    [AutomaticRetry(Attempts = 0)]
    public async Task DrainOneRecordingAsync(int tenantId)
    {
        if (_gate.IsPaused)
            return;

        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasTelefonchy || !config.TelefonchyRecordingEnabled)
            return;

        _calls.BindTenant(tenantId);
        var record = await PickPendingRecordingAsync(tenantId);
        if (record is null)
            return;

        var data = DynamicRecordService.ParseData(record);
        await _trace.WriteAsync(tenantId, new TelefonchyTraceEntry
        {
            Kind = "job",
            Action = "drain-merged",
            Cuid = data.GetValueOrDefault("cuid"),
            CallId = data.GetValueOrDefault("callId"),
            CrmRecordId = record.Id,
            Summary = $"درین صف ضبط — رکورد #{record.Id}"
        });

        var state = await _calls.EnsureRecordingAsync(
            record.ModuleId, record, lookupMissingIds: false, fallbackFileId: true);
        if (state != TelefonchyCallService.CallRecordingState.Ready)
            await _calls.MarkRecordingAttemptAsync(record.ModuleId, record);
        _logger.LogInformation(
            "Telefonchy drain {State} tenant {TenantId} record {RecordId}.",
            state, tenantId, record.Id);

        if (!_gate.IsPaused && await PickPendingRecordingAsync(tenantId) is not null)
        {
            BackgroundJob.Schedule<TelefonchyCallSyncJob>(
                j => j.DrainOneRecordingAsync(tenantId),
                TimeSpan.FromSeconds(3));
        }
    }

    private async Task<DynamicRecord?> PickPendingRecordingAsync(int tenantId)
    {
        _calls.BindTenant(tenantId);
        var module = await _db.Modules.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Name == "calls" && m.IsActive);
        if (module is null)
            return null;

        var since = DateTime.UtcNow.AddHours(-36);
        var records = await _db.Records.AsNoTracking()
            .Where(r => r.ModuleId == module.Id && r.CreatedAtUtc >= since)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();

        foreach (var record in records)
        {
            var data = DynamicRecordService.ParseData(record);
            if (_calls.HasStoredRecording(record, data)
                || TelefonchyCallService.HasRecording(data)
                || !TelefonchyCallService.IsRecordingPending(data))
                continue;
            if (string.IsNullOrWhiteSpace(data.GetValueOrDefault("cuid")))
                continue;
            var recStatus = data.GetValueOrDefault("recordingStatus");
            if (recStatus is "unavailable" or "failed" or "expired" or "missing")
                continue;
            if (DateTime.TryParse(data.GetValueOrDefault("recordingLastFetchUtc"), out var last)
                && DateTime.UtcNow - last.ToUniversalTime() < TimeSpan.FromSeconds(20))
                continue;
            return record;
        }

        return null;
    }

    /// <summary>جاب‌های قدیمی صف‌شده؛ دیگر API نمی‌زنند تا توکن محدود نشود.</summary>
    [AutomaticRetry(Attempts = 0)]
    public Task FetchMergedRecordingAsync(int tenantId, string? cuid, string? recordId, int attempt) =>
        Task.CompletedTask;

    [Obsolete]
    [AutomaticRetry(Attempts = 0)]
    public Task RefreshRecordingAsync(int tenantId, string? cuid, string? callId, string? phone) =>
        Task.CompletedTask;

    [Obsolete]
    [AutomaticRetry(Attempts = 0)]
    public Task RefreshRecordingAsync(int tenantId, string? cuid, string? callId, string? phone, int attempt) =>
        Task.CompletedTask;
}
