using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Services;

/// <summary>ارسال وب‌هوک خروجی با امضای HMAC. هنگفایر در خطا دوباره تلاش می‌کند.</summary>
public class ApiWebhookJob
{
    public const int DisableAfterFailures = 8;

    private readonly CrmDbContext _db;
    private readonly TenantContext _tenant;
    private readonly IHttpClientFactory _http;

    public ApiWebhookJob(CrmDbContext db, TenantContext tenant, IHttpClientFactory http)
    {
        _db = db;
        _tenant = tenant;
        _http = http;
    }

    public async Task DeliverAsync(int tenantId, int moduleId, int recordId, string eventName)
    {
        _tenant.TenantId = tenantId;
        _tenant.IsTenantAdmin = true;

        var moduleName = await _db.Modules.AsNoTracking()
            .Where(m => m.Id == moduleId)
            .Select(m => m.Name)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(moduleName))
            return;

        var record = await _db.Records.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recordId && r.TenantId == tenantId && r.ModuleId == moduleId);

        var payload = JsonSerializer.Serialize(new
        {
            @event = eventName,
            module = moduleName,
            id = recordId,
            title = record?.Title,
            deleted = record?.IsDeleted ?? true,
            data = ParseObject(record?.CustomData)
        });

        var hooks = await _db.ApiWebhooks.Where(h => h.IsActive).ToListAsync();
        var failed = false;
        foreach (var hook in hooks)
        {
            if (!Matches(hook, eventName, moduleName))
                continue;
            try
            {
                await PostAsync(hook, eventName, payload, countFailure: true);
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }
        }

        if (failed)
            throw new InvalidOperationException("One or more webhooks failed.");
    }

    public Task<string> SendTestAsync(int webhookId)
    {
        return SendTestCoreAsync(webhookId);
    }

    private async Task<string> SendTestCoreAsync(int webhookId)
    {
        var hook = await _db.ApiWebhooks.FirstOrDefaultAsync(h => h.Id == webhookId);
        if (hook is null)
            return "وب‌هوک پیدا نشد.";

        var payload = JsonSerializer.Serialize(new
        {
            @event = "webhook.test",
            module = "leads",
            id = 0,
            title = "تست",
            data = new { ok = true }
        });
        var status = await PostAsync(hook, "webhook.test", payload, countFailure: false);
        return status;
    }

    private async Task<string> PostAsync(ApiWebhook hook, string eventName, string payload, bool countFailure)
    {
        hook.LastAttemptUtc = DateTime.UtcNow;
        try
        {
            var client = _http.CreateClient("api-webhook");
            using var request = new HttpRequestMessage(HttpMethod.Post, hook.Url);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("X-Crm-Event", eventName);
            request.Headers.TryAddWithoutValidation("X-Crm-Signature", Sign(hook.Secret, payload));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Crm24-Webhook", "1"));

            using var response = await client.SendAsync(request);
            hook.LastStatus = ((int)response.StatusCode).ToString();
            if (response.IsSuccessStatusCode)
            {
                hook.FailCount = 0;
                await _db.SaveChangesAsync();
                return hook.LastStatus;
            }

            await RegisterFailureAsync(hook, countFailure, hook.LastStatus);
            if (countFailure && hook.IsActive)
                throw new InvalidOperationException($"Webhook {hook.Id} returned {hook.LastStatus}.");
            return hook.LastStatus;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RegisterFailureAsync(hook, countFailure, "error");
            if (countFailure && hook.IsActive)
                throw;
            return "error";
        }
    }

    private async Task RegisterFailureAsync(ApiWebhook hook, bool countFailure, string status)
    {
        hook.LastStatus = status;
        if (countFailure)
        {
            hook.FailCount++;
            if (hook.FailCount >= DisableAfterFailures)
                hook.IsActive = false;
        }
        await _db.SaveChangesAsync();
    }

    public static string Sign(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? ""));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return "sha256=" + hash;
    }

    private static bool Matches(ApiWebhook hook, string eventName, string moduleName)
    {
        var events = (hook.Events ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (events.Length > 0 && !events.Contains(eventName, StringComparer.OrdinalIgnoreCase) && !events.Contains("*"))
            return false;

        var modules = (hook.ModuleNames ?? "*").Trim();
        if (modules is "*" or "")
            return true;
        var names = modules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return names.Contains(moduleName, StringComparer.OrdinalIgnoreCase);
    }

    private static object ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new { };
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return new { };
        }
    }
}
