using Microsoft.Data.Sqlite;
namespace OfficeNetwork.Server.Services;
public sealed class FilingService
{
    private readonly string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network","choiceflame.db");
    public FilingService()
    {
        using var db=new SqliteConnection($"Data Source={path}");
        db.Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="CREATE TABLE IF NOT EXISTS FilingRegister(Reference TEXT PRIMARY KEY,Title TEXT NOT NULL,Category TEXT NOT NULL,Department TEXT NOT NULL,Confidential INTEGER NOT NULL DEFAULT 0)";
        cmd.ExecuteNonQuery();
    }
    public object[] List(bool manager)
    {
        using var db=new SqliteConnection($"Data Source={path}");
        db.Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT Reference,Title,Category,Department,Confidential FROM FilingRegister WHERE Confidential=0 OR $manager=1 ORDER BY Reference";
        cmd.Parameters.AddWithValue("$manager",manager?1:0);
        using var reader=cmd.ExecuteReader();
        var records=new List<object>();
        while(reader.Read())records.Add(new {reference=reader.GetString(0),title=reader.GetString(1),category=reader.GetString(2),department=reader.GetString(3),confidential=reader.GetInt32(4)!=0});
        return records.ToArray();
    }
    public void Add(string reference,string title,string category,string department,bool confidential)
    {
        if(string.IsNullOrWhiteSpace(reference)||string.IsNullOrWhiteSpace(title)||reference.Length>80||title.Length>250)throw new ArgumentException("Invalid reference or title.");
        using var db=new SqliteConnection($"Data Source={path}");
        db.Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO FilingRegister VALUES($reference,$title,$category,$department,$confidential)";
        cmd.Parameters.AddWithValue("$reference",reference.Trim());
        cmd.Parameters.AddWithValue("$title",title.Trim());
        cmd.Parameters.AddWithValue("$category",category??"");
        cmd.Parameters.AddWithValue("$department",department??"");
        cmd.Parameters.AddWithValue("$confidential",confidential?1:0);
        cmd.ExecuteNonQuery();
    }
}
