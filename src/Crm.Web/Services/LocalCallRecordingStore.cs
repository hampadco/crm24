using Microsoft.EntityFrameworkCore;
using Crm.Core.Abstractions;
using Crm.Core.Entities;
using Crm.Infrastructure.Data;

namespace Crm.Web.Services;

public class LocalCallRecordingStore : ICallRecordingStore
{
    private readonly IWebHostEnvironment _env;
    private readonly CrmDbContext _db;

    public LocalCallRecordingStore(IWebHostEnvironment env, CrmDbContext db)
    {
        _env = env;
        _db = db;
    }

    public async Task<string> SaveAsync(
        int tenantId,
        int callRecordId,
        byte[] content,
        string extension,
        string contentType,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        if (content.Length == 0)
            throw new ArgumentException("فایل ضبط خالی است.", nameof(content));

        var ext = NormalizeExt(extension, contentType, originalFileName);
        var relativeDir = Path.Combine("uploads", "records", tenantId.ToString(), "calls", callRecordId.ToString());
        var absDir = Path.Combine(_env.WebRootPath, relativeDir);
        Directory.CreateDirectory(absDir);

        var fileName = "merged" + ext;
        var absPath = Path.Combine(absDir, fileName);
        await File.WriteAllBytesAsync(absPath, content, cancellationToken);

        var storedPath = "/" + Path.Combine(relativeDir, fileName).Replace('\\', '/');
        var exists = await _db.Attachments.AnyAsync(a =>
            a.ModuleName == "calls" && a.RecordId == callRecordId && a.StoredPath == storedPath, cancellationToken);
        if (!exists)
        {
            _db.Attachments.Add(new Attachment
            {
                ModuleName = "calls",
                RecordId = callRecordId,
                FileName = string.IsNullOrWhiteSpace(originalFileName) ? fileName : originalFileName,
                StoredPath = storedPath,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? GuessContentType(ext) : contentType,
                SizeBytes = content.Length
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return storedPath;
    }

    public string? TryGetAbsolutePath(int tenantId, int callRecordId, string? storedRelativePath)
    {
        var root = Path.GetFullPath(_env.WebRootPath);
        if (!string.IsNullOrWhiteSpace(storedRelativePath))
        {
            var rel = storedRelativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var abs = Path.GetFullPath(Path.Combine(_env.WebRootPath, rel));
            if (abs.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && File.Exists(abs)
                && new FileInfo(abs).Length > 4096)
                return abs;
        }

        var dir = Path.Combine(_env.WebRootPath, "uploads", "records", tenantId.ToString(), "calls", callRecordId.ToString());
        if (!Directory.Exists(dir))
            return null;

        return Directory.EnumerateFiles(dir)
            .Where(f => new FileInfo(f).Length > 4096)
            .OrderByDescending(f => f.Contains("merged", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    private static string NormalizeExt(string extension, string contentType, string originalFileName)
    {
        var ext = extension;
        if (string.IsNullOrWhiteSpace(ext))
            ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext) && contentType.Contains("wav", StringComparison.OrdinalIgnoreCase))
            ext = ".wav";
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".mp3";
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        return ext.ToLowerInvariant();
    }

    private static string GuessContentType(string ext) =>
        ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav" : "audio/mpeg";
}
