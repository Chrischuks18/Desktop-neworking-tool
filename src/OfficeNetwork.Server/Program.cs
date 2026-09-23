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
app.MapGet("/api/files/{folder}", (string folder, OfficeConfigurationService config) =>
{
    if (!Enum.TryParse<OfficeFolder>(folder, true, out var parsed)) return Results.BadRequest("Unknown folder.");
    return Results.Ok(config.ListFiles(parsed));
});
app.MapPost("/api/files/{folder}/submit", (string folder, string fileName, OfficeConfigurationService config) =>
{
    if (!Enum.TryParse<OfficeFolder>(folder, true, out var parsed)) return Results.BadRequest("Unknown folder.");
    return config.MoveFile(parsed, OfficeFolder.SubmittedFiles, fileName) ? Results.Ok() : Results.NotFound();
});
app.MapPost("/api/files/SubmittedFiles/approve", (string fileName, OfficeConfigurationService config) =>
    config.MoveFile(OfficeFolder.SubmittedFiles, OfficeFolder.FinalFiles, fileName) ? Results.Ok() : Results.NotFound());

app.MapHub<OfficeChatHub>("/hubs/chat");
app.Run("http://0.0.0.0:5077");
