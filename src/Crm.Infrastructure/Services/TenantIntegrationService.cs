using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Infrastructure.Data;

namespace Crm.Infrastructure.Services;

/// <summary>پیکربندی یکپارچگی‌های هر Tenant — داخل ستون jsonb «Settings» جدول Tenants.</summary>
public class IntegrationConfig
{
    // ایمیل (SMTP per-tenant)
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }
    public string? SmtpFrom { get; set; }

    // پنل پیامک ایرانی (الگوی Kavenegar-مانند)
    public string? SmsApiUrl { get; set; }
    public string? SmsApiKey { get; set; }
    public string? SmsFrom { get; set; }

    // پیام‌رسان بله
    public string? BaleBotToken { get; set; }

    // حسابداری (وب‌سرویس مقصد)
    public string? AccountingWebhookUrl { get; set; }

    // VoIP سانترال (عمومی، غیر تلفنچی)
    public bool VoipEnabled { get; set; }

    // تلفنچی
    public bool TelefonchyEnabled { get; set; }
    public string? TelefonchyBaseUrl { get; set; }
    public string? TelefonchyToken { get; set; }
    public string? TelefonchyServiceId { get; set; }
    public string? TelefonchyHookSecret { get; set; }
    public bool TelefonchySyncEnabled { get; set; } = true;
    public bool TelefonchyAutoLogCalls { get; set; } = true;
    public bool TelefonchyRecordingEnabled { get; set; } = true;
    public string? TelefonchyDefaultExten { get; set; }
    public DateTime? TelefonchyLastSyncUtc { get; set; }

    public bool HasSmtp => !string.IsNullOrWhiteSpace(SmtpHost);
    public bool HasSms => !string.IsNullOrWhiteSpace(SmsApiUrl);
    public bool HasBale => !string.IsNullOrWhiteSpace(BaleBotToken);
    public bool HasAccounting => !string.IsNullOrWhiteSpace(AccountingWebhookUrl);
    public bool HasTelefonchy => TelefonchyEnabled
        && !string.IsNullOrWhiteSpace(TelefonchyToken)
        && !string.IsNullOrWhiteSpace(TelefonchyServiceId);

    public string ResolvedTelefonchyBaseUrl =>
        string.IsNullOrWhiteSpace(TelefonchyBaseUrl)
            ? "https://panel.telefonchy.com/webservice/v1"
            : TelefonchyBaseUrl.Trim().TrimEnd('/');
}

public class TenantIntegrationService
{
    private const string SectionKey = "integrations";

    private readonly CrmDbContext _db;
    private readonly ITenantContext _tenant;

    public TenantIntegrationService(CrmDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IntegrationConfig> GetAsync(int? tenantId = null)
    {
        var id = tenantId ?? _tenant.TenantId;
        if (id is null)
            return new IntegrationConfig();

        var settingsJson = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => t.Settings)
            .FirstOrDefaultAsync() ?? "{}";

        var root = JsonNode.Parse(settingsJson) as JsonObject;
        var section = root?[SectionKey];
        return section is null
            ? new IntegrationConfig()
            : section.Deserialize<IntegrationConfig>() ?? new IntegrationConfig();
    }

    public async Task SaveAsync(IntegrationConfig config, int? tenantId = null)
    {
        var id = tenantId ?? _tenant.TenantId
            ?? throw new InvalidOperationException("Tenant context missing.");

        var tenant = await _db.Tenants.FirstAsync(t => t.Id == id);
        var root = JsonNode.Parse(tenant.Settings) as JsonObject ?? new JsonObject();
        root[SectionKey] = JsonSerializer.SerializeToNode(config);
        tenant.Settings = root.ToJsonString();
        await _db.SaveChangesAsync();
    }

    public async Task<int?> FindTenantIdByHookSecretAsync(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return null;

        var tenantId = await _db.Tenants
            .FromSqlInterpolated($"""
                SELECT * FROM "Tenants"
                WHERE "Settings"::jsonb -> 'integrations' ->> 'TelefonchyHookSecret' = {secret.Trim()}
                LIMIT 1
                """)
            .AsNoTracking()
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync();
        return tenantId;
    }

    public static string NewHookSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
