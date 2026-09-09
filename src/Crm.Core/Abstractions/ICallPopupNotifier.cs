namespace Crm.Core.Abstractions;

/// <summary>اعلان لحظه‌ای تماس (پاپ‌آپ SignalR در لایه وب).</summary>
public interface ICallPopupNotifier
{
    Task NotifyIncomingAsync(int userId, object payload, CancellationToken cancellationToken = default);
    Task NotifyStatusAsync(int userId, object payload, CancellationToken cancellationToken = default);
}
