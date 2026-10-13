using System.IO.Compression;
using OfficeNetwork.Server.Services;

namespace OfficeNetwork.Server.Services;

public sealed class OfficeArchiveService(OperationsService operations,OfficeConfigurationService configuration)
{
    private static readonly string DirectoryPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network","Archives");

    public string Create()
    {
        Directory.CreateDirectory(DirectoryPath);
        var destination=Path.Combine(DirectoryPath,$"office-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        var staging=Path.Combine(Path.GetTempPath(),"choiceflame-archive-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var database=operations.Backup();
            File.Copy(database,Path.Combine(staging,"choiceflame.db"));
            var root=Path.GetFullPath(configuration.Configuration.RootPath);
            if(!System.IO.Directory.Exists(root))throw new DirectoryNotFoundException("Office files are unavailable.");
            var files=Path.Combine(staging,"OfficeFiles");
            Directory.CreateDirectory(files);
            foreach(var path in System.IO.Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories))
            {
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
                var relative=Path.GetRelativePath(root,path);
                var target=Path.Combine(files,relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(path,target);
            }
            ZipFile.CreateFromDirectory(staging,destination);
            return Path.GetFileName(destination);
        }
        catch
        {
            if(File.Exists(destination))File.Delete(destination);
            throw;
        }
        finally{try{Directory.Delete(staging,true);}catch{}}
    }

    public IReadOnlyList<string> List()
    {
        Directory.CreateDirectory(DirectoryPath);
        return Directory.GetFiles(DirectoryPath,"office-*.zip").Select(Path.GetFileName).OfType<string>().OrderByDescending(x=>x).Take(100).ToArray();
    }

    public string? Find(string name)
    {
        if(name!=Path.GetFileName(name)||!name.StartsWith("office-",StringComparison.Ordinal)||!name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))return null;
        var path=Path.Combine(DirectoryPath,name);
        return File.Exists(path)?path:null;
    }
}
