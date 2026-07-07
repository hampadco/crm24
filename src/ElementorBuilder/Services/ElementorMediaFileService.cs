using ElementorBuilder.Abstractions;
using ElementorBuilder.Helpers;
using ElementorBuilder.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ElementorBuilder.Services;

public class ElementorMediaFileService
{
    private readonly IWebHostEnvironment _env;
    private readonly ElementorBuilderOptions _options;
    private readonly IElementorMediaUsageChecker _usageChecker;

    public ElementorMediaFileService(
        IWebHostEnvironment env,
        IOptions<ElementorBuilderOptions> options,
        IElementorMediaUsageChecker usageChecker)
    {
        _env = env;
        _options = options.Value;
        _usageChecker = usageChecker;
    }

    public async Task<(bool Success, string? Url, string? Error)> UploadAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return (false, null, "فایلی انتخاب نشده است");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_options.AllowedExtensions.Contains(extension))
            return (false, null, "فرمت فایل مجاز نیست");

        var maxBytes = _options.MaxUploadSizeMb * 1024L * 1024L;
        if (file.Length > maxBytes)
            return (false, null, $"حجم فایل نباید بیشتر از {_options.MaxUploadSizeMb} مگابایت باشد");

        var uploadsRoot = GetUploadRoot(_options.UploadFolder);
        Directory.CreateDirectory(uploadsRoot);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(uploadsRoot, fileName);

        await using (var stream = File.Create(filePath))
        {
            await file.CopyToAsync(stream);
        }

        var urlFolder = _options.UploadFolder.Trim('/').Replace('\\', '/');
        return (true, $"/{urlFolder}/{fileName}", null);
    }

    public async Task<(bool Success, string Message)> TryDeleteIfUnreferencedAsync(
        string? url,
        CancellationToken cancellationToken = default)
    {
        var normalized = ElementorMediaUrlHelper.NormalizeMediaUrl(url);
        if (normalized is null || !ElementorMediaUrlHelper.IsManagedUploadUrl(normalized, _options))
            return (false, "آدرس فایل معتبر نیست.");

        if (await _usageChecker.IsUrlInUseAsync(normalized, cancellationToken))
            return (false, "فایل هنوز در محتوای دیگری استفاده می‌شود.");

        return TryDeleteByUrl(normalized)
            ? (true, "فایل حذف شد.")
            : (false, "فایل یافت نشد.");
    }

    public bool TryDeleteByUrl(string? url)
    {
        var normalized = ElementorMediaUrlHelper.NormalizeMediaUrl(url);
        if (normalized is null || !ElementorMediaUrlHelper.IsManagedUploadUrl(normalized, _options))
            return false;

        if (!TryResolveManagedFilePath(normalized, out var filePath))
            return false;

        if (!File.Exists(filePath))
            return false;

        File.Delete(filePath);
        return true;
    }

    public async Task CleanupRemovedMediaAsync(
        IEnumerable<string> previousUrls,
        IEnumerable<string> currentUrls,
        CancellationToken cancellationToken = default)
    {
        foreach (var url in ElementorMediaUrlHelper.GetRemovedUrls(previousUrls, currentUrls))
        {
            if (!ElementorMediaUrlHelper.IsManagedUploadUrl(url, _options))
                continue;

            if (await _usageChecker.IsUrlInUseAsync(url, cancellationToken))
                continue;

            TryDeleteByUrl(url);
        }
    }

    public async Task DeleteMediaUrlsAsync(
        IEnumerable<string> urls,
        CancellationToken cancellationToken = default)
    {
        foreach (var url in urls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var normalized = ElementorMediaUrlHelper.NormalizeMediaUrl(url);
            if (normalized is null || !ElementorMediaUrlHelper.IsManagedUploadUrl(normalized, _options))
                continue;

            if (await _usageChecker.IsUrlInUseAsync(normalized, cancellationToken))
                continue;

            TryDeleteByUrl(normalized);
        }
    }

    private bool TryResolveManagedFilePath(string normalizedUrl, out string filePath)
    {
        filePath = string.Empty;
        var relativePath = normalizedUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(_env.WebRootPath, relativePath));

        foreach (var prefix in ElementorMediaUrlHelper.GetManagedUrlPrefixes(_options))
        {
            var folder = prefix.Trim('/').Replace('/', Path.DirectorySeparatorChar);
            var managedRoot = Path.GetFullPath(Path.Combine(_env.WebRootPath, folder));
            if (candidate.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase))
            {
                filePath = candidate;
                return true;
            }
        }

        return false;
    }

    private string GetUploadRoot(string folder) =>
        Path.Combine(_env.WebRootPath, folder.Replace('/', Path.DirectorySeparatorChar));
}
