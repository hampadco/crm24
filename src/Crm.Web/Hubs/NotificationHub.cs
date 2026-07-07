using Microsoft.AspNetCore.SignalR;

namespace Crm.Web.Hubs;

/// <summary>هاب اعلان لحظه‌ای؛ کلاینت پنل به گروه user-{id} متصل می‌شود.</summary>
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");

        await base.OnConnectedAsync();
    }
}
