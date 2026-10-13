using Microsoft.Data.Sqlite;
namespace OfficeNetwork.Server.Services;
public sealed record EquipmentItem(Guid Id,string AssetTag,string Name,string Location,string Custodian,string Condition,string Notes);
public sealed class EquipmentRegisterService
{
    private readonly string connection;
    public EquipmentRegisterService()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network");
        Directory.CreateDirectory(dir);connection=$"Data Source={Path.Combine(dir,"choiceflame.db")}";
        using var db=new SqliteConnection(connection);db.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="CREATE TABLE IF NOT EXISTS EquipmentRegister(Id TEXT PRIMARY KEY,AssetTag TEXT NOT NULL UNIQUE,Name TEXT NOT NULL,Location TEXT NOT NULL,Custodian TEXT NOT NULL,Condition TEXT NOT NULL,Notes TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
    }
    public EquipmentItem[] List()
    {
        using var db=new SqliteConnection(connection);db.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT Id,AssetTag,Name,Location,Custodian,Condition,Notes FROM EquipmentRegister ORDER BY AssetTag";
        using var r=cmd.ExecuteReader();var items=new List<EquipmentItem>();
        while(r.Read())items.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6)));
        return items.ToArray();
    }
    public EquipmentItem Add(EquipmentItem item)
    {
        if(string.IsNullOrWhiteSpace(item.AssetTag)||string.IsNullOrWhiteSpace(item.Name)||item.AssetTag.Length>80||item.Name.Length>250)throw new ArgumentException("Invalid asset");
        var id=Guid.NewGuid();using var db=new SqliteConnection(connection);db.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO EquipmentRegister VALUES($id,$tag,$name,$location,$custodian,$condition,$notes)";
        cmd.Parameters.AddWithValue("$id",id.ToString());cmd.Parameters.AddWithValue("$tag",item.AssetTag);
        cmd.Parameters.AddWithValue("$name",item.Name);cmd.Parameters.AddWithValue("$location",item.Location);
        cmd.Parameters.AddWithValue("$custodian",item.Custodian);cmd.Parameters.AddWithValue("$condition",item.Condition);
        cmd.Parameters.AddWithValue("$notes",item.Notes);cmd.ExecuteNonQuery();return item with {Id=id};
    }
}
