using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using OfficeNetwork.Server.Hubs;
using OfficeNetwork.Server.Services;
using OfficeNetwork.Shared;

var dataFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network");
OfficeDataSecurity.Protect(dataFolder);
if(args.Contains("--restore-archive"))
{
    try
    {
        var index=Array.IndexOf(args,"--restore-archive");
        if(index+1>=args.Length || !args.Contains("--confirm-restore"))throw new InvalidOperationException("Usage: OfficeNetwork.Server.exe --restore-archive <zip-path> --confirm-restore [--restore-root <office-folder>]");
        var rootIndex=Array.IndexOf(args,"--restore-root");
        if(rootIndex>=0 && rootIndex+1>=args.Length)throw new InvalidOperationException("Provide a folder after --restore-root.");
        var settings=Path.Combine(dataFolder,"server-storage.txt");
        var root=rootIndex>=0?args[rootIndex+1]:File.Exists(settings)?File.ReadAllText(settings).Trim():Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),"Choice Flame Communications");
        var result=OfficeRestoreService.Restore(args[index+1],dataFolder,root);
        Console.WriteLine($"Restore completed. Office folder: {result.OfficeRoot}\nPrevious office files: {result.PreviousOfficeFiles}\nPrevious database: {result.PreviousDatabaseFolder}\nStart the server normally and sign in using an account from the restored backup.");
    }
    catch(Exception ex){Console.Error.WriteLine("Restore failed: "+ex.Message);Environment.ExitCode=1;}
    return;
}
using var serverInstanceLock=OfficeRestoreService.AcquireServerLock(dataFolder);
OfficeRestoreService.EnsureNoInterruptedRestore(dataFolder);
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRateLimiter(options=>
{
    options.RejectionStatusCode=StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login-ip",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions
    {
        PermitLimit=20,Window=TimeSpan.FromMinutes(5),QueueLimit=0,AutoReplenishment=true
    }));
});
builder.Services.AddSignalR();
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSingleton<OfficeConfigurationService>();
builder.Services.AddSingleton<FileRecoveryService>();
builder.Services.AddSingleton<UserAccountService>();
builder.Services.AddSingleton<WorkAssignmentService>();
builder.Services.AddSingleton<ActivityCalendarService>();
builder.Services.AddSingleton<AttendanceService>();
builder.Services.AddSingleton<OperationsService>();
builder.Services.AddSingleton<OfficeArchiveService>();
builder.Services.AddSingleton<FilingService>();
builder.Services.AddSingleton<ProductionTrackerService>();
builder.Services.AddSingleton<MeetingMinutesService>();
builder.Services.AddSingleton<EquipmentRegisterService>();
builder.Services.AddHostedService<ScheduledBackupService>();
builder.Services.AddHostedService<SessionCleanupService>();
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

app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new { application = "Choice Flame Communications Network", status = "online", machine = Environment.MachineName }));
// Public LAN-safe health endpoint used before any Director account exists.
// It intentionally exposes no users, sessions, files, configuration or other office data.
app.MapGet("/api/status", () => Results.Ok(new { server = Environment.MachineName, status = "online" }));
static OfficeUser? Auth(HttpRequest request, UserAccountService users)
{
    var header = request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? users.FromToken(header[7..].Trim()) : null;
}

app.MapGet("/api/activities", IResult (DateTime? from,DateTime? to,HttpRequest http,ActivityCalendarService calendar,UserAccountService users) =>
{
    if(Auth(http,users) is null)return Results.Unauthorized();
    var start=(from??DateTime.Today.AddMonths(-1)).Date;var end=(to??DateTime.Today.AddMonths(3)).Date;
    return Results.Ok(calendar.List(start,end));
});
app.MapPost("/api/activities", IResult (SaveActivityRequest request,HttpRequest http,ActivityCalendarService calendar,UserAccountService users) =>
{
    var caller=Auth(http,users);if(caller is null)return Results.Unauthorized();if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    try{return Results.Ok(calendar.Create(request,caller));}catch(InvalidOperationException ex){return Results.BadRequest(ex.Message);}
});
app.MapPut("/api/activities/{id:guid}", IResult (Guid id,SaveActivityRequest request,HttpRequest http,ActivityCalendarService calendar,UserAccountService users) =>
{
    var caller=Auth(http,users);if(caller is null)return Results.Unauthorized();if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    try{return calendar.Update(id,request)?Results.Ok():Results.NotFound();}catch(InvalidOperationException ex){return Results.BadRequest(ex.Message);}
});
app.MapDelete("/api/activities/{id:guid}", IResult (Guid id,HttpRequest http,ActivityCalendarService calendar,UserAccountService users) =>
{
    var caller=Auth(http,users);if(caller is null)return Results.Unauthorized();if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    return calendar.Delete(id)?Results.Ok():Results.NotFound();
});

