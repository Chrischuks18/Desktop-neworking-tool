using Microsoft.Data.Sqlite;
namespace OfficeNetwork.Server.Services;
public sealed record ProductionItem(Guid Id,string Title,string Stage,string AssignedTo,string Deadline,string Notes);
public sealed class ProductionTrackerService
{
    private readonly string connection;
    public ProductionTrackerService()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network");
        Directory.CreateDirectory(dir);
        connection=$"Data Source={Path.Combine(dir,"choiceflame.db")}";
        using var db=new SqliteConnection(connection);db.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="CREATE TABLE IF NOT EXISTS NewsProduction(Id TEXT PRIMARY KEY,Title TEXT NOT NULL,Stage TEXT NOT NULL,AssignedTo TEXT NOT NULL,Deadline TEXT NOT NULL,Notes TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
    }
    public ProductionItem[] List()
    {
        using var db=new SqliteConnection(connection);db.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT Id,Title,Stage,AssignedTo,Deadline,Notes FROM NewsProduction ORDER BY Deadline";
        using var r=cmd.ExecuteReader();var items=new List<ProductionItem>();
        while(r.Read())items.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5)));
        return items.ToArray();
    }
    public ProductionItem Add(ProductionItem item)
    {
        if(string.IsNullOrWhiteSpace(item.Title)||item.Title.Length>250)throw new ArgumentException("Invalid title");
        var id=Guid.NewGuid();using var db=new SqliteConnection(connection);db.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO NewsProduction VALUES($id,$title,$stage,$assigned,$deadline,$notes)";
        cmd.Parameters.AddWithValue("$id",id.ToString());cmd.Parameters.AddWithValue("$title",item.Title);
        cmd.Parameters.AddWithValue("$stage",item.Stage);cmd.Parameters.AddWithValue("$assigned",item.AssignedTo);
        cmd.Parameters.AddWithValue("$deadline",item.Deadline);cmd.Parameters.AddWithValue("$notes",item.Notes);
        cmd.ExecuteNonQuery();return item with {Id=id};
    }
}
