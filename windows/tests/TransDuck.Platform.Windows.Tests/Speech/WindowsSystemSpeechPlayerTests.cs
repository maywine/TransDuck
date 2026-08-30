// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Lookup;
using TransDuck.Platform.Windows.Speech;

namespace TransDuck.Platform.Windows.Tests.Speech;

public sealed class WindowsSystemSpeechPlayerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SpeakAsync_RejectsEmptyTermsBeforeTouchingWindowsSpeech(string term)
    {
        using var player = new WindowsSystemSpeechPlayer();

        var result = await player.SpeakAsync(term, CancellationToken.None);

        Assert.Equal(SpeechPlaybackStatus.InvalidText, result.Status);
    }

    [Fact]
    public async Task SpeakAsync_RejectsTermsLongerThanDictionaryEntries()
    {
        using var player = new WindowsSystemSpeechPlayer();

        var result = await player.SpeakAsync(new string('a', 513), CancellationToken.None);

        Assert.Equal(SpeechPlaybackStatus.InvalidText, result.Status);
    }

    [Fact]
    public async Task SpeakAsync_HonorsPreCancellationBeforeTouchingWindowsSpeech()
    {
        using var player = new WindowsSystemSpeechPlayer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await player.SpeakAsync("duck", cancellation.Token);

        Assert.Equal(SpeechPlaybackStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task SpeakAsync_ReturnsUnavailableOutsideWindowsWithoutNativeActivation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var player = new WindowsSystemSpeechPlayer();
        var result = await player.SpeakAsync("duck", CancellationToken.None);

        Assert.Equal(SpeechPlaybackStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Dispose_PreventsLaterPlaybackAndStopRemainsIdempotent()
    {
        var player = new WindowsSystemSpeechPlayer();

        player.Stop();
        player.Dispose();
        player.Stop();
        player.Dispose();
        var result = await player.SpeakAsync("duck", CancellationToken.None);

        Assert.Equal(SpeechPlaybackStatus.Unavailable, result.Status);
    }
}
