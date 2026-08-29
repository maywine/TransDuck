// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Platform.Windows.Capture;

namespace TransDuck.Platform.Windows.Tests.Capture;

public sealed class ScreenSelectionTests
{
    private static readonly DisplayMonitor LeftMonitor = new(
        new IntPtr(42),
        new PixelRect(-1920, 0, 0, 1080),
        DpiX: 144,
        DpiY: 144,
        DeviceName: "DISPLAY1");

    [Fact]
    public void IsValid_AcceptsNonEmptySelectionInsideSingleNegativeCoordinateMonitor()
    {
        var selection = new ScreenSelection(LeftMonitor, new PixelRect(-1800, 100, -200, 900));

        Assert.True(selection.IsValid);
    }

    [Fact]
    public void IsValid_RejectsEmptyOrCrossMonitorSelection()
    {
        var empty = new ScreenSelection(LeftMonitor, new PixelRect(-100, 200, -100, 300));
        var crossMonitor = new ScreenSelection(LeftMonitor, new PixelRect(-100, 200, 100, 300));

        Assert.False(empty.IsValid);
        Assert.False(crossMonitor.IsValid);
    }
}
