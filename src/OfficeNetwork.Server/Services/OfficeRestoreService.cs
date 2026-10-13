using System.IO.Compression;
using System.Text.Json;
using System.Security.AccessControl;
using Microsoft.Data.Sqlite;

namespace OfficeNetwork.Server.Services;

public sealed record RestoreResult(string OfficeRoot, string PreviousOfficeFiles, string PreviousDatabaseFolder);

// Restore runs before the web host starts. The same exclusive lock is held by the running server.
public static class OfficeRestoreService
{
    public static FileStream AcquireServerLock(string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        try { return new FileStream(Path.Combine(dataFolder,"server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
        catch (IOException ex) { throw new InvalidOperationException("Stop the running Choice Flame office server before restoring or starting another instance.",ex); }
    }

    public static void EnsureNoInterruptedRestore(string dataFolder)
    {
        var rollback=Path.Combine(dataFolder,"RestoreRollback");
        if(!Directory.Exists(rollback))return;
        foreach(var path in Directory.EnumerateFiles(rollback,"restore-journal.json",SearchOption.AllDirectories))
        {
            using var journal=JsonDocument.Parse(File.ReadAllText(path));
            var state=journal.RootElement.GetProperty("state").GetString();
            if(state is not ("completed" or "rolled-back"))throw new InvalidOperationException($"An interrupted restore needs recovery before the server can start. Consult {path} and BACKUP-RESTORE.md.");
        }
    }

    public static bool PathsOverlap(string first, string second)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        return a.Equals(b,StringComparison.OrdinalIgnoreCase) || a.StartsWith(b+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || b.StartsWith(a+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }

    public static RestoreResult Restore(string archivePath, string dataFolder, string officeRoot)
    {
        using var serverLock = AcquireServerLock(dataFolder);
        EnsureNoInterruptedRestore(dataFolder);
        return RestoreWhileStopped(archivePath, dataFolder, officeRoot);
    }

    private static RestoreResult RestoreWhileStopped(string archivePath, string dataFolder, string officeRoot)
    {
        dataFolder = Path.GetFullPath(dataFolder);
        officeRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(officeRoot));
        if (Path.GetPathRoot(officeRoot) == officeRoot || PathsOverlap(dataFolder, officeRoot))
            throw new InvalidOperationException("Restore requires a dedicated office folder separate from the database folder.");
        RejectLinkedAncestors(dataFolder);
        RejectLinkedAncestors(officeRoot);
        var id = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(dataFolder,"restore-staging-"+id);
        var stagedFiles = officeRoot+".restore-staging-"+id;
        var previousFiles = officeRoot+".before-restore-"+id;
        var previousDatabase = Path.Combine(dataFolder,"RestoreRollback",id);
        var database = Path.Combine(dataFolder,"choiceflame.db");
        var settings = Path.Combine(dataFolder,"server-storage.txt");
        Directory.CreateDirectory(staging);
        var movedFiles = false;
        var installedFiles = false;
        var installedDatabase = false;
        var settingsChanged = false;
        var movedDatabaseFiles = new List<string>();
        try
        {
            ExtractAndValidate(archivePath,staging);
            // Stage on the office drive so the final folder moves stay on one filesystem.
            CopyDirectory(Path.Combine(staging,"OfficeFiles"),stagedFiles,officeRoot);
            Directory.CreateDirectory(previousDatabase);
            if (File.Exists(settings)) File.Copy(settings,Path.Combine(previousDatabase,"server-storage.txt"));
            // Journal enables recovery after an interrupted or failed restore. Never auto-delete rollback copies.
            var journal = Path.Combine(previousDatabase,"restore-journal.json");
            File.WriteAllText(journal,JsonSerializer.Serialize(new {archive=Path.GetFullPath(archivePath),officeRoot,previousFiles,previousDatabase,state="prepared"}));
            if (Directory.Exists(officeRoot)) { Directory.Move(officeRoot,previousFiles); movedFiles=true; }
            Directory.Move(stagedFiles,officeRoot);
            installedFiles=true;
            // Preserve the old database together with its SQLite sidecars, without opening it.
            foreach(var name in new[]{"choiceflame.db","choiceflame.db-wal","choiceflame.db-shm","choiceflame.db-journal"})
            {
                var path=Path.Combine(dataFolder,name);
                if(!File.Exists(path))continue;
                File.Move(path,Path.Combine(previousDatabase,name));
                movedDatabaseFiles.Add(name);
            }
            File.Move(Path.Combine(staging,"choiceflame.db"),database);
            installedDatabase=true;
            File.WriteAllText(settings+".restore.tmp",officeRoot);
            File.Move(settings+".restore.tmp",settings,true);
            settingsChanged=true;
            File.WriteAllText(journal,JsonSerializer.Serialize(new {archive=Path.GetFullPath(archivePath),officeRoot,previousFiles,previousDatabase,state="completed"}));
            return new(officeRoot,previousFiles,previousDatabase);
        }
        catch (Exception restoreError)
        {
            try
            {
                if(installedDatabase && File.Exists(database))File.Delete(database);
                foreach(var name in movedDatabaseFiles)File.Move(Path.Combine(previousDatabase,name),Path.Combine(dataFolder,name));
                if(installedFiles && Directory.Exists(officeRoot))Directory.Delete(officeRoot,true);
                if(movedFiles)Directory.Move(previousFiles,officeRoot);
                if(settingsChanged)
                {
                    var saved=Path.Combine(previousDatabase,"server-storage.txt");
                    if(File.Exists(saved))File.Copy(saved,settings,true);else File.Delete(settings);
                }
                var journal=Path.Combine(previousDatabase,"restore-journal.json");
                if(File.Exists(journal))File.WriteAllText(journal,JsonSerializer.Serialize(new {officeRoot,previousFiles,previousDatabase,state="rolled-back"}));
            }
            catch(Exception rollbackError)
            {
                throw new AggregateException($"Restore and rollback failed. Keep all files in {previousDatabase} and {previousFiles}; use the restore journal for manual recovery.",restoreError,rollbackError);
            }
            throw;
        }
        finally
        {
            // Keep rollback folders, even if a restore fails.
            if(Directory.Exists(staging))Directory.Delete(staging,true);
            if(Directory.Exists(stagedFiles))Directory.Delete(stagedFiles,true);
            if(File.Exists(settings+".restore.tmp"))File.Delete(settings+".restore.tmp");
        }
    }

    internal static void RejectLinkedAncestors(string path)
    {
        for(var item=new DirectoryInfo(path);item is not null;item=item.Parent)
            if(item.Exists && (item.Attributes & FileAttributes.ReparsePoint)!=0)
                throw new InvalidOperationException("Restore cannot use linked folders.");
    }

    private static void ExtractAndValidate(string archivePath,string staging)
    {
        using var archive=ZipFile.OpenRead(archivePath);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total=0;
        if(archive.Entries.Count>100000)throw new InvalidDataException("Archive contains too many entries.");
        foreach(var entry in archive.Entries)
        {
            var name=entry.FullName;
            var parts=name.TrimEnd('/').Split('/');
            if(string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.Contains('\\') || parts.Any(p=>string.IsNullOrWhiteSpace(p) || p is "." or ".." || p.Contains(':') || p.EndsWith('.') || p.EndsWith(' ') || IsReservedWindowsName(p) || p.IndexOfAny(Path.GetInvalidFileNameChars())>=0) || !seen.Add(name.TrimEnd('/')))
                throw new InvalidDataException("Archive contains an unsafe or duplicate path.");
            if(name!="choiceflame.db" && name!="snapshot-manifest.json" && name!="OfficeFiles/" && !name.StartsWith("OfficeFiles/",StringComparison.Ordinal))
                throw new InvalidDataException("Archive contains an unexpected entry.");
            // Unix symlinks must not be restored as normal files.
            if(((entry.ExternalAttributes>>16)&0xF000)==0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint)!=0)
                throw new InvalidDataException("Archive contains a linked file.");
            total=checked(total+entry.Length);
            if(total>100L*1024*1024*1024)throw new InvalidDataException("Archive exceeds the 100 GB restore limit.");
            var target=Path.GetFullPath(Path.Combine(staging,name.Replace('/',Path.DirectorySeparatorChar)));
            if(!target.StartsWith(staging+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Archive path escapes the restore folder.");
            if(name.EndsWith('/'))Directory.CreateDirectory(target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target,false);
            }
        }
        var db=Path.Combine(staging,"choiceflame.db");
        if(!File.Exists(db) || !Directory.Exists(Path.Combine(staging,"OfficeFiles")))throw new InvalidDataException("Archive must contain the database and office files.");
        using(var connection=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=db,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
        {
            connection.Open();
            using var command=connection.CreateCommand();
            command.CommandText="PRAGMA integrity_check";
            if(!string.Equals(command.ExecuteScalar()?.ToString(),"ok",StringComparison.Ordinal))throw new InvalidDataException("Archive database failed its integrity check.");
            command.CommandText="SELECT COUNT(*) FROM Users WHERE IsEnabled=1 AND Role=1";
            if(Convert.ToInt64(command.ExecuteScalar())==0)throw new InvalidDataException("Archive has no enabled Director account.");
        }
        var manifestPath=Path.Combine(staging,"snapshot-manifest.json");
        if(!File.Exists(manifestPath))return; // Older archives still receive path and SQLite validation.
        var manifest=JsonSerializer.Deserialize<ArchiveManifest>(File.ReadAllText(manifestPath))??throw new InvalidDataException("Invalid archive manifest.");
        if(manifest.Version!=1 || manifest.Checksums is null)throw new InvalidDataException("Unsupported archive manifest.");
        var files=Directory.EnumerateFiles(staging,"*",SearchOption.AllDirectories).Where(p=>p!=manifestPath).ToArray();
        if(files.Length!=manifest.Checksums.Count)throw new InvalidDataException("Archive manifest does not match the files.");
        foreach(var file in files)
        {
            var relative=Path.GetRelativePath(staging,file).Replace('\\','/');
            if(!manifest.Checksums.TryGetValue(relative,out var expected) || !string.Equals(expected,OfficeArchiveService.HashFile(file),StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Archive file failed its checksum validation.");
        }
    }

    private static bool IsReservedWindowsName(string name)
    {
        var stem=name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length==4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9');
    }
    private static void CopyDirectory(string source,string destination,string permissionTemplate)
    {
        RejectLinkedAncestors(permissionTemplate);
        Directory.CreateDirectory(destination);
        if(OperatingSystem.IsWindows() && Directory.Exists(permissionTemplate))
            new DirectoryInfo(destination).SetAccessControl(new DirectoryInfo(permissionTemplate).GetAccessControl());
        foreach(var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory,Path.Combine(destination,Path.GetFileName(directory)),Path.Combine(permissionTemplate,Path.GetFileName(directory)));
        foreach(var file in Directory.EnumerateFiles(source))
        {
            var target=Path.Combine(destination,Path.GetFileName(file));
            File.Copy(file,target,false);
            var original=Path.Combine(permissionTemplate,Path.GetFileName(file));
            if(OperatingSystem.IsWindows() && File.Exists(original))
                new FileInfo(target).SetAccessControl(new FileInfo(original).GetAccessControl());
        }
    }
}
