using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.HttpOverrides;
using OfficeNetwork.Server.Hubs;
using OfficeNetwork.Server.Services;
using OfficeNetwork.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSingleton<OfficeConfigurationService>();
builder.Services.AddSingleton<UserAccountService>();
builder.Services.AddSingleton<WorkAssignmentService>();
builder.WebHost.UseUrls("http://0.0.0.0:5077");

var app = builder.Build();

static bool IsPrivateOrLoopback(IPAddress? address)
{
    if(address is null)return false;
    if(IPAddress.IsLoopback(address))return true;
    if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();
    if(address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork)
    {
        var b=address.GetAddressBytes();
        return b[0]==10 || (b[0]==172 && b[1]>=16 && b[1]<=31) || (b[0]==192 && b[1]==168) || (b[0]==169 && b[1]==254);
    }
    return address.Equals(IPAddress.IPv6Loopback) || address.IsIPv6LinkLocal || (address.GetAddressBytes()[0]&0xFE)==0xFC;
}

app.Use(async (context,next) =>
{
    if(!IsPrivateOrLoopback(context.Connection.RemoteIpAddress))
    {
        context.Response.StatusCode=StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Choice Flame Network accepts office LAN connections only.");
        return;
    }
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    context.Response.Headers["X-Frame-Options"]="DENY";
    context.Response.Headers["Cache-Control"]="no-store";
    await next();
});

