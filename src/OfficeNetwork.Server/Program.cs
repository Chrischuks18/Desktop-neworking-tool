using Microsoft.AspNetCore.Http;
using OfficeNetwork.Server.Hubs;
using OfficeNetwork.Server.Services;
using OfficeNetwork.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSingleton<OfficeConfigurationService>();
builder.Services.AddSingleton<UserAccountService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { application = "Choice Flame Communications Network", status = "online", machine = Environment.MachineName }));
app.MapGet("/api/status", (PresenceService presence) => Results.Ok(new { server = Environment.MachineName, onlineUsers = presence.GetOnlineUsers() }));
app.MapPost("/api/users", IResult (CreateUserRequest request, UserAccountService users, OfficeConfigurationService config) =>
{
    try { var user = users.Create(request); config.CreateUserFolders(user); return Results.Ok(user); }
    catch (InvalidOperationException ex) { return Results.BadRequest(ex.Message); }
});
app.MapGet("/api/users", (UserAccountService users) => Results.Ok(users.Users));
app.MapPost("/api/login", IResult (LoginRequest request, UserAccountService users) =>
{
    var login = users.Login(request);
    return login is null ? Results.Unauthorized() : Results.Ok(login);
});

app.MapGet("/api/configuration", (OfficeConfigurationService config) => Results.Ok(config.Configuration));

app.MapPost("/api/setup", (ServerConfiguration request, OfficeConfigurationService config) =>
{
    config.Configure(request);
    return Results.Ok(new { configured = true, folders = config.CreateFolderStructure(request.RootPath) });
});

static OfficeUser? Auth(HttpRequest request, UserAccountService users)
{
    var header = request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? users.FromToken(header[7..].Trim()) : null;
}

app.MapGet("/api/files/{folder}", IResult (string folder, HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user = Auth(request, users);
    if (user is null) return Results.Unauthorized();
    if (!Enum.TryParse<OfficeFolder>(folder, true, out var parsed)) return Results.BadRequest("Unknown folder.");
    return Results.Ok(config.ListFilesForUser(parsed, user));
});

app.MapPost("/api/files/WorkingFiles/submit", IResult (string fileName, HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user = Auth(request, users);
    if (user is null) return Results.Unauthorized();
    if (user.Role is OfficeRole.Director or OfficeRole.ServerAdministrator) return Results.Forbid();
    return config.SubmitForUser(user, fileName) ? Results.Ok() : Results.NotFound();
});

app.MapPost("/api/files/SubmittedFiles/approve", IResult (string ownerUserName, string fileName, HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user = Auth(request, users);
    if (user is null) return Results.Unauthorized();
    if (user.Role != OfficeRole.Director) return Results.Forbid();
    return config.ApproveForDirector(ownerUserName, fileName) ? Results.Ok() : Results.NotFound();
});

app.MapHub<OfficeChatHub>("/hubs/chat");
app.Run("http://0.0.0.0:5077");
