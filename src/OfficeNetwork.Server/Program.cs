using Microsoft.AspNetCore.Http;
using OfficeNetwork.Server.Hubs;
using OfficeNetwork.Server.Services;
using OfficeNetwork.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSingleton<OfficeConfigurationService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { application = "Choice Flame Communications Network", status = "online", machine = Environment.MachineName }));
app.MapGet("/api/status", (PresenceService presence) => Results.Ok(new { server = Environment.MachineName, onlineUsers = presence.GetOnlineUsers() }));
app.MapGet("/api/configuration", (OfficeConfigurationService config) => Results.Ok(config.Configuration));

app.MapPost("/api/setup", (ServerConfiguration request, OfficeConfigurationService config) =>
{
    config.Configure(request);
    return Results.Ok(new { configured = true, folders = config.CreateFolderStructure(request.RootPath) });
});

app.MapGet("/api/files/{folder}", IResult (string folder, OfficeConfigurationService config) =>
{
    if (!Enum.TryParse<OfficeFolder>(folder, true, out var parsed))
        return Results.BadRequest("Unknown folder.");
    return Results.Ok(config.ListFiles(parsed));
});

app.MapPost("/api/files/{folder}/submit", IResult (string folder, string fileName, OfficeConfigurationService config) =>
{
    if (!Enum.TryParse<OfficeFolder>(folder, true, out var parsed))
        return Results.BadRequest("Unknown folder.");
    if (!config.MoveFile(parsed, OfficeFolder.SubmittedFiles, fileName))
        return Results.NotFound();
    return Results.Ok();
});

app.MapPost("/api/files/SubmittedFiles/approve", IResult (string fileName, OfficeConfigurationService config) =>
{
    if (!config.MoveFile(OfficeFolder.SubmittedFiles, OfficeFolder.FinalFiles, fileName))
        return Results.NotFound();
    return Results.Ok();
});

app.MapHub<OfficeChatHub>("/hubs/chat");
app.Run("http://0.0.0.0:5077");
