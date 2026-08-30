using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using TransDuck.MacOS.App.Views;

namespace TransDuck.MacOS.App;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MacAppRuntime? _runtime;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;
    private int _stopping;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _runtime = new MacAppRuntime();
            _mainWindow = new MainWindow(_runtime);
            if (!Program.StartInBackground)
            {
                desktop.MainWindow = _mainWindow;
            }
            desktop.Exit += HandleDesktopExit;
            desktop.ShutdownRequested += HandleShutdownRequested;
            _runtime.PresentationRequested += HandlePresentationRequested;
            _ = InitializeRuntimeAsync(_runtime);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeRuntimeAsync(MacAppRuntime runtime)
    {
        try
        {
            await runtime.InitializeAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            runtime.ReportStartupFailure();
        }
    }

    private void HandleOpenRequested(object? sender, EventArgs eventArgs) => ShowMainWindow();

    private void HandleSelectionRequested(object? sender, EventArgs eventArgs)
    {
        ShowMainWindow();
        if (_runtime is { } runtime)
        {
            _ = runtime.TranslateSelectedTextAsync(promptForPermission: true);
        }
    }

    private void HandleEnglishOcrRequested(object? sender, EventArgs eventArgs)
    {
        ShowMainWindow();
        if (_runtime is { } runtime)
        {
            _ = runtime.CaptureOcrAndTranslateAsync("en-US");
        }
    }

    private void HandleChineseOcrRequested(object? sender, EventArgs eventArgs)
    {
        ShowMainWindow();
        if (_runtime is { } runtime)
        {
            _ = runtime.CaptureOcrAndTranslateAsync("zh-Hans");
        }
    }

    private void HandleHistoryRequested(object? sender, EventArgs eventArgs)
        => ShowHistoryWindow();

    internal void ShowHistoryWindow()
    {
        if (_runtime is null)
        {
            return;
        }

        _historyWindow ??= new HistoryWindow(_runtime);
        Present(_historyWindow);
    }

    private void HandleSettingsRequested(object? sender, EventArgs eventArgs)
        => ShowSettingsWindow();

    internal void ShowSettingsWindow()
    {
        if (_runtime is null)
        {
            return;
        }

        _settingsWindow ??= new SettingsWindow(_runtime);
        Present(_settingsWindow);
    }

    private void HandleQuitRequested(object? sender, EventArgs eventArgs) => _ = StopAsync();

    private void HandleShutdownRequested(object? sender, ShutdownRequestedEventArgs eventArgs)
    {
        if (Volatile.Read(ref _stopping) != 0)
        {
            return;
        }

        eventArgs.Cancel = true;
        _ = StopAsync();
    }

    private void HandlePresentationRequested(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(ShowMainWindow);

    private void ShowMainWindow()
    {
        if (_mainWindow is not null)
        {
            Present(_mainWindow);
        }
    }

    private static void Present(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private async Task StopAsync(int exitCode = 0)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return;
        }

        if (_runtime is { } runtime)
        {
            runtime.PresentationRequested -= HandlePresentationRequested;
            _mainWindow?.PrepareForShutdown();
            _settingsWindow?.PrepareForShutdown();
            _historyWindow?.PrepareForShutdown();
            await runtime.DisposeAsync();
            _settingsWindow?.Close();
            _historyWindow?.Close();
            _mainWindow?.Close();
        }

        _runtime = null;
        _desktop?.Shutdown(exitCode);
    }

    private void HandleDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        if (_desktop is { } desktop)
        {
            desktop.ShutdownRequested -= HandleShutdownRequested;
        }

        if (_runtime is { } runtime)
        {
            runtime.PresentationRequested -= HandlePresentationRequested;
            _mainWindow?.PrepareForShutdown();
            _settingsWindow?.PrepareForShutdown();
            _historyWindow?.PrepareForShutdown();
            var disposal = runtime.DisposeAsync().AsTask();
            _ = disposal.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            _runtime = null;
        }

        _settingsWindow = null;
        _historyWindow = null;
        _mainWindow = null;
        _desktop = null;
    }
}
