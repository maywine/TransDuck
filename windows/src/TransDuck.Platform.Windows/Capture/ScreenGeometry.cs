using System.Runtime.InteropServices;
using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Capture;

/// <summary>
/// Describes desktop geometry in virtual-screen physical pixels, never device-independent UI units.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelRect Intersect(PixelRect other) => new(
        Math.Max(Left, other.Left),
        Math.Max(Top, other.Top),
        Math.Min(Right, other.Right),
        Math.Min(Bottom, other.Bottom));

    public PixelRect Offset(int horizontal, int vertical) => new(
        Left + horizontal,
        Top + vertical,
        Right + horizontal,
        Bottom + vertical);
}

public sealed record DisplayMonitor(
    IntPtr Handle,
    PixelRect PhysicalBounds,
    uint DpiX,
    uint DpiY,
    string DeviceName);

public sealed record ScreenSelection(DisplayMonitor Monitor, PixelRect PhysicalBounds)
{
    public bool IsValid => !PhysicalBounds.IsEmpty &&
        PhysicalBounds.Intersect(Monitor.PhysicalBounds) == PhysicalBounds;
}

public static class MonitorTopology
{
    private const uint DefaultDpi = 96;

    public static IReadOnlyList<DisplayMonitor> GetMonitors()
    {
        var monitors = new List<DisplayMonitor>();
        Win32DisplayNative.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx
            {
                Size = (uint)Marshal.SizeOf<MonitorInfoEx>(),
                DeviceName = string.Empty,
            };
            if (!Win32DisplayNative.GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            var bounds = new PixelRect(
                info.Monitor.Left,
                info.Monitor.Top,
                info.Monitor.Right,
                info.Monitor.Bottom);
            var (dpiX, dpiY) = GetDpi(monitor);
            monitors.Add(new DisplayMonitor(monitor, bounds, dpiX, dpiY, info.DeviceName));
            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    private static (uint DpiX, uint DpiY) GetDpi(IntPtr monitor)
    {
        try
        {
            return Win32DisplayNative.GetDpiForMonitor(
                monitor,
                MonitorDpiType.EffectiveDpi,
                out var dpiX,
                out var dpiY) == 0
                ? (dpiX, dpiY)
                : (DefaultDpi, DefaultDpi);
        }
        catch (EntryPointNotFoundException)
        {
            return (DefaultDpi, DefaultDpi);
        }
    }
}
