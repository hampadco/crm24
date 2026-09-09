using Microsoft.AspNetCore.SignalR;
using Crm.Core.Abstractions;
using Crm.Web.Hubs;

namespace Crm.Web.Hubs;

public class SignalRCallPopupNotifier : ICallPopupNotifier
{
    private readonly IHubContext<NotificationHub> _hub;

    public SignalRCallPopupNotifier(IHubContext<NotificationHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyIncomingAsync(int userId, object payload, CancellationToken cancellationToken = default) =>
        _hub.Clients.Group($"user-{userId}").SendAsync("incomingCall", payload, cancellationToken);

    public Task NotifyStatusAsync(int userId, object payload, CancellationToken cancellationToken = default) =>
        _hub.Clients.Group($"user-{userId}").SendAsync("callStatus", payload, cancellationToken);
}
