using OfficeNetwork.Server.Services;

namespace OfficeNetwork.Server.Services;

public sealed class ScheduledBackupService(OperationsService operations,OfficeArchiveService archives,ILogger<ScheduledBackupService> logger) : BackgroundService
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
            try
            {
                var last=archives.List().FirstOrDefault();
                if(last is null || !DateTime.TryParseExact(last.Substring(7,15),"yyyyMMdd-HHmmss",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var date) || DateTime.UtcNow-date>=TimeSpan.FromHours(24))
                    logger.LogInformation("Full office archive created: {Archive}",archives.Create());
            }
            catch(Exception ex){logger.LogError(ex,"Scheduled full office archive failed");}
            try{await Task.Delay(TimeSpan.FromHours(1),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
}
