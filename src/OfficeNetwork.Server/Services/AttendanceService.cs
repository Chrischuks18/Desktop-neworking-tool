using Microsoft.Data.Sqlite;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Services;

public sealed class AttendanceService
{
    private readonly string _db;
    public AttendanceService()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network");
        Directory.CreateDirectory(dir); _db=Path.Combine(dir,"choiceflame.db");
        using var c=Open(); using var cmd=c.CreateCommand();
        cmd.CommandText="CREATE TABLE IF NOT EXISTS Attendance (Id TEXT PRIMARY KEY, UserId TEXT NOT NULL, UserName TEXT NOT NULL, DisplayName TEXT NOT NULL, Role INTEGER NOT NULL, WorkDate TEXT NOT NULL, ClockIn TEXT NOT NULL, ClockOut TEXT NULL, Status TEXT NOT NULL); CREATE TABLE IF NOT EXISTS LoginHistory (Id TEXT PRIMARY KEY, UserId TEXT NOT NULL, UserName TEXT NOT NULL, DisplayName TEXT NOT NULL, Role INTEGER NOT NULL, LoggedInAt TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open(){var c=new SqliteConnection($"Data Source={_db}");c.Open();return c;}
    private static DateTimeOffset Now()=>DateTimeOffset.UtcNow;
    public void RecordLogin(LoginResult u)
    {
        if(u.Role==OfficeRole.Director)return;
        using var c=Open();using var cmd=c.CreateCommand();
        cmd.CommandText="INSERT INTO LoginHistory VALUES($id,$uid,$un,$dn,$role,$at)";
        cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString());cmd.Parameters.AddWithValue("$uid",u.UserId.ToString());cmd.Parameters.AddWithValue("$un",u.UserName);cmd.Parameters.AddWithValue("$dn",u.DisplayName);cmd.Parameters.AddWithValue("$role",(int)u.Role);cmd.Parameters.AddWithValue("$at",Now().ToString("O"));cmd.ExecuteNonQuery();
    }
    public AttendanceRecord ClockIn(OfficeUser u)
    {
        if(u.Role==OfficeRole.Director)throw new InvalidOperationException("Director does not clock in.");
        var now=Now(); var local=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now,"W. Central Africa Standard Time"); var day=local.ToString("yyyy-MM-dd");
        using var c=Open(); using(var find=c.CreateCommand()){find.CommandText="SELECT Id FROM Attendance WHERE UserId=$u AND WorkDate=$d LIMIT 1";find.Parameters.AddWithValue("$u",u.Id.ToString());find.Parameters.AddWithValue("$d",day);if(find.ExecuteScalar()!=null)throw new InvalidOperationException("You have already clocked in today.");}
        var status=local.TimeOfDay>new TimeSpan(9,15,0)?"Late":"On Time"; var id=Guid.NewGuid();
        using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO Attendance VALUES($id,$uid,$un,$dn,$role,$day,$ci,NULL,$status)";
        cmd.Parameters.AddWithValue("$id",id.ToString());cmd.Parameters.AddWithValue("$uid",u.Id.ToString());cmd.Parameters.AddWithValue("$un",u.UserName);cmd.Parameters.AddWithValue("$dn",u.DisplayName);cmd.Parameters.AddWithValue("$role",(int)u.Role);cmd.Parameters.AddWithValue("$day",day);cmd.Parameters.AddWithValue("$ci",now.ToString("O"));cmd.Parameters.AddWithValue("$status",status);cmd.ExecuteNonQuery();
        return new(id,u.Id,u.UserName,u.DisplayName,u.Role,DateTime.Parse(day),now,null,status);
    }
    public AttendanceRecord ClockOut(OfficeUser u)
    {
        var now=Now();
        var local=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now,"W. Central Africa Standard Time");
        var day=local.ToString("yyyy-MM-dd");
        using var c=Open();using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT Id,WorkDate,ClockIn,Status FROM Attendance WHERE UserId=$u AND WorkDate=$d AND ClockOut IS NULL LIMIT 1";
        cmd.Parameters.AddWithValue("$u",u.Id.ToString());cmd.Parameters.AddWithValue("$d",day);
        using var r=cmd.ExecuteReader();
        if(!r.Read())throw new InvalidOperationException("No active clock-in was found for today.");
        var id=Guid.Parse(r.GetString(0));var d=DateTime.Parse(r.GetString(1));var ci=DateTimeOffset.Parse(r.GetString(2));var st=r.GetString(3);r.Close();
        using var up=c.CreateCommand();up.CommandText="UPDATE Attendance SET ClockOut=$o WHERE Id=$id";up.Parameters.AddWithValue("$o",now.ToString("O"));up.Parameters.AddWithValue("$id",id.ToString());up.ExecuteNonQuery();
        return new(id,u.Id,u.UserName,u.DisplayName,u.Role,d,ci,now,st);
    }
    public AttendanceRecord? Current(OfficeUser u)
    {
        var local=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(Now(),"W. Central Africa Standard Time");
        var day=local.ToString("yyyy-MM-dd");
        using var c=Open();using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT Id,WorkDate,ClockIn,ClockOut,Status FROM Attendance WHERE UserId=$u AND WorkDate=$d LIMIT 1";
        cmd.Parameters.AddWithValue("$u",u.Id.ToString());cmd.Parameters.AddWithValue("$d",day);
        using var r=cmd.ExecuteReader();if(!r.Read())return null;
        return new(Guid.Parse(r.GetString(0)),u.Id,u.UserName,u.DisplayName,u.Role,DateTime.Parse(r.GetString(1)),DateTimeOffset.Parse(r.GetString(2)),r.IsDBNull(3)?null:DateTimeOffset.Parse(r.GetString(3)),r.GetString(4));
    }
    public AttendanceRecord[] ListAttendance(){using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT Id,UserId,UserName,DisplayName,Role,WorkDate,ClockIn,ClockOut,Status FROM Attendance ORDER BY ClockIn DESC LIMIT 2000";using var r=cmd.ExecuteReader();var a=new List<AttendanceRecord>();while(r.Read())a.Add(new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetString(3),(OfficeRole)r.GetInt32(4),DateTime.Parse(r.GetString(5)),DateTimeOffset.Parse(r.GetString(6)),r.IsDBNull(7)?null:DateTimeOffset.Parse(r.GetString(7)),r.GetString(8)));return a.ToArray();}
    public LoginHistoryRecord[] ListLogins(){using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT Id,UserId,UserName,DisplayName,Role,LoggedInAt FROM LoginHistory ORDER BY LoggedInAt DESC LIMIT 3000";using var r=cmd.ExecuteReader();var a=new List<LoginHistoryRecord>();while(r.Read())a.Add(new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetString(3),(OfficeRole)r.GetInt32(4),DateTimeOffset.Parse(r.GetString(5))));return a.ToArray();}
}