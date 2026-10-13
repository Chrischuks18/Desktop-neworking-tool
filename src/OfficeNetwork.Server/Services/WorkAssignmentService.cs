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
        using(var events=c.CreateCommand())
        {
            events.CommandText=@"CREATE TABLE IF NOT EXISTS AssignmentWorkflowEvents(
            Id TEXT PRIMARY KEY, AssignmentId TEXT NOT NULL, At TEXT NOT NULL, Action TEXT NOT NULL, ActorName TEXT NOT NULL, FileName TEXT NULL, Note TEXT NULL);";
            events.ExecuteNonQuery();
        }
        using(var meta=c.CreateCommand()) { meta.CommandText=@"CREATE TABLE IF NOT EXISTS AssignmentManagement(AssignmentId TEXT PRIMARY KEY,Priority TEXT NOT NULL DEFAULT 'Normal',Progress INTEGER NOT NULL DEFAULT 0); CREATE TABLE IF NOT EXISTS AssignmentSubtasks(Id TEXT PRIMARY KEY,AssignmentId TEXT NOT NULL,Title TEXT NOT NULL,IsDone INTEGER NOT NULL DEFAULT 0);"; meta.ExecuteNonQuery(); }
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
        AddEvent(a.Id,"Assigned",actor.DisplayName,fileName,instructions);
        return a;
    }
    public IReadOnlyList<WorkAssignment> List(OfficeUser caller)
    {
        using var c=new SqliteConnection(_connectionString); c.Open(); using var q=c.CreateCommand();
        q.CommandText=caller.Role is OfficeRole.Director or OfficeRole.Admin ? "SELECT * FROM WorkAssignments ORDER BY AssignedAt DESC" : "SELECT * FROM WorkAssignments WHERE AssignedToUserId=$uid ORDER BY CASE Status WHEN 'Pending' THEN 0 ELSE 1 END, AssignedAt DESC";
        if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin)) q.Parameters.AddWithValue("$uid",caller.Id.ToString());
        using var r=q.ExecuteReader(); var list=new List<WorkAssignment>(); while(r.Read()) list.Add(Reconcile(Read(r))); return list;
    }
    public IReadOnlyList<AssignmentManagement> Management(OfficeUser caller)
    {
        var visible=List(caller).Select(x=>x.Id).ToHashSet();
        using var c=new SqliteConnection(_connectionString);c.Open();
        using var q=c.CreateCommand();q.CommandText="SELECT AssignmentId,Priority,Progress FROM AssignmentManagement";
        using var r=q.ExecuteReader();var items=new List<AssignmentManagement>();
        while(r.Read())if(Guid.TryParse(r.GetString(0),out var id)&&visible.Contains(id))items.Add(new(id,r.GetString(1),r.GetInt32(2)));
        return items;
    }
    public bool UpdateManagement(OfficeUser caller,Guid id,string priority,int progress)
    {
        if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin))return false;
        if(!List(caller).Any(x=>x.Id==id))return false;
        if(priority is not ("Urgent" or "High" or "Normal" or "Low") || progress<0 || progress>100)return false;
        using var c=new SqliteConnection(_connectionString);c.Open();using var q=c.CreateCommand();
        q.CommandText="INSERT INTO AssignmentManagement(AssignmentId,Priority,Progress) VALUES($id,$priority,$progress) ON CONFLICT(AssignmentId) DO UPDATE SET Priority=excluded.Priority,Progress=excluded.Progress";
        q.Parameters.AddWithValue("$id",id.ToString());q.Parameters.AddWithValue("$priority",priority);q.Parameters.AddWithValue("$progress",progress);q.ExecuteNonQuery();
        AddEvent(id,"Management Updated",caller.DisplayName,null,$"Priority: {priority}; progress: {progress}%");
        return true;
    }
    public bool ExtendDeadline(OfficeUser caller,Guid id,DateTimeOffset due)
    {
        if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin) || !List(caller).Any(x=>x.Id==id))return false;
        using var c=new SqliteConnection(_connectionString);c.Open();using var q=c.CreateCommand();
        q.CommandText="UPDATE WorkAssignments SET DueAt=$due,LastActionAt=$at WHERE Id=$id";
        q.Parameters.AddWithValue("$due",due.ToString("O"));q.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));q.Parameters.AddWithValue("$id",id.ToString());q.ExecuteNonQuery();
        AddEvent(id,"Deadline Changed",caller.DisplayName,null,due.ToString("O"));return true;
    }
    public bool Reassign(OfficeUser caller,Guid id,OfficeUser target)
    {
        if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin) || target.Role is not (OfficeRole.Editor or OfficeRole.NewsSourcing))return false;
        var item=List(caller).FirstOrDefault(x=>x.Id==id);
        if(item is null || item.Status is "Approved" or "Completed" or "Submitted")return false;
        using var c=new SqliteConnection(_connectionString);c.Open();using var q=c.CreateCommand();
        q.CommandText="UPDATE WorkAssignments SET AssignedToUserId=$uid,AssignedToUserName=$name,AssignedToDisplayName=$display,LastActionAt=$at WHERE Id=$id";
        q.Parameters.AddWithValue("$uid",target.Id.ToString());q.Parameters.AddWithValue("$name",target.UserName);q.Parameters.AddWithValue("$display",target.DisplayName);q.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));q.Parameters.AddWithValue("$id",id.ToString());q.ExecuteNonQuery();
        AddEvent(id,"Reassigned",caller.DisplayName,null,$"From {item.AssignedToDisplayName} to {target.DisplayName}");return true;
    }
    public bool AddComment(OfficeUser caller,Guid id,string comment)
    {
        if(string.IsNullOrWhiteSpace(comment)||comment.Length>4000||!List(caller).Any(x=>x.Id==id))return false;
        AddEvent(id,"Comment",caller.DisplayName,null,comment.Trim());return true;
    }
    public IReadOnlyList<AssignmentSubtask> Subtasks(OfficeUser caller,Guid id)
    {
        if(!List(caller).Any(x=>x.Id==id))return [];
        using var c=new SqliteConnection(_connectionString);c.Open();using var q=c.CreateCommand();
        q.CommandText="SELECT Id,AssignmentId,Title,IsDone FROM AssignmentSubtasks WHERE AssignmentId=$id ORDER BY rowid";
        q.Parameters.AddWithValue("$id",id.ToString());using var r=q.ExecuteReader();var result=new List<AssignmentSubtask>();
        while(r.Read())result.Add(new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetInt32(3)!=0));return result;
    }
    public bool AddSubtask(OfficeUser caller,Guid id,string title)
    {
        if(caller.Role is not (OfficeRole.Director or OfficeRole.Admin)||string.IsNullOrWhiteSpace(title)||title.Length>250||!List(caller).Any(x=>x.Id==id))return false;
        using var c=new SqliteConnection(_connectionString);c.Open();using var q=c.CreateCommand();
        q.CommandText="INSERT INTO AssignmentSubtasks(Id,AssignmentId,Title,IsDone) VALUES($sid,$id,$title,0)";
        q.Parameters.AddWithValue("$sid",Guid.NewGuid().ToString());q.Parameters.AddWithValue("$id",id.ToString());q.Parameters.AddWithValue("$title",title.Trim());q.ExecuteNonQuery();
        AddEvent(id,"Subtask Added",caller.DisplayName,null,title.Trim());return true;
    }
    public bool CompleteSubtask(OfficeUser caller,Guid id,Guid subtaskId,bool done)
    {
        if(!List(caller).Any(x=>x.Id==id))return false;
        using var c=new SqliteConnection(_connectionString);c.Open();using var q=c.CreateCommand();
        q.CommandText="UPDATE AssignmentSubtasks SET IsDone=$done WHERE Id=$sid AND AssignmentId=$id";
        q.Parameters.AddWithValue("$done",done?1:0);q.Parameters.AddWithValue("$sid",subtaskId.ToString());q.Parameters.AddWithValue("$id",id.ToString());
        if(q.ExecuteNonQuery()==0)return false;
        AddEvent(id,done?"Subtask Completed":"Subtask Reopened",caller.DisplayName,null,subtaskId.ToString());return true;
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
        AddEvent(id,item.RevisionCount==0?"Submitted":"Resubmitted",caller.DisplayName,storedName,item.RevisionCount==0?"Initial submission":"Corrected version submitted");
        return item with {Status="Submitted",CompletedAt=done,SubmittedFileName=storedName,RevisionCount=item.RevisionCount+1,LastActionAt=done};
    }
    public void MarkReturnedForCorrection(string ownerUserName, string fileName, string actorName, string? note)
    {
        UpdateWorkflowStatus(ownerUserName,fileName,"Correction Required",actorName,"Returned for Correction",note,false);
    }

    public void MarkApproved(string ownerUserName, string fileName, string actorName)
    {
        UpdateWorkflowStatus(ownerUserName,fileName,"Approved",actorName,"Approved",null,false);
    }

    public void MarkResubmitted(string ownerUserName, string fileName, string actorName)
    {
        UpdateWorkflowStatus(ownerUserName,fileName,"Submitted",actorName,"Resubmitted",null,true);
    }

    public void MarkRecalled(string ownerUserName, string fileName, string actorName)
    {
        UpdateWorkflowStatus(ownerUserName,fileName,"Pending",actorName,"Recalled",null,false);
    }

    private void UpdateWorkflowStatus(string ownerUserName,string fileName,string status,string actorName,string action,string? note,bool incrementRevision)
    {
        var safeFile=Path.GetFileName(fileName);
        using var c=new SqliteConnection(_connectionString); c.Open(); using var find=c.CreateCommand();
        find.CommandText=@"SELECT Id FROM WorkAssignments
                           WHERE AssignedToUserName=$owner AND SubmittedFileName=$file
                           AND Status IN ('Submitted','Correction Required')
                           ORDER BY CompletedAt DESC LIMIT 1";
        find.Parameters.AddWithValue("$owner",ownerUserName); find.Parameters.AddWithValue("$file",safeFile);
        var idText=find.ExecuteScalar()?.ToString();
        if(!Guid.TryParse(idText,out var assignmentId))return;
        using var q=c.CreateCommand();
        q.CommandText=incrementRevision
            ?"UPDATE WorkAssignments SET Status=$status, RevisionCount=RevisionCount+1, CompletedAt=$at, LastActionAt=$at WHERE Id=$id"
            :"UPDATE WorkAssignments SET Status=$status, LastActionAt=$at WHERE Id=$id";
        var at=DateTimeOffset.UtcNow;
        q.Parameters.AddWithValue("$status",status); q.Parameters.AddWithValue("$at",at.ToString("O")); q.Parameters.AddWithValue("$id",assignmentId.ToString()); q.ExecuteNonQuery();
        AddEvent(assignmentId,action,actorName,safeFile,note);
    }

    public IReadOnlyList<AssignmentWorkflowEvent> History(OfficeUser caller, Guid assignmentId)
    {
        var assignment=List(caller).FirstOrDefault(x=>x.Id==assignmentId);
        if(assignment is null)return [];
        using var c=new SqliteConnection(_connectionString);c.Open();using var q=c.CreateCommand();
        q.CommandText="SELECT Id,AssignmentId,At,Action,ActorName,FileName,Note FROM AssignmentWorkflowEvents WHERE AssignmentId=$id ORDER BY At";
        q.Parameters.AddWithValue("$id",assignmentId.ToString());
        using var r=q.ExecuteReader();var list=new List<AssignmentWorkflowEvent>();
        while(r.Read())list.Add(new AssignmentWorkflowEvent(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),DateTimeOffset.Parse(r.GetString(2)),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetString(6)));
        return list;
    }

    private void AddEvent(Guid assignmentId,string action,string actorName,string? fileName,string? note)
    {
        using var c=new SqliteConnection(_connectionString);c.Open();using var q=c.CreateCommand();
        q.CommandText="INSERT INTO AssignmentWorkflowEvents(Id,AssignmentId,At,Action,ActorName,FileName,Note) VALUES($eid,$aid,$at,$action,$actor,$file,$note)";
        q.Parameters.AddWithValue("$eid",Guid.NewGuid().ToString());q.Parameters.AddWithValue("$aid",assignmentId.ToString());q.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));
        q.Parameters.AddWithValue("$action",action);q.Parameters.AddWithValue("$actor",actorName);q.Parameters.AddWithValue("$file",(object?)fileName??DBNull.Value);q.Parameters.AddWithValue("$note",(object?)note??DBNull.Value);q.ExecuteNonQuery();
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