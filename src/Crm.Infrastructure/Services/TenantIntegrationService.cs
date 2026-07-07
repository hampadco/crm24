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

    // VoIP سانترال
    public bool VoipEnabled { get; set; }

    public bool HasSmtp => !string.IsNullOrWhiteSpace(SmtpHost);
    public bool HasSms => !string.IsNullOrWhiteSpace(SmsApiUrl);
    public bool HasBale => !string.IsNullOrWhiteSpace(BaleBotToken);
    public bool HasAccounting => !string.IsNullOrWhiteSpace(AccountingWebhookUrl);
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

    public async Task SaveAsync(IntegrationConfig config)
    {
        if (_tenant.TenantId is not int tenantId)
            throw new InvalidOperationException("Tenant context missing.");

        var tenant = await _db.Tenants.FirstAsync(t => t.Id == tenantId);
        var root = JsonNode.Parse(tenant.Settings) as JsonObject ?? new JsonObject();
        root[SectionKey] = JsonSerializer.SerializeToNode(config);
        tenant.Settings = root.ToJsonString();
        await _db.SaveChangesAsync();
    }
}
