using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Capture;

/// <summary>
/// Shows one Avalonia overlay per monitor and returns a single-monitor physical-pixel selection.
/// </summary>
public sealed class ScreenSelectionOverlay
{
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;
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
                _dispatcher.Post(CancelSelection);
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
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        CanResize = false;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        Focusable = true;

        _canvas = new Canvas { Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)) };
        _selectionRectangle = new Rectangle
        {
            IsVisible = false,
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(50, 30, 144, 255)),
        };
        _canvas.Children.Add(_selectionRectangle);
        Content = _canvas;
        Opened += PositionOverMonitor;
        Closed += HandleClosed;
    }

    public event EventHandler<ScreenSelection>? SelectionCompleted;

    public event Action? SelectionCancelled;

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        if (!eventArgs.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        _start = eventArgs.GetPosition(_canvas);
        eventArgs.Pointer.Capture(_canvas);
        eventArgs.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        if (_start is not { } start ||
            !eventArgs.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        ShowSelection(start, eventArgs.GetPosition(_canvas));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs eventArgs)
    {
        base.OnPointerReleased(eventArgs);
        if (_start is not { } start)
        {
            return;
        }

        eventArgs.Pointer.Capture(null);
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

    private void HandleClosed(object? sender, EventArgs eventArgs)
    {
        Opened -= PositionOverMonitor;
        Closed -= HandleClosed;
        if (!_completed)
        {
            SelectionCancelled?.Invoke();
        }
    }

    private void PositionOverMonitor(object? sender, EventArgs eventArgs)
    {
        var bounds = _monitor.PhysicalBounds;
        Win32DisplayNative.SetWindowPos(
            this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
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
        _selectionRectangle.IsVisible = true;
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
