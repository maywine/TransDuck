// Copyright (c) 2026 maywine. All rights reserved.

namespace TransDuck.Platform.Windows.Tests.Speech;

public sealed class WindowsSystemSpeechPlayerSourceTests
{
    [Fact]
    public void Player_UsesDesktopSapiAndWaitsForSpeakCompleted()
    {
        var source = ReadSource();

        Assert.Contains("using System.Speech.Synthesis", source, StringComparison.Ordinal);
        Assert.Contains("new SpeechSynthesizer()", source, StringComparison.Ordinal);
        Assert.Contains("synthesizer.SpeakCompleted += speakCompleted", source,
            StringComparison.Ordinal);
        Assert.Contains("synthesizer.SpeakAsync(text)", source, StringComparison.Ordinal);
        Assert.Contains("await completion.Task", source, StringComparison.Ordinal);
        Assert.Contains("SpeechPlaybackStatus.Completed", source, StringComparison.Ordinal);
        Assert.Contains("SpeechPlaybackStatus.Failed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_CancelsThePriorRequestAndReleasesTheDesktopSynthesizer()
    {
        var source = ReadSource();

        Assert.Contains("CancelNonFatal(_currentCancellation)", source, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource", source,
            StringComparison.Ordinal);
        Assert.Contains("synthesizer.SpeakAsyncCancelAll()", source, StringComparison.Ordinal);
        Assert.Contains("SpeechPlaybackStatus.Cancelled", source, StringComparison.Ordinal);
        Assert.Contains("synthesizer.SpeakCompleted -= speakCompleted", source,
            StringComparison.Ordinal);
        Assert.Contains("DisposeNonFatal(synthesizer)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_DoesNotUseWinRtOrRemoteDictionaryAudio()
    {
        var source = ReadSource();

        Assert.DoesNotContain("Windows.Media.SpeechSynthesis", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaSource", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaPlayer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audioUrl", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSource()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "windows",
                    "src",
                    "TransDuck.Platform.Windows",
                    "Speech",
                    "WindowsSystemSpeechPlayer.cs");
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
            }
        }

        throw new FileNotFoundException("WindowsSystemSpeechPlayer.cs was not found from the test host path.");
    }
}
