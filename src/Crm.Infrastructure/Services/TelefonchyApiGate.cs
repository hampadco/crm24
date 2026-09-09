using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Crm.Infrastructure.Services;

/// <summary>
/// محدودیت تلفنچی: برای هر توکن و هر مسیر حداکثر ۶۰ درخواست در دقیقه.
/// GET وضعیت و Timeline از remains کم نمی‌کنند، ولی همان سقف مسیر را دارند.
/// </summary>
public sealed class TelefonchyApiGate
{
    public const int MaxPerPathPerMinute = 55;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly Dictionary<string, PathBucket> _paths = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _globalPausedUntilUtc;
    private readonly ILogger<TelefonchyApiGate> _logger;

    public TelefonchyApiGate(ILogger<TelefonchyApiGate> logger)
    {
        _logger = logger;
    }

    public bool IsPaused => DateTime.UtcNow < _globalPausedUntilUtc;

    public async Task WaitAsync(string path, CancellationToken ct = default)
    {
        var key = NormalizePath(path);
        await _mutex.WaitAsync(ct);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var now = DateTime.UtcNow;
                if (now < _globalPausedUntilUtc)
                {
                    await DelayUnlockedAsync(_globalPausedUntilUtc - now, ct);
                    continue;
                }

                var bucket = GetBucket(key);
                if (now < bucket.PausedUntilUtc)
                {
                    await DelayUnlockedAsync(bucket.PausedUntilUtc - now, ct);
                    continue;
                }

                while (bucket.Hits.Count > 0 && now - bucket.Hits.Peek() > Window)
                    bucket.Hits.Dequeue();

                if (bucket.Hits.Count < MaxPerPathPerMinute)
                {
                    bucket.Hits.Enqueue(DateTime.UtcNow);
                    return;
                }

                var waitFor = bucket.Hits.Peek() + Window - now + TimeSpan.FromMilliseconds(300);
                if (waitFor < TimeSpan.FromMilliseconds(100))
                    waitFor = TimeSpan.FromMilliseconds(100);
                await DelayUnlockedAsync(waitFor, ct);
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    public void Observe(HttpResponseMessage response, string path, string? body = null)
    {
        var key = NormalizePath(path);
        var retryAfter = ParseRetryAfter(response.Headers);
        var remaining = ReadIntHeader(response.Headers, "X-RateLimit-Remaining")
            ?? ReadIntHeader(response.Headers, "RateLimit-Remaining");
        var limit = ReadIntHeader(response.Headers, "X-RateLimit-Limit")
            ?? ReadIntHeader(response.Headers, "RateLimit-Limit");
        var status = (int)response.StatusCode;
        var limited = status == 429 || LooksRateLimited(body);

        if (limited && LooksTokenBanned(body))
        {
            PauseGlobal(retryAfter ?? TimeSpan.FromMinutes(2), body);
            return;
        }

        if (limited)
        {
            PausePath(key, retryAfter ?? TimeSpan.FromSeconds(30), body ?? $"HTTP {status}");
            return;
        }

        if (remaining is 0)
            PausePath(key, retryAfter ?? TimeSpan.FromSeconds(20), "X-RateLimit-Remaining=0");

        if (limit is not null || remaining is not null)
        {
            _mutex.Wait();
            try
            {
                var bucket = GetBucket(key);
                bucket.LastLimit = limit;
                bucket.LastRemaining = remaining;
            }
            finally
            {
                _mutex.Release();
            }
        }
    }

    public void NotifyLimited(string path, string? reason = null, TimeSpan? retryAfter = null)
    {
        if (LooksTokenBanned(reason))
        {
            PauseGlobal(retryAfter ?? TimeSpan.FromMinutes(2), reason);
            return;
        }

        PausePath(NormalizePath(path), retryAfter ?? TimeSpan.FromSeconds(30), reason);
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        var value = path.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            value = uri.AbsolutePath;

        var q = value.IndexOf('?');
        if (q >= 0)
            value = value[..q];
        if (!value.StartsWith('/'))
            value = "/" + value;

        if (value.StartsWith("/smartcall/status/", StringComparison.OrdinalIgnoreCase))
            return "/smartcall/status";
        if (value.Contains("/operators/status", StringComparison.OrdinalIgnoreCase))
            return "/operators/status";
        if (value.Contains("/operators/events", StringComparison.OrdinalIgnoreCase))
            return "/operators/events";
        if (value.Contains("record-quality/merged", StringComparison.OrdinalIgnoreCase))
            return "/calls/record-quality/merged";

        return value.TrimEnd('/').ToLowerInvariant();
    }

    /// <summary>GET /operators/status و Timeline از remains کم نمی‌کنند.</summary>
    public static bool DoesNotConsumeRemains(string? path)
    {
        var key = NormalizePath(path);
        return key.Equals("/operators/status", StringComparison.OrdinalIgnoreCase)
            || key.Equals("/operators/events", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksRateLimited(string? text) =>
        LooksTokenBanned(text)
        || (!string.IsNullOrWhiteSpace(text)
            && (text.Contains("محدودیت", StringComparison.Ordinal)
                || text.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Retry-After", StringComparison.OrdinalIgnoreCase)
                || text.Contains("X-RateLimit", StringComparison.OrdinalIgnoreCase)));

    public static bool LooksTokenBanned(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && (text.Contains("دسترسی محدود", StringComparison.Ordinal)
            || text.Contains("10 درخواست", StringComparison.Ordinal)
            || text.Contains("محدودیت اعمال شده", StringComparison.Ordinal));

    private void PauseGlobal(TimeSpan duration, string? reason)
    {
        var until = DateTime.UtcNow + Clamp(duration);
        if (until > _globalPausedUntilUtc)
            _globalPausedUntilUtc = until;
        _logger.LogWarning("Telefonchy token paused until {Until:u}. {Reason}", _globalPausedUntilUtc, reason);
    }

    private void PausePath(string key, TimeSpan duration, string? reason)
    {
        var until = DateTime.UtcNow + Clamp(duration);
        _mutex.Wait();
        try
        {
            var bucket = GetBucket(key);
            if (until > bucket.PausedUntilUtc)
                bucket.PausedUntilUtc = until;
        }
        finally
        {
            _mutex.Release();
        }

        _logger.LogWarning("Telefonchy path {Path} paused until {Until:u}. {Reason}", key, until, reason);
    }

    private PathBucket GetBucket(string key)
    {
        if (!_paths.TryGetValue(key, out var bucket))
        {
            bucket = new PathBucket();
            _paths[key] = bucket;
        }

        return bucket;
    }

    private static TimeSpan Clamp(TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(1))
            return TimeSpan.FromSeconds(1);
        if (duration > TimeSpan.FromMinutes(5))
            return TimeSpan.FromMinutes(5);
        return duration;
    }

    private static int? ReadIntHeader(HttpResponseHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values))
            return null;
        var raw = values.FirstOrDefault();
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseHeaders headers)
    {
        var retry = headers.RetryAfter;
        if (retry is null)
            return null;
        if (retry.Delta is TimeSpan delta)
            return delta;
        if (retry.Date is DateTimeOffset date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1);
        }

        return null;
    }

    private async Task DelayUnlockedAsync(TimeSpan delay, CancellationToken ct)
    {
        _mutex.Release();
        try
        {
            await Task.Delay(delay, ct);
        }
        finally
        {
            await _mutex.WaitAsync(ct);
        }
    }

    private sealed class PathBucket
    {
        public Queue<DateTime> Hits { get; } = new();
        public DateTime PausedUntilUtc { get; set; }
        public int? LastRemaining { get; set; }
        public int? LastLimit { get; set; }
    }
}
