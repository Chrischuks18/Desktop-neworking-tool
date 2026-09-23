using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Services;

public sealed class UserAccountService
{
    private readonly ConcurrentDictionary<string, Guid> _tokens = new();
    private readonly string _connectionString;

    public UserAccountService()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Choice Flame Communications Network");
        Directory.CreateDirectory(dataDir);
        _connectionString = $"Data Source={Path.Combine(dataDir, "choiceflame.db")}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Users (
                Id TEXT PRIMARY KEY,
                UserName TEXT NOT NULL UNIQUE COLLATE NOCASE,
                DisplayName TEXT NOT NULL,
                Role INTEGER NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                Salt BLOB NOT NULL,
                PasswordHash BLOB NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<OfficeUser> Users
    {
        get
        {
            var result = new List<OfficeUser>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, UserName, DisplayName, Role, IsEnabled FROM Users ORDER BY DisplayName";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                result.Add(new OfficeUser(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), (OfficeRole)reader.GetInt32(3), reader.GetBoolean(4)));
            return result;
        }
    }

    public OfficeUser Create(CreateUserRequest request)
    {
        if (request.Role == OfficeRole.ServerAdministrator) throw new InvalidOperationException("Server Administrator is a machine administration role.");
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Password))
            throw new InvalidOperationException("Full name, username and password are required.");

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(request.Password, salt, 120000, HashAlgorithmName.SHA256, 32);
        var user = new OfficeUser(Guid.NewGuid(), request.UserName.Trim(), request.DisplayName.Trim(), request.Role);

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Users (Id, UserName, DisplayName, Role, IsEnabled, Salt, PasswordHash, CreatedAt) VALUES ($id,$username,$display,$role,1,$salt,$hash,$created)";
            command.Parameters.AddWithValue("$id", user.Id.ToString());
            command.Parameters.AddWithValue("$username", user.UserName);
            command.Parameters.AddWithValue("$display", user.DisplayName);
            command.Parameters.AddWithValue("$role", (int)user.Role);
            command.Parameters.AddWithValue("$salt", salt);
            command.Parameters.AddWithValue("$hash", hash);
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
            return user;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Username already exists.");
        }
    }

    public LoginResult? Login(LoginRequest request)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, UserName, DisplayName, Role, IsEnabled, Salt, PasswordHash FROM Users WHERE UserName=$username COLLATE NOCASE";
        command.Parameters.AddWithValue("$username", request.UserName.Trim());
        using var reader = command.ExecuteReader();
        if (!reader.Read() || !reader.GetBoolean(4)) return null;

        var salt = (byte[])reader["Salt"];
        var storedHash = (byte[])reader["PasswordHash"];
        var candidate = Rfc2898DeriveBytes.Pbkdf2(request.Password, salt, 120000, HashAlgorithmName.SHA256, 32);
        if (!CryptographicOperations.FixedTimeEquals(candidate, storedHash)) return null;

        var user = new OfficeUser(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), (OfficeRole)reader.GetInt32(3), true);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tokens[token] = user.Id;
        return new(user.Id, user.UserName, user.DisplayName, user.Role, token);
    }

    public OfficeUser? FromToken(string? token)
    {
        if (token is null || !_tokens.TryGetValue(token, out var id)) return null;
        return Users.FirstOrDefault(x => x.Id == id && x.IsEnabled);
    }
}
