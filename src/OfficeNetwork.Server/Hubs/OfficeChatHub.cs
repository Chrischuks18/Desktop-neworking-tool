using Microsoft.AspNetCore.SignalR;
using OfficeNetwork.Server.Services;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Hubs;

public sealed class OfficeChatHub(PresenceService presence, UserAccountService users) : Hub
{
    private OfficeUser RequireUser()
    {
        var http = Context.GetHttpContext();
        var token = http?.Request.Query["access_token"].ToString();
        var user = string.IsNullOrWhiteSpace(token) ? null : users.FromToken(token);
        if (user is null || !user.IsEnabled) throw new HubException("Authentication required.");
        return user;
    }

    public async Task Register()
    {
        var account = RequireUser();
        var user = new PresenceInfo(account.Id, account.DisplayName, account.Role, true);
        presence.SetOnline(Context.ConnectionId, user);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"role:{account.Role}");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{account.Id}");
        await Clients.All.SendAsync("PresenceChanged", presence.GetOnlineUsers());
    }

    public Task SendToEveryone(string text)
    {
        var sender = RequireUser();
        if (sender.Role != OfficeRole.Director) throw new HubException("Only the Director can message everyone.");
        return Clients.All.SendAsync("ReceiveMessage", NewMessage(sender, text, null, null, true));
    }

    public Task SendPrivate(string text, Guid recipientId)
    {
        var sender = RequireUser();
        return Clients.Group($"user:{recipientId}").SendAsync("ReceiveMessage", NewMessage(sender, text, recipientId, null, false));
    }

    public Task SendToRole(string text, OfficeRole role)
    {
        var sender = RequireUser();
        if (sender.Role != OfficeRole.Director) throw new HubException("Only the Director can message a whole department.");
        return Clients.Group($"role:{role}").SendAsync("ReceiveMessage", NewMessage(sender, text, null, role, false));
    }

    private static ChatMessage NewMessage(OfficeUser sender, string text, Guid? recipientId, OfficeRole? recipientRole, bool broadcast) =>
        new(Guid.NewGuid(), sender.Id, sender.DisplayName, recipientId, recipientRole, text.Trim(), DateTimeOffset.UtcNow, broadcast);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        presence.SetOffline(Context.ConnectionId);
        await Clients.All.SendAsync("PresenceChanged", presence.GetOnlineUsers());
        await base.OnDisconnectedAsync(exception);
    }
}
