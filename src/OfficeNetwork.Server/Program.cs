using Microsoft.AspNetCore.Http;
using System.Net;
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
static OfficeUser? Auth(HttpRequest request, UserAccountService users)
{
    var header = request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? users.FromToken(header[7..].Trim()) : null;
}

app.MapPost("/api/users", IResult (CreateUserRequest request, HttpRequest http, UserAccountService users, OfficeConfigurationService config) =>
{
    var existing = users.Users;
    var caller = Auth(http, users);
    var initialDirector = existing.Count == 0 && request.Role == OfficeRole.Director;
    if (initialDirector && !IPAddress.IsLoopback(http.HttpContext.Connection.RemoteIpAddress ?? IPAddress.None))
        return Results.Forbid();
    if (!initialDirector && caller?.Role != OfficeRole.Director) return Results.Forbid();
    try { var user = users.Create(request); config.CreateUserFolders(user); return Results.Ok(user); }
    catch (InvalidOperationException ex) { return Results.BadRequest(ex.Message); }
});
app.MapGet("/api/users", IResult (HttpRequest http, UserAccountService users) =>
{
    var caller = Auth(http, users);
    return caller?.Role == OfficeRole.Director ? Results.Ok(users.Users) : Results.Forbid();
});
app.MapPost("/api/login", IResult (LoginRequest request, UserAccountService users) =>
{
    var login = users.Login(request);
    return login is null ? Results.Unauthorized() : Results.Ok(login);
});

app.MapGet("/api/configuration", IResult (HttpRequest http, OfficeConfigurationService config, UserAccountService users) =>
{
    var caller = Auth(http, users);
    return caller?.Role == OfficeRole.Director ? Results.Ok(config.Configuration) : Results.Forbid();
});

app.MapPost("/api/setup", IResult (ServerConfiguration request, HttpRequest http, OfficeConfigurationService config, UserAccountService users) =>
{
    var caller = Auth(http, users);
    var local = IPAddress.IsLoopback(http.HttpContext.Connection.RemoteIpAddress ?? IPAddress.None);
    if (caller?.Role != OfficeRole.Director && !local) return Results.Forbid();
    config.Configure(request);
    return Results.Ok(new { configured = true, folders = config.CreateFolderStructure(request.RootPath) });
});

app.MapGet("/api/files/{folder}", IResult (string folder, HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user = Auth(request, users);
    if (user is null) return Results.Unauthorized();
    if (!Enum.TryParse<OfficeFolder>(folder, true, out var parsed)) return Results.BadRequest("Unknown folder.");
    return Results.Ok(config.ListFilesForUser(parsed, user));
});


app.MapPost("/api/files/WorkingFiles/upload", async Task<IResult> (HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user = Auth(request, users);
    if (user is null) return Results.Unauthorized();
    if (user.Role is not (OfficeRole.Editor or OfficeRole.NewsSourcing or OfficeRole.Director)) return Results.Forbid();
    if (!request.HasFormContentType) return Results.BadRequest("A file is required.");
    var form = await request.ReadFormAsync();
    var upload = form.Files.FirstOrDefault();
    if (upload is null || upload.Length == 0) return Results.BadRequest("A file is required.");
    var safeName = Path.GetFileName(upload.FileName);
    if (string.IsNullOrWhiteSpace(safeName)) return Results.BadRequest("Invalid file name.");
    var folder = config.FolderPathForUser(OfficeFolder.WorkingFiles, user);
    Directory.CreateDirectory(folder);
    var destination = Path.Combine(folder, safeName);
    await using var stream = File.Create(destination);
    await upload.CopyToAsync(stream);
    return Results.Ok(new { fileName = safeName, size = upload.Length });
});

app.MapGet("/api/files/{folder}/download", IResult (string folder, string fileName, string? ownerUserName, HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user = Auth(request, users);
    if (user is null) return Results.Unauthorized();
    if (!Enum.TryParse<OfficeFolder>(folder, true, out var parsed)) return Results.BadRequest("Unknown folder.");
    var safeName = Path.GetFileName(fileName);
    string baseFolder;
    if (user.Role == OfficeRole.Director && parsed is OfficeFolder.WorkingFiles or OfficeFolder.SubmittedFiles && !string.IsNullOrWhiteSpace(ownerUserName))
    {
        var safeOwner = string.Concat(ownerUserName.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        baseFolder = Path.Combine(config.FolderPathForUser(parsed, user), safeOwner);
    }
    else
    {
        baseFolder = config.FolderPathForUser(parsed, user);
    }
    var fullPath = Path.Combine(baseFolder, safeName);
    if (!File.Exists(fullPath)) return Results.NotFound();
    return Results.File(fullPath, "application/octet-stream", safeName);
});

app.MapPost("/api/files/WorkingFiles/submit", IResult (string fileName, HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user = Auth(request, users);
    if (user is null) return Results.Unauthorized();
    if (user.Role is OfficeRole.Director or OfficeRole.ServerAdministrator) return Results.Forbid();
    return config.SubmitForUser(user, fileName) ? Results.Ok() : Results.NotFound();
});

app.MapPost("/api/files/SubmittedFiles/recall", IResult (string fileName, HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user = Auth(request, users); if (user is null) return Results.Unauthorized();
    return config.RecallForUser(user, fileName) ? Results.Ok() : Results.NotFound();
});

app.MapPost("/api/files/SubmittedFiles/return", IResult (ReturnFileRequest body, HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user = Auth(request, users); if (user is null) return Results.Unauthorized();
    if (user.Role != OfficeRole.Director) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(body.DirectorMinute)) return Results.BadRequest("A Director's minute/instruction is required.");
    return config.ReturnForCorrection(user, body.OwnerUserName, body.FileName, body.DirectorMinute) ? Results.Ok() : Results.NotFound();
});

app.MapDelete("/api/files/WorkingFiles", IResult (string fileName, HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user=Auth(request,users); if(user is null) return Results.Unauthorized();
    return config.DeleteWorkingFile(user,fileName)?Results.Ok():Results.NotFound();
});

app.MapPost("/api/files/WorkingFiles/rename", IResult (RenameFileRequest body, HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user=Auth(request,users); if(user is null) return Results.Unauthorized();
    return config.RenameWorkingFile(user,body.FileName,body.NewFileName)?Results.Ok():Results.BadRequest("Could not rename file.");
});

app.MapGet("/api/files/history", IResult (HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user=Auth(request,users); if(user is null) return Results.Unauthorized();
    var history=config.History();
    return Results.Ok(user.Role==OfficeRole.Director ? history : history.Where(x=>x.OwnerUserName.Equals(user.UserName,StringComparison.OrdinalIgnoreCase)));
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
