using System.Collections.Concurrent;
using System.Security.Cryptography;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Services;

public sealed class UserAccountService
{
    private sealed record StoredUser(OfficeUser User, byte[] Salt, byte[] Hash);
    private readonly ConcurrentDictionary<string, StoredUser> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Guid> _tokens = new();

    public IReadOnlyList<OfficeUser> Users => _users.Values.Select(x => x.User).OrderBy(x => x.DisplayName).ToArray();

    public OfficeUser Create(CreateUserRequest request)
    {
        if (request.Role == OfficeRole.ServerAdministrator) throw new InvalidOperationException("Server Administrator is a machine administration role.");
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password)) throw new InvalidOperationException("Username and password are required.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(request.Password, salt, 120000, HashAlgorithmName.SHA256, 32);
        var user = new OfficeUser(Guid.NewGuid(), request.UserName.Trim(), request.DisplayName.Trim(), request.Role);
        if (!_users.TryAdd(user.UserName, new StoredUser(user, salt, hash))) throw new InvalidOperationException("Username already exists.");
        return user;
    }

    public LoginResult? Login(LoginRequest request)
    {
        if (!_users.TryGetValue(request.UserName, out var stored) || !stored.User.IsEnabled) return null;
        var hash = Rfc2898DeriveBytes.Pbkdf2(request.Password, stored.Salt, 120000, HashAlgorithmName.SHA256, 32);
        if (!CryptographicOperations.FixedTimeEquals(hash, stored.Hash)) return null;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tokens[token] = stored.User.Id;
        return new(stored.User.Id, stored.User.UserName, stored.User.DisplayName, stored.User.Role, token);
    }

    public OfficeUser? FromToken(string? token)
    {
        if (token is null || !_tokens.TryGetValue(token, out var id)) return null;
        return _users.Values.Select(x => x.User).FirstOrDefault(x => x.Id == id && x.IsEnabled);
    }
}
