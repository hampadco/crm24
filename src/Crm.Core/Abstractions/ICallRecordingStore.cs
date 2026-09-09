namespace Crm.Core.Abstractions;

/// <summary>ذخیرهٔ محلی فایل ضبط تماس برای جاب‌های پس‌زمینه و پخش در پنل.</summary>
public interface ICallRecordingStore
{
    Task<string> SaveAsync(
        int tenantId,
        int callRecordId,
        byte[] content,
        string extension,
        string contentType,
        string originalFileName,
        CancellationToken cancellationToken = default);

    string? TryGetAbsolutePath(int tenantId, int callRecordId, string? storedRelativePath);
}
