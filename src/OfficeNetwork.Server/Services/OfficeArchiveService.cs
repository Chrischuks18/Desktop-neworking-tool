using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace OfficeNetwork.Server.Services;

public sealed record ArchiveManifest(int Version, DateTimeOffset CreatedAt, string OriginalRoot, Dictionary<string,string> Checksums);

public sealed class OfficeArchiveService(OperationsService operations, OfficeConfigurationService configuration)
{
    private readonly object _gate = new();
    public string DirectoryPath => Path.Combine(operations.DataFolder, "Archives");

    public string Create()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(DirectoryPath);
            var destination = Path.Combine(DirectoryPath, $"office-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
            var staging = Path.Combine(operations.DataFolder, "archive-staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                // SQLite's online backup API includes committed WAL data.
                var database = operations.Backup();
                File.Copy(database, Path.Combine(staging, "choiceflame.db"));
                var root = Path.GetFullPath(configuration.Configuration.RootPath);
                OfficeRestoreService.RejectLinkedAncestors(root);
                if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Office files are unavailable.");
                if (OfficeRestoreService.PathsOverlap(root, operations.DataFolder))
                    throw new InvalidOperationException("Office storage must be separate from the application's database and archives folder.");
                var files = Path.Combine(staging, "OfficeFiles");
                Directory.CreateDirectory(files);
                CopyTree(root, files);
                var checksums = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                    .ToDictionary(p => Path.GetRelativePath(staging, p).Replace('\\','/'), HashFile, StringComparer.OrdinalIgnoreCase);
                File.WriteAllText(Path.Combine(staging, "snapshot-manifest.json"), JsonSerializer.Serialize(new ArchiveManifest(1, DateTimeOffset.UtcNow, root, checksums)));
                ZipFile.CreateFromDirectory(staging, destination + ".partial");
                File.Move(destination + ".partial", destination);
                return Path.GetFileName(destination);
            }
            finally
            {
                if (File.Exists(destination + ".partial")) File.Delete(destination + ".partial");
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }
    }

    private static void CopyTree(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Backup cannot follow linked office folders.");
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(directory));
            Directory.CreateDirectory(target);
            CopyTree(directory, target);
        }
        foreach (var path in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Backup cannot follow linked office files.");
            // Fail instead of silently saving a file that changes while being copied.
            var before = new FileInfo(path);
            var length = before.Length;
            var modified = before.LastWriteTimeUtc;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(Path.Combine(destination, Path.GetFileName(path)), FileMode.CreateNew))
                input.CopyTo(output);
            var after = new FileInfo(path);
            if (after.Length != length || after.LastWriteTimeUtc != modified)
                throw new IOException("An office file changed during backup. Retry when staff have saved and closed their files.");
        }
    }

    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public IReadOnlyList<string> List()
    {
        Directory.CreateDirectory(DirectoryPath);
        return Directory.GetFiles(DirectoryPath, "office-*.zip").Select(Path.GetFileName).OfType<string>().OrderByDescending(x => x).Take(100).ToArray();
    }

    public bool IsBackupDue(DateTime utcNow) => !List().Any(name => utcNow - File.GetLastWriteTimeUtc(Path.Combine(DirectoryPath,name)) < TimeSpan.FromHours(24));

    public string? Find(string name)
    {
        if (name != Path.GetFileName(name) || name.Contains('\\') || !name.StartsWith("office-", StringComparison.Ordinal) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return null;
        var path = Path.Combine(DirectoryPath, name);
        return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 ? path : null;
    }
}
