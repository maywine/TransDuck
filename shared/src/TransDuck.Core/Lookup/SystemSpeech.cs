// Copyright (c) 2026 maywine. All rights reserved.

namespace TransDuck.Core.Lookup;

public enum SpeechPlaybackStatus
{
    Completed,
    InvalidText,
    Unavailable,
    Failed,
    Cancelled,
}

public readonly record struct SpeechPlaybackResult(SpeechPlaybackStatus Status)
{
    public bool Succeeded => Status == SpeechPlaybackStatus.Completed;
}

public interface ISystemSpeechPlayer : IDisposable
{
    Task<SpeechPlaybackResult> SpeakAsync(
        string text,
        CancellationToken cancellationToken);

    void Stop();
}
