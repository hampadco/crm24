using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

public sealed class TelefonchyTraceEntry
{
    public string Kind { get; init; } = "event";
    public string Action { get; init; } = "";
    public string? Direction { get; init; }
    public string? Status { get; init; }
    public string? Cuid { get; init; }
    public string? CallId { get; init; }
    public string? RequestId { get; init; }
    public string? Phone { get; init; }
    public string? Summary { get; init; }
    public string? Path { get; init; }
    public int? HttpStatus { get; init; }
    public int? CrmRecordId { get; init; }
    public string? Payload { get; init; }
}

/// <summary>ثبت لاگ تلفنچی در دیتابیس جدا از تراکنش اصلی، تا عیب‌یابی سناریوها ممکن باشد.</summary>
public class TelefonchyTraceService
{
    private const int MaxPayloadChars = 80_000;
    private readonly IServiceScopeFactory _scopes;
    private readonly ITenantContext _tenant;
    private readonly ILogger<TelefonchyTraceService> _logger;

    public TelefonchyTraceService(
        IServiceScopeFactory scopes,
        ITenantContext tenant,
        ILogger<TelefonchyTraceService> logger)
    {
        _scopes = scopes;
        _tenant = tenant;
        _logger = logger;
    }

    public Task WriteAsync(TelefonchyTraceEntry entry, CancellationToken ct = default) =>
        WriteAsync(_tenant.TenantId, entry, ct);

    public async Task WriteAsync(int? tenantId, TelefonchyTraceEntry entry, CancellationToken ct = default)
    {
        var tid = tenantId ?? _tenant.TenantId;
        if (tid is not int id || id <= 0)
            return;

        try
        {
            using var scope = _scopes.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<TenantContext>();
            ctx.TenantId = id;
            ctx.IsTenantAdmin = true;
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            db.TelefonchyTraces.Add(new TelefonchyTrace
            {
                TenantId = id,
                AtUtc = DateTime.UtcNow,
                Kind = Truncate(entry.Kind, 32) ?? "event",
                Action = Truncate(entry.Action, 64) ?? "",
                Direction = Truncate(entry.Direction, 16),
                Status = Truncate(entry.Status, 64),
                Cuid = Truncate(entry.Cuid, 80),
                CallId = Truncate(entry.CallId, 80),
                RequestId = Truncate(entry.RequestId, 80),
                Phone = Truncate(entry.Phone, 32),
                Summary = Truncate(entry.Summary, 500),
                Path = Truncate(entry.Path, 200),
                HttpStatus = entry.HttpStatus,
                CrmRecordId = entry.CrmRecordId,
                Payload = Redact(entry.Payload)
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Telefonchy trace write failed.");
        }
    }

    public static TelefonchyTraceEntry FromCall(
        string kind, string action, TelefonchyCallDto call, string? summary = null, string? payload = null, int? crmId = null) =>
        new()
        {
            Kind = kind,
            Action = action,
            Direction = call.Direction,
            Status = call.Status ?? call.Event,
            Cuid = call.Cuid,
            CallId = call.CallId ?? call.RecordId,
            RequestId = call.RequestId,
            Phone = call.ExternalNumber,
            Summary = summary,
            CrmRecordId = crmId,
            Payload = payload
        };

    public static string? ToJson(object? value)
    {
        if (value is null)
            return null;
        if (value is string s)
            return s;
        try
        {
            return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = false });
        }
        catch
        {
            return value.ToString();
        }
    }

    private static string? Redact(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var text = raw.Length > MaxPayloadChars ? raw[..MaxPayloadChars] + "…[truncated]" : raw;
        text = Regex.Replace(text, "(webservice[_-]?token\"?\\s*[:=]\\s*\"?)[^\"&,\\s}]+", "$1***", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "(w3rv-[a-f0-9-]+)", "***", RegexOptions.IgnoreCase);
        return text;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
