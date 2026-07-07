using ElementorBuilder.Abstractions;

namespace ElementorBuilder.Services;

/// <summary>
/// Default checker: assumes URLs are not shared across entities.
/// Replace in host apps that store media in multiple records.
/// </summary>
public class NullElementorMediaUsageChecker : IElementorMediaUsageChecker
{
    public Task<bool> IsUrlInUseAsync(string url, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
