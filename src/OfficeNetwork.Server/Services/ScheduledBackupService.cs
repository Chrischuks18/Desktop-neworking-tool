using OfficeNetwork.Server.Services;

namespace OfficeNetwork.Server.Services;

public sealed class ScheduledBackupService(OperationsService operations,ILogger<ScheduledBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var backupFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Choice Flame Communications Network","Backups");
                Directory.CreateDirectory(backupFolder);
                var newest=Directory.GetFiles(backupFolder,"choiceflame-*.db").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
                if(DateTime.UtcNow-newest>=TimeSpan.FromHours(24))
                {
                    var path=operations.Backup();
                    logger.LogInformation("Office database backup created: {FileName}",Path.GetFileName(path));
                }
            }
            catch(Exception ex){logger.LogError(ex,"Scheduled office database backup failed");}
            try{await Task.Delay(TimeSpan.FromHours(1),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
}
