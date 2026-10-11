using System.Text.Json;
using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using Crm.Core.Abstractions;

namespace Crm.Infrastructure.Services;

public sealed class ImportJobRequest
{
    public int ModuleId { get; set; }
    public List<ImportColumnBinding> Bindings { get; set; } = [];
}

public sealed class ImportRunStatus
{
    public string State { get; set; } = "queued";
    public int Total { get; set; }
    public int Processed { get; set; }
    public int Imported { get; set; }
    public int LastRow { get; set; } = 1;
    public int ErrorCount { get; set; }
    public List<string> Errors { get; set; } = [];
    public string? Message { get; set; }
}

public class RecordImportJob
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly RecordImportExportService _import;
    private readonly TenantContext _tenant;
    private readonly ILogger<RecordImportJob> _logger;

    public RecordImportJob(RecordImportExportService import, TenantContext tenant, ILogger<RecordImportJob> logger)
    {
        _import = import;
        _tenant = tenant;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 1)]
    public async Task RunAsync(int tenantId, int userId, string dataPath, string jobPath, string statusPath, PerformContext? context)
    {
        _tenant.TenantId = tenantId;
        if (userId > 0)
            _tenant.UserId = userId;

        var status = await ReadStatusAsync(statusPath) ?? new ImportRunStatus();
        if (status.State is "done")
            return;

        try
        {
            if (!File.Exists(dataPath) || !File.Exists(jobPath))
                throw new FileNotFoundException("فایل ورود پیدا نشد.");

            var request = JsonSerializer.Deserialize<ImportJobRequest>(await File.ReadAllTextAsync(jobPath), JsonOptions)
                ?? throw new InvalidOperationException("نگاشت ورود خوانده نشد.");

            await using var stream = File.OpenRead(dataPath);
            var table = RecordImportExportService.ReadTable(stream, dataPath);
            status.State = "running";
            status.Total = table.Rows.Count(row => !row.All(string.IsNullOrWhiteSpace));
            status.Message = null;
            await WriteStatusAsync(statusPath, status);

            var cursor = new ImportRunCursor
            {
                Total = status.Total,
                Processed = status.Processed,
                Imported = status.Imported,
                LastRow = status.LastRow,
                Errors = status.Errors.ToList()
            };

            await _import.ImportMappedAsync(request.ModuleId, table, request.Bindings, cursor, async snap =>
            {
                status.Total = snap.Total;
                status.Processed = snap.Processed;
                status.Imported = snap.Imported;
                status.LastRow = snap.LastRow;
                status.ErrorCount = snap.Errors.Count;
                status.Errors = snap.Errors.TakeLast(20).ToList();
                status.State = "running";
                await WriteStatusAsync(statusPath, status);
            });

            status.State = "done";
            status.Message = status.ErrorCount > 0
                ? $"{status.Imported} رکورد وارد شد و {status.ErrorCount} ردیف خطا داشت."
                : $"{status.Imported} رکورد وارد شد.";
            await WriteStatusAsync(statusPath, status);
            TryDelete(dataPath);
            TryDelete(jobPath);
        }
        catch (Exception ex)
        {
            var retry = context?.GetJobParameter<int>("RetryCount") ?? 0;
            if (retry >= 1)
            {
                status.State = "failed";
                status.Message = "ورود ناتمام ماند. رکوردهایی که تا اینجا ثبت شده‌اند باقی می‌مانند.";
            }
            else
            {
                status.State = "running";
                status.Message = "اختلال کوتاهی پیش آمد. ورود از همان ردیف ادامه پیدا می‌کند.";
            }

            await WriteStatusAsync(statusPath, status);
            _logger.LogError(ex, "Excel import failed for tenant {TenantId}.", tenantId);
            throw;
        }
    }

    public static async Task<ImportRunStatus?> ReadStatusAsync(string path)
    {
        if (!File.Exists(path))
            return null;
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<ImportRunStatus>(stream, JsonOptions);
    }

    public static async Task WriteStatusAsync(string path, ImportRunStatus status)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(status, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            /* فایل موقتی */
        }
    }
}
