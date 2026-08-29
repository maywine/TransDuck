using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Windows.Threading;
using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Capture;

/// <summary>
/// Shows one WPF overlay per monitor and returns a single-monitor physical-pixel selection.
/// </summary>
public sealed class ScreenSelectionOverlay
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly List<SelectionOverlayWindow> _windows = [];
    private TaskCompletionSource<ScreenSelection?>? _completion;

    public Task<ScreenSelection?> SelectAsync(CancellationToken cancellationToken)
    {
        if (_completion is not null)
        {
            throw new InvalidOperationException("A screen selection is already active.");
        }

        var monitors = MonitorTopology.GetMonitors();
        if (monitors.Count == 0)
        {
            return Task.FromResult<ScreenSelection?>(null);
        }

        _completion = new TaskCompletionSource<ScreenSelection?>(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var monitor in monitors)
        {
            var window = new SelectionOverlayWindow(monitor);
            window.SelectionCompleted += CompleteSelection;
            window.SelectionCancelled += CancelSelection;
            _windows.Add(window);
            window.Show();
        }

        var registration = cancellationToken.Register(() =>
        {
            if (_dispatcher.CheckAccess())
            {
                CancelSelection();
            }
            else
            {
                _dispatcher.BeginInvoke(CancelSelection);
            }
        });
        _ = _completion.Task.ContinueWith(
            _ => registration.Dispose(),
            TaskScheduler.Default);
        return _completion.Task;
    }

    private void CompleteSelection(object? sender, ScreenSelection selection)
    {
        if (_completion?.TrySetResult(selection) == true)
        {
            CloseWindows();
        }
    }

    private void CancelSelection()
    {
        if (_completion?.TrySetResult(null) == true)
        {
            CloseWindows();
        }
    }

    private void CloseWindows()
    {
        foreach (var window in _windows.ToArray())
        {
            window.SelectionCompleted -= CompleteSelection;
            window.SelectionCancelled -= CancelSelection;
            window.Close();
        }

        _windows.Clear();
        _completion = null;
    }
}

internal sealed class SelectionOverlayWindow : Window
{
    private readonly DisplayMonitor _monitor;
    private readonly Canvas _canvas;
    private readonly Rectangle _selectionRectangle;
    private Point? _start;
    private bool _completed;

    public SelectionOverlayWindow(DisplayMonitor monitor)
    {
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        Focusable = true;

        _canvas = new Canvas { Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)) };
        _selectionRectangle = new Rectangle
        {
            Visibility = Visibility.Collapsed,
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(50, 30, 144, 255)),
        };
        _canvas.Children.Add(_selectionRectangle);
        Content = _canvas;
        SourceInitialized += PositionOverMonitor;
    }

    public event EventHandler<ScreenSelection>? SelectionCompleted;

    public event Action? SelectionCancelled;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs eventArgs)
    {
        base.OnPreviewMouseLeftButtonDown(eventArgs);
        Focus();
        _start = eventArgs.GetPosition(_canvas);
        Mouse.Capture(_canvas);
        eventArgs.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs eventArgs)
    {
        base.OnPreviewMouseMove(eventArgs);
        if (_start is not { } start || eventArgs.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        ShowSelection(start, eventArgs.GetPosition(_canvas));
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs eventArgs)
    {
        base.OnPreviewMouseLeftButtonUp(eventArgs);
        if (_start is not { } start)
        {
            return;
        }

        Mouse.Capture(null);
        _start = null;
        var selection = ToSelection(start, eventArgs.GetPosition(_canvas));
        if (selection.PhysicalBounds.IsEmpty)
        {
            return;
        }

        _completed = true;
        SelectionCompleted?.Invoke(this, selection);
        eventArgs.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            _completed = true;
            SelectionCancelled?.Invoke();
            eventArgs.Handled = true;
        }

        base.OnKeyDown(eventArgs);
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        SourceInitialized -= PositionOverMonitor;
        if (!_completed)
        {
            SelectionCancelled?.Invoke();
        }

        base.OnClosed(eventArgs);
    }

    private void PositionOverMonitor(object? sender, EventArgs eventArgs)
    {
        var bounds = _monitor.PhysicalBounds;
        Win32DisplayNative.SetWindowPos(
            new WindowInteropHelper(this).Handle,
            new IntPtr(-1),
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            Win32DisplayNative.SwpNoActivate | Win32DisplayNative.SwpShowWindow);
    }

    private void ShowSelection(Point start, Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        Canvas.SetLeft(_selectionRectangle, left);
        Canvas.SetTop(_selectionRectangle, top);
        _selectionRectangle.Width = Math.Abs(end.X - start.X);
        _selectionRectangle.Height = Math.Abs(end.Y - start.Y);
        _selectionRectangle.Visibility = Visibility.Visible;
    }

    private ScreenSelection ToSelection(Point start, Point end)
    {
        var scaleX = _monitor.DpiX / 96d;
        var scaleY = _monitor.DpiY / 96d;
        var left = _monitor.PhysicalBounds.Left + (int)Math.Floor(Math.Min(start.X, end.X) * scaleX);
        var top = _monitor.PhysicalBounds.Top + (int)Math.Floor(Math.Min(start.Y, end.Y) * scaleY);
        var right = _monitor.PhysicalBounds.Left + (int)Math.Ceiling(Math.Max(start.X, end.X) * scaleX);
        var bottom = _monitor.PhysicalBounds.Top + (int)Math.Ceiling(Math.Max(start.Y, end.Y) * scaleY);
        var physicalBounds = new PixelRect(left, top, right, bottom)
            .Intersect(_monitor.PhysicalBounds);
        return new ScreenSelection(_monitor, physicalBounds);
    }
}
