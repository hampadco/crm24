using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Crm.Infrastructure.Services;

public sealed class IpPanelSendResult
{
    public bool Ok { get; init; }
    public bool Partial { get; init; }
    public string? Error { get; init; }
    public List<long> OutboxIds { get; init; } = [];
}

public sealed class IpPanelBulkReport
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public int? StateId { get; init; }
    public string? StatusText { get; init; }
    public decimal? Cost { get; init; }
}

public sealed class IpPanelRecipientRow
{
    public string Phone { get; init; } = "";
    public string? MessageStatus { get; init; }
}

public sealed class IpPanelRecipientPage
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public int LastPage { get; init; } = 1;
    public List<IpPanelRecipientRow> Rows { get; init; } = [];
}

/// <summary>کلاینت Edge API آی‌پی‌پنل. کاوه‌نگار اینجا نیست.</summary>
public class IpPanelSmsClient
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<IpPanelSmsClient> _logger;

    public IpPanelSmsClient(IHttpClientFactory http, ILogger<IpPanelSmsClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public static string NormalizeSender(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return "";
        if (digits.StartsWith("0098", StringComparison.Ordinal))
            digits = digits[4..];
        else if (digits.StartsWith("98", StringComparison.Ordinal) && digits.Length > 8)
            digits = digits[2..];
        else if (digits.StartsWith('0'))
            digits = digits[1..];
        return "+98" + digits;
    }

    public static string? ToE164(string? raw)
    {
        var canon = PhoneIdentity.Canonical(raw);
        return canon is null ? null : "+98" + canon;
    }

    public static string Display(string? e164)
    {
        var canon = PhoneIdentity.Canonical(e164);
        return canon is null ? (e164 ?? "") : "0" + canon;
    }

    public Task<IpPanelSendResult> SendSameTextAsync(
        IntegrationConfig config, string message, IReadOnlyList<string> recipients, DateTime? sendUtc, CancellationToken ct) =>
        PostSendAsync(config, new
        {
            sending_type = "webservice",
            from_number = NormalizeSender(config.SmsFrom),
            message,
            @params = new { recipients },
            send_time = FormatUtc(sendUtc)
        }, ct);

    public Task<IpPanelSendResult> SendPeerAsync(
        IntegrationConfig config,
        IReadOnlyList<(string Phone, string Message)> items,
        DateTime? sendUtc,
        CancellationToken ct) =>
        PostSendAsync(config, new
        {
            sending_type = "peer_to_peer",
            from_number = NormalizeSender(config.SmsFrom),
            @params = items.Select(i => new { recipients = new[] { i.Phone }, message = i.Message }).ToArray(),
            send_time = FormatUtc(sendUtc)
        }, ct);

    public async Task<(bool Ok, string? Error)> CancelAsync(IntegrationConfig config, long outboxId, CancellationToken ct)
    {
        var doc = await SendAsync(config, HttpMethod.Post, "/api/send/cancel", new { message_outbox_id = outboxId }, ct);
        return doc is null
            ? (false, "پاسخ آی‌پی‌پنل خوانده نشد.")
            : (doc.Value.ok, doc.Value.error);
    }

    public async Task<(bool Ok, decimal? Credit, string? Error)> GetCreditAsync(IntegrationConfig config, CancellationToken ct)
    {
        var doc = await SendAsync(config, HttpMethod.Get, "/api/payment/credit/mine", null, ct);
        if (doc is null)
            return (false, null, "پاسخ آی‌پی‌پنل خوانده نشد.");
        if (!doc.Value.ok)
            return (false, null, doc.Value.error);
        decimal? credit = null;
        if (doc.Value.data.ValueKind == JsonValueKind.Object
            && doc.Value.data.TryGetProperty("credit", out var creditEl)
            && creditEl.TryGetDecimal(out var value))
            credit = value;
        return (true, credit, null);
    }

    public async Task<IpPanelBulkReport> GetBulkAsync(IntegrationConfig config, long outboxId, CancellationToken ct)
    {
        var doc = await SendAsync(config, HttpMethod.Get, $"/api/report/by_bulk?messages_outbox_id={outboxId}", null, ct);
        if (doc is null)
            return new IpPanelBulkReport { Error = "پاسخ گزارش خوانده نشد." };
        if (!doc.Value.ok)
            return new IpPanelBulkReport { Error = doc.Value.error };
        var data = doc.Value.data;
        int? state = null;
        decimal? cost = null;
        string? status = null;
        if (data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("state_id", out var stateEl) && stateEl.TryGetInt32(out var s))
                state = s;
            if (data.TryGetProperty("cost", out var costEl) && costEl.TryGetDecimal(out var c))
                cost = c;
            if (data.TryGetProperty("status", out var statusEl))
                status = statusEl.GetString();
        }
        return new IpPanelBulkReport { Ok = true, StateId = state, Cost = cost, StatusText = status };
    }

    public async Task<IpPanelRecipientPage> GetRecipientsAsync(
        IntegrationConfig config, long outboxId, int page, int perPage, CancellationToken ct)
    {
        var path = $"/api/report/recipients?page={page}&per_page={perPage}&bulk_id={outboxId}";
        var doc = await SendAsync(config, HttpMethod.Get, path, null, ct);
        if (doc is null)
            return new IpPanelRecipientPage { Error = "پاسخ گیرندگان خوانده نشد." };
        if (!doc.Value.ok)
            return new IpPanelRecipientPage { Error = doc.Value.error };

        var rows = new List<IpPanelRecipientRow>();
        if (doc.Value.data.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in doc.Value.data.EnumerateArray())
            {
                var phone = row.TryGetProperty("recipient", out var p) ? p.GetString() : null;
                var status = row.TryGetProperty("message_status", out var s) ? s.ToString().Trim('"') : null;
                if (!string.IsNullOrWhiteSpace(phone))
                    rows.Add(new IpPanelRecipientRow { Phone = phone!, MessageStatus = status });
            }
        }

        var last = 1;
        if (doc.Value.meta.ValueKind == JsonValueKind.Object
            && doc.Value.meta.TryGetProperty("last_page", out var lastEl)
            && lastEl.TryGetInt32(out var lastPage))
            last = Math.Max(1, lastPage);

        return new IpPanelRecipientPage { Ok = true, LastPage = last, Rows = rows };
    }

    private async Task<IpPanelSendResult> PostSendAsync(IntegrationConfig config, object body, CancellationToken ct)
    {
        var doc = await SendAsync(config, HttpMethod.Post, "/api/send", body, ct);
        if (doc is null)
            return new IpPanelSendResult { Error = "پاسخ آی‌پی‌پنل خوانده نشد." };
        if (!doc.Value.ok)
            return new IpPanelSendResult { Error = doc.Value.error ?? "ارسال پذیرفته نشد." };
        return new IpPanelSendResult { Ok = true, OutboxIds = ReadOutboxIds(doc.Value.data) };
    }

    private async Task<(bool ok, string? error, JsonElement data, JsonElement meta)?> SendAsync(
        IntegrationConfig config, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!config.HasIpPanel)
            return (false, "آی‌پی‌پنل فعال نیست.", default, default);

        var client = _http.CreateClient("ippanel");
        var url = config.ResolvedIpPanelBaseUrl + path;
        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", config.SmsApiKey!.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await client.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!TryParseEnvelope(text, out var data, out var meta, out var ok, out var error))
            {
                _logger.LogWarning("IPPanel unreadable response {Status} {Path}", (int)response.StatusCode, path);
                return (false, "پاسخ آی‌پی‌پنل نامعتبر بود.", default, default);
            }

            if (!response.IsSuccessStatusCode || !ok)
            {
                _logger.LogWarning("IPPanel {Path} failed: {Error}", path, error);
                return (false, string.IsNullOrWhiteSpace(error) ? "درخواست آی‌پی‌پنل رد شد." : error, data, meta);
            }

            return (true, null, data, meta);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "IPPanel request failed {Path}", path);
            return (false, "ارتباط با آی‌پی‌پنل برقرار نشد.", default, default);
        }
    }

    private static bool TryParseEnvelope(
        string text, out JsonElement data, out JsonElement meta, out bool ok, out string? error)
    {
        data = default;
        meta = default;
        ok = false;
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            var root = doc.RootElement.Clone();
            if (root.TryGetProperty("data", out var dataEl))
                data = dataEl.Clone();
            if (root.TryGetProperty("meta", out var metaEl))
            {
                meta = metaEl.Clone();
                if (metaEl.TryGetProperty("status", out var statusEl))
                {
                    ok = statusEl.ValueKind == JsonValueKind.True
                         || (statusEl.ValueKind == JsonValueKind.String
                             && bool.TryParse(statusEl.GetString(), out var b) && b);
                }
                if (metaEl.TryGetProperty("message", out var messageEl))
                    error = messageEl.GetString();
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<long> ReadOutboxIds(JsonElement data)
    {
        var list = new List<long>();
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("message_outbox_ids", out var arr))
            return list;
        if (arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var n))
                list.Add(n);
            else if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), out var s))
                list.Add(s);
        }
        return list;
    }

    private static string? FormatUtc(DateTime? utc) =>
        utc is null ? null : utc.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
