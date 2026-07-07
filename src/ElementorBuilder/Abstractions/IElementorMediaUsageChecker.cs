namespace ElementorBuilder.Abstractions;

/// <summary>
/// Host apps implement this to prevent deleting files still referenced elsewhere.
/// </summary>
public interface IElementorMediaUsageChecker
{
    Task<bool> IsUrlInUseAsync(string url, CancellationToken cancellationToken = default);
}
