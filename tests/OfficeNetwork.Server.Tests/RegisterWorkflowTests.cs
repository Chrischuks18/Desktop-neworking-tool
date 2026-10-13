using Microsoft.Data.Sqlite;
using OfficeNetwork.Server.Services;
using OfficeNetwork.Shared;
using Xunit;

namespace OfficeNetwork.Server.Tests;

public sealed class RegisterWorkflowTests : IDisposable
{
    private readonly string _data=Path.Combine(Path.GetTempPath(),"choiceflame-register-tests-"+Guid.NewGuid().ToString("N"));

    [Fact]
    public void NewsCanMoveThroughProductionWithoutCreatingDuplicateRecords()
    {
        var service=new ProductionTrackerService(_data);
        var story=service.Add(new(Guid.Empty,"Parish anniversary","Idea","Editor","2026-10-20","Interview parish leaders"));
        foreach(var stage in new[]{"Research","Writing","Editing","Approved","Published"})
        {
            var updated=service.Update(story.Id,story with {Stage=stage,Notes="Updated production note"});
            Assert.NotNull(updated);
            Assert.Equal(story.Id,updated.Id);
        }
        var persisted=Assert.Single(new ProductionTrackerService(_data).List());
        Assert.Equal("Published",persisted.Stage);
        Assert.Equal(story.Id,persisted.Id);
    }

    [Fact]
    public void MeetingMinutesAndActionItemsCanBeRevisedAndPersist()
    {
        var service=new MeetingMinutesService(_data);
        var meeting=service.Add(new(Guid.Empty,"Editorial meeting","2026-10-13","Director","Approved next edition","Editor: submit draft by Friday"));
        Assert.NotNull(service.Update(meeting.Id,meeting with {Minutes="Approved next edition and budget",ActionItems="Editor: draft submitted; Director: review by Friday"}));
        var persisted=Assert.Single(new MeetingMinutesService(_data).List());
        Assert.Equal(meeting.Id,persisted.Id);
        Assert.Contains("budget",persisted.Minutes);
        Assert.Contains("draft submitted",persisted.ActionItems);
    }

    [Fact]
    public void EquipmentCustodyAndConditionCanChangeWithoutLosingIdentity()
    {
        var service=new EquipmentRegisterService(_data);
        var asset=service.Add(new(Guid.Empty,"CAM-001","Office camera","Studio","Editor","Good",""));
        Assert.NotNull(service.Update(asset.Id,asset with {Custodian="Technician",Condition="Under Repair",Location="Workshop"}));
        var persisted=Assert.Single(new EquipmentRegisterService(_data).List());
        Assert.Equal(asset.Id,persisted.Id);
        Assert.Equal("CAM-001",persisted.AssetTag);
        Assert.Equal("Technician",persisted.Custodian);
        Assert.Equal("Under Repair",persisted.Condition);
        Assert.Throws<SqliteException>(()=>service.Add(asset with {Id=Guid.Empty}));
        Assert.Single(service.List());
    }

    [Fact]
    public void DuplicateAssetTagUpdateLeavesBothExistingAssetsIntact()
    {
        var service=new EquipmentRegisterService(_data);
        var camera=service.Add(new(Guid.Empty,"CAM-001","Camera","Studio","Editor","Good",""));
        var computer=service.Add(new(Guid.Empty,"PC-001","Computer","Office","Director","Good",""));
        Assert.Throws<SqliteException>(()=>service.Update(computer.Id,computer with {AssetTag=camera.AssetTag}));
        Assert.Equal(2,service.List().Length);
        Assert.Equal("PC-001",service.List().Single(x=>x.Id==computer.Id).AssetTag);
    }

    [Fact]
    public void InvalidUpdateCannotOverwriteAnExistingStory()
    {
        var service=new ProductionTrackerService(_data);
        var story=service.Add(new(Guid.Empty,"Original title","Writing","Editor","","Original notes"));
        Assert.Throws<ArgumentException>(()=>service.Update(story.Id,story with {Title=""}));
        Assert.Equal(story,Assert.Single(service.List()));
        Assert.Null(service.Update(Guid.NewGuid(),story));
        Assert.Single(service.List());
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("not-a-date")]
    public void InvalidDatesAreRejected(string date)
    {
        Assert.Throws<ArgumentException>(()=>new ProductionTrackerService(_data).Add(new(Guid.Empty,"Story","Idea","Editor",date,"")));
        Assert.Throws<ArgumentException>(()=>new MeetingMinutesService(_data).Add(new(Guid.Empty,"Meeting",date,"Director","Decisions","")));
    }

    [Fact]
    public void OpeningExistingRegisterSchemaDoesNotModifyLegacyRows()
    {
        Directory.CreateDirectory(_data);
        var id=Guid.NewGuid();
        using(var db=new SqliteConnection($"Data Source={Path.Combine(_data,"choiceflame.db")}"))
        {
            db.Open();using var command=db.CreateCommand();
            command.CommandText="CREATE TABLE NewsProduction(Id TEXT PRIMARY KEY,Title TEXT NOT NULL,Stage TEXT NOT NULL,AssignedTo TEXT NOT NULL,Deadline TEXT NOT NULL,Notes TEXT NOT NULL); INSERT INTO NewsProduction VALUES($id,'Existing story','Legacy stage','Existing staff','13/10/2026','Preserve these notes');";
            command.Parameters.AddWithValue("$id",id.ToString());command.ExecuteNonQuery();
        }
        var stored=Assert.Single(new ProductionTrackerService(_data).List());
        Assert.Equal(id,stored.Id);
        Assert.Equal("Legacy stage",stored.Stage);
        Assert.Equal("13/10/2026",stored.Deadline);
        Assert.Equal("Preserve these notes",stored.Notes);
    }

    [Fact]
    public void AllThreeRegistersSurviveFullBackupAndRestore()
    {
        var users=new UserAccountService(_data);
        users.CreateInitialDirector(new("director","Director",OfficeRole.Director,"OriginalPass1"));
        var production=new ProductionTrackerService(_data);
        var minutes=new MeetingMinutesService(_data);
        var equipment=new EquipmentRegisterService(_data);
        var story=production.Add(new(Guid.Empty,"Story","Writing","Editor","2026-10-20","Original notes"));
        var meeting=minutes.Add(new(Guid.Empty,"Meeting","2026-10-13","Director","Original minutes","Action for Editor"));
        var asset=equipment.Add(new(Guid.Empty,"CAM-1","Camera","Studio","Editor","Good","Original equipment note"));
        var root=_data+"-office";
        var config=new OfficeConfigurationService(_data,root);
        var archives=new OfficeArchiveService(new OperationsService(_data),config);
        try
        {
            var archive=Path.Combine(archives.DirectoryPath,archives.Create());
            production.Update(story.Id,story with {Stage="Published"});
            minutes.Update(meeting.Id,meeting with {Minutes="Later minutes"});
            equipment.Update(asset.Id,asset with {Condition="Retired"});
            SqliteConnection.ClearAllPools();
            var restored=OfficeRestoreService.Restore(archive,_data,root);
            Assert.Equal(story,Assert.Single(production.List()));
            Assert.Equal(meeting,Assert.Single(minutes.List()));
            Assert.Equal(asset,Assert.Single(equipment.List()));
            Directory.Delete(restored.PreviousOfficeFiles,true);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if(Directory.Exists(_data))Directory.Delete(_data,true);
    }
}