// Local setup diagnostics expose only booleans, never account names or credentials.
app.MapGet("/api/setup/status",IResult (HttpContext context,UserAccountService users)=>
{
    if(!IPAddress.IsLoopback(context.Connection.RemoteIpAddress??IPAddress.None))return Results.StatusCode(StatusCodes.Status403Forbidden);
    var existing=users.Users;
    return Results.Ok(new DirectorSetupStatus(existing.Count==0,existing.Any(x=>x.IsEnabled && x.Role==OfficeRole.Director)));
});

app.MapPost("/api/users", IResult (CreateUserRequest request, HttpRequest http, UserAccountService users, OfficeConfigurationService config) =>
{
    var existing = users.Users;
    var caller = Auth(http, users);
    var initialDirector = existing.Count == 0 && request.Role == OfficeRole.Director;
    if (initialDirector && !IPAddress.IsLoopback(http.HttpContext.Connection.RemoteIpAddress ?? IPAddress.None))
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    if(!initialDirector && caller is null && IPAddress.IsLoopback(http.HttpContext.Connection.RemoteIpAddress??IPAddress.None))
        return Results.Conflict("Existing accounts were found. First-Director setup cannot replace them. Sign in with the existing Director account; reinstalling the app preserves accounts.");
    if (!initialDirector && caller?.Role is not (OfficeRole.Director or OfficeRole.Admin)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (caller?.Role == OfficeRole.Admin && request.Role is OfficeRole.Director or OfficeRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);
    OfficeUser? created=null;
    try
    {
        created=initialDirector?users.CreateInitialDirector(request):users.Create(request);
        config.CreateUserFolders(created);
        return Results.Ok(created);
    }
    catch(Exception ex) when(ex is InvalidOperationException or IOException or UnauthorizedAccessException or DirectoryNotFoundException or DriveNotFoundException)
    {
        if(created is not null)users.Delete(created.Id);
        return Results.BadRequest("Could not create the account: "+ex.Message);
    }
});
app.MapGet("/api/users", IResult (HttpRequest http, UserAccountService users) =>
{
    var caller = Auth(http, users);
    return caller?.Role is OfficeRole.Director or OfficeRole.Admin ? Results.Ok(users.Users) : Results.StatusCode(StatusCodes.Status403Forbidden);
});
app.MapGet("/api/users/assignable", IResult (HttpRequest http, UserAccountService users) =>
{
    var caller = Auth(http, users);
    if(caller?.Role is not (OfficeRole.Director or OfficeRole.Admin)) return Results.StatusCode(StatusCodes.Status403Forbidden);
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
        if(target.Role is OfficeRole.Director or OfficeRole.Admin || body.Role is OfficeRole.Director or OfficeRole.Admin)return Results.StatusCode(StatusCodes.Status403Forbidden);
    }
    else return Results.StatusCode(StatusCodes.Status403Forbidden);
    try{return Results.Ok(users.Update(id,body));}catch(InvalidOperationException ex){return Results.BadRequest(ex.Message);}
});
app.MapDelete("/api/users/{id:guid}", IResult (Guid id, HttpRequest http, UserAccountService users) =>
{
    var caller=Auth(http,users); if(caller is null)return Results.Unauthorized();
    if(id==caller.Id)return Results.BadRequest("You cannot delete the account you are currently signed in with.");
    var target=users.Users.FirstOrDefault(x=>x.Id==id); if(target is null)return Results.NotFound();
    if(caller.Role==OfficeRole.Admin && target.Role is OfficeRole.Director or OfficeRole.Admin)return Results.StatusCode(StatusCodes.Status403Forbidden);
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    return users.Delete(id)?Results.Ok():Results.NotFound();
});
var loginAttempts=new ConcurrentDictionary<string,(int Count,DateTimeOffset WindowStart,DateTimeOffset? BlockedUntil)>();
app.MapPost("/api/login", IResult (LoginRequest request, HttpContext context, UserAccountService users) =>
{
    if(string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Length>80 || string.IsNullOrEmpty(request.Password) || request.Password.Length>1024)return Results.BadRequest("Provide a valid username and password.");
    if(loginAttempts.Count>1000)foreach(var stale in loginAttempts.Where(x=>DateTimeOffset.UtcNow-x.Value.WindowStart>TimeSpan.FromMinutes(10)).ToArray())loginAttempts.TryRemove(stale.Key,out _);
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
        if(login is not null){loginAttempts.TryRemove(key,out _);var attendance=context.RequestServices.GetRequiredService<AttendanceService>();attendance.RecordLogin(login);return Results.Ok(login);}
        loginAttempts.AddOrUpdate(key,_=>(1,now,null),(_,current)=>
        {
            var count=current.Count+1;
            return count>=5?(count,current.WindowStart,now.AddMinutes(5)):(count,current.WindowStart,null);
        });
        return Results.Unauthorized();
    }
    catch(InvalidOperationException ex){return Results.Conflict(ex.Message);}
}).RequireRateLimiting("login-ip");
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

