using Microsoft.Data.Sqlite;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Services;

public sealed class ActivityCalendarService
{
    private readonly string _connectionString;
    public ActivityCalendarService()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network");
        Directory.CreateDirectory(dir);
        _connectionString=$"Data Source={Path.Combine(dir,"choiceflame.db")}";
        using var connection=new SqliteConnection(_connectionString);connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText="CREATE TABLE IF NOT EXISTS Activities (Id TEXT PRIMARY KEY, ActivityDate TEXT NOT NULL, Title TEXT NOT NULL, Details TEXT NOT NULL, CreatedBy TEXT NOT NULL, CreatedAt TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }
    public IReadOnlyList<OfficeActivity> List(DateTime from,DateTime to)
    {
        var items=new List<OfficeActivity>();using var c=new SqliteConnection(_connectionString);c.Open();using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT Id,ActivityDate,Title,Details,CreatedBy,CreatedAt FROM Activities WHERE ActivityDate >= $from AND ActivityDate <= $to ORDER BY ActivityDate,Title";
        cmd.Parameters.AddWithValue("$from",from.Date.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$to",to.Date.ToString("yyyy-MM-dd"));using var r=cmd.ExecuteReader();
        while(r.Read())items.Add(new OfficeActivity(Guid.Parse(r.GetString(0)),DateTime.Parse(r.GetString(1)),r.GetString(2),r.GetString(3),r.GetString(4),DateTimeOffset.Parse(r.GetString(5))));return items;
    }
    public OfficeActivity Create(SaveActivityRequest request,OfficeUser actor)
    {
        if(string.IsNullOrWhiteSpace(request.Title))throw new InvalidOperationException("Activity title is required.");
        var item=new OfficeActivity(Guid.NewGuid(),request.ActivityDate.Date,request.Title.Trim(),request.Details?.Trim()??"",actor.DisplayName,DateTimeOffset.UtcNow);
        using var c=new SqliteConnection(_connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO Activities(Id,ActivityDate,Title,Details,CreatedBy,CreatedAt) VALUES($id,$date,$title,$details,$by,$at)";
        cmd.Parameters.AddWithValue("$id",item.Id.ToString());cmd.Parameters.AddWithValue("$date",item.ActivityDate.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$title",item.Title);cmd.Parameters.AddWithValue("$details",item.Details);cmd.Parameters.AddWithValue("$by",item.CreatedBy);cmd.Parameters.AddWithValue("$at",item.CreatedAt.ToString("O"));cmd.ExecuteNonQuery();return item;
    }
    public bool Update(Guid id,SaveActivityRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.Title))throw new InvalidOperationException("Activity title is required.");using var c=new SqliteConnection(_connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText="UPDATE Activities SET ActivityDate=$date,Title=$title,Details=$details WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id",id.ToString());cmd.Parameters.AddWithValue("$date",request.ActivityDate.Date.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$title",request.Title.Trim());cmd.Parameters.AddWithValue("$details",request.Details?.Trim()??"");return cmd.ExecuteNonQuery()>0;
    }
    public bool Delete(Guid id){using var c=new SqliteConnection(_connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText="DELETE FROM Activities WHERE Id=$id";cmd.Parameters.AddWithValue("$id",id.ToString());return cmd.ExecuteNonQuery()>0;}
}
