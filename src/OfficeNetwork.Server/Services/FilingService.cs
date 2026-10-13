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
}
