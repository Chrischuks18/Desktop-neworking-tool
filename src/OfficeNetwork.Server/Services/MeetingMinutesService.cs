using Microsoft.Data.Sqlite;
using OfficeNetwork.Shared;
namespace OfficeNetwork.Server.Services;

public sealed class MeetingMinutesService
{
    private readonly string connection;
    public MeetingMinutesService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network")) { }
    public MeetingMinutesService(string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        connection=$"Data Source={Path.Combine(dataFolder,"choiceflame.db")}";
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="CREATE TABLE IF NOT EXISTS MeetingMinutesRegister(Id TEXT PRIMARY KEY,MeetingTitle TEXT NOT NULL,MeetingDate TEXT NOT NULL,RecordedBy TEXT NOT NULL,Minutes TEXT NOT NULL,ActionItems TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open(){var db=new SqliteConnection(connection);db.Open();return db;}
    public MeetingMinute[] List()
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT Id,MeetingTitle,MeetingDate,RecordedBy,Minutes,ActionItems FROM MeetingMinutesRegister ORDER BY MeetingDate DESC";
        using var r=cmd.ExecuteReader();var items=new List<MeetingMinute>();
        while(r.Read())items.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5)));
        return items.ToArray();
    }
    private static void Validate(MeetingMinute item)
    {
        RegisterValidation.Text(item.MeetingTitle,"MeetingTitle",250,true);
        RegisterValidation.Text(item.MeetingDate,"MeetingDate",40,true);
        RegisterValidation.Text(item.RecordedBy,"RecordedBy",250,true);
        RegisterValidation.Text(item.Minutes,"Minutes",50000,true);
        RegisterValidation.Text(item.ActionItems,"ActionItems",20000,false);
        RegisterValidation.Date(item.MeetingDate,"Meeting date",true);
    }
    private static void Bind(SqliteCommand cmd,MeetingMinute item)
    {
        cmd.Parameters.AddWithValue("$id",item.Id.ToString());
        cmd.Parameters.AddWithValue("$MeetingTitle",item.MeetingTitle.Trim());
        cmd.Parameters.AddWithValue("$MeetingDate",item.MeetingDate.Trim());
        cmd.Parameters.AddWithValue("$RecordedBy",item.RecordedBy.Trim());
        cmd.Parameters.AddWithValue("$Minutes",item.Minutes.Trim());
        cmd.Parameters.AddWithValue("$ActionItems",item.ActionItems.Trim());
    }
    public MeetingMinute Add(MeetingMinute item)
    {
        Validate(item);
        var saved=item with {Id=Guid.NewGuid()};
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO MeetingMinutesRegister(Id,MeetingTitle,MeetingDate,RecordedBy,Minutes,ActionItems) VALUES($id,$MeetingTitle,$MeetingDate,$RecordedBy,$Minutes,$ActionItems)";
        Bind(cmd,saved);cmd.ExecuteNonQuery();return saved;
    }
    public MeetingMinute? Update(Guid id,MeetingMinute item)
    {
        Validate(item);
        var saved=item with {Id=id};
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE MeetingMinutesRegister SET MeetingTitle=$MeetingTitle,MeetingDate=$MeetingDate,RecordedBy=$RecordedBy,Minutes=$Minutes,ActionItems=$ActionItems WHERE Id=$id";
        Bind(cmd,saved);return cmd.ExecuteNonQuery()==0?null:saved;
    }
}
