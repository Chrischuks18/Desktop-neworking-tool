using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Services;

public sealed class OfficeConfigurationService
{
    private readonly object _workflowLock = new();
    private static readonly string SettingsDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network");
    private static readonly string SettingsPath=Path.Combine(SettingsDirectory,"server-storage.txt");
    private string WorkflowLogPath => Path.Combine(Configuration.RootPath, ".choiceflame-workflow.log");
    private string MinutePath(string owner, string fileName) => Path.Combine(Configuration.RootPath, ".minutes", owner, Path.GetFileName(fileName) + ".txt");
    public ServerConfiguration Configuration { get; private set; }

    public OfficeConfigurationService()
    {
        var fallback=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),"Choice Flame Communications");
        string root=fallback;
        try
        {
            if(File.Exists(SettingsPath))
            {
                var saved=File.ReadAllText(SettingsPath).Trim();
                if(!string.IsNullOrWhiteSpace(saved))root=saved;
            }
        }
        catch { }
        Configuration=new ServerConfiguration(root,Environment.MachineName,5077);
        CreateFolderStructure(root);
    }

    public IReadOnlyList<FolderPermission> DefaultPermissions { get; } =
    [
        new(OfficeRole.ServerAdministrator, OfficeFolder.WorkingFiles, true, true, true, true),
        new(OfficeRole.ServerAdministrator, OfficeFolder.SubmittedFiles, true, true, true, true),
        new(OfficeRole.ServerAdministrator, OfficeFolder.FinalFiles, true, true, true, true),
        new(OfficeRole.Director, OfficeFolder.WorkingFiles, true, true, true, true),
        new(OfficeRole.Director, OfficeFolder.SubmittedFiles, true, true, true, true),
        new(OfficeRole.Director, OfficeFolder.FinalFiles, true, true, true, true),
        new(OfficeRole.Admin, OfficeFolder.WorkingFiles, true, true, true, true),
        new(OfficeRole.Admin, OfficeFolder.SubmittedFiles, true, true, true, true),
        new(OfficeRole.Admin, OfficeFolder.FinalFiles, true, true, false, false),
        new(OfficeRole.Editor, OfficeFolder.WorkingFiles, true, true, false, false),
        new(OfficeRole.Editor, OfficeFolder.SubmittedFiles, true, true, false, false),
        new(OfficeRole.Editor, OfficeFolder.FinalFiles, true, false, false, false),
        new(OfficeRole.NewsSourcing, OfficeFolder.WorkingFiles, true, true, false, false),
        new(OfficeRole.NewsSourcing, OfficeFolder.SubmittedFiles, true, true, false, false),
        new(OfficeRole.NewsSourcing, OfficeFolder.FinalFiles, true, false, false, false)
    ];

    public void Configure(ServerConfiguration configuration)
    {
        var root=Path.GetFullPath(configuration.RootPath.Trim());
        Directory.CreateDirectory(root);
        Configuration=configuration with { RootPath=root };
        CreateFolderStructure(root);
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath,root);
    }

    public (bool Success,string Message,int FilesCopied) ChangeStorage(string newRoot,bool copyExisting)
    {
        var destination=Path.GetFullPath(newRoot.Trim());
        var source=Path.GetFullPath(Configuration.RootPath);
        if(string.Equals(source.TrimEnd(Path.DirectorySeparatorChar),destination.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))
            return (true,"The selected folder is already the current office storage location.",0);
        if(destination.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) ||
           source.StartsWith(destination.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            return (false,"Choose a separate storage folder, not a folder inside the current office storage location.",0);
        Directory.CreateDirectory(destination);
        var copied=0;
        if(copyExisting)
        {
            foreach(var directory in Directory.EnumerateDirectories(source,"*",SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destination,Path.GetRelativePath(source,directory)));
            foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories))
            {
                var target=Path.Combine(destination,Path.GetRelativePath(source,file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file,target,true);
                copied++;
            }
        }
        CreateFolderStructure(destination);
        Configure(Configuration with { RootPath=destination });
        return (true,copyExisting?$"Storage changed successfully. {copied} existing file(s) were copied.":"Storage changed successfully.",copied);
    }

    public string[] CreateFolderStructure(string root)
    {
        Directory.CreateDirectory(root);
        return Enum.GetValues<OfficeFolder>().Select(folder => {
            var path = FolderPath(folder, root); Directory.CreateDirectory(path); return path;
        }).ToArray();
    }

    public string[] CreateUserFolders(OfficeUser user)
    {
        if (user.Role is OfficeRole.Director or OfficeRole.Admin or OfficeRole.ServerAdministrator) return [];
        var safeUser = string.Concat(user.UserName.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safeUser)) throw new InvalidOperationException("Invalid username for folder creation.");
        var working = Path.Combine(FolderPath(OfficeFolder.WorkingFiles, Configuration.RootPath), safeUser);
        var submitted = Path.Combine(FolderPath(OfficeFolder.SubmittedFiles, Configuration.RootPath), safeUser);
        Directory.CreateDirectory(working);
        Directory.CreateDirectory(submitted);
        return [working, submitted];
    }

    public string FolderPathForUser(OfficeFolder folder, OfficeUser user)
    {
        if (user.Role is OfficeRole.Director or OfficeRole.Admin or OfficeRole.ServerAdministrator || folder == OfficeFolder.FinalFiles)
            return FolderPath(folder, Configuration.RootPath);
        var safeUser = string.Concat(user.UserName.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        return Path.Combine(FolderPath(folder, Configuration.RootPath), safeUser);
    }

    public IReadOnlyList<OfficeFileItem> ListFilesForUser(OfficeFolder folder, OfficeUser user)
    {
        if (user.Role is OfficeRole.Director or OfficeRole.Admin && folder is OfficeFolder.WorkingFiles or OfficeFolder.SubmittedFiles)
        {
            var root = FolderPath(folder, Configuration.RootPath);
            Directory.CreateDirectory(root);
            return new DirectoryInfo(root).EnumerateDirectories()
                .SelectMany(d => d.EnumerateFiles().Select(f => new OfficeFileItem(f.Name, f.FullName, f.Length, f.LastWriteTimeUtc, d.Name,
                    folder == OfficeFolder.SubmittedFiles ? "Awaiting Review" : "Working", folder == OfficeFolder.SubmittedFiles ? f.LastWriteTimeUtc : null, ReadMinute(d.Name, f.Name))))
                .OrderByDescending(f => f.ModifiedAt).ToArray();
        }
        var path = FolderPathForUser(folder, user);
        Directory.CreateDirectory(path);
        return new DirectoryInfo(path).EnumerateFiles()
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new OfficeFileItem(f.Name, f.FullName, f.Length, f.LastWriteTimeUtc, user.UserName,
                folder == OfficeFolder.FinalFiles ? "Approved" : folder == OfficeFolder.SubmittedFiles ? "Awaiting Review" : "Working", folder == OfficeFolder.SubmittedFiles ? f.LastWriteTimeUtc : null, ReadMinute(user.UserName, f.Name)))
            .ToArray();
    }

    public bool SubmitForUser(OfficeUser user, string fileName)
    {
        if (user.Role is OfficeRole.Director or OfficeRole.ServerAdministrator) return false;
        var safeName = Path.GetFileName(fileName);
        var source = Path.Combine(FolderPathForUser(OfficeFolder.WorkingFiles, user), safeName);
        if (!File.Exists(source)) return false;
        var destination = FolderPathForUser(OfficeFolder.SubmittedFiles, user);
        Directory.CreateDirectory(destination);
        File.Move(source, Path.Combine(destination, safeName), true);
        ClearMinute(user.UserName, safeName);
        Log("Submitted", safeName, user.UserName, user.DisplayName);
        return true;
    }

    public bool RecallForUser(OfficeUser user, string fileName)
    {
        if (user.Role is OfficeRole.Director or OfficeRole.ServerAdministrator) return false;
        var safeName = Path.GetFileName(fileName);
        var source = Path.Combine(FolderPathForUser(OfficeFolder.SubmittedFiles, user), safeName);
        if (!File.Exists(source)) return false;
        var destination = FolderPathForUser(OfficeFolder.WorkingFiles, user);
        Directory.CreateDirectory(destination);
        File.Move(source, Path.Combine(destination, safeName), true);
        Log("Recalled", safeName, user.UserName, user.DisplayName);
        return true;
    }

    public bool ReturnForCorrection(OfficeUser director, string ownerUserName, string fileName, string minute)
    {
        var owner = SafeOwner(ownerUserName);
        var safeName = Path.GetFileName(fileName);
        var source = Path.Combine(FolderPath(OfficeFolder.SubmittedFiles, Configuration.RootPath), owner, safeName);
        if (!File.Exists(source)) return false;
        var destination = Path.Combine(FolderPath(OfficeFolder.WorkingFiles, Configuration.RootPath), owner);
        Directory.CreateDirectory(destination);
        File.Move(source, Path.Combine(destination, safeName), true);
        var minuteFile = MinutePath(owner, safeName);
        Directory.CreateDirectory(Path.GetDirectoryName(minuteFile)!);
        File.WriteAllText(minuteFile, minute.Trim());
        Log("Returned for Correction", safeName, owner, director.DisplayName, minute.Trim());
        return true;
    }

    public bool DeleteWorkingFile(OfficeUser user, string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        var path = Path.Combine(FolderPathForUser(OfficeFolder.WorkingFiles, user), safeName);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        ClearMinute(user.UserName, safeName);
        Log("Deleted", safeName, user.UserName, user.DisplayName);
        return true;
    }

    public bool RenameWorkingFile(OfficeUser user, string fileName, string newFileName)
    {
        var oldName = Path.GetFileName(fileName); var newName = Path.GetFileName(newFileName);
        if (string.IsNullOrWhiteSpace(newName)) return false;
        var folder = FolderPathForUser(OfficeFolder.WorkingFiles, user);
        var source = Path.Combine(folder, oldName); var dest = Path.Combine(folder, newName);
        if (!File.Exists(source) || File.Exists(dest)) return false;
        File.Move(source, dest);
        var minute = ReadMinute(user.UserName, oldName);
        if (!string.IsNullOrWhiteSpace(minute)) { ClearMinute(user.UserName, oldName); var mp=MinutePath(user.UserName,newName); Directory.CreateDirectory(Path.GetDirectoryName(mp)!); File.WriteAllText(mp, minute); }
        Log("Renamed", newName, user.UserName, user.DisplayName, $"From {oldName}");
        return true;
    }

    public IReadOnlyList<WorkflowEvent> History()
    {
        if (!File.Exists(WorkflowLogPath)) return [];
        lock (_workflowLock)
            return File.ReadAllLines(WorkflowLogPath).Reverse().Take(500).Select(ParseEvent).Where(x => x is not null).Cast<WorkflowEvent>().ToArray();
    }

    private void Log(string action, string file, string owner, string actor, string? note = null)
    {
        var clean = (string? x) => (x ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        lock (_workflowLock)
        {
            Directory.CreateDirectory(Configuration.RootPath);
            File.AppendAllText(WorkflowLogPath, $"{DateTimeOffset.UtcNow:O}|{clean(action)}|{clean(file)}|{clean(owner)}|{clean(actor)}|{clean(note)}{Environment.NewLine}");
        }
    }
    private static WorkflowEvent? ParseEvent(string line)
    {
        var p=line.Split('|'); if(p.Length<6 || !DateTimeOffset.TryParse(p[0],out var at)) return null;
        return new WorkflowEvent(at,p[1],p[2],p[3],p[4],string.IsNullOrWhiteSpace(p[5])?null:p[5]);
    }
    private static string SafeOwner(string owner) => string.Concat(owner.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
    private string? ReadMinute(string owner,string file) { var p=MinutePath(SafeOwner(owner),file); return File.Exists(p)?File.ReadAllText(p):null; }
    private void ClearMinute(string owner,string file) { var p=MinutePath(SafeOwner(owner),file); if(File.Exists(p)) File.Delete(p); }

    public bool ApproveForDirector(string ownerUserName, string fileName)
    {
        var safeOwner = string.Concat(ownerUserName.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        var safeName = Path.GetFileName(fileName);
        var source = Path.Combine(FolderPath(OfficeFolder.SubmittedFiles, Configuration.RootPath), safeOwner, safeName);
        if (!File.Exists(source)) return false;
        var destination = FolderPath(OfficeFolder.FinalFiles, Configuration.RootPath);
        Directory.CreateDirectory(destination);
        File.Move(source, Path.Combine(destination, safeName), true);
        ClearMinute(safeOwner, safeName);
        Log("Approved", safeName, safeOwner, "Director");
        return true;
    }

    public IReadOnlyList<OfficeFileItem> ListFiles(OfficeFolder folder)
    {
        var path = FolderPath(folder, Configuration.RootPath);
        Directory.CreateDirectory(path);
        return new DirectoryInfo(path).EnumerateFiles()
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new OfficeFileItem(f.Name, f.FullName, f.Length, f.LastWriteTimeUtc))
            .ToArray();
    }

    public bool MoveFile(OfficeFolder from, OfficeFolder to, string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        var source = Path.Combine(FolderPath(from, Configuration.RootPath), safeName);
        if (!File.Exists(source)) return false;
        var destinationFolder = FolderPath(to, Configuration.RootPath);
        Directory.CreateDirectory(destinationFolder);
        File.Move(source, Path.Combine(destinationFolder, safeName), true);
        return true;
    }

    private static string FolderPath(OfficeFolder folder, string root) => Path.Combine(root, folder switch
    {
        OfficeFolder.WorkingFiles => "Working Files",
        OfficeFolder.SubmittedFiles => "Submitted Files",
        OfficeFolder.FinalFiles => "Final Files",
        _ => folder.ToString()
    });
}
