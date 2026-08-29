// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Platform.Windows.Capture;

namespace TransDuck.Platform.Windows.Tests.Capture;

public sealed class PixelRectTests
{
    [Fact]
    public void Intersect_PreservesNegativeVirtualDesktopCoordinates()
    {
        var leftMonitor = new PixelRect(-1920, 0, 0, 1080);
        var selection = new PixelRect(-1800, 100, 100, 900);

        var intersection = leftMonitor.Intersect(selection);

        Assert.Equal(new PixelRect(-1800, 100, 0, 900), intersection);
    }

    [Fact]
    public void Intersect_ReturnsEmptyRectForNonOverlappingBounds()
    {
        var first = new PixelRect(-200, 0, -100, 100);
        var second = new PixelRect(20, 0, 120, 100);

        var intersection = first.Intersect(second);

        Assert.True(intersection.IsEmpty);
        Assert.Equal(new PixelRect(20, 0, -100, 100), intersection);
    }

    [Fact]
    public void Offset_MovesAllEdgesWithoutChangingSize()
    {
        var source = new PixelRect(-640, 50, -340, 250);

        var offset = source.Offset(1280, -50);

        Assert.Equal(new PixelRect(640, 0, 940, 200), offset);
        Assert.Equal(source.Width, offset.Width);
        Assert.Equal(source.Height, offset.Height);
    }
}
