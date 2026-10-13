using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using OfficeNetwork.Server.Services;
using OfficeNetwork.Shared;
using Xunit;

namespace OfficeNetwork.Server.Tests;

public sealed class AccountSecurityTests : IDisposable
{
    private readonly string _data=Path.Combine(Path.GetTempPath(),"choiceflame-auth-tests-"+Guid.NewGuid().ToString("N"));
    private readonly UserAccountService _users;
    public AccountSecurityTests()=>_users=new UserAccountService(_data);

    [Theory]
    [InlineData(OfficeRole.Editor,true,"ReplacementPass1")]
    [InlineData(OfficeRole.NewsSourcing,true,null)]
    [InlineData(OfficeRole.Editor,false,null)]
    public void AccountEditsRevokeHttpSessionAndDisconnectLiveHub(OfficeRole role,bool enabled,string? password)
    {
        var user=_users.Create(new("writer","Office Writer",OfficeRole.Editor,"OriginalPass1"));
        var login=_users.Login(new("writer","OriginalPass1"))!;
        var disconnected=false;
        _users.TrackConnection("connection-1",login.Token,()=>disconnected=true);
        _users.Update(user.Id,new("writer","Updated Writer",role,enabled,password));
        Assert.Null(_users.FromToken(login.Token));
        Assert.True(disconnected);
        if(password is not null)
        {
            Assert.Null(_users.Login(new("writer","OriginalPass1")));
            Assert.NotNull(_users.Login(new("writer",password)));
        }
    }

    [Fact]
    public void LogoutAndDeletionAlsoRevokeLiveConnections()
    {
        var user=_users.Create(new("writer","Office Writer",OfficeRole.Editor,"OriginalPass1"));
        var first=_users.Login(new("writer","OriginalPass1"))!;
        var aborted=0;
        _users.TrackConnection("first",first.Token,()=>aborted++);
        _users.Logout(first.Token);
        Assert.Null(_users.FromToken(first.Token));
        var second=_users.Login(new("writer","OriginalPass1"))!;
        _users.TrackConnection("second",second.Token,()=>aborted++);
        Assert.True(_users.Delete(user.Id));
        Assert.Null(_users.FromToken(second.Token));
        Assert.Equal(2,aborted);
    }

    [Fact]
    public async Task ConcurrentLoginsCannotBypassSingleComputerPolicy()
    {
        _users.Create(new("writer","Office Writer",OfficeRole.Editor,"OriginalPass1"));
        var attempts=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>
        {
            try{return _users.Login(new("writer","OriginalPass1")) is not null;}
            catch(InvalidOperationException){return false;}
        })));
        Assert.Equal(1,attempts.Count(x=>x));
    }

    [Fact]
    public async Task InitialDirectorCreationCannotRace()
    {
        var attempts=await Task.WhenAll(Enumerable.Range(0,4).Select(i=>Task.Run(()=>
        {
            try{_users.CreateInitialDirector(new("director"+i,"Director",OfficeRole.Director,"OriginalPass1"));return true;}
            catch(InvalidOperationException){return false;}
        })));
        Assert.Equal(1,attempts.Count(x=>x));
        Assert.Single(_users.Users);
    }

    [Theory]
    [InlineData((OfficeRole)999,"OriginalPass1")]
    [InlineData(OfficeRole.ServerAdministrator,"OriginalPass1")]
    [InlineData(OfficeRole.Editor,"short")]
    public void InvalidRolesAndWeakNewPasswordsAreRejected(OfficeRole role,string password)
    {
        Assert.Throws<InvalidOperationException>(()=>_users.Create(new("writer","Writer",role,password)));
        Assert.Empty(_users.Users);
    }

    [Fact]
    public void ExistingShortPasswordStillAuthenticatesAndIsNotRehashedOnNameEdit()
    {
        var id=Guid.NewGuid();
        var salt=RandomNumberGenerator.GetBytes(16);
        var hash=Rfc2898DeriveBytes.Pbkdf2("old",salt,120000,HashAlgorithmName.SHA256,32);
        using(var db=new SqliteConnection($"Data Source={Path.Combine(_data,"choiceflame.db")}"))
        {
            db.Open();
            using var command=db.CreateCommand();
            command.CommandText="INSERT INTO Users VALUES($id,'legacy','Legacy Writer',2,1,$salt,$hash,$at)";
            command.Parameters.AddWithValue("$id",id.ToString());
            command.Parameters.AddWithValue("$salt",salt);
            command.Parameters.AddWithValue("$hash",hash);
            command.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        Assert.NotNull(_users.Login(new("legacy","old")));
        _users.Update(id,new("legacy","Renamed Writer",OfficeRole.Editor,true));
        Assert.NotNull(_users.Login(new("legacy","old")));
        using var check=new SqliteConnection($"Data Source={Path.Combine(_data,"choiceflame.db")}");
        check.Open();
        using var query=check.CreateCommand();
        query.CommandText="SELECT PasswordHash FROM Users WHERE Id=$id";
        query.Parameters.AddWithValue("$id",id.ToString());
        Assert.True(CryptographicOperations.FixedTimeEquals(hash,(byte[])query.ExecuteScalar()!));
    }

    [Fact]
    public void NotificationReadUpdatesAreRestrictedToTheirOwner()
    {
        var ops=new OperationsService(_data);
        var owner=Guid.NewGuid();
        ops.Notify(owner,"Work assigned","Report");
        var notice=Assert.Single(ops.Notifications(owner));
        Assert.False(ops.MarkRead(Guid.NewGuid(),notice.Id));
        Assert.False(Assert.Single(ops.Notifications(owner)).IsRead);
        Assert.True(ops.MarkRead(owner,notice.Id));
        Assert.True(Assert.Single(ops.Notifications(owner)).IsRead);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_data,true);
    }
}
