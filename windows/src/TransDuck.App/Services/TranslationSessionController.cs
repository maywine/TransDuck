// Copyright (c) 2026 maywine. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;

namespace TransDuck.App.Services;

/// <summary>
/// Cancels superseded streams and accepts UI updates only from the active generation.
/// </summary>
internal sealed class TranslationSessionController
{
    private readonly IStreamingTranslationService _legacyTranslationService;
    private readonly GenerationGuard _generationGuard = new();
    private CancellationTokenSource? _activeCancellation;

    public TranslationSessionController(IStreamingTranslationService legacyTranslationService)
    {
        _legacyTranslationService = legacyTranslationService;
    }

    public void CancelCurrent()
    {
        _generationGuard.InvalidateCurrent();
        _activeCancellation?.Cancel();
    }

    public async Task RunAsync(
        TranslationRequest request,
        Action<string> appendText,
        Action<string> setStatus)
    {
        _ = await RunCoreAsync(
            cancellationToken => _legacyTranslationService.TranslateAsync(request, cancellationToken),
            appendText,
            setStatus);
    }

    public Task<TranslationSessionResult> RunAsync(
        ITranslationProvider provider,
        TranslationProviderRequest request,
        Action<string> appendText,
        Action<string> setStatus)
    {
        if (provider is null || request is null)
        {
            setStatus(DescribeFailure(QueryErrorCode.InvalidRequest));
            return Task.FromResult(TranslationSessionResult.Failed(
                string.Empty,
                QueryErrorCode.InvalidRequest,
                retryable: false));
        }

        return RunCoreAsync(
            cancellationToken => provider.TranslateAsync(request, cancellationToken),
            appendText,
            setStatus);
    }

    private async Task<TranslationSessionResult> RunCoreAsync(
        Func<CancellationToken, IAsyncEnumerable<TranslationStreamEvent>> createStream,
        Action<string> appendText,
        Action<string> setStatus)
    {
        CancelCurrent();
        var generation = _generationGuard.StartNewGeneration();
        using var cancellation = new CancellationTokenSource();
        _activeCancellation = cancellation;
        var text = new StringBuilder();
        setStatus(AppStrings.Get("translation.status.receiving"));

        try
        {
            await foreach (var streamEvent in createStream(cancellation.Token)
                                   .WithCancellation(cancellation.Token))
            {
                if (!_generationGuard.IsCurrent(generation))
                {
                    return TranslationSessionResult.Cancelled(text.ToString());
                }

                try
                {
                    streamEvent.Validate();
                }
                catch (InvalidOperationException)
                {
                    setStatus(DescribeFailure(QueryErrorCode.Internal));
                    return TranslationSessionResult.Failed(
                        text.ToString(),
                        QueryErrorCode.Internal,
                        retryable: false);
                }

                switch (streamEvent.Kind)
                {
                    case TranslationStreamEventKind.Delta:
                        text.Append(streamEvent.Text);
                        appendText(text.ToString());
                        break;
                    case TranslationStreamEventKind.Completed:
                        setStatus(AppStrings.Get("translation.status.completed"));
                        return TranslationSessionResult.Completed(text.ToString());
                    case TranslationStreamEventKind.Cancelled:
                        setStatus(AppStrings.Get("translation.status.cancelled"));
                        return TranslationSessionResult.Cancelled(text.ToString());
                    case TranslationStreamEventKind.Failed:
                        var errorCode = streamEvent.ErrorCode ?? QueryErrorCode.Internal;
                        setStatus(DescribeFailure(errorCode));
                        return TranslationSessionResult.Failed(
                            text.ToString(),
                            errorCode,
                            streamEvent.Retryable);
                }
            }

            setStatus(DescribeFailure(QueryErrorCode.ProviderUnavailable));
            return TranslationSessionResult.Failed(
                text.ToString(),
                QueryErrorCode.ProviderUnavailable,
                retryable: true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (_generationGuard.IsCurrent(generation))
            {
                setStatus(AppStrings.Get("translation.status.cancelled"));
            }

            return TranslationSessionResult.Cancelled(text.ToString());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (_generationGuard.IsCurrent(generation))
            {
                setStatus(DescribeFailure(QueryErrorCode.Internal));
            }

            return TranslationSessionResult.Failed(
                text.ToString(),
                QueryErrorCode.Internal,
                retryable: false);
        }
        finally
        {
            if (ReferenceEquals(_activeCancellation, cancellation))
            {
                _activeCancellation = null;
            }
        }
    }

    private static string DescribeFailure(QueryErrorCode errorCode) => AppStrings.DescribeQueryError(errorCode);
}

/// <summary>
/// Represents the internal terminal outcome used for history and safe diagnostics.
/// </summary>
internal sealed record TranslationSessionResult(
    TranslationStreamEventKind TerminalKind,
    string Text,
    QueryErrorCode? ErrorCode = null,
    bool Retryable = false)
{
    public static TranslationSessionResult Completed(string text) =>
        new(TranslationStreamEventKind.Completed, text);

    public static TranslationSessionResult Cancelled(string text) =>
        new(TranslationStreamEventKind.Cancelled, text);

    public static TranslationSessionResult Failed(string text, QueryErrorCode errorCode, bool retryable) =>
        new(TranslationStreamEventKind.Failed, text, errorCode, retryable);
}
