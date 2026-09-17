using System.Threading;
using System.Windows;

namespace Click2Key;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\Click2Key.CatNineKeyMapper.1.0.0";
    private const string ActivationEventName = @"Local\Click2Key.CatNineKeyMapper.Activate.1.0.0";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationCancellation;
    private Task? _activationListener;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _instanceMutex = new Mutex(true, InstanceMutexName, out _ownsMutex);

        if (!_ownsMutex)
        {
            try { _activationEvent.Set(); }
            finally
            {
                _activationEvent.Dispose();
                _instanceMutex.Dispose();
            }

            Shutdown();
            return;
        }

        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        _activationCancellation = new CancellationTokenSource();
        var cancellation = _activationCancellation;
        var activationEvent = _activationEvent;
        _activationListener = Task.Run(() => ListenForActivation(activationEvent, cancellation.Token));
    }

    private void ListenForActivation(EventWaitHandle activationEvent, CancellationToken cancellationToken)
    {
        var waitHandles = new WaitHandle[] { activationEvent, cancellationToken.WaitHandle };
        while (WaitHandle.WaitAny(waitHandles) == 0)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow window)
                    window.RestoreWindow();
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationCancellation?.Cancel();
        try { _activationListener?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }

        _activationCancellation?.Dispose();
        _activationEvent?.Dispose();
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
