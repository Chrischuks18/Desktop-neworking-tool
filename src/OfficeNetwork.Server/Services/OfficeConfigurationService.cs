using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Services;

public sealed class OfficeConfigurationService
{
    public ServerConfiguration Configuration { get; private set; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Choice Flame Communications"), Environment.MachineName, 5077);

    public IReadOnlyList<FolderPermission> DefaultPermissions { get; } =
    [
        new(OfficeRole.ServerAdministrator, OfficeFolder.WorkingFiles, true, true, true, true),
        new(OfficeRole.ServerAdministrator, OfficeFolder.SubmittedFiles, true, true, true, true),
        new(OfficeRole.ServerAdministrator, OfficeFolder.FinalFiles, true, true, true, true),
        new(OfficeRole.Director, OfficeFolder.WorkingFiles, true, true, true, true),
        new(OfficeRole.Director, OfficeFolder.SubmittedFiles, true, true, true, true),
        new(OfficeRole.Director, OfficeFolder.FinalFiles, true, true, true, true),
        new(OfficeRole.Editor, OfficeFolder.WorkingFiles, true, true, false, false),
        new(OfficeRole.Editor, OfficeFolder.SubmittedFiles, true, true, false, false),
        new(OfficeRole.Editor, OfficeFolder.FinalFiles, true, false, false, false),
        new(OfficeRole.NewsSourcing, OfficeFolder.WorkingFiles, true, true, false, false),
        new(OfficeRole.NewsSourcing, OfficeFolder.SubmittedFiles, true, true, false, false),
        new(OfficeRole.NewsSourcing, OfficeFolder.FinalFiles, true, false, false, false)
    ];

    public void Configure(ServerConfiguration configuration)
    {
        Configuration = configuration;
        CreateFolderStructure(configuration.RootPath);
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
        if (user.Role is OfficeRole.Director or OfficeRole.ServerAdministrator) return [];
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
        if (user.Role is OfficeRole.Director or OfficeRole.ServerAdministrator || folder == OfficeFolder.FinalFiles)
            return FolderPath(folder, Configuration.RootPath);
        var safeUser = string.Concat(user.UserName.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        return Path.Combine(FolderPath(folder, Configuration.RootPath), safeUser);
    }

    public IReadOnlyList<OfficeFileItem> ListFilesForUser(OfficeFolder folder, OfficeUser user)
    {
        if (user.Role == OfficeRole.Director && folder is OfficeFolder.WorkingFiles or OfficeFolder.SubmittedFiles)
        {
            var root = FolderPath(folder, Configuration.RootPath);
            Directory.CreateDirectory(root);
            return new DirectoryInfo(root).EnumerateDirectories()
                .SelectMany(d => d.EnumerateFiles().Select(f => new OfficeFileItem(f.Name, f.FullName, f.Length, f.LastWriteTimeUtc, d.Name)))
                .OrderByDescending(f => f.ModifiedAt).ToArray();
        }
        var path = FolderPathForUser(folder, user);
        Directory.CreateDirectory(path);
        return new DirectoryInfo(path).EnumerateFiles()
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new OfficeFileItem(f.Name, f.FullName, f.Length, f.LastWriteTimeUtc))
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
        return true;
    }

    public bool ApproveForDirector(string ownerUserName, string fileName)
    {
        var safeOwner = string.Concat(ownerUserName.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        var safeName = Path.GetFileName(fileName);
        var source = Path.Combine(FolderPath(OfficeFolder.SubmittedFiles, Configuration.RootPath), safeOwner, safeName);
        if (!File.Exists(source)) return false;
        var destination = FolderPath(OfficeFolder.FinalFiles, Configuration.RootPath);
        Directory.CreateDirectory(destination);
        File.Move(source, Path.Combine(destination, safeName), true);
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