app.MapGet("/api/attendance/current", IResult (HttpRequest http, UserAccountService users, AttendanceService attendance) => { var u=Auth(http,users); return u is null?Results.Unauthorized():u.Role==OfficeRole.Director?Results.StatusCode(StatusCodes.Status403Forbidden):Results.Ok(attendance.Current(u)); });
app.MapPost("/api/attendance/clock-in", IResult (HttpRequest http, UserAccountService users, AttendanceService attendance) => { var u=Auth(http,users); if(u is null)return Results.Unauthorized(); try{return Results.Ok(attendance.ClockIn(u));}catch(InvalidOperationException ex){return Results.BadRequest(ex.Message);} });
app.MapPost("/api/attendance/clock-out", IResult (HttpRequest http, UserAccountService users, AttendanceService attendance) => { var u=Auth(http,users); if(u is null)return Results.Unauthorized(); try{return Results.Ok(attendance.ClockOut(u));}catch(InvalidOperationException ex){return Results.BadRequest(ex.Message);} });
app.MapGet("/api/attendance", IResult (HttpRequest http, UserAccountService users, AttendanceService attendance) => { var u=Auth(http,users); return u?.Role==OfficeRole.Director?Results.Ok(attendance.ListAttendance()):Results.StatusCode(StatusCodes.Status403Forbidden); });
app.MapGet("/api/attendance/logins", IResult (HttpRequest http, UserAccountService users, AttendanceService attendance) => { var u=Auth(http,users); return u?.Role==OfficeRole.Director?Results.Ok(attendance.ListLogins()):Results.StatusCode(StatusCodes.Status403Forbidden); });

app.MapGet("/api/configuration", IResult (HttpRequest http, OfficeConfigurationService config, UserAccountService users) =>
{
    var caller = Auth(http, users);
    return caller?.Role == OfficeRole.Director ? Results.Ok(config.Configuration) : Results.StatusCode(StatusCodes.Status403Forbidden);
});

app.MapPost("/api/configuration/storage", IResult (ChangeStorageRequest request, HttpRequest http, OfficeConfigurationService config, UserAccountService users) =>
{
    var caller=Auth(http,users);
    if(caller?.Role != OfficeRole.Director)return Results.StatusCode(StatusCodes.Status403Forbidden);
    if(string.IsNullOrWhiteSpace(request.RootPath))return Results.BadRequest("Choose a storage folder.");
    try
    {
        var result=config.ChangeStorage(request.RootPath,request.CopyExistingFiles);
        return result.Success?Results.Ok(new { rootPath=config.Configuration.RootPath,filesCopied=result.FilesCopied,message=result.Message }):Results.BadRequest(result.Message);
    }
    catch(Exception ex){return Results.BadRequest("Storage location could not be changed: "+ex.Message);}
});

app.MapPost("/api/setup", IResult (ServerConfiguration request, HttpRequest http, OfficeConfigurationService config, UserAccountService users) =>
{
    var caller = Auth(http, users);
    var local = IPAddress.IsLoopback(http.HttpContext.Connection.RemoteIpAddress ?? IPAddress.None);
    if (caller?.Role != OfficeRole.Director && !local) return Results.StatusCode(StatusCodes.Status403Forbidden);
    config.Configure(request);
    return Results.Ok(new { configured = true, folders = config.CreateFolderStructure(request.RootPath) });
});

