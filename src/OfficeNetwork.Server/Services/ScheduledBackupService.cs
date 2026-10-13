namespace OfficeNetwork.Server.Services;

public sealed class ScheduledBackupService(OperationsService operations,OfficeArchiveService archives,UserAccountService users,ILogger<ScheduledBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            // Keep the small database backup independent: a locked office document must not prevent it.
            try
            {
                if(operations.IsDatabaseBackupDue(DateTime.UtcNow))
                {
                    var path=await Task.Run(operations.Backup,stoppingToken);
                    logger.LogInformation("Office database backup created: {FileName}",Path.GetFileName(path));
                }
            }
            catch(Exception ex){logger.LogError(ex,"Scheduled office database backup failed");}
            try
            {
                if(users.Users.Any(x=>x.IsEnabled && x.Role==OfficeNetwork.Shared.OfficeRole.Director) && archives.IsBackupDue(DateTime.UtcNow))
                {
                    var path=await Task.Run(archives.Create,stoppingToken);
                    logger.LogInformation("Full office backup created: {FileName}",Path.GetFileName(path));
                }
            }
            catch(Exception ex){logger.LogError(ex,"Scheduled full office backup failed");}
            try{await Task.Delay(TimeSpan.FromHours(1),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
}
