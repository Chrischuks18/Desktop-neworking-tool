using OfficeNetwork.Server.Hubs;
using OfficeNetwork.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSingleton<OfficeConfigurationService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    application = "Office Network Manager",
    status = "online",
    machine = Environment.MachineName
}));

app.MapGet("/api/status", (PresenceService presence) => Results.Ok(new
{
    server = Environment.MachineName,
    onlineUsers = presence.GetOnlineUsers()
}));

app.MapHub<OfficeChatHub>("/hubs/chat");

app.Run("http://0.0.0.0:5077");
