using TransDuck.Platform.MacOS.Selection;

namespace TransDuck.Platform.MacOS.Tests.Selection;

public sealed class MacAccessibilitySelectionServiceTests
{
    [Fact]
    public void PermissionDenial_DoesNotAttemptToReadSelection()
    {
        var backend = new FakeAccessibilityBackend { Trusted = false };
        var service = new MacAccessibilitySelectionService(backend);

        var result = service.ReadSelectedText(promptForPermission: true);

        Assert.Equal(MacSelectionStatus.PermissionRequired, result.Status);
        Assert.Null(result.Text);
        Assert.True(backend.LastPrompt);
        Assert.Equal(0, backend.ReadCount);
    }

    [Fact]
    public void SuccessfulRead_PreservesSelectedTextExactly()
    {
        const string selected = " selected text\n第二行 ";
        var backend = new FakeAccessibilityBackend
        {
            Trusted = true,
            ReadResult = new MacAccessibilityReadResult(MacAccessibilityReadStatus.Succeeded, selected),
        };
        var service = new MacAccessibilitySelectionService(backend);

        var result = service.ReadSelectedText();

        Assert.True(result.Succeeded);
        Assert.Equal(selected, result.Text);
    }

    [Theory]
    [InlineData(MacAccessibilityReadStatus.NoFocusedElement, MacSelectionStatus.NoFocusedElement)]
    [InlineData(MacAccessibilityReadStatus.NoValue, MacSelectionStatus.NoSelection)]
    [InlineData(MacAccessibilityReadStatus.Unsupported, MacSelectionStatus.Unsupported)]
    [InlineData(MacAccessibilityReadStatus.Failed, MacSelectionStatus.Failed)]
    public void BackendFailures_MapToClosedStatuses(
        MacAccessibilityReadStatus backendStatus,
        MacSelectionStatus expected)
    {
        var backend = new FakeAccessibilityBackend
        {
            Trusted = true,
            ReadResult = new MacAccessibilityReadResult(backendStatus),
        };
        var service = new MacAccessibilitySelectionService(backend);

        var result = service.ReadSelectedText();

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Text);
    }

    [Fact]
    public void EmptySuccessfulRead_IsReportedAsNoSelection()
    {
        var backend = new FakeAccessibilityBackend
        {
            Trusted = true,
            ReadResult = new MacAccessibilityReadResult(MacAccessibilityReadStatus.Succeeded, "  \n"),
        };

        var result = new MacAccessibilitySelectionService(backend).ReadSelectedText();

        Assert.Equal(MacSelectionStatus.NoSelection, result.Status);
    }

    [Fact]
    public async Task UnsupportedAccessibilitySelection_UsesCopyFallback()
    {
        var backend = new FakeAccessibilityBackend
        {
            Trusted = true,
            ReadResult = new MacAccessibilityReadResult(MacAccessibilityReadStatus.Unsupported),
        };
        var copy = new FakeSelectionCopyBackend { Text = " selected in Feishu " };
        var service = new MacAccessibilitySelectionService(backend, copy);

        var result = await service.ReadSelectedTextAsync();

        Assert.Equal(MacSelectionStatus.Succeeded, result.Status);
        Assert.Equal(" selected in Feishu ", result.Text);
        Assert.Equal(1, copy.ReadCount);
    }

    [Fact]
    public async Task PermissionDenial_DoesNotUseCopyFallback()
    {
        var copy = new FakeSelectionCopyBackend { Text = "unexpected" };
        var service = new MacAccessibilitySelectionService(
            new FakeAccessibilityBackend { Trusted = false }, copy);

        var result = await service.ReadSelectedTextAsync();

        Assert.Equal(MacSelectionStatus.PermissionRequired, result.Status);
        Assert.Equal(0, copy.ReadCount);
    }

    [Fact]
    public async Task CopyFallbackWithoutText_PreservesAccessibilityFailure()
    {
        var backend = new FakeAccessibilityBackend
        {
            Trusted = true,
            ReadResult = new MacAccessibilityReadResult(MacAccessibilityReadStatus.NoFocusedElement),
        };
        var copy = new FakeSelectionCopyBackend { Text = null };
        var service = new MacAccessibilitySelectionService(backend, copy);

        var result = await service.ReadSelectedTextAsync();

        Assert.Equal(MacSelectionStatus.NoFocusedElement, result.Status);
        Assert.Equal(1, copy.ReadCount);
    }
}

internal sealed class FakeSelectionCopyBackend : IMacSelectionCopyBackend
{
    public string? Text { get; init; }

    public int ReadCount { get; private set; }

    public Task<string?> ReadSelectedTextAsync(CancellationToken cancellationToken)
    {
        ReadCount++;
        return Task.FromResult(Text);
    }
}

internal sealed class FakeAccessibilityBackend : IMacAccessibilityBackend
{
    public bool Trusted { get; init; }

    public bool LastPrompt { get; private set; }

    public int ReadCount { get; private set; }

    public MacAccessibilityReadResult ReadResult { get; init; } =
        new(MacAccessibilityReadStatus.NoValue);

    public bool IsProcessTrusted(bool prompt)
    {
        LastPrompt = prompt;
        return Trusted;
    }

    public MacAccessibilityReadResult ReadSelectedText()
    {
        ReadCount++;
        return ReadResult;
    }
}
