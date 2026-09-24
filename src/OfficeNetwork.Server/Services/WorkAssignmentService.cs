using Microsoft.Data.Sqlite;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Services;

public sealed class WorkAssignmentService
{
    private readonly string _connectionString;
    private readonly OfficeConfigurationService _config;
    public WorkAssignmentService(OfficeConfigurationService config)
    {
        _config=config;
        var dataDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network");
        Directory.CreateDirectory(dataDir);
        _connectionString=$"Data Source={Path.Combine(dataDir,"choiceflame.db")}";
        Initialize();
    }
    private void Initialize()
    {
        using var c=new SqliteConnection(_connectionString); c.Open(); using var q=c.CreateCommand();
        q.CommandText = @"CREATE TABLE IF NOT EXISTS WorkAssignments(
        Id TEXT PRIMARY KEY, AssignedToUserId TEXT NOT NULL, AssignedToUserName TEXT NOT NULL, AssignedToDisplayName TEXT NOT NULL,
        AssignedByUserId TEXT NOT NULL, AssignedByDisplayName TEXT NOT NULL, Title TEXT NOT NULL, Instructions TEXT NOT NULL,
        FileName TEXT NULL, AssignedAt TEXT NOT NULL, DueAt TEXT NULL, Status TEXT NOT NULL, CompletedAt TEXT NULL);";
        q.ExecuteNonQuery();
    }
    public WorkAssignment Create(OfficeUser actor, OfficeUser target, string title, string instructions, DateTimeOffset? dueAt, string? fileName)
    {
        var a=new WorkAssignment(Guid.NewGuid(),target.Id,target.UserName,target.DisplayName,actor.Id,actor.DisplayName,title.Trim(),instructions.Trim(),fileName,DateTimeOffset.UtcNow,dueAt,"Pending");
        using var c=new SqliteConnection(_connectionString); c.Open(); using var q=c.CreateCommand();
        q.CommandText = "INSERT INTO WorkAssignments VALUES($id,$tid,$tun,$tdn,$bid,$bdn,$title,$notes,$file,$at,$due,$status,NULL)";
        q.Parameters.AddWithValue("$id",a.Id.ToString()); q.Parameters.AddWithValue("$tid",target.Id.ToString()); q.Parameters.AddWithValue("$tun",target.UserName); q.Parameters.AddWithValue("$tdn",target.DisplayName);
        q.Parameters.AddWithValue("$bid",actor.Id.ToString()); q.Parameters.AddWithValue("$bdn",actor.DisplayName); q.Parameters.AddWithValue("$title",a.Title); q.Parameters.AddWithValue("$notes",a.Instructions);
        q.Parameters.AddWithValue("$file",(object?)fileName??DBNull.Value); q.Parameters.AddWithValue("$at",a.AssignedAt.ToString("O")); q.Parameters.AddWithValue("$due",(object?)dueAt?.ToString("O")??DBNull.Value); q.Parameters.AddWithValue("$status","Pending"); q.ExecuteNonQuery();
        return a;
    }
    public IReadOnlyList<WorkAssignment> List(OfficeUser caller)
    {
        using var c=new SqliteConnection(_connectionString); c.Open(); using var q=c.CreateCommand();
        q.CommandText=caller.Role is OfficeRole.Director or OfficeRole.Admin ? "SELECT * FROM WorkAssignments ORDER BY AssignedAt DESC" : "SELECT * FROM WorkAssignments WHERE AssignedToUserId=$uid ORDER BY CASE Status WHEN 'Pending' THEN 0 ELSE 1 END, AssignedAt DESC";
        if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin)) q.Parameters.AddWithValue("$uid",caller.Id.ToString());
        using var r=q.ExecuteReader(); var list=new List<WorkAssignment>(); while(r.Read()) list.Add(Read(r)); return list;
    }
    public WorkAssignment? CompleteAndSubmit(OfficeUser caller, Guid id, string completedFileName, Stream completedFile)
    {
        var item=List(caller).FirstOrDefault(x=>x.Id==id && x.AssignedToUserId==caller.Id && x.Status=="Pending");
        if(item is null)return null;
        var safeName=Path.GetFileName(completedFileName);
        if(string.IsNullOrWhiteSpace(safeName))return null;
        var submittedFolder=_config.FolderPathForUser(OfficeFolder.SubmittedFiles, caller);
        Directory.CreateDirectory(submittedFolder);
        var destination=UniquePath(submittedFolder,safeName);
        using(var output=File.Create(destination)) completedFile.CopyTo(output);
        var done=DateTimeOffset.UtcNow;
        using var c=new SqliteConnection(_connectionString); c.Open(); using var q=c.CreateCommand();
        q.CommandText="UPDATE WorkAssignments SET Status='Submitted', CompletedAt=$done WHERE Id=$id";
        q.Parameters.AddWithValue("$done",done.ToString("O")); q.Parameters.AddWithValue("$id",id.ToString()); q.ExecuteNonQuery();
        return item with {Status="Submitted",CompletedAt=done};
    }
    private static string UniquePath(string folder,string fileName)
    {
        var path=Path.Combine(folder,fileName); if(!File.Exists(path))return path;
        var stem=Path.GetFileNameWithoutExtension(fileName); var ext=Path.GetExtension(fileName); var i=2;
        do { path=Path.Combine(folder,$"{stem} ({i++}){ext}"); } while(File.Exists(path));
        return path;
    }
    public string AttachmentFolder(Guid assignmentId){var p=Path.Combine(_config.Configuration.RootPath,"Assigned Work",assignmentId.ToString("N"));Directory.CreateDirectory(p);return p;}
    private static WorkAssignment Read(SqliteDataReader r)=>new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetString(3),Guid.Parse(r.GetString(4)),r.GetString(5),r.GetString(6),r.GetString(7),r.IsDBNull(8)?null:r.GetString(8),DateTimeOffset.Parse(r.GetString(9)),r.IsDBNull(10)?null:DateTimeOffset.Parse(r.GetString(10)),r.GetString(11),r.IsDBNull(12)?null:DateTimeOffset.Parse(r.GetString(12)));
}