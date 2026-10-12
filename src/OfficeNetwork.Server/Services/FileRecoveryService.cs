using System.Text.Json;
using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Services;

public sealed record RecoveryItem(Guid Id,string Folder,string FileName,string Owner,string Actor,DateTimeOffset SavedAt,string Reason,bool Deleted);
public sealed class FileRecoveryService(OfficeConfigurationService config)
{
    private readonly object _gate=new();
    private string Root=>Path.Combine(config.Configuration.RootPath,".choiceflame-recovery");
    private string Index=>Path.Combine(Root,"index.json");
    private List<RecoveryItem> Read()
    {
        try{return File.Exists(Index)?JsonSerializer.Deserialize<List<RecoveryItem>>(File.ReadAllText(Index))??[]:[];}catch{return [];}
    }
    private void Write(List<RecoveryItem> items)
    {
        Directory.CreateDirectory(Root);
        var temp=Index+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(items));
        File.Move(temp,Index,true);
    }
    public RecoveryItem? Save(string source,OfficeFolder folder,string owner,string actor,string reason,bool deleted)
    {
        lock(_gate)
        {
            if(!File.Exists(source))return null;
            var id=Guid.NewGuid();
            Directory.CreateDirectory(Root);
            var target=Path.Combine(Root,id.ToString("N")+".bin");
            File.Copy(source,target,false);
            var item=new RecoveryItem(id,folder.ToString(),Path.GetFileName(source),owner,actor,DateTimeOffset.UtcNow,reason,deleted);
            var all=Read();all.Add(item);Write(all);
            return item;
        }
    }
    public IReadOnlyList<RecoveryItem> List(OfficeUser caller)
    {
        lock(_gate)return Read().Where(x=>caller.Role is OfficeRole.Director or OfficeRole.Admin || x.Owner.Equals(caller.UserName,StringComparison.OrdinalIgnoreCase)).OrderByDescending(x=>x.SavedAt).ToArray();
    }
    public bool Restore(Guid id,OfficeUser caller)
    {
        lock(_gate)
        {
            var item=Read().FirstOrDefault(x=>x.Id==id);
            if(item is null || !(caller.Role is OfficeRole.Director or OfficeRole.Admin) && !item.Owner.Equals(caller.UserName,StringComparison.OrdinalIgnoreCase))return false;
            if(!Enum.TryParse<OfficeFolder>(item.Folder,out var folder) || folder is not (OfficeFolder.WorkingFiles or OfficeFolder.SharedFiles))return false;
            var source=Path.Combine(Root,id.ToString("N")+".bin");
            if(!File.Exists(source))return false;
            var baseFolder=config.FolderPathForUser(folder,caller);
            if(folder==OfficeFolder.WorkingFiles)
            {
                var safeOwner=string.Concat(item.Owner.Where(ch=>char.IsLetterOrDigit(ch)||ch is '-' or '_'));
                baseFolder=Path.Combine(config.Configuration.RootPath,"Working Files",safeOwner);
            }
            Directory.CreateDirectory(baseFolder);
            var destination=Path.Combine(baseFolder,Path.GetFileName(item.FileName));
            if(File.Exists(destination))return false;
            File.Copy(source,destination,false);
            return true;
        }
    }
}