app.MapPost("/api/files/SharedFiles/upload", async Task<IResult> (HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    if(!request.HasFormContentType)return Results.BadRequest("Select a file.");
    var form=await request.ReadFormAsync();
    var upload=form.Files.FirstOrDefault();
    if(upload is null || upload.Length==0 || upload.Length>100*1024*1024)return Results.BadRequest("Choose a file of up to 100 MB.");
    var name=Path.GetFileName(upload.FileName);
    if(string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Contains(':') || name.Contains('\\'))return Results.BadRequest("Invalid filename.");
    var folder=config.FolderPathForUser(OfficeFolder.SharedFiles,caller);
    Directory.CreateDirectory(folder);
    var destination=Path.Combine(folder,name);
    if(File.Exists(destination))return Results.Conflict("A shared file with this name already exists. Rename your file before uploading.");
    try
    {
        await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        await upload.CopyToAsync(output);
        return Results.Ok(new {fileName=name,size=upload.Length,uploadedBy=caller.DisplayName});
    }
    catch(IOException){return Results.Conflict("A shared file with this name already exists.");}
});
app.MapDelete("/api/files/SharedFiles", IResult (string fileName,HttpRequest request,OfficeConfigurationService config,UserAccountService users) =>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    var safe=Path.GetFileName(fileName);
    var path=Path.Combine(config.FolderPathForUser(OfficeFolder.SharedFiles,caller),safe);
    if(!File.Exists(path))return Results.NotFound();
    File.Delete(path);
    return Results.Ok();
});

app.MapGet("/api/recovery",IResult (HttpRequest request,UserAccountService users,FileRecoveryService recovery)=>
{
    var caller=Auth(request,users);
    return caller is null?Results.Unauthorized():Results.Ok(recovery.List(caller));
});
app.MapPost("/api/recovery/{id:guid}/restore",IResult (Guid id,HttpRequest request,UserAccountService users,FileRecoveryService recovery)=>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    return recovery.Restore(id,caller)?Results.Ok():Results.Conflict("Snapshot not found, access denied, or destination filename already exists.");
});
app.MapGet("/api/files/{folder}", IResult (string folder, HttpRequest request, OfficeConfigurationService config, UserAccountService users) =>
{
    var user = Auth(request, users);
    if (user is null) return Results.Unauthorized();
    if (!Enum.TryParse<OfficeFolder>(folder, true, out var parsed)) return Results.BadRequest("Unknown folder.");
    return Results.Ok(config.ListFilesForUser(parsed, user));
});