app.MapGet("/", () => Results.Ok(new { application = "Choice Flame Communications Network", status = "online", machine = Environment.MachineName }));
app.MapGet("/api/status", IResult (HttpRequest http, PresenceService presence, UserAccountService users) =>
{
    var caller=Auth(http,users);
    return caller is null?Results.Unauthorized():Results.Ok(new { server = Environment.MachineName, onlineUsers = presence.GetOnlineUsers() });
});
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
    if (!initialDirector && caller?.Role is not (OfficeRole.Director or OfficeRole.Admin)) return Results.Forbid();
    if (caller?.Role == OfficeRole.Admin && request.Role is OfficeRole.Director or OfficeRole.Admin) return Results.Forbid();
    try { var user = users.Create(request); config.CreateUserFolders(user); return Results.Ok(user); }
    catch (InvalidOperationException ex) { return Results.BadRequest(ex.Message); }
});
app.MapGet("/api/users", IResult (HttpRequest http, UserAccountService users) =>
{
    var caller = Auth(http, users);
    return caller?.Role is OfficeRole.Director or OfficeRole.Admin ? Results.Ok(users.Users) : Results.Forbid();
});
app.MapGet("/api/users/assignable", IResult (HttpRequest http, UserAccountService users) =>
{
    var caller = Auth(http, users);
    if(caller?.Role is not (OfficeRole.Director or OfficeRole.Admin)) return Results.Forbid();
    return Results.Ok(users.Users.Where(x => x.IsEnabled && x.Role is OfficeRole.Editor or OfficeRole.NewsSourcing).ToArray());
});
app.MapPut("/api/users/{id:guid}", IResult (Guid id, UpdateUserRequest body, HttpRequest http, UserAccountService users) =>
{
    var caller=Auth(http,users); if(caller is null)return Results.Unauthorized();
    var target=users.Users.FirstOrDefault(x=>x.Id==id); if(target is null)return Results.NotFound();
    if(caller.Role==OfficeRole.Director)
    {
        if(id==caller.Id && (!body.IsEnabled || body.Role!=OfficeRole.Director)) return Results.BadRequest("The signed-in Director cannot disable or remove their own Director role.");
    }
    else if(caller.Role==OfficeRole.Admin)
    {
        if(target.Role is OfficeRole.Director or OfficeRole.Admin || body.Role is OfficeRole.Director or OfficeRole.Admin)return Results.Forbid();
    }
    else return Results.Forbid();
    try{return Results.Ok(users.Update(id,body));}catch(InvalidOperationException ex){return Results.BadRequest(ex.Message);}
});
app.MapDelete("/api/users/{id:guid}", IResult (Guid id, HttpRequest http, UserAccountService users) =>
{
    var caller=Auth(http,users); if(caller is null)return Results.Unauthorized();
    if(id==caller.Id)return Results.BadRequest("You cannot delete the account you are currently signed in with.");
    var target=users.Users.FirstOrDefault(x=>x.Id==id); if(target is null)return Results.NotFound();
    if(caller.Role==OfficeRole.Admin && target.Role is OfficeRole.Director or OfficeRole.Admin)return Results.Forbid();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.Forbid();
    return users.Delete(id)?Results.Ok():Results.NotFound();
});
var loginAttempts=new ConcurrentDictionary<string,(int Count,DateTimeOffset WindowStart,DateTimeOffset? BlockedUntil)>();
app.MapPost("/api/login", IResult (LoginRequest request, HttpContext context, UserAccountService users) =>
{
    var key=(context.Connection.RemoteIpAddress?.ToString()??"unknown")+"|"+request.UserName.Trim().ToLowerInvariant();
    var now=DateTimeOffset.UtcNow;
    if(loginAttempts.TryGetValue(key,out var state))
    {
        if(state.BlockedUntil is DateTimeOffset blocked && blocked>now)
            return Results.Problem("Too many failed login attempts. Try again in a few minutes.",statusCode:429);
        if(now-state.WindowStart>TimeSpan.FromMinutes(10)) loginAttempts.TryRemove(key,out _);
    }
    try
    {
        var login=users.Login(request);
        if(login is not null){loginAttempts.TryRemove(key,out _);return Results.Ok(login);}
        var current=loginAttempts.GetOrAdd(key,_=>(0,now,null));
        var count=current.Count+1;
        loginAttempts[key]=count>=5?(count,current.WindowStart,now.AddMinutes(5)):(count,current.WindowStart,null);
        return Results.Unauthorized();
    }
    catch(InvalidOperationException ex){return Results.Conflict(ex.Message);}
});
app.MapPost("/api/logout", IResult (HttpRequest request, UserAccountService users) =>
{
    var auth=request.Headers.Authorization.ToString();
    users.Logout(auth.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?auth[7..].Trim():null);
    return Results.Ok();
});
app.MapPost("/api/session/heartbeat", IResult (HttpRequest request, UserAccountService users) =>
{
    return Auth(request,users) is null?Results.Unauthorized():Results.Ok();
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
    if (user.Role is OfficeRole.Director or OfficeRole.Admin && parsed is OfficeFolder.WorkingFiles or OfficeFolder.SubmittedFiles && !string.IsNullOrWhiteSpace(ownerUserName))
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

app.MapGet("/api/files/FinalFiles/backup", IResult (HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user=Auth(request,users);
    if(user is null)return Results.Unauthorized();
    if(user.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.Forbid();
    var finalFolder=config.FolderPathForUser(OfficeFolder.FinalFiles,user);
    if(!Directory.Exists(finalFolder))return Results.NotFound("Final Files folder is not available.");
    var tempPath=Path.Combine(Path.GetTempPath(),"ChoiceFlame-FinalFiles-"+Guid.NewGuid().ToString("N")+".zip");
    System.IO.Compression.ZipFile.CreateFromDirectory(finalFolder,tempPath,System.IO.Compression.CompressionLevel.Optimal,false);
    var bytes=File.ReadAllBytes(tempPath);
    File.Delete(tempPath);
    var name=$"Choice-Flame-Final-Files-Backup-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip";
    return Results.File(bytes,"application/zip",name);
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
    if (user.Role is not (OfficeRole.Director or OfficeRole.Admin)) return Results.Forbid();
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
    return Results.Ok(user.Role is OfficeRole.Director or OfficeRole.Admin ? history : history.Where(x=>x.OwnerUserName.Equals(user.UserName,StringComparison.OrdinalIgnoreCase)));
});

app.MapPost("/api/files/SubmittedFiles/approve", IResult (string ownerUserName, string fileName, HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user = Auth(request, users);
    if (user is null) return Results.Unauthorized();
    if (user.Role is not (OfficeRole.Director or OfficeRole.Admin)) return Results.Forbid();
    return config.ApproveForDirector(ownerUserName, fileName) ? Results.Ok() : Results.NotFound();
});


app.MapGet("/api/assignments", IResult (HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users); return caller is null?Results.Unauthorized():Results.Ok(assignments.List(caller));
});
app.MapPost("/api/assignments", async Task<IResult> (HttpRequest request, UserAccountService users, WorkAssignmentService assignments, IHubContext<OfficeChatHub> hub) =>
{
    var caller=Auth(request,users); if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.Forbid();
    if(!request.HasFormContentType)return Results.BadRequest("Assignment details are required.");
    var form=await request.ReadFormAsync(); if(!Guid.TryParse(form["assignedToUserId"],out var targetId))return Results.BadRequest("Select a staff member.");
    var target=users.Users.FirstOrDefault(x=>x.Id==targetId && x.Role is OfficeRole.Editor or OfficeRole.NewsSourcing); if(target is null)return Results.BadRequest("Assignments can be given to Editors or News Sourcing staff.");
    var title=form["title"].ToString(); var notes=form["instructions"].ToString(); if(string.IsNullOrWhiteSpace(title))return Results.BadRequest("A work title is required.");
    DateTimeOffset? due=null; if(DateTimeOffset.TryParse(form["dueAt"],out var parsedDue))due=parsedDue;
    var file=form.Files.FirstOrDefault(); var item=assignments.Create(caller,target,title,notes,due,file is null?null:Path.GetFileName(file.FileName));
    if(file is not null){var path=Path.Combine(assignments.AttachmentFolder(item.Id),Path.GetFileName(file.FileName)); await using var stream=File.Create(path); await file.CopyToAsync(stream);}
    await hub.Clients.Group($"user:{target.Id}").SendAsync("AssignmentNotification",item.Id,item.Title,caller.DisplayName,item.Instructions);
    return Results.Ok(item);
});
app.MapGet("/api/assignments/{id:guid}/attachment", IResult (Guid id, HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users); if(caller is null)return Results.Unauthorized(); var item=assignments.List(caller).FirstOrDefault(x=>x.Id==id);
    if(item is null || string.IsNullOrWhiteSpace(item.FileName))return Results.NotFound(); var path=Path.Combine(assignments.AttachmentFolder(id),Path.GetFileName(item.FileName)); return File.Exists(path)?Results.File(path,"application/octet-stream",item.FileName):Results.NotFound();
});
app.MapPost("/api/assignments/{id:guid}/complete", async Task<IResult> (Guid id, HttpRequest request, UserAccountService users, WorkAssignmentService assignments, IHubContext<OfficeChatHub> hub) =>
{
    var caller=Auth(request,users); if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Editor or OfficeRole.NewsSourcing))return Results.Forbid();
    if(!request.HasFormContentType)return Results.BadRequest("Choose the finished work file to submit.");
    var form=await request.ReadFormAsync(); var upload=form.Files.FirstOrDefault();
    if(upload is null || upload.Length==0)return Results.BadRequest("Choose the finished work file to submit.");
    WorkAssignment? item;
    await using(var input=upload.OpenReadStream()) item=assignments.CompleteAndSubmit(caller,id,upload.FileName,input);
    if(item is null)return Results.NotFound();
    await hub.Clients.Group("role:Director").SendAsync("AssignmentCompleted",item.Id,item.Title,caller.DisplayName,item.CompletedAt);
    await hub.Clients.Group("role:Admin").SendAsync("AssignmentCompleted",item.Id,item.Title,caller.DisplayName,item.CompletedAt);
    return Results.Ok(item);
});

app.MapHub<OfficeChatHub>("/hubs/chat");
app.Run("http://0.0.0.0:5077");
