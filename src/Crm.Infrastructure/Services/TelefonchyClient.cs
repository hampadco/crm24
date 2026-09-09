using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Crm.Infrastructure.Services;

public sealed class TelefonchyCallDto
{
    public string? Cuid { get; set; }
    public string? CallId { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public string? Event { get; set; }
    public string? Src { get; set; }
    public string? Dest { get; set; }
    public string? Extension { get; set; }
    /// <summary>شماره خط سرویس؛ طرف بیرونی تماس محسوب نمی‌شود.</summary>
    public string? Trunk { get; set; }
    public string? FileId { get; set; }
    public string? FileRecord { get; set; }
    public string? FileStatus { get; set; }
    public bool HasAudio { get; set; }
    public int? WaitSeconds { get; set; }
    public int? TalkSeconds { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? ServiceId { get; set; }
    public string? WebserviceToken { get; set; }
    public string? RequestId { get; set; }
    /// <summary>شناسه MongoDB تماس در تلفنچی؛ برای دانلود ضبط merged لازم است.</summary>
    public string? RecordId { get; set; }
    public string? DownloadUrl { get; set; }

    public string Direction =>
        string.Equals(Type, "incoming", StringComparison.OrdinalIgnoreCase) ? "incoming" : "outgoing";

    /// <summary>
    /// شماره طرف بیرونی تماس. تکیه بر جهت تماس شکننده است، چون تلفنچی در پاهای
    /// مختلف یک تماس سریع جای مبدأ و مقصد را عوض می‌کند. پس هر طرفی که داخلی و
    /// شماره خط سرویس نباشد و شکل یک شماره واقعی داشته باشد انتخاب می‌شود.
    /// </summary>
    public string? ExternalNumber
    {
        get
        {
            var preferred = Direction == "incoming"
                ? new[] { Src, Dest }
                : new[] { Dest, Src };

            var external = preferred.FirstOrDefault(v => IsExternalParty(v));
            if (external is not null)
                return external;

            return preferred.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }
    }

    /// <summary>آیا این طرف تماس یک شماره بیرونی واقعی است (نه داخلی و نه خط سرویس).</summary>
    public bool IsExternalParty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (!string.IsNullOrWhiteSpace(Extension) && trimmed == Extension.Trim())
            return false;
        if (!string.IsNullOrWhiteSpace(Trunk) && trimmed == Trunk.Trim())
            return false;

        // شماره ماسک‌شده مثل 991****706 هم طرف بیرونی است.
        if (trimmed.Contains('*'))
            return true;

        var digits = trimmed.Count(char.IsDigit);
        return digits >= 9;
    }

    /// <summary>هیچ طرفی از این تماس بیرونی نیست، یعنی پای داخلی تماس است.</summary>
    public bool HasNoExternalParty => !IsExternalParty(Src) && !IsExternalParty(Dest);
}

public sealed class TelefonchyCallPage
{
    public IReadOnlyList<TelefonchyCallDto> Calls { get; init; } = [];
    public int CurrentPage { get; init; } = 1;
    public int LastPage { get; init; } = 1;
    public int TotalItems { get; init; }
}

public sealed class TelefonchyRecordingResult
{
    public byte[]? Content { get; init; }
    public string ContentType { get; init; } = "audio/mpeg";
    public string FileName { get; init; } = "";
    public string? Error { get; init; }

    public bool Ok => Error is null && Content is { Length: > 0 };
}

public enum TelefonchyMergedRecordingState
{
    Ready,
    Processing,
    Failed
}

public sealed class TelefonchyMergedRecordingResult
{
    public TelefonchyMergedRecordingState State { get; init; } = TelefonchyMergedRecordingState.Failed;
    public byte[]? Content { get; init; }
    public string ContentType { get; init; } = "audio/wav";
    public string FileName { get; init; } = "";
    public string? Message { get; init; }

