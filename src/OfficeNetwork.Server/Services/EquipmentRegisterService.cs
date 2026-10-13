using Microsoft.Data.Sqlite;
using OfficeNetwork.Shared;
namespace OfficeNetwork.Server.Services;

public sealed class EquipmentRegisterService
{
    private readonly string connection;
    public EquipmentRegisterService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network")) { }
    public EquipmentRegisterService(string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        connection=$"Data Source={Path.Combine(dataFolder,"choiceflame.db")}";
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="CREATE TABLE IF NOT EXISTS EquipmentRegister(Id TEXT PRIMARY KEY,AssetTag TEXT NOT NULL UNIQUE,Name TEXT NOT NULL,Location TEXT NOT NULL,Custodian TEXT NOT NULL,Condition TEXT NOT NULL,Notes TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open(){var db=new SqliteConnection(connection);db.Open();return db;}
    public EquipmentItem[] List()
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT Id,AssetTag,Name,Location,Custodian,Condition,Notes FROM EquipmentRegister ORDER BY AssetTag";
        using var r=cmd.ExecuteReader();var items=new List<EquipmentItem>();
        while(r.Read())items.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6)));
        return items.ToArray();
    }
    private static void Validate(EquipmentItem item)
    {
        RegisterValidation.Text(item.AssetTag,"AssetTag",80,true);
        RegisterValidation.Text(item.Name,"Name",250,true);
        RegisterValidation.Text(item.Location,"Location",250,false);
        RegisterValidation.Text(item.Custodian,"Custodian",250,false);
        RegisterValidation.Text(item.Condition,"Condition",80,true);
        RegisterValidation.Text(item.Notes,"Notes",10000,false);
    }
    private static void Bind(SqliteCommand cmd,EquipmentItem item)
    {
        cmd.Parameters.AddWithValue("$id",item.Id.ToString());
        cmd.Parameters.AddWithValue("$AssetTag",item.AssetTag.Trim());
        cmd.Parameters.AddWithValue("$Name",item.Name.Trim());
        cmd.Parameters.AddWithValue("$Location",item.Location.Trim());
        cmd.Parameters.AddWithValue("$Custodian",item.Custodian.Trim());
        cmd.Parameters.AddWithValue("$Condition",item.Condition.Trim());
        cmd.Parameters.AddWithValue("$Notes",item.Notes.Trim());
    }
    public EquipmentItem Add(EquipmentItem item)
    {
        Validate(item);
        var saved=item with {Id=Guid.NewGuid()};
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO EquipmentRegister(Id,AssetTag,Name,Location,Custodian,Condition,Notes) VALUES($id,$AssetTag,$Name,$Location,$Custodian,$Condition,$Notes)";
        Bind(cmd,saved);cmd.ExecuteNonQuery();return saved;
    }
    public EquipmentItem? Update(Guid id,EquipmentItem item)
    {
        Validate(item);
        var saved=item with {Id=id};
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE EquipmentRegister SET AssetTag=$AssetTag,Name=$Name,Location=$Location,Custodian=$Custodian,Condition=$Condition,Notes=$Notes WHERE Id=$id";
        Bind(cmd,saved);return cmd.ExecuteNonQuery()==0?null:saved;
    }
}
