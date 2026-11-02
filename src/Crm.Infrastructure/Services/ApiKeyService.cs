using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Services;

/// <summary>ساخت، هش و بررسی scope کلیدهای API.</summary>
public class ApiKeyService
{
    public const string KindSecret = "secret";
    public const string KindPublic = "public";

    private readonly CrmDbContext _db;

    public ApiKeyService(CrmDbContext db)
    {
        _db = db;
    }

    public static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string NewRawKey(string kind)
    {
        var prefix = string.Equals(kind, KindPublic, StringComparison.OrdinalIgnoreCase) ? "crm_pk_" : "crm_sk_";
        return prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    }

    public async Task<ApiKey?> FindByRawAsync(string raw, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var hash = Hash(raw.Trim());
        return await _db.ApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive && !k.IsDeleted, cancellationToken);
    }

    public void Touch(ApiKey key)
    {
        var now = DateTime.UtcNow;
        if (key.LastUsedAtUtc is not null && now - key.LastUsedAtUtc.Value < TimeSpan.FromSeconds(30))
            return;

        key.LastUsedAtUtc = now;
        key.RequestCount++;
    }

    /// <summary>
    /// scope خالی برای کلید محرمانه یعنی همه ماژول‌ها.
    /// برای کلید عمومی یعنی فقط ساختن سرنخ.
    /// </summary>
    public static string BuildScopes(string kind, IEnumerable<string>? moduleAccess)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (moduleAccess is not null)
        {
            foreach (var item in moduleAccess)
            {
                if (string.IsNullOrWhiteSpace(item))
                    continue;
                var parts = item.Split(':', 2, StringSplitOptions.TrimEntries);
                var module = parts[0];
                if (module is "*" or "")
                {
                    return "*";
                }

                var access = parts.Length == 2 ? parts[1].ToLowerInvariant() : "rw";
                if (access is not ("r" or "w" or "rw"))
                    access = "rw";
                map[module] = access;
            }
        }

        if (map.Count == 0)
            return string.Equals(kind, KindPublic, StringComparison.OrdinalIgnoreCase) ? """{"leads":"rw"}""" : "*";

        return JsonSerializer.Serialize(map);
    }

    public static bool Allows(ApiKey key, string module, bool write)
    {
        if (!key.IsActive)
            return false;
        if (key.ExpiresAtUtc is DateTime exp && exp < DateTime.UtcNow)
            return false;

        var scope = (key.ScopesJson ?? "*").Trim();
        if (scope is "" or "*")
            return write ? key.CanWrite : key.CanRead || key.CanWrite;

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(scope);
            if (map is null)
                return false;
            if (!map.TryGetValue(module, out var access) && !map.TryGetValue("*", out access))
                return false;
            access ??= "";
            if (write)
                return access.Contains('w', StringComparison.OrdinalIgnoreCase);
            return access.Contains('r', StringComparison.OrdinalIgnoreCase) || access.Contains('w', StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool CanWriteSomething(ApiKey key)
    {
        var scope = (key.ScopesJson ?? "*").Trim();
        if (scope is "" or "*")
            return key.CanWrite;
        return scope.Contains("\"w\"", StringComparison.OrdinalIgnoreCase)
               || scope.Contains("\"rw\"", StringComparison.OrdinalIgnoreCase);
    }

    public static bool OriginAllowed(ApiKey key, string? origin)
    {
        if (!string.Equals(key.Kind, KindPublic, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(key.AllowedOrigins))
            return false;

        var wanted = origin.Trim().TrimEnd('/');
        foreach (var line in key.AllowedOrigins.Split(['\r', '\n', ',', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(line.Trim().TrimEnd('/'), wanted, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
