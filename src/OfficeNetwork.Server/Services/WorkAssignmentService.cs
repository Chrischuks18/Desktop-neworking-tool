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
        FileName TEXT NULL, AssignedAt TEXT NOT NULL, DueAt TEXT NULL, Status TEXT NOT NULL, CompletedAt TEXT NULL, SubmittedFileName TEXT NULL, RevisionCount INTEGER NOT NULL DEFAULT 0, LastActionAt TEXT NULL);";
        q.ExecuteNonQuery();
        foreach (var sql in new[]
        {
            "ALTER TABLE WorkAssignments ADD COLUMN SubmittedFileName TEXT NULL",
            "ALTER TABLE WorkAssignments ADD COLUMN RevisionCount INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE WorkAssignments ADD COLUMN LastActionAt TEXT NULL"
        })
        {
            try { using var alter=c.CreateCommand(); alter.CommandText=sql; alter.ExecuteNonQuery(); }
            catch (SqliteException) { }
        }
    }
    public WorkAssignment Create(OfficeUser actor, OfficeUser target, string title, string instructions, DateTimeOffset? dueAt, string? fileName)
    {
        var a=new WorkAssignment(Guid.NewGuid(),target.Id,target.UserName,target.DisplayName,actor.Id,actor.DisplayName,title.Trim(),instructions.Trim(),fileName,DateTimeOffset.UtcNow,dueAt,"Pending");
        using var c=new SqliteConnection(_connectionString); c.Open(); using var q=c.CreateCommand();
        q.CommandText = @"INSERT INTO WorkAssignments
        (Id,AssignedToUserId,AssignedToUserName,AssignedToDisplayName,AssignedByUserId,AssignedByDisplayName,Title,Instructions,FileName,AssignedAt,DueAt,Status,CompletedAt,SubmittedFileName,RevisionCount,LastActionAt)
        VALUES($id,$tid,$tun,$tdn,$bid,$bdn,$title,$notes,$file,$at,$due,$status,NULL,NULL,0,$at)";
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
        using var r=q.ExecuteReader(); var list=new List<WorkAssignment>(); while(r.Read()) list.Add(Reconcile(Read(r))); return list;
    }
    public WorkAssignment? CompleteAndSubmit(OfficeUser caller, Guid id, string completedFileName, Stream completedFile)
    {
        var item=List(caller).FirstOrDefault(x=>x.Id==id && x.AssignedToUserId==caller.Id && x.Status is "Pending" or "Correction Required");
        if(item is null)return null;
        var safeName=Path.GetFileName(completedFileName);
        if(string.IsNullOrWhiteSpace(safeName))return null;
        var submittedFolder=_config.FolderPathForUser(OfficeFolder.SubmittedFiles, caller);
        Directory.CreateDirectory(submittedFolder);
        var destination=UniquePath(submittedFolder,safeName);
        using(var output=File.Create(destination)) completedFile.CopyTo(output);
        var storedName=Path.GetFileName(destination);
        var done=DateTimeOffset.UtcNow;
        using var c=new SqliteConnection(_connectionString); c.Open(); using var q=c.CreateCommand();
        q.CommandText="UPDATE WorkAssignments SET Status='Submitted', CompletedAt=$done, SubmittedFileName=$file, RevisionCount=RevisionCount+1, LastActionAt=$done WHERE Id=$id";
        q.Parameters.AddWithValue("$done",done.ToString("O")); q.Parameters.AddWithValue("$file",storedName); q.Parameters.AddWithValue("$id",id.ToString()); q.ExecuteNonQuery();
        return item with {Status="Submitted",CompletedAt=done};
    }
    public void MarkReturnedForCorrection(string ownerUserName, string fileName)
    {
        UpdateWorkflowStatus(ownerUserName,fileName,"Correction Required");
    }

    public void MarkApproved(string ownerUserName, string fileName)
    {
        UpdateWorkflowStatus(ownerUserName,fileName,"Approved");
    }

    private void UpdateWorkflowStatus(string ownerUserName,string fileName,string status)
    {
        using var c=new SqliteConnection(_connectionString); c.Open(); using var q=c.CreateCommand();
        q.CommandText=@"UPDATE WorkAssignments SET Status=$status, LastActionAt=$at
                        WHERE Id=(SELECT Id FROM WorkAssignments
                        WHERE AssignedToUserName=$owner AND SubmittedFileName=$file AND Status='Submitted'
                        ORDER BY CompletedAt DESC LIMIT 1)";
        q.Parameters.AddWithValue("$status",status); q.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));
        q.Parameters.AddWithValue("$owner",ownerUserName); q.Parameters.AddWithValue("$file",Path.GetFileName(fileName)); q.ExecuteNonQuery();
    }

    private static string UniquePath(string folder,string fileName)
    {
        var path=Path.Combine(folder,fileName); if(!File.Exists(path))return path;
        var stem=Path.GetFileNameWithoutExtension(fileName); var ext=Path.GetExtension(fileName); var i=2;
        do { path=Path.Combine(folder,$"{stem} ({i++}){ext}"); } while(File.Exists(path));
        return path;
    }
    public string AttachmentFolder(Guid assignmentId){var p=Path.Combine(_config.Configuration.RootPath,"Assigned Work",assignmentId.ToString("N"));Directory.CreateDirectory(p);return p;}
    private WorkAssignment Reconcile(WorkAssignment item)
    {
        if(string.IsNullOrWhiteSpace(item.SubmittedFileName) || item.Status is not ("Submitted" or "Correction Required")) return item;
        var owner=string.Concat(item.AssignedToUserName.Where(ch=>char.IsLetterOrDigit(ch)||ch is '-' or '_'));
        var file=Path.GetFileName(item.SubmittedFileName);
        var working=Path.Combine(_config.Configuration.RootPath,"Working Files",owner,file);
        var submitted=Path.Combine(_config.Configuration.RootPath,"Submitted Files",owner,file);
        var final=Path.Combine(_config.Configuration.RootPath,"Final Files",file);
        var actual=File.Exists(final)?"Approved":File.Exists(working)?"Correction Required":File.Exists(submitted)?"Submitted":item.Status;
        if(actual==item.Status)return item;
        var at=DateTimeOffset.UtcNow;
        using var c=new SqliteConnection(_connectionString);c.Open();using var q=c.CreateCommand();
        q.CommandText="UPDATE WorkAssignments SET Status=$status,LastActionAt=$at WHERE Id=$id";
        q.Parameters.AddWithValue("$status",actual);q.Parameters.AddWithValue("$at",at.ToString("O"));q.Parameters.AddWithValue("$id",item.Id.ToString());q.ExecuteNonQuery();
        return item with {Status=actual,LastActionAt=at};
    }
    private static WorkAssignment Read(SqliteDataReader r)=>new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetString(3),Guid.Parse(r.GetString(4)),r.GetString(5),r.GetString(6),r.GetString(7),r.IsDBNull(8)?null:r.GetString(8),DateTimeOffset.Parse(r.GetString(9)),r.IsDBNull(10)?null:DateTimeOffset.Parse(r.GetString(10)),r.GetString(11),r.IsDBNull(12)?null:DateTimeOffset.Parse(r.GetString(12)),r.IsDBNull(13)?null:r.GetString(13),r.IsDBNull(14)?0:r.GetInt32(14),r.IsDBNull(15)?null:DateTimeOffset.Parse(r.GetString(15)));
}