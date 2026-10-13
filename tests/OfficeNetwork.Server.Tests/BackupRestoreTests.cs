using System.IO.Compression;
using Microsoft.Data.Sqlite;
using OfficeNetwork.Server.Services;
using OfficeNetwork.Shared;
using Xunit;

namespace OfficeNetwork.Server.Tests;

public sealed class BackupRestoreTests : IDisposable
{
    private readonly string _temporary=Path.Combine(Path.GetTempPath(),"choiceflame-tests-"+Guid.NewGuid().ToString("N"));
    private string Data=>Path.Combine(_temporary,"data");
    private string Root=>Path.Combine(_temporary,"office");
    private readonly UserAccountService _users;
    private readonly OfficeArchiveService _archives;
    private readonly OfficeConfigurationService _configuration;
    private readonly OfficeUser _director;

    public BackupRestoreTests()
    {
        Directory.CreateDirectory(_temporary);
        _users=new UserAccountService(Data);
        _director=_users.CreateInitialDirector(new("director","Office Director",OfficeRole.Director,"OriginalPass1"));
        _configuration=new OfficeConfigurationService(Data,Root);
        _configuration.Configure(_configuration.Configuration);
        _archives=new OfficeArchiveService(new OperationsService(Data),_configuration);
        File.WriteAllText(Path.Combine(Root,"Final Files","report.txt"),"Original report");
        Directory.CreateDirectory(Path.Combine(Root,".minutes","writer"));
        File.WriteAllText(Path.Combine(Root,".minutes","writer","note.txt"),"Review note");
    }

    [Fact]
    public void FullRestorePreservesFilesAccountsCredentialsAndPreviousState()
    {
        var archive=Path.Combine(_archives.DirectoryPath,_archives.Create());
        File.WriteAllText(Path.Combine(Root,"Final Files","report.txt"),"Newer report");
        _users.Update(_director.Id,new("director","New name",OfficeRole.Director,true,"ChangedPass1"));
        SqliteConnection.ClearAllPools();
        var result=OfficeRestoreService.Restore(archive,Data,Root);
        Assert.Equal("Original report",File.ReadAllText(Path.Combine(Root,"Final Files","report.txt")));
        Assert.Equal("Review note",File.ReadAllText(Path.Combine(Root,".minutes","writer","note.txt")));
        Assert.Equal("Newer report",File.ReadAllText(Path.Combine(result.PreviousOfficeFiles,"Final Files","report.txt")));
        Assert.True(File.Exists(Path.Combine(result.PreviousDatabaseFolder,"choiceflame.db")));
        var restored=new UserAccountService(Data);
        var login=restored.Login(new("director","OriginalPass1"));
        Assert.NotNull(login);
        Assert.Equal(_director.Id,login.UserId);
        Assert.Equal("Office Director",login.DisplayName);
        Assert.Null(restored.Login(new("director","ChangedPass1")));
        OfficeRestoreService.EnsureNoInterruptedRestore(Data);
    }

    [Fact]
    public void RestoreRefusesWhileServerLockIsHeld()
    {
        var archive=Path.Combine(_archives.DirectoryPath,_archives.Create());
        using var active=OfficeRestoreService.AcquireServerLock(Data);
        Assert.Throws<InvalidOperationException>(()=>OfficeRestoreService.Restore(archive,Data,Root));
        Assert.Equal("Original report",File.ReadAllText(Path.Combine(Root,"Final Files","report.txt")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("OfficeFiles/../../escape.txt")]
    [InlineData("OfficeFiles/folder\\escape.txt")]
    [InlineData("OfficeFiles/report.txt:stream")]
    [InlineData("OfficeFiles/CON.txt")]
    [InlineData("OfficeFiles/folder./report.txt")]
    public void UnsafePathsAreRejectedWithoutChangingLiveData(string entryName)
    {
        var archive=Path.Combine(_temporary,"unsafe.zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
        using(var writer=new StreamWriter(zip.CreateEntry(entryName).Open()))writer.Write("unsafe");
        Assert.Throws<InvalidDataException>(()=>OfficeRestoreService.Restore(archive,Data,Root));
        Assert.Equal("Original report",File.ReadAllText(Path.Combine(Root,"Final Files","report.txt")));
        Assert.False(File.Exists(Path.Combine(_temporary,"escape.txt")));
    }

    [Fact]
    public void DamagedArchiveIsRejectedBeforeLiveFilesAreChanged()
    {
        var archive=Path.Combine(_archives.DirectoryPath,_archives.Create());
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Update))
        {
            var entry=zip.GetEntry("OfficeFiles/Final Files/report.txt")!;
            entry.Delete();
            using var writer=new StreamWriter(zip.CreateEntry("OfficeFiles/Final Files/report.txt").Open());
            writer.Write("Corrupted report");
        }
        Assert.Throws<InvalidDataException>(()=>OfficeRestoreService.Restore(archive,Data,Root));
        Assert.Equal("Original report",File.ReadAllText(Path.Combine(Root,"Final Files","report.txt")));
    }

    [Fact]
    public void DatabaseReplacementFailureRollsOfficeFilesBack()
    {
        if(!OperatingSystem.IsWindows())return; // Windows file-lock behaviour is exercised by both Windows workflows.
        var archive=Path.Combine(_archives.DirectoryPath,_archives.Create());
        File.WriteAllText(Path.Combine(Root,"Final Files","report.txt"),"Current report");
        SqliteConnection.ClearAllPools();
        using var locked=new FileStream(Path.Combine(Data,"choiceflame.db"),FileMode.Open,FileAccess.Read,FileShare.None);
        Assert.ThrowsAny<IOException>(()=>OfficeRestoreService.Restore(archive,Data,Root));
        Assert.Equal("Current report",File.ReadAllText(Path.Combine(Root,"Final Files","report.txt")));
        OfficeRestoreService.EnsureNoInterruptedRestore(Data);
    }

    [Fact]
    public void LegacyArchiveWithoutManifestCanStillRestore()
    {
        var archive=Path.Combine(_archives.DirectoryPath,_archives.Create());
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Update))zip.GetEntry("snapshot-manifest.json")!.Delete();
        SqliteConnection.ClearAllPools();
        OfficeRestoreService.Restore(archive,Data,Root);
        Assert.Equal("Original report",File.ReadAllText(Path.Combine(Root,"Final Files","report.txt")));
    }

    [Fact]
    public void DailySchedulingRequiresACompletedFullArchive()
    {
        Assert.True(_archives.IsBackupDue(DateTime.UtcNow));
        var name=_archives.Create();
        Assert.False(_archives.IsBackupDue(DateTime.UtcNow));
        File.SetLastWriteTimeUtc(Path.Combine(_archives.DirectoryPath,name),DateTime.UtcNow.AddHours(-25));
        Assert.True(_archives.IsBackupDue(DateTime.UtcNow));
    }

    [Fact]
    public void LinkedOfficeFolderCannotEscapeBackupRoot()
    {
        var external=Path.Combine(_temporary,"external");
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external,"private.txt"),"Outside office storage");
        // Junction creation does not require Windows Developer Mode or symlink privileges.
        var link=Path.Combine(Root,"linked");
        if(OperatingSystem.IsWindows())
        {
            using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe") {Arguments=$"/c mklink /J \"{link}\" \"{external}\"",UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})!;
            process.WaitForExit();
            Assert.Equal(0,process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link,external);
        try
        {
            Assert.Throws<InvalidOperationException>(()=>_archives.Create());
            Assert.Empty(_archives.List());
        }
        finally{Directory.Delete(link);}
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if(Directory.Exists(_temporary))Directory.Delete(_temporary,true);
    }
}
