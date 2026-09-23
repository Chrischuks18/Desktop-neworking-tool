using Microsoft.AspNetCore.SignalR;
using OfficeNetwork.Server.Services;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Hubs;

public sealed class OfficeChatHub(PresenceService presence) : Hub
{
    public async Task Register(Guid userId, string displayName, OfficeRole role)
    {
        var user = new PresenceInfo(userId, displayName, role, true);
        presence.SetOnline(Context.ConnectionId, user);

        await Groups.AddToGroupAsync(Context.ConnectionId, $"role:{role}");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        await Clients.All.SendAsync("PresenceChanged", presence.GetOnlineUsers());
    }

    public Task SendToEveryone(ChatMessage message) =>
        Clients.All.SendAsync("ReceiveMessage", message with
        {
            RecipientId = null,
            RecipientRole = null,
            IsBroadcast = true,
            SentAt = DateTimeOffset.UtcNow
        });

    public Task SendPrivate(ChatMessage message, Guid recipientId) =>
        Clients.Group($"user:{recipientId}").SendAsync("ReceiveMessage", message with
        {
            RecipientId = recipientId,
            IsBroadcast = false,
            SentAt = DateTimeOffset.UtcNow
        });

    public Task SendToRole(ChatMessage message, OfficeRole role) =>
        Clients.Group($"role:{role}").SendAsync("ReceiveMessage", message with
        {
            RecipientRole = role,
            IsBroadcast = false,
            SentAt = DateTimeOffset.UtcNow
        });

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        presence.SetOffline(Context.ConnectionId);
        await Clients.All.SendAsync("PresenceChanged", presence.GetOnlineUsers());
        await base.OnDisconnectedAsync(exception);
    }
}
