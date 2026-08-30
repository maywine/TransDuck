// Copyright (c) 2026 maywine. All rights reserved.

using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using TransDuck.Core.Lookup;

namespace TransDuck.Platform.Windows.Speech;

/// <summary>
/// Plays one locally synthesized pronunciation at a time through the Windows desktop speech engine.
/// </summary>
public sealed class WindowsSystemSpeechPlayer : ISystemSpeechPlayer
{
    private const int MaximumTextLength = 512;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private CancellationTokenSource? _currentCancellation;
    private bool _disposed;
    private bool _serialDisposed;
    private int _activeRequests;

    public async Task<SpeechPlaybackResult> SpeakAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var term = text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(term) || term.Length > MaximumTextLength)
        {
            return new SpeechPlaybackResult(SpeechPlaybackStatus.InvalidText);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new SpeechPlaybackResult(SpeechPlaybackStatus.Cancelled);
        }

        if (!OperatingSystem.IsWindows())
        {
            return new SpeechPlaybackResult(SpeechPlaybackStatus.Unavailable);
        }

        CancellationTokenSource requestCancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return new SpeechPlaybackResult(SpeechPlaybackStatus.Unavailable);
            }

            CancelNonFatal(_currentCancellation);
            requestCancellation = new CancellationTokenSource();
            _currentCancellation = requestCancellation;
            _activeRequests++;
        }

        var acquired = false;
        var disposeSerial = false;
        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                requestCancellation.Token);
            try
            {
                await _serial.WaitAsync(linkedCancellation.Token);
                acquired = true;
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
                return new SpeechPlaybackResult(SpeechPlaybackStatus.Cancelled);
            }

            if (linkedCancellation.IsCancellationRequested)
            {
                return new SpeechPlaybackResult(SpeechPlaybackStatus.Cancelled);
            }

            using var session = new SpeechPlaybackSession();
            var status = await session.PlayAsync(term, linkedCancellation.Token);
            return new SpeechPlaybackResult(status);
        }
        finally
        {
            if (acquired)
            {
                _serial.Release();
            }

            lock (_gate)
            {
                if (ReferenceEquals(_currentCancellation, requestCancellation))
                {
                    _currentCancellation = null;
                }

                _activeRequests--;
                if (_disposed && _activeRequests == 0 && !_serialDisposed)
                {
                    _serialDisposed = true;
                    disposeSerial = true;
                }
            }

            requestCancellation.Dispose();
            if (disposeSerial)
            {
                _serial.Dispose();
            }
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            cancellation = _currentCancellation;
        }

        CancelNonFatal(cancellation);
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        var disposeSerial = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellation = _currentCancellation;
            if (_activeRequests == 0 && !_serialDisposed)
            {
                _serialDisposed = true;
                disposeSerial = true;
            }
        }

        CancelNonFatal(cancellation);
        if (disposeSerial)
        {
            _serial.Dispose();
        }
    }

    private static void CancelNonFatal(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A completed request can release its token source while a replacement starts.
        }
    }

    private sealed class SpeechPlaybackSession : IDisposable
    {
        private SpeechSynthesizer? _synthesizer;
        private int _disposed;

        public async Task<SpeechPlaybackStatus> PlayAsync(
            string text,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var synthesizer = new SpeechSynthesizer();
                _synthesizer = synthesizer;
                if (!synthesizer.GetInstalledVoices().Any(static voice => voice.Enabled))
                {
                    return SpeechPlaybackStatus.Unavailable;
                }

                var completion = new TaskCompletionSource<SpeechPlaybackStatus>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler<SpeakCompletedEventArgs> speakCompleted = (_, eventArgs) =>
                    completion.TrySetResult(eventArgs.Cancelled
                        ? SpeechPlaybackStatus.Cancelled
                        : eventArgs.Error is null
                            ? SpeechPlaybackStatus.Completed
                            : SpeechPlaybackStatus.Failed);
                synthesizer.SpeakCompleted += speakCompleted;
                try
                {
                    using var cancellation = cancellationToken.Register(
                        static state => TryCancel((SpeechSynthesizer)state!),
                        synthesizer);
                    cancellationToken.ThrowIfCancellationRequested();
                    synthesizer.SpeakAsync(text);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        TryCancel(synthesizer);
                    }

                    return await completion.Task;
                }
                finally
                {
                    synthesizer.SpeakCompleted -= speakCompleted;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return SpeechPlaybackStatus.Cancelled;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return SpeechPlaybackStatus.Cancelled;
            }
            catch (Exception exception) when (exception is PlatformNotSupportedException or COMException or
                                               UnauthorizedAccessException or InvalidOperationException)
            {
                return SpeechPlaybackStatus.Unavailable;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                return SpeechPlaybackStatus.Failed;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var synthesizer = _synthesizer;
            _synthesizer = null;
            if (synthesizer is null)
            {
                return;
            }

            TryCancel(synthesizer);
            DisposeNonFatal(synthesizer);
        }

        private static void TryCancel(SpeechSynthesizer synthesizer)
        {
            try
            {
                synthesizer.SpeakAsyncCancelAll();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                // A completed or unavailable synthesizer has no outstanding speech to cancel.
            }
        }

        private static void DisposeNonFatal(IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                // Cancellation can already have closed the native SAPI object.
            }
        }

    }
}
