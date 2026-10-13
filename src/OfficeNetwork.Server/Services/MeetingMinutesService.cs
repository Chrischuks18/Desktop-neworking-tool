using Microsoft.Data.Sqlite;
namespace OfficeNetwork.Server.Services;
public sealed record MeetingMinute(Guid Id,string MeetingTitle,string MeetingDate,string RecordedBy,string Minutes,string ActionItems);
public sealed class MeetingMinutesService
{
    private readonly string connection;
    public MeetingMinutesService()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network");
        Directory.CreateDirectory(dir);connection=$"Data Source={Path.Combine(dir,"choiceflame.db")}";
        using var db=new SqliteConnection(connection);db.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="CREATE TABLE IF NOT EXISTS MeetingMinutesRegister(Id TEXT PRIMARY KEY,MeetingTitle TEXT NOT NULL,MeetingDate TEXT NOT NULL,RecordedBy TEXT NOT NULL,Minutes TEXT NOT NULL,ActionItems TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
    }
    public MeetingMinute[] List()
    {
        using var db=new SqliteConnection(connection);db.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT Id,MeetingTitle,MeetingDate,RecordedBy,Minutes,ActionItems FROM MeetingMinutesRegister ORDER BY MeetingDate DESC";
        using var r=cmd.ExecuteReader();var items=new List<MeetingMinute>();
        while(r.Read())items.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5)));
        return items.ToArray();
    }
    public MeetingMinute Add(MeetingMinute item)
    {
        if(string.IsNullOrWhiteSpace(item.MeetingTitle)||item.MeetingTitle.Length>250)throw new ArgumentException("Invalid meeting title");
        var id=Guid.NewGuid();using var db=new SqliteConnection(connection);db.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO MeetingMinutesRegister VALUES($id,$title,$date,$by,$minutes,$actions)";
        cmd.Parameters.AddWithValue("$id",id.ToString());cmd.Parameters.AddWithValue("$title",item.MeetingTitle);
        cmd.Parameters.AddWithValue("$date",item.MeetingDate);cmd.Parameters.AddWithValue("$by",item.RecordedBy);
        cmd.Parameters.AddWithValue("$minutes",item.Minutes);cmd.Parameters.AddWithValue("$actions",item.ActionItems);
        cmd.ExecuteNonQuery();return item with {Id=id};
    }
}
