namespace Crm.Web.Services;

using ElementorBuilder.Services;

public class MediaUploadService
{
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"
    };

    private static readonly HashSet<string> AllowedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".ogg", ".aac"
    };

    private readonly IWebHostEnvironment _env;
    private readonly ElementorMediaFileService _mediaFiles;
    private const string DefaultFolder = "uploads/taben";
    private const int MaxSizeMb = 10;

    public MediaUploadService(IWebHostEnvironment env, ElementorMediaFileService mediaFiles)
    {
        _env = env;
        _mediaFiles = mediaFiles;
    }

    public async Task<(bool Success, string? Url, string? Error)> UploadImageAsync(IFormFile? file, string? folder = null)
    {
        if (file == null || file.Length == 0)
            return (false, null, "فایلی انتخاب نشده است.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
            return (false, null, "فرمت تصویر مجاز نیست. (jpg, png, gif, webp, svg)");

        var maxBytes = MaxSizeMb * 1024L * 1024L;
        if (file.Length > maxBytes)
            return (false, null, $"حجم فایل نباید بیشتر از {MaxSizeMb} مگابایت باشد.");

        return await SaveFileAsync(file, extension, folder ?? DefaultFolder);
    }

    public async Task<(bool Success, string? Url, string? Error)> UploadAudioAsync(IFormFile? file, string? folder = null)
    {
        if (file == null || file.Length == 0)
            return (false, null, "فایلی انتخاب نشده است.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedAudioExtensions.Contains(extension))
            return (false, null, "فرمت صوتی مجاز نیست. (mp3, wav, m4a, ogg, aac)");

        const int maxAudioMb = 20;
        var maxBytes = maxAudioMb * 1024L * 1024L;
        if (file.Length > maxBytes)
            return (false, null, $"حجم فایل نباید بیشتر از {maxAudioMb} مگابایت باشد.");

        return await SaveFileAsync(file, extension, folder ?? DefaultFolder);
    }

    private async Task<(bool Success, string? Url, string? Error)> SaveFileAsync(
        IFormFile file,
        string extension,
        string folder)
    {
        var uploadFolder = folder.Trim('/').Replace('\\', '/');
        var uploadsRoot = Path.Combine(_env.WebRootPath, uploadFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(uploadsRoot);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(uploadsRoot, fileName);

        await using (var stream = File.Create(filePath))
        {
            await file.CopyToAsync(stream);
        }

        return (true, $"/{uploadFolder}/{fileName}", null);
    }

    public bool TryDeleteByUrl(string? url) => _mediaFiles.TryDeleteByUrl(url);
}
