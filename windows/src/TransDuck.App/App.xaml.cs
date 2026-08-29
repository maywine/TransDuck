using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using TransDuck.App.Services;

namespace TransDuck.App;

/// <summary>
/// Starts and stops the explicit tray-hosted application lifetime.
/// </summary>
public partial class App : Application
{
    private AppRuntime? _runtime;
    private Mutex? _sessionMutex;
    private bool _ownsSessionMutex;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        try
        {
            if (!TryAcquireSessionMutex())
            {
                Shutdown();
                return;
            }

            AppStrings.InitializeForCurrentCulture();
            _runtime = new AppRuntime();
            await _runtime.StartAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (_runtime is { } runtime)
            {
                try
                {
                    await runtime.StopAsync();
                }
                catch (Exception stoppingException) when (
                    stoppingException is not OutOfMemoryException and not StackOverflowException)
                {
                    // Startup failure still shuts down after a nonfatal stopping failure.
                }
            }

            _runtime = null;
            MessageBox.Show(AppStrings.Get("app.startup.failure"), AppStrings.Get("app.brand"));
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _runtime?.Dispose();
        _runtime = null;
        ReleaseSessionMutex();
        base.OnExit(eventArgs);
    }

    private bool TryAcquireSessionMutex()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Current user SID is unavailable.");
        using var process = Process.GetCurrentProcess();
        var sessionId = process.SessionId;
        var mutexName = $"Local\\TransDuck.Windows.{sid}.{sessionId}";
        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        // A second tray host would compete for the same global hotkey, so it exits before constructing AppRuntime.
        _sessionMutex = mutex;
        _ownsSessionMutex = true;
        return true;
    }

    private void ReleaseSessionMutex()
    {
        var mutex = _sessionMutex;
        _sessionMutex = null;
        try
        {
            if (_ownsSessionMutex)
            {
                mutex?.ReleaseMutex();
            }
        }
        catch (ApplicationException)
        {
            // Shutdown must still release the handle when an abnormal startup path has already relinquished ownership.
        }
        finally
        {
            _ownsSessionMutex = false;
            mutex?.Dispose();
        }
    }
}
