using OfficeNetwork.Shared;

namespace OfficeNetwork.Server.Services;

public sealed class OfficeConfigurationService
{
    public IReadOnlyList<FolderPermission> DefaultPermissions { get; } =
    [
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

    public string[] CreateFolderStructure(string root)
    {
        Directory.CreateDirectory(root);
        return Enum.GetValues<OfficeFolder>()
            .Select(folder =>
            {
                var path = Path.Combine(root, FolderName(folder));
                Directory.CreateDirectory(path);
                return path;
            })
            .ToArray();
    }

    private static string FolderName(OfficeFolder folder) => folder switch
    {
        OfficeFolder.WorkingFiles => "Working Files",
        OfficeFolder.SubmittedFiles => "Submitted Files",
        OfficeFolder.FinalFiles => "Final Files",
        _ => folder.ToString()
    };
}