    public bool IsReady => State == TelefonchyMergedRecordingState.Ready && Content is { Length: > 0 };
}

public sealed class TelefonchyExtenDto
{
    public string? Number { get; set; }
    public string? Name { get; set; }
}

public sealed class TelefonchyServiceDto
{
    public string? ServiceId { get; set; }
    public List<string> Trunks { get; } = [];
    public List<TelefonchyExtenDto> Extens { get; } = [];
}

public sealed class TelefonchySmartCallResult
{
    public bool Ok { get; init; }
    public string? Message { get; init; }
    public string? RequestId { get; init; }
    public string? Cuid { get; init; }
    public string? CallStatus { get; init; }
    public string? FailureReason { get; init; }
}

/// <summary>کلاینت HTTP وب‌سرویس تلفنچی. توکن هرگز لاگ نمی‌شود.</summary>
public class TelefonchyClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TenantIntegrationService _integrations;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TelefonchyClient> _logger;

    public TelefonchyClient(
        TenantIntegrationService integrations,
        IHttpClientFactory httpFactory,
        ILogger<TelefonchyClient> logger)
    {
        _integrations = integrations;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<TelefonchyCallPage?> GetCallsAsync(
        int? tenantId = null,
        int page = 1,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string sort = "created_at DESC",
        string? callDest = null,
        CancellationToken ct = default)
    {
        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasTelefonchy)
            return null;

        var query = new Dictionary<string, string?>
        {
            ["service_id"] = config.TelefonchyServiceId,
            ["page"] = Math.Max(1, page).ToString(CultureInfo.InvariantCulture),
            ["sort"] = sort
        };
        if (!string.IsNullOrWhiteSpace(callDest))
            query["call_dest"] = callDest.Trim();
        if (fromUtc is DateTime from)
            query["date_from"] = ToJalaliParam(from);
        if (toUtc is DateTime to)
            query["date_to"] = ToJalaliParam(to, endOfDay: true);

        var json = await SendAsync(config, HttpMethod.Get, "/calls", query, ct);
        if (json is null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var calls = new List<TelefonchyCallDto>();
            TryGetProperty(root, "data", out var data);

            // نسخه فعلی API آرایه تماس‌ها را در data.data می‌گذارد؛ data.calls شکل قدیمی است.
            if (!TryGetProperty(data, "data", out var arr) || arr.ValueKind != JsonValueKind.Array)
                TryGetProperty(data, "calls", out arr);
            if (arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                    calls.Add(ParseCall(item));
            }

            var current = 1;
            var last = 1;
            var total = calls.Count;
            if (TryGetProperty(data, "pagination", out var pagination)
                || TryGetProperty(root, "paginator", out pagination))
            {
                current = ReadInt(pagination, "current_page") ?? ReadInt(pagination, "current") ?? 1;
                last = ReadInt(pagination, "total_pages") ?? ReadInt(pagination, "last") ?? 1;
                total = ReadInt(pagination, "total_items") ?? total;
            }

            return new TelefonchyCallPage
            {
                Calls = calls,
                CurrentPage = current,
                LastPage = Math.Max(1, last),
                TotalItems = total
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telefonchy: failed to parse calls list.");
            return null;
        }
    }

    /// <summary>
    /// یک تماس را از لیست API پیدا می‌کند. file_id فقط در GET /calls می‌آید، نه در GET /call.
    /// با call_dest فیلتر می‌شود تا صفحهٔ اول کافی باشد.
    /// </summary>
    public async Task<TelefonchyCallDto?> FindCallAsync(
        string? cuid, string? callId, string? phone, int? tenantId = null, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow.AddHours(-36);
        var to = DateTime.UtcNow.AddDays(1);
        string? dest = null;
        if (TelefonchyCallService.CanonicalPhone(phone) is string canon)
            dest = "0" + canon;

        if (dest is not null)
        {
            var byDest = await ScanCallsAsync(tenantId, from, to, dest, 3, cuid, callId, ct);
            if (byDest is not null)
                return byDest;
        }

        return await ScanCallsAsync(tenantId, from, to, dest: null, maxPages: 3, cuid, callId, ct);
    }

    private async Task<TelefonchyCallDto?> ScanCallsAsync(
        int? tenantId, DateTime from, DateTime to, string? dest, int maxPages,
        string? cuid, string? callId, CancellationToken ct)
    {
        for (var page = 1; page <= maxPages; page++)
        {
            var result = await GetCallsAsync(tenantId, page, from, to, callDest: dest, ct: ct);
            if (result is null || result.Calls.Count == 0)
                return null;

            var match = result.Calls.FirstOrDefault(c =>
                (!string.IsNullOrWhiteSpace(cuid) && c.Cuid == cuid)
                || (!string.IsNullOrWhiteSpace(callId) && c.CallId == callId));
            if (match is not null)
                return match;

            if (result.CurrentPage >= result.LastPage)
                break;
        }

        return null;
    }

    public async Task<TelefonchyCallDto?> GetCallAsync(string callId, int? tenantId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callId))
            return null;

        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasTelefonchy)
            return null;

        var json = await SendAsync(config, HttpMethod.Get, "/call", new Dictionary<string, string?>
        {
            ["call_id"] = callId.Trim()
        }, ct);
        if (json is null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (TryGetProperty(root, "data", out var data))
                return ParseCall(data);
            return ParseCall(root);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telefonchy: failed to parse call {CallId}.", callId);
            return null;
        }
    }

    /// <summary>لیست سرویس‌ها، خطوط و داخلی‌های ثبت‌شده در تلفنچی. برای عیب‌یابی مقدار exten.</summary>
    public async Task<List<TelefonchyServiceDto>?> GetServicesAsync(
        int? tenantId = null, CancellationToken ct = default)
    {
        var config = await _integrations.GetAsync(tenantId);
        if (!config.TelefonchyEnabled || string.IsNullOrWhiteSpace(config.TelefonchyToken))
            return null;

        var json = await SendAsync(config, HttpMethod.Get, "/services", null, ct);
        if (json is null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryGetProperty(doc.RootElement, "data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;

            var services = new List<TelefonchyServiceDto>();
            foreach (var item in data.EnumerateArray())
            {
                var service = new TelefonchyServiceDto { ServiceId = ReadString(item, "service_id") };

                if (TryGetProperty(item, "trunks", out var trunks) && trunks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var trunk in trunks.EnumerateArray())
                    {
                        var number = ReadString(trunk, "number");
                        if (!string.IsNullOrWhiteSpace(number))
                            service.Trunks.Add(number);
                    }
                }

                if (TryGetProperty(item, "extens", out var extens) && extens.ValueKind == JsonValueKind.Array)
                {
                    foreach (var exten in extens.EnumerateArray())
                    {
                        service.Extens.Add(new TelefonchyExtenDto
                        {
                            Number = ReadString(exten, "number"),
                            Name = ReadString(exten, "name")
                        });
                    }
                }

                services.Add(service);
            }

            return services;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telefonchy: failed to parse services response.");
            return null;
        }
    }

    public async Task<TelefonchySmartCallResult?> SmartCallAsync(
        string exten, string to, int? tenantId = null, CancellationToken ct = default)
    {
        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasTelefonchy)
            return new TelefonchySmartCallResult { Ok = false, Message = "اتصال تلفنچی پیکربندی نشده است." };

        var json = await SendAsync(config, HttpMethod.Get, "/smartcall", new Dictionary<string, string?>
        {
            ["service_id"] = config.TelefonchyServiceId,
            ["exten"] = exten.Trim(),
            ["to"] = to.Trim()
        }, ct);
        if (json is null)
            return new TelefonchySmartCallResult { Ok = false, Message = "ارتباط با تلفنچی برقرار نشد." };

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var code = ReadInt(root, "code") ?? 0;
            var data = TryGetProperty(root, "data", out var d) ? d : root;
            var ok = code == 200 || string.Equals(ReadString(root, "status"), "Ok", StringComparison.OrdinalIgnoreCase);
            return new TelefonchySmartCallResult
            {
                Ok = ok,
                Message = ReadString(root, "message"),
                RequestId = ReadString(data, "request_id"),
                Cuid = ReadString(data, "cuid"),
                CallStatus = ReadString(data, "call_status") ?? ReadString(data, "status"),
                FailureReason = ReadString(data, "failure_reason")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telefonchy: failed to parse smartcall response.");
            return new TelefonchySmartCallResult { Ok = false, Message = "پاسخ تلفنچی قابل‌خواندن نبود." };
        }
    }

    public async Task<TelefonchySmartCallResult?> GetSmartCallStatusAsync(
        string requestId, int? tenantId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return null;

        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasTelefonchy)
            return null;

        var path = "/smartcall/status/" + Uri.EscapeDataString(requestId.Trim());
        var json = await SendAsync(config, HttpMethod.Get, path, null, ct);
        if (json is null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var data = TryGetProperty(root, "data", out var d) ? d : root;
            var code = ReadInt(root, "code") ?? 0;
            return new TelefonchySmartCallResult
            {
                Ok = code == 200 || string.Equals(ReadString(root, "status"), "Ok", StringComparison.OrdinalIgnoreCase),
                Message = ReadString(root, "message"),
                RequestId = ReadString(data, "request_id") ?? requestId,
                Cuid = ReadString(data, "cuid"),
                CallStatus = ReadString(data, "call_status") ?? ReadString(data, "status"),
                FailureReason = ReadString(data, "failure_reason")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telefonchy: failed to parse smartcall status.");
            return null;
        }
    }

    public async Task<TelefonchyRecordingResult> GetRecordingAsync(
        string fileId, string cuid, int? tenantId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileId) || string.IsNullOrWhiteSpace(cuid))
            return new TelefonchyRecordingResult { Error = "شناسه فایل یا شناسه تماس خالی است." };

        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasTelefonchy || !config.TelefonchyRecordingEnabled)
            return new TelefonchyRecordingResult { Error = "اتصال تلفنچی یا گزینه ضبط مکالمه فعال نیست." };

        try
        {
            using var request = BuildRequest(config, HttpMethod.Get, "/calls/record", new Dictionary<string, string?>
            {
                ["file_id"] = fileId.Trim(),
                ["cuid"] = cuid.Trim()
            }, acceptJson: false);
            var client = _httpFactory.CreateClient("telefonchy");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telefonchy recording HTTP {Status}.", (int)response.StatusCode);
                return new TelefonchyRecordingResult
                {
                    Error = $"تلفنچی کد {(int)response.StatusCode} برگرداند."
                };
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
                return new TelefonchyRecordingResult { Error = "پاسخ تلفنچی خالی بود." };

            // این مسیر حتی در خطا هم HTTP 200 و Content-Type صوتی می‌دهد و
            // متن خطا را در بدنه JSON می‌فرستد. بدون این بررسی، فایل خراب کش می‌شود.
            if (TryReadJsonError(bytes, out var apiError))
            {
                _logger.LogWarning("Telefonchy recording rejected: {Error}", apiError);
                return new TelefonchyRecordingResult { Error = apiError };
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                ?? $"{cuid}.mp3";
            return new TelefonchyRecordingResult
            {
                Content = bytes,
                ContentType = contentType,
                FileName = fileName
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telefonchy: recording download failed.");
            return new TelefonchyRecordingResult { Error = "دریافت فایل صوتی با خطا مواجه شد." };
        }
    }

    /// <summary>
    /// ضبط کیفیت merged که پنل تلفنچی پخش می‌کند. برخلاف file_id، برای تماس‌های
    /// پاسخ‌داده‌شده معمولاً ظرف یکی دو دقیقه آماده می‌شود یا با همین درخواست ساخته می‌شود.
    /// </summary>
    public async Task<TelefonchyMergedRecordingResult> GetMergedRecordingAsync(
        string cuid, string recordId, int? tenantId = null, string? downloadUrl = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cuid) || string.IsNullOrWhiteSpace(recordId))
            return new TelefonchyMergedRecordingResult
            {
                State = TelefonchyMergedRecordingState.Failed,
                Message = "شناسه تماس یا شناسه رکورد ضبط خالی است."
            };

        var config = await _integrations.GetAsync(tenantId);
        if (!config.HasTelefonchy || !config.TelefonchyRecordingEnabled)
            return new TelefonchyMergedRecordingResult
            {
                State = TelefonchyMergedRecordingState.Failed,
                Message = "اتصال تلفنچی یا گزینه ضبط مکالمه فعال نیست."
            };

        try
        {
            using var request = IsTrustedDownloadUrl(config, downloadUrl)
                ? BuildAbsoluteRequest(config, downloadUrl!)
                : BuildRequest(config, HttpMethod.Get, "/calls/record-quality/merged", new Dictionary<string, string?>
                {
                    ["cuid"] = cuid.Trim(),
                    ["record_id"] = recordId.Trim()
                }, acceptJson: false);

            var client = _httpFactory.CreateClient("telefonchy");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var jsonStatus = TryReadMergedStatus(bytes, out var jsonMessage);

            if (response.StatusCode == System.Net.HttpStatusCode.Accepted
                || string.Equals(jsonStatus, "processing", StringComparison.OrdinalIgnoreCase))
            {
                return new TelefonchyMergedRecordingResult
                {
                    State = TelefonchyMergedRecordingState.Processing,
                    Message = jsonMessage ?? "فایل صوت در حال آماده‌سازی است."
                };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound
                || string.Equals(jsonStatus, "failed", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(jsonStatus))
            {
                return new TelefonchyMergedRecordingResult
                {
                    State = TelefonchyMergedRecordingState.Failed,
                    Message = jsonMessage ?? "تلفنچی فایل صوتی این تماس را نداد."
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telefonchy merged recording HTTP {Status}.", (int)response.StatusCode);
                return new TelefonchyMergedRecordingResult
                {
                    State = TelefonchyMergedRecordingState.Failed,
                    Message = $"تلفنچی کد {(int)response.StatusCode} برگرداند."
                };
            }

            if (bytes.Length == 0)
                return new TelefonchyMergedRecordingResult
                {
                    State = TelefonchyMergedRecordingState.Failed,
                    Message = "پاسخ تلفنچی خالی بود."
                };

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/wav";
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                ?? $"{cuid}-merged.wav";
            return new TelefonchyMergedRecordingResult
            {
                State = TelefonchyMergedRecordingState.Ready,
                Content = bytes,
                ContentType = contentType,
                FileName = fileName
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telefonchy: merged recording download failed.");
            return new TelefonchyMergedRecordingResult
            {
                State = TelefonchyMergedRecordingState.Failed,
                Message = "دریافت فایل صوتی با خطا مواجه شد."
            };
        }
    }

    public static bool IsTrustedDownloadUrl(IntegrationConfig config, string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;
        if (!Uri.TryCreate(config.ResolvedTelefonchyBaseUrl, UriKind.Absolute, out var baseUri))
            return false;
        return string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// شناسه واقعی فایل ضبط خط تیره دارد (مثل 4i1E-....). مقدار 0 یعنی ضبط نیست
    /// و id/record_id تماس شناسه MongoDB بدون خط تیره است و به درد دانلود نمی‌خورد.
    /// </summary>
    private static string? NormalizeFileId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();
        if (value is "0" or "-" or "false" or "null")
            return null;
        return value.Contains('-') ? value : null;
    }

    private static string? NormalizeMongoId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();
        if (value is "0" or "1" or "-" or "false" or "true" or "null")
            return null;
        if (IsAsteriskUniqueId(value) || value.Contains('.'))
            return null;
        return value.Length >= 16 ? value : null;
    }

    /// <summary>اگر بدنه پاسخ به‌جای صوت، JSON خطا باشد، پیام آن را برمی‌گرداند.</summary>
    private static bool TryReadJsonError(byte[] bytes, out string error)
    {
        error = "";
        // فایل صوتی واقعی هیچ‌وقت این‌قدر کوچک نیست و با { شروع نمی‌شود.
        if (bytes.Length == 0 || bytes.Length > 4096 || bytes[0] != (byte)'{')
            return false;

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var status = ReadString(doc.RootElement, "status");
            var message = ReadString(doc.RootElement, "message");
            if (string.Equals(status, "Ok", StringComparison.OrdinalIgnoreCase))
                return false;

            error = message ?? "تلفنچی فایل صوتی این تماس را نداد.";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryReadMergedStatus(byte[] bytes, out string? message)
    {
        message = null;
        if (bytes.Length == 0 || bytes.Length > 4096 || bytes[0] != (byte)'{')
            return null;

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            message = ReadString(doc.RootElement, "message");
            return ReadString(doc.RootElement, "status");
        }
        catch
        {
            return null;
        }
    }

    public static TelefonchyCallDto ParseCall(JsonElement el)
    {
        var nested = el;
        if (TryGetProperty(el, "data", out var inner) && inner.ValueKind == JsonValueKind.Object)
            nested = inner;

        var dto = new TelefonchyCallDto
        {
            Cuid = ReadString(nested, "cuid") ?? ReadString(nested, "unique_id"),
            CallId = ReadString(nested, "id") ?? ReadString(nested, "call_id") ?? ReadString(nested, "object_id"),
            Type = ReadString(nested, "type"),
            Status = ReadString(nested, "status"),
            Event = ReadString(nested, "event"),
            Src = ReadString(nested, "src") ?? ReadNumberOrString(nested, "call_source"),
            Dest = ReadString(nested, "dest") ?? ReadNumberOrString(nested, "call_dest") ?? ReadString(nested, "dst"),
            Trunk = ReadString(nested, "trunk") ?? ReadString(nested, "trunk_number"),
            FileId = NormalizeFileId(ReadString(nested, "file_id")),
            FileRecord = ReadString(nested, "file_record") ?? ReadString(nested, "record"),
            FileStatus = ReadString(nested, "file_status"),
            HasAudio = ReadString(nested, "has_audio") is "true" or "1"
                || ReadInt(nested, "has_audio") == 1
                || ReadString(nested, "quality_recording_available") is "true" or "1",
            WaitSeconds = ReadInt(nested, "time_wait") ?? ReadInt(nested, "wait"),
            TalkSeconds = ReadInt(nested, "time_talk") ?? ReadInt(nested, "billsec"),
            StartedAt = ReadDate(nested, "started_at") ?? ReadDate(nested, "datetime") ?? ReadDate(nested, "date"),
            EndedAt = ReadDate(nested, "ended_at"),
            ServiceId = ReadString(nested, "service_id"),
            WebserviceToken = ReadString(nested, "webservice_token"),
            RequestId = ReadString(nested, "request_id"),
            DownloadUrl = ReadString(nested, "download_url")
        };

        dto.RecordId = NormalizeMongoId(ReadString(nested, "object_id"))
            ?? NormalizeMongoId(ReadString(nested, "record_id"))
            ?? NormalizeMongoId(ReadString(nested, "id"));

        // در CDR، call_id همان unique id استریسک است (مثل 1788970111.1564374)
        // نه شناسه MongoDB لیست تماس‌ها. اگر cuid جدا نباشد، همین را cuid می‌گیریم.
        if (string.IsNullOrWhiteSpace(dto.Cuid) && IsAsteriskUniqueId(dto.CallId))
        {
            dto.Cuid = dto.CallId;
            dto.CallId = null;
        }
        else if (IsAsteriskUniqueId(dto.CallId) && dto.Cuid == dto.CallId)
        {
            dto.CallId = null;
        }

        if (string.IsNullOrWhiteSpace(dto.CallId) && !string.IsNullOrWhiteSpace(dto.RecordId))
            dto.CallId = dto.RecordId;

        if (TryGetProperty(nested, "exten", out var exten))
        {
            dto.Extension = exten.ValueKind == JsonValueKind.Object
                ? ReadString(exten, "number") ?? ReadString(exten, "exten_id")
                : ReadString(nested, "exten");
        }
        else
        {
            dto.Extension = ReadString(nested, "exten");
        }

        if (TryGetProperty(nested, "contact", out var contact) && contact.ValueKind == JsonValueKind.Object)
        {
            if (string.IsNullOrWhiteSpace(dto.Src) && TryGetProperty(contact, "call_source", out var srcObj))
                dto.Src = ReadString(srcObj, "name") is var _ ? ReadString(nested, "call_source") ?? dto.Src : dto.Src;
        }

        return dto;
    }

    /// <summary>شناسه یکتای استریسک نقطه دارد؛ شناسه رکورد MongoDB ندارد.</summary>
    public static bool IsAsteriskUniqueId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var dot = value.IndexOf('.');
        if (dot < 8)
            return false;
        for (var i = 0; i < dot; i++)
        {
            if (!char.IsDigit(value[i]))
                return false;
        }
        return true;
    }

    public static TelefonchyCallDto? ParseWebhookPayload(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var data = TryGetProperty(root, "data", out var d) && d.ValueKind == JsonValueKind.Object ? d : root;
            var dto = ParseCall(data);
            dto.WebserviceToken ??= ReadString(data, "webservice_token") ?? ReadString(root, "webservice_token");
            dto.Event ??= ReadString(data, "event") ?? ReadString(root, "event");
            dto.Cuid ??= ReadString(data, "unique_id") ?? ReadString(root, "cuid");
            dto.DownloadUrl ??= ReadString(data, "download_url") ?? ReadString(root, "download_url");
            dto.RecordId ??= NormalizeMongoId(ReadString(root, "object_id"))
                ?? NormalizeMongoId(ReadString(root, "record_id"));
            return dto;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> SendAsync(
        IntegrationConfig config,
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken ct)
    {
        try
        {
            using var request = BuildRequest(config, method, path, query);
            var client = _httpFactory.CreateClient("telefonchy");
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telefonchy HTTP {Status} on {Path}.", (int)response.StatusCode, path);
                return null;
            }

            return body;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telefonchy request failed on {Path}.", path);
            return null;
        }
    }

    private static HttpRequestMessage BuildRequest(
        IntegrationConfig config,
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        bool acceptJson = true)
    {
        var url = config.ResolvedTelefonchyBaseUrl + (path.StartsWith('/') ? path : "/" + path);
        if (query is { Count: > 0 })
        {
            var qs = string.Join("&", query
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));
            if (qs.Length > 0)
                url += "?" + qs;
        }

        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("webservice-token", config.TelefonchyToken);
        if (acceptJson)
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static HttpRequestMessage BuildAbsoluteRequest(IntegrationConfig config, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("webservice-token", config.TelefonchyToken);
        return request;
    }

    /// <summary>
    /// تلفنچی تاریخ را فقط در قالب «1405/06/18 00:00» می‌پذیرد.
    /// برای انتهای بازه باید 23:59 فرستاده شود، وگرنه تماس‌های همان روز جا می‌مانند.
    /// </summary>
    public static string ToJalaliParam(DateTime utcOrLocal, bool endOfDay = false)
    {
        var local = utcOrLocal.Kind == DateTimeKind.Utc ? utcOrLocal.ToLocalTime() : utcOrLocal;
        var pc = new PersianCalendar();
        var time = endOfDay ? "23:59" : "00:00";
        return $"{pc.GetYear(local):0000}/{pc.GetMonth(local):00}/{pc.GetDayOfMonth(local):00} {time}";
    }

    private static bool TryGetProperty(JsonElement el, string name, out JsonElement value)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out value))
            return true;
        value = default;
        return false;
    }

    /// <summary>
    /// در پیلود CDR و لیست تماس‌ها، call_source و call_dest آبجکت‌اند و شماره
    /// داخل فیلد number قرار دارد؛ در ایونت‌ها همان مقدار رشته است.
    /// </summary>
    private static string? ReadNumberOrString(JsonElement el, string name)
    {
        if (!TryGetProperty(el, name, out var v))
            return null;
        if (v.ValueKind == JsonValueKind.Object)
            return ReadString(v, "number");
        return ReadString(el, name);
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (!TryGetProperty(el, name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString(),
            JsonValueKind.Number => v.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => null
        };
    }

    private static int? ReadInt(JsonElement el, string name)
    {
        if (!TryGetProperty(el, name, out var v))
            return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
            return n;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
            return (int)Math.Round(d);
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return (int)Math.Round(parsed);
        return null;
    }

    private static DateTime? ReadDate(JsonElement el, string name)
    {
        var raw = ReadString(el, name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        return null;
    }
}
