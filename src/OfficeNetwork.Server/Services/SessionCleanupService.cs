namespace OfficeNetwork.Server.Services;

public sealed class SessionCleanupService(UserAccountService users) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while(await timer.WaitForNextTickAsync(stoppingToken))users.RemoveExpiredSessions();
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
    }
}
