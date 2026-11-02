using System.Collections.Concurrent;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Services;

namespace Crm.Web.Controllers.Api;

/// <summary>وب‌هوک تلفنچی و تماس ورودی سانترال. رکوردها در ApiRecordsController هستند.</summary>
[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/v1")]
public class ApiV1Controller : ControllerBase
{
    private const int RateLimitPerMinute = 120;
    private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> HookRateLimits = new();

    private readonly ITenantContext _tenant;

    public ApiV1Controller(ITenantContext tenant)
    {
        _tenant = tenant;
    }

    public record VoipIncomingModel(string Caller, string? Called);

    /// <summary>
    /// وب‌سرویس سانترال: تماس ورودی → ثبت خودکار تماس + پاپ‌آپ SignalR
    /// و لینک به مخاطبِ هم‌شماره.
    /// </summary>
    [ServiceFilter(typeof(Crm.Web.Security.ApiScopeFilter))]
    [EnableRateLimiting("api")]
    [HttpPost("voip/incoming")]
    public async Task<IActionResult> VoipIncoming([FromBody] VoipIncomingModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Caller))
            return BadRequest(new { error = "caller is required." });

        var calls = HttpContext.RequestServices.GetRequiredService<TelefonchyCallService>();
        var dto = new TelefonchyCallDto
        {
            Type = "incoming",
            Event = "ringing",
            Src = model.Caller.Trim(),
            Dest = model.Called,
            StartedAt = DateTime.Now
        };
        await calls.HandleRingingAsync(dto);
        return Ok(new { ok = true });
    }

    /// <summary>وب‌هوک ایونت تماس تلفنچی (ringing / hangup).</summary>
    [AllowAnonymous]
    [HttpPost("telefonchy/events/{secret}")]
    [RequestSizeLimit(256_000)]
    public Task<IActionResult> TelefonchyEvent(string secret) =>
        AcceptTelefonchyHookAsync(secret, isCdr: false);

    /// <summary>وب‌هوک CDR تلفنچی — اطلاعات کامل تماس پس از اتمام.</summary>
    [AllowAnonymous]
    [HttpPost("telefonchy/cdr/{secret}")]
    [RequestSizeLimit(256_000)]
    public Task<IActionResult> TelefonchyCdr(string secret) =>
        AcceptTelefonchyHookAsync(secret, isCdr: true);

    private async Task<IActionResult> AcceptTelefonchyHookAsync(string secret, bool isCdr)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16)
            return NotFound();

        if (!TryHookRateLimit(secret))
            return StatusCode(429, new { error = "Rate limit exceeded." });

        var integrations = HttpContext.RequestServices.GetRequiredService<TenantIntegrationService>();
        var tenantId = await integrations.FindTenantIdByHookSecretAsync(secret);
        if (tenantId is null)
            return NotFound();

        Request.EnableBuffering();
        string json;
        if (Request.HasFormContentType)
        {
            json = JsonSerializer.Serialize(Request.Form.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToString()));
        }
        else
        {
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            json = await reader.ReadToEndAsync();
            Request.Body.Position = 0;
        }

        if (string.IsNullOrWhiteSpace(json))
            json = "{}";

        var call = TelefonchyClient.ParseWebhookPayload(json);
        var config = await integrations.GetAsync(tenantId);
        var trace = HttpContext.RequestServices.GetRequiredService<TelefonchyTraceService>();
        if (!string.IsNullOrWhiteSpace(call?.WebserviceToken)
            && !string.Equals(call.WebserviceToken, config.TelefonchyToken, StringComparison.Ordinal))
        {
            await trace.WriteAsync(tenantId, new TelefonchyTraceEntry
            {
                Kind = "webhook",
                Action = isCdr ? "cdr-hook" : "events-hook",
                Status = "403",
                Summary = "وب‌هوک رد شد: توکن نامعتبر",
                Payload = json
            });
            return StatusCode(403, new { error = "Invalid token." });
        }

        if (_tenant is TenantContext mutable)
        {
            mutable.TenantId = tenantId;
            mutable.IsTenantAdmin = true;
        }

        if (isCdr)
            BackgroundJob.Enqueue<TelefonchyWebhookJob>(j => j.ProcessCdrAsync(tenantId.Value, json));
        else
            BackgroundJob.Enqueue<TelefonchyWebhookJob>(j => j.ProcessEventAsync(tenantId.Value, json));

        await trace.WriteAsync(tenantId, new TelefonchyTraceEntry
        {
            Kind = "webhook",
            Action = isCdr ? "cdr-hook" : "events-hook",
            Direction = call?.Direction,
            Status = call?.Event ?? call?.Status,
            Cuid = call?.Cuid,
            CallId = call?.CallId ?? call?.RecordId,
            Phone = call?.ExternalNumber,
            Summary = isCdr
                ? $"وب‌هوک CDR رسید — event={call?.Event} status={call?.Status} talk={call?.TalkSeconds}"
                : $"وب‌هوک رویداد رسید — event={call?.Event} {call?.Src} → {call?.Dest}",
            Payload = json
        });

        return Ok(new { status = "ok" });
    }

    private static bool TryHookRateLimit(string secret)
    {
        var now = DateTime.UtcNow;
        var window = HookRateLimits.AddOrUpdate(secret,
            _ => (1, now),
            (_, current) => now - current.WindowStart > TimeSpan.FromMinutes(1)
                ? (1, now)
                : (current.Count + 1, current.WindowStart));
        return window.Count <= RateLimitPerMinute;
    }
}
