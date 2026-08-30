// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Translation;

/// <summary>
/// Represents one provider terminal without UI or persistence dependencies.
/// </summary>
public sealed record TranslationProviderRunResult(
    TranslationStreamEventKind TerminalKind,
    string Text,
    QueryErrorCode? ErrorCode = null,
    bool Retryable = false)
{
    public static TranslationProviderRunResult Completed(string text) =>
        new(TranslationStreamEventKind.Completed, text);

    public static TranslationProviderRunResult Cancelled(string text) =>
        new(TranslationStreamEventKind.Cancelled, text);

    public static TranslationProviderRunResult Failed(
        string text,
        QueryErrorCode errorCode,
        bool retryable) =>
        new(TranslationStreamEventKind.Failed, text, errorCode, retryable);
}

/// <summary>
/// Collects one provider stream while allowing independent concurrent provider runs.
/// </summary>
public static class TranslationProviderRunner
{
    public static async Task<TranslationProviderRunResult> RunAsync(
        ITranslationProvider provider,
        TranslationProviderRequest request,
        Action<string>? textChanged,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        var text = new StringBuilder();
        try
        {
            await foreach (var streamEvent in provider.TranslateAsync(request, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                streamEvent.Validate();
                switch (streamEvent.Kind)
                {
                    case TranslationStreamEventKind.Delta:
                        text.Append(streamEvent.Text);
                        textChanged?.Invoke(text.ToString());
                        break;
                    case TranslationStreamEventKind.Completed:
                        return text.Length == 0
                            ? TranslationProviderRunResult.Failed(
                                string.Empty,
                                QueryErrorCode.Internal,
                                retryable: false)
                            : TranslationProviderRunResult.Completed(text.ToString());
                    case TranslationStreamEventKind.Cancelled:
                        return TranslationProviderRunResult.Cancelled(text.ToString());
                    case TranslationStreamEventKind.Failed:
                        return TranslationProviderRunResult.Failed(
                            text.ToString(),
                            streamEvent.ErrorCode ?? QueryErrorCode.Internal,
                            streamEvent.Retryable);
                }
            }

            return TranslationProviderRunResult.Failed(
                text.ToString(),
                QueryErrorCode.ProviderUnavailable,
                retryable: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return TranslationProviderRunResult.Cancelled(text.ToString());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return TranslationProviderRunResult.Failed(
                text.ToString(),
                QueryErrorCode.Internal,
                retryable: false);
        }
    }
}
