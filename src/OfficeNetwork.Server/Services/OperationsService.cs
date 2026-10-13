using Microsoft.Data.Sqlite;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Services;

// Additive storage: no existing user, assignment or file tables are modified.
public sealed class OperationsService
{
    private readonly string _database;
    private readonly string _folder;
    public string DataFolder => _folder;
    public OperationsService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network")) { }
    public OperationsService(string dataFolder)
    {
        _folder=Path.GetFullPath(dataFolder);
        Directory.CreateDirectory(_folder);
        _database=Path.Combine(_folder,"choiceflame.db");
        using var c=Open();
        using var q=c.CreateCommand();
        q.CommandText=@"CREATE TABLE IF NOT EXISTS OfficeAudit(Id TEXT PRIMARY KEY, At TEXT NOT NULL, ActorId TEXT NOT NULL, Action TEXT NOT NULL, Details TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS OfficeNotifications(Id TEXT PRIMARY KEY, UserId TEXT NOT NULL, At TEXT NOT NULL, Title TEXT NOT NULL, Message TEXT NOT NULL, IsRead INTEGER NOT NULL DEFAULT 0);";
        q.ExecuteNonQuery();
    }
    private SqliteConnection Open(){var c=new SqliteConnection($"Data Source={_database}");c.Open();return c;}
    public void Audit(OfficeUser actor,string action,string details)
    {
        using var c=Open();using var q=c.CreateCommand();
        q.CommandText="INSERT INTO OfficeAudit VALUES($id,$at,$actor,$action,$details)";
        q.Parameters.AddWithValue("$id",Guid.NewGuid().ToString());q.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));q.Parameters.AddWithValue("$actor",actor.Id.ToString());q.Parameters.AddWithValue("$action",action);q.Parameters.AddWithValue("$details",details.Length>1000?details[..1000]:details);q.ExecuteNonQuery();
    }
    public IReadOnlyList<OfficeAuditEntry> Audits()
    {
        using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT Id,At,ActorId,Action,Details FROM OfficeAudit ORDER BY At DESC LIMIT 500";
        using var r=q.ExecuteReader();var items=new List<OfficeAuditEntry>();
        while(r.Read())items.Add(new(Guid.Parse(r.GetString(0)),DateTimeOffset.Parse(r.GetString(1)),Guid.Parse(r.GetString(2)),r.GetString(3),r.GetString(4)));return items;
    }
    public void Notify(Guid userId,string title,string message)
    {
        using var c=Open();using var q=c.CreateCommand();
        q.CommandText="INSERT INTO OfficeNotifications VALUES($id,$uid,$at,$title,$message,0)";
        q.Parameters.AddWithValue("$id",Guid.NewGuid().ToString());q.Parameters.AddWithValue("$uid",userId.ToString());q.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));q.Parameters.AddWithValue("$title",title);q.Parameters.AddWithValue("$message",message);q.ExecuteNonQuery();
    }
    public IReadOnlyList<OfficeNotification> Notifications(Guid userId)
    {
        using var c=Open();using var q=c.CreateCommand();
        q.CommandText="SELECT Id,UserId,At,Title,Message,IsRead FROM OfficeNotifications WHERE UserId=$uid ORDER BY At DESC LIMIT 300";
        q.Parameters.AddWithValue("$uid",userId.ToString());using var r=q.ExecuteReader();var items=new List<OfficeNotification>();
        while(r.Read())items.Add(new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),DateTimeOffset.Parse(r.GetString(2)),r.GetString(3),r.GetString(4),r.GetInt32(5)!=0));return items;
    }
    public bool MarkRead(Guid userId,Guid id)
    {
        using var c=Open();using var q=c.CreateCommand();
        q.CommandText="UPDATE OfficeNotifications SET IsRead=1 WHERE Id=$id AND UserId=$uid";
        q.Parameters.AddWithValue("$id",id.ToString());q.Parameters.AddWithValue("$uid",userId.ToString());return q.ExecuteNonQuery()>0;
    }
    public string Backup()
    {
        var backupDir=Path.Combine(_folder,"Backups");Directory.CreateDirectory(backupDir);
        var path=Path.Combine(backupDir,$"choiceflame-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        using(var source=Open())using(var destination=new SqliteConnection($"Data Source={path}"))
        {
            destination.Open();source.BackupDatabase(destination);
        }
        return path;
    }
}
