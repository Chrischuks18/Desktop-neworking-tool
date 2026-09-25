using System.Threading;
using System.Windows;

namespace OfficeNetwork.Client;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName=@"Local\ChoiceFlameCommunicationsNetwork.Client";
    private const string RestoreEventName=@"Local\ChoiceFlameCommunicationsNetwork.Restore";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _restoreEvent;
    private RegisteredWaitHandle? _restoreRegistration;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex=new Mutex(true,InstanceMutexName,out var isFirstInstance);
        if(!isFirstInstance)
        {
            try
            {
                using var restore=EventWaitHandle.OpenExisting(RestoreEventName);
                restore.Set();
            }
            catch { }
            Shutdown();
            return;
        }

        _restoreEvent=new EventWaitHandle(false,EventResetMode.AutoReset,RestoreEventName);
        _restoreRegistration=ThreadPool.RegisterWaitForSingleObject(_restoreEvent,(_,_) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if(MainWindow is MainWindow window)window.RestoreFromExternalLaunch();
            });
        },null,Timeout.Infinite,false);

        base.OnStartup(e);
        var window=new MainWindow();
        MainWindow=window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _restoreRegistration?.Unregister(null);
        _restoreEvent?.Dispose();
        if(_instanceMutex is not null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch { }
            _instanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
