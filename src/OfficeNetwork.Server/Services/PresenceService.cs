using System.Collections.Concurrent;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Services;

public sealed class PresenceService
{
    private readonly ConcurrentDictionary<string, PresenceInfo> _connections = new();

    public void SetOnline(string connectionId, PresenceInfo user) =>
        _connections[connectionId] = user with { IsOnline = true };

    public void SetOffline(string connectionId) =>
        _connections.TryRemove(connectionId, out _);

    public IReadOnlyCollection<PresenceInfo> GetOnlineUsers() =>
        _connections.Values
            .GroupBy(x => x.UserId)
            .Select(x => x.First())
            .OrderBy(x => x.DisplayName)
            .ToArray();
}
