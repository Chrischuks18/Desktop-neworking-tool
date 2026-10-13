using Microsoft.Data.Sqlite;
using OfficeNetwork.Shared;
namespace OfficeNetwork.Server.Services;

public sealed class ProductionTrackerService
{
    private readonly string connection;
    public ProductionTrackerService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network")) { }
    public ProductionTrackerService(string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        connection=$"Data Source={Path.Combine(dataFolder,"choiceflame.db")}";
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="CREATE TABLE IF NOT EXISTS NewsProduction(Id TEXT PRIMARY KEY,Title TEXT NOT NULL,Stage TEXT NOT NULL,AssignedTo TEXT NOT NULL,Deadline TEXT NOT NULL,Notes TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open(){var db=new SqliteConnection(connection);db.Open();return db;}
    public ProductionItem[] List()
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT Id,Title,Stage,AssignedTo,Deadline,Notes FROM NewsProduction ORDER BY Deadline";
        using var r=cmd.ExecuteReader();var items=new List<ProductionItem>();
        while(r.Read())items.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5)));
        return items.ToArray();
    }
    private static void Validate(ProductionItem item)
    {
        RegisterValidation.Text(item.Title,"Title",250,true);
        RegisterValidation.Text(item.Stage,"Stage",80,true);
        RegisterValidation.Text(item.AssignedTo,"AssignedTo",250,false);
        RegisterValidation.Text(item.Deadline,"Deadline",40,false);
        RegisterValidation.Text(item.Notes,"Notes",10000,false);
        RegisterValidation.Date(item.Deadline,"Deadline",false);
    }
    private static void Bind(SqliteCommand cmd,ProductionItem item)
    {
        cmd.Parameters.AddWithValue("$id",item.Id.ToString());
        cmd.Parameters.AddWithValue("$Title",item.Title.Trim());
        cmd.Parameters.AddWithValue("$Stage",item.Stage.Trim());
        cmd.Parameters.AddWithValue("$AssignedTo",item.AssignedTo.Trim());
        cmd.Parameters.AddWithValue("$Deadline",item.Deadline.Trim());
        cmd.Parameters.AddWithValue("$Notes",item.Notes.Trim());
    }
    public ProductionItem Add(ProductionItem item)
    {
        Validate(item);
        var saved=item with {Id=Guid.NewGuid()};
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO NewsProduction(Id,Title,Stage,AssignedTo,Deadline,Notes) VALUES($id,$Title,$Stage,$AssignedTo,$Deadline,$Notes)";
        Bind(cmd,saved);cmd.ExecuteNonQuery();return saved;
    }
    public ProductionItem? Update(Guid id,ProductionItem item)
    {
        Validate(item);
        var saved=item with {Id=id};
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE NewsProduction SET Title=$Title,Stage=$Stage,AssignedTo=$AssignedTo,Deadline=$Deadline,Notes=$Notes WHERE Id=$id";
        Bind(cmd,saved);return cmd.ExecuteNonQuery()==0?null:saved;
    }
}