app.MapPost("/api/files/WorkingFiles/upload", async Task<IResult> (HttpRequest request, OfficeConfigurationService config, UserAccountService users, FileRecoveryService recovery) =>
{
    var user = Auth(request, users);
    if (user is null) return Results.Unauthorized();
    if (user.Role is not (OfficeRole.Editor or OfficeRole.NewsSourcing or OfficeRole.Director)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!request.HasFormContentType) return Results.BadRequest("A file is required.");
    var form = await request.ReadFormAsync();
    var upload = form.Files.FirstOrDefault();
    if (upload is null || upload.Length == 0) return Results.BadRequest("A file is required.");
    var safeName = Path.GetFileName(upload.FileName);
    if (string.IsNullOrWhiteSpace(safeName)) return Results.BadRequest("Invalid file name.");
    var folder = config.FolderPathForUser(OfficeFolder.WorkingFiles, user);
    Directory.CreateDirectory(folder);
    var destination = Path.Combine(folder, safeName);
    if(File.Exists(destination))recovery.Save(destination,OfficeFolder.WorkingFiles,user.UserName,user.DisplayName,"Previous version before upload",false);
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
    if(user.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    var finalFolder=config.FolderPathForUser(OfficeFolder.FinalFiles,user);
    if(!Directory.Exists(finalFolder))return Results.NotFound("Final Files folder is not available.");
    var tempPath=Path.Combine(Path.GetTempPath(),"ChoiceFlame-FinalFiles-"+Guid.NewGuid().ToString("N")+".zip");
    System.IO.Compression.ZipFile.CreateFromDirectory(finalFolder,tempPath,System.IO.Compression.CompressionLevel.Optimal,false);
    var bytes=File.ReadAllBytes(tempPath);
    File.Delete(tempPath);
    var name=$"Choice-Flame-Final-Files-Backup-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip";
    return Results.File(bytes,"application/zip",name);
});

app.MapPost("/api/files/WorkingFiles/submit", IResult (string fileName, HttpRequest request, OfficeConfigurationService config, UserAccountService users, WorkAssignmentService assignments) =>
{
    var user = Auth(request, users);
    if (user is null) return Results.Unauthorized();
    if (user.Role is OfficeRole.Director or OfficeRole.ServerAdministrator) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if(!config.SubmitForUser(user,fileName)) return Results.NotFound();
    assignments.MarkResubmitted(user.UserName,fileName,user.DisplayName);
    return Results.Ok();
});

app.MapPost("/api/files/SubmittedFiles/recall", IResult (string fileName, HttpRequest request, OfficeConfigurationService config, UserAccountService users, WorkAssignmentService assignments) =>
{
    var user = Auth(request, users); if (user is null) return Results.Unauthorized();
    if(!config.RecallForUser(user,fileName)) return Results.NotFound();
    assignments.MarkRecalled(user.UserName,fileName,user.DisplayName);
    return Results.Ok();
});

app.MapPost("/api/files/SubmittedFiles/return", IResult (ReturnFileRequest body, HttpRequest request, OfficeConfigurationService config, UserAccountService users, WorkAssignmentService assignments) =>
{
    var user = Auth(request, users); if (user is null) return Results.Unauthorized();
    if (user.Role is not (OfficeRole.Director or OfficeRole.Admin)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (string.IsNullOrWhiteSpace(body.DirectorMinute)) return Results.BadRequest("A Director's minute/instruction is required.");
    if(!config.ReturnForCorrection(user, body.OwnerUserName, body.FileName, body.DirectorMinute)) return Results.NotFound();
    assignments.MarkReturnedForCorrection(body.OwnerUserName,body.FileName,user.DisplayName,body.DirectorMinute);
    return Results.Ok();
});

app.MapDelete("/api/files/WorkingFiles", IResult (string fileName, HttpRequest request, OfficeConfigurationService config, UserAccountService users, FileRecoveryService recovery) =>
{
    var user=Auth(request,users); if(user is null) return Results.Unauthorized();
    var source=Path.Combine(config.FolderPathForUser(OfficeFolder.WorkingFiles,user),Path.GetFileName(fileName));
    if(!File.Exists(source))return Results.NotFound();
    recovery.Save(source,OfficeFolder.WorkingFiles,user.UserName,user.DisplayName,"Deleted",true);
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

app.MapPost("/api/files/SubmittedFiles/approve", IResult (string ownerUserName, string fileName, HttpRequest request, OfficeConfigurationService config, UserAccountService users, WorkAssignmentService assignments) =>
{
    var user = Auth(request, users);
    if (user is null) return Results.Unauthorized();
    if (user.Role is not (OfficeRole.Director or OfficeRole.Admin)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var assignment=assignments.List(user).FirstOrDefault(x=>x.AssignedToUserName.Equals(ownerUserName,StringComparison.OrdinalIgnoreCase) && x.SubmittedFileName==Path.GetFileName(fileName) && x.Status=="Submitted");
    if(!config.ApproveForDirector(ownerUserName, fileName, user.DisplayName, assignment?.RevisionCount ?? 0)) return Results.NotFound();
    assignments.MarkApproved(ownerUserName,fileName,user.DisplayName);
    return Results.Ok();
});

app.MapGet("/api/assignments", IResult (HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users); return caller is null?Results.Unauthorized():Results.Ok(assignments.List(caller));
});
app.MapGet("/api/assignments/{id:guid}/history", IResult (Guid id, HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users); return caller is null?Results.Unauthorized():Results.Ok(assignments.History(caller,id));
});
app.MapGet("/api/assignments/management", IResult (HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users);
    return caller is null?Results.Unauthorized():Results.Ok(assignments.Management(caller));
});
app.MapPost("/api/assignments/{id:guid}/management", IResult (Guid id, AssignmentManagement change, HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    return assignments.UpdateManagement(caller,id,change.Priority,change.Progress)?Results.Ok():Results.BadRequest();
});
app.MapPost("/api/assignments/{id:guid}/comments", IResult (Guid id, AssignmentComment change, HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    return assignments.AddComment(caller,id,change.Text)?Results.Ok():Results.BadRequest();
});
app.MapPost("/api/assignments/{id:guid}/deadline", IResult (Guid id, DeadlineChange change, HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    return assignments.ExtendDeadline(caller,id,change.DueAt)?Results.Ok():Results.BadRequest();
});
app.MapPost("/api/assignments/{id:guid}/reassign", IResult (Guid id, ReassignChange change, HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    var target=users.Users.FirstOrDefault(x=>x.Id==change.TargetUserId);
    return target is not null && assignments.Reassign(caller,id,target)?Results.Ok():Results.BadRequest();
});
app.MapGet("/api/assignments/{id:guid}/subtasks", IResult (Guid id, HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users);
    return caller is null?Results.Unauthorized():Results.Ok(assignments.Subtasks(caller,id));
});
app.MapPost("/api/assignments/{id:guid}/subtasks", IResult (Guid id, SubtaskChange change, HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    return assignments.AddSubtask(caller,id,change.Title)?Results.Ok():Results.BadRequest();
});
app.MapPost("/api/assignments/{id:guid}/subtasks/{subtaskId:guid}", IResult (Guid id, Guid subtaskId, SubtaskCompletion change, HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    return assignments.CompleteSubtask(caller,id,subtaskId,change.Done)?Results.Ok():Results.BadRequest();
});
app.MapPost("/api/assignments", async Task<IResult> (HttpRequest request, UserAccountService users, WorkAssignmentService assignments, IHubContext<OfficeChatHub> hub, OperationsService ops) =>
{
    var caller=Auth(request,users); if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    if(!request.HasFormContentType)return Results.BadRequest("Assignment details are required.");
    var form=await request.ReadFormAsync(); if(!Guid.TryParse(form["assignedToUserId"],out var targetId))return Results.BadRequest("Select a staff member.");
    var target=users.Users.FirstOrDefault(x=>x.Id==targetId && x.Role is OfficeRole.Editor or OfficeRole.NewsSourcing); if(target is null)return Results.BadRequest("Assignments can be given to Editors or News Sourcing staff.");
    var title=form["title"].ToString(); var notes=form["instructions"].ToString(); if(string.IsNullOrWhiteSpace(title))return Results.BadRequest("A work title is required.");
    DateTimeOffset? due=null; if(DateTimeOffset.TryParse(form["dueAt"],out var parsedDue))due=parsedDue;
    var file=form.Files.FirstOrDefault(); var item=assignments.Create(caller,target,title,notes,due,file is null?null:Path.GetFileName(file.FileName));
    if(file is not null){var path=Path.Combine(assignments.AttachmentFolder(item.Id),Path.GetFileName(file.FileName)); await using var stream=File.Create(path); await file.CopyToAsync(stream);}
    ops.Notify(target.Id,"New Work Assigned",$"{caller.DisplayName}: {item.Title}");
    ops.Audit(caller,"Assignment Created",item.Id.ToString());
    await hub.Clients.Group($"user:{target.Id}").SendAsync("AssignmentNotification",item.Id,item.Title,caller.DisplayName,item.Instructions);
    return Results.Ok(item);
});
app.MapGet("/api/assignments/{id:guid}/attachment", IResult (Guid id, HttpRequest request, UserAccountService users, WorkAssignmentService assignments) =>
{
    var caller=Auth(request,users); if(caller is null)return Results.Unauthorized(); var item=assignments.List(caller).FirstOrDefault(x=>x.Id==id);
    if(item is null || string.IsNullOrWhiteSpace(item.FileName))return Results.NotFound(); var path=Path.Combine(assignments.AttachmentFolder(id),Path.GetFileName(item.FileName)); return File.Exists(path)?Results.File(path,"application/octet-stream",item.FileName):Results.NotFound();
});
app.MapPost("/api/assignments/{id:guid}/complete", async Task<IResult> (Guid id, HttpRequest request, UserAccountService users, WorkAssignmentService assignments, IHubContext<OfficeChatHub> hub, OperationsService ops) =>
{
    var caller=Auth(request,users); if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Editor or OfficeRole.NewsSourcing))return Results.StatusCode(StatusCodes.Status403Forbidden);
    if(!request.HasFormContentType)return Results.BadRequest("Choose the finished work file to submit.");
    var form=await request.ReadFormAsync(); var upload=form.Files.FirstOrDefault();
    if(upload is null || upload.Length==0)return Results.BadRequest("Choose the finished work file to submit.");
    WorkAssignment? item;
    await using(var input=upload.OpenReadStream()) item=assignments.CompleteAndSubmit(caller,id,upload.FileName,input);
    if(item is null)return Results.NotFound();
    foreach(var manager in users.Users.Where(x=>x.Role is OfficeRole.Director or OfficeRole.Admin))ops.Notify(manager.Id,"Work Submitted",$"{caller.DisplayName}: {item.Title}");
    ops.Audit(caller,"Assignment Submitted",item.Id.ToString());
    await hub.Clients.Group("role:Director").SendAsync("AssignmentCompleted",item.Id,item.Title,caller.DisplayName,item.CompletedAt);
    await hub.Clients.Group("role:Admin").SendAsync("AssignmentCompleted",item.Id,item.Title,caller.DisplayName,item.CompletedAt);
    return Results.Ok(item);
});

