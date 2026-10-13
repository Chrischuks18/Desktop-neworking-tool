using OfficeNetwork.Server.Services;

namespace OfficeNetwork.Server.Services;

public sealed class ScheduledBackupService(OfficeArchiveService archives,ILogger<ScheduledBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if(archives.IsBackupDue(DateTime.UtcNow))
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