app.MapGet("/api/operations/audit",IResult (HttpRequest request,UserAccountService users,OperationsService ops)=>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    return caller.Role is OfficeRole.Director or OfficeRole.Admin?Results.Ok(ops.Audits()):Results.StatusCode(StatusCodes.Status403Forbidden);
});
app.MapGet("/api/operations/notifications",IResult (HttpRequest request,UserAccountService users,OperationsService ops)=>
{
    var caller=Auth(request,users);
    return caller is null?Results.Unauthorized():Results.Ok(ops.Notifications(caller.Id));
});
app.MapPost("/api/operations/notifications/{id:guid}/read",IResult (Guid id,HttpRequest request,UserAccountService users,OperationsService ops)=>
{
    var caller=Auth(request,users);
    return caller is null?Results.Unauthorized():ops.MarkRead(caller.Id,id)?Results.Ok():Results.NotFound();
});
app.MapPost("/api/operations/backup",IResult (HttpRequest request,UserAccountService users,OperationsService ops)=>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    var path=ops.Backup();ops.Audit(caller,"Database Backup",Path.GetFileName(path));
    return Results.Ok(new {fileName=Path.GetFileName(path),createdAt=DateTimeOffset.UtcNow});
});
app.MapGet("/api/operations/archives",IResult (HttpRequest request,UserAccountService users,OfficeArchiveService archives)=>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    return caller.Role is OfficeRole.Director or OfficeRole.Admin?Results.Ok(archives.List()):Results.StatusCode(StatusCodes.Status403Forbidden);
});
app.MapPost("/api/operations/archives",IResult (HttpRequest request,UserAccountService users,OfficeArchiveService archives,OperationsService ops)=>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    try
    {
        var name=archives.Create();
        ops.Audit(caller,"Office Archive Created",name);
        return Results.Ok(new {fileName=name});
    }
    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
        return Results.Problem("Backup could not complete. Save and close office files, check available disk space and server folder permissions, then retry.",statusCode:409);
    }
});
app.MapGet("/api/operations/archives/{name}",IResult (string name,HttpRequest request,UserAccountService users,OfficeArchiveService archives)=>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    var path=archives.Find(name);
    return path is null?Results.NotFound():Results.File(path,"application/zip",name);
});
app.MapGet("/api/filing",IResult (HttpRequest request,UserAccountService users,FilingService filing)=>
{
    var caller=Auth(request,users);
    return caller is null?Results.Unauthorized():Results.Ok(filing.List(caller.Role is OfficeRole.Director or OfficeRole.Admin));
});
app.MapPost("/api/filing",IResult (FilingRequest item,HttpRequest request,UserAccountService users,FilingService filing,OperationsService ops)=>
{
    var caller=Auth(request,users);
    if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    try
    {
        filing.Add(item.Reference,item.Title,item.Category,item.Department,item.Confidential);
        ops.Audit(caller,"Filing Registered",item.Reference);
        return Results.Ok();
    }
    catch(ArgumentException ex){return Results.BadRequest(ex.Message);}
    catch(Microsoft.Data.Sqlite.SqliteException){return Results.Conflict("Filing reference already exists.");}
});
app.MapGet("/api/production",IResult (HttpRequest request,UserAccountService users,ProductionTrackerService service)=>
{
    return Auth(request,users) is null?Results.Unauthorized():Results.Ok(service.List());
});
app.MapPost("/api/production",IResult (ProductionItem item,HttpRequest request,UserAccountService users,ProductionTrackerService service,OperationsService ops)=>
{
    var caller=Auth(request,users);if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    try{var saved=service.Add(item);ops.Audit(caller,"News Production Created",saved.Id.ToString());return Results.Ok(saved);}
    catch(ArgumentException ex){return Results.BadRequest(ex.Message);}
});
app.MapGet("/api/meeting-minutes",IResult (HttpRequest request,UserAccountService users,MeetingMinutesService service)=>
{
    return Auth(request,users) is null?Results.Unauthorized():Results.Ok(service.List());
});
app.MapPost("/api/meeting-minutes",IResult (MeetingMinute item,HttpRequest request,UserAccountService users,MeetingMinutesService service,OperationsService ops)=>
{
    var caller=Auth(request,users);if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    try{var saved=service.Add(item);ops.Audit(caller,"Meeting Minutes Created",saved.Id.ToString());return Results.Ok(saved);}
    catch(ArgumentException ex){return Results.BadRequest(ex.Message);}
});
app.MapGet("/api/equipment",IResult (HttpRequest request,UserAccountService users,EquipmentRegisterService service)=>
{
    return Auth(request,users) is null?Results.Unauthorized():Results.Ok(service.List());
});
app.MapPost("/api/equipment",IResult (EquipmentItem item,HttpRequest request,UserAccountService users,EquipmentRegisterService service,OperationsService ops)=>
{
    var caller=Auth(request,users);if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    try{var saved=service.Add(item);ops.Audit(caller,"Equipment Registered",saved.AssetTag);return Results.Ok(saved);}
    catch(ArgumentException ex){return Results.BadRequest(ex.Message);}
    catch(Microsoft.Data.Sqlite.SqliteException){return Results.Conflict("Asset tag already exists.");}
});
app.MapPut("/api/production/{id:guid}",IResult (Guid id,ProductionItem item,HttpRequest request,UserAccountService users,ProductionTrackerService service,OperationsService ops)=>
{
    var caller=Auth(request,users);if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    try
    {
        var saved=service.Update(id,item);
        if(saved is null)return Results.NotFound();
        ops.Audit(caller,"News Production Updated",id.ToString());return Results.Ok(saved);
    }
    catch(ArgumentException ex){return Results.BadRequest(ex.Message);}
    catch(Microsoft.Data.Sqlite.SqliteException){return Results.Conflict("A record with these details already exists.");}
});
app.MapPut("/api/meeting-minutes/{id:guid}",IResult (Guid id,MeetingMinute item,HttpRequest request,UserAccountService users,MeetingMinutesService service,OperationsService ops)=>
{
    var caller=Auth(request,users);if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    try
    {
        var saved=service.Update(id,item);
        if(saved is null)return Results.NotFound();
        ops.Audit(caller,"Meeting Minutes Updated",id.ToString());return Results.Ok(saved);
    }
    catch(ArgumentException ex){return Results.BadRequest(ex.Message);}
    catch(Microsoft.Data.Sqlite.SqliteException){return Results.Conflict("A record with these details already exists.");}
});
app.MapPut("/api/equipment/{id:guid}",IResult (Guid id,EquipmentItem item,HttpRequest request,UserAccountService users,EquipmentRegisterService service,OperationsService ops)=>
{
    var caller=Auth(request,users);if(caller is null)return Results.Unauthorized();
    if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return Results.StatusCode(StatusCodes.Status403Forbidden);
    try
    {
        var saved=service.Update(id,item);
        if(saved is null)return Results.NotFound();
        ops.Audit(caller,"Equipment Updated",id.ToString());return Results.Ok(saved);
    }
    catch(ArgumentException ex){return Results.BadRequest(ex.Message);}
    catch(Microsoft.Data.Sqlite.SqliteException){return Results.Conflict("A record with these details already exists.");}
});
app.MapHub<OfficeChatHub>("/hubs/chat");
app.Run("http://0.0.0.0:5077");

record AssignmentComment(string Text);

record DeadlineChange(DateTimeOffset DueAt);
record ReassignChange(Guid TargetUserId);
record SubtaskChange(string Title);
record SubtaskCompletion(bool Done);

record FilingRequest(string Reference,string Title,string Category,string Department,bool Confidential);
