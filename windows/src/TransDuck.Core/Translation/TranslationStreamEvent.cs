using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Translation;

/// <summary>
/// Represents the stable outcomes emitted by a streaming translation provider.
/// </summary>
public sealed record TranslationStreamEvent(
    TranslationStreamEventKind Kind,
    string? Text = null,
    string? ErrorMessage = null,
    QueryErrorCode? ErrorCode = null,
    bool Retryable = false)
{
    /// <summary>Gets whether this event terminates the current provider stream.</summary>
    public bool IsTerminal => Kind is not TranslationStreamEventKind.Delta;

    public static TranslationStreamEvent Delta(string text) =>
        new(TranslationStreamEventKind.Delta, Text: text);

    public static TranslationStreamEvent Completed() =>
        new(TranslationStreamEventKind.Completed);

    public static TranslationStreamEvent Cancelled() =>
        new(TranslationStreamEventKind.Cancelled);

    /// <summary>Creates a backwards-compatible internal non-retryable failure.</summary>
    public static TranslationStreamEvent Failed(string message) =>
        Failed(message, QueryErrorCode.Internal, retryable: false);

    /// <summary>Creates a stable categorized failure with a safe provider message.</summary>
    public static TranslationStreamEvent Failed(
        string message,
        QueryErrorCode errorCode,
        bool retryable) =>
        new(TranslationStreamEventKind.Failed, ErrorMessage: message, ErrorCode: errorCode,
            Retryable: retryable);

    /// <summary>Validates that terminal events have exactly one permitted shape.</summary>
    public void Validate()
    {
        switch (Kind)
        {
            case TranslationStreamEventKind.Delta:
                if (string.IsNullOrEmpty(Text) || ErrorMessage is not null || ErrorCode is not null || Retryable)
                {
                    throw new InvalidOperationException("Invalid delta translation stream event.");
                }

                break;
            case TranslationStreamEventKind.Completed:
            case TranslationStreamEventKind.Cancelled:
                if (Text is not null || ErrorMessage is not null || ErrorCode is not null || Retryable)
                {
                    throw new InvalidOperationException("Invalid terminal translation stream event.");
                }

                break;
            case TranslationStreamEventKind.Failed:
                if (Text is not null || string.IsNullOrWhiteSpace(ErrorMessage) || ErrorCode is null ||
                    !Enum.IsDefined(ErrorCode.Value))
                {
                    throw new InvalidOperationException("Invalid failed translation stream event.");
                }

                break;
            default:
                throw new InvalidOperationException("Unknown translation stream event kind.");
        }
    }
}

public enum TranslationStreamEventKind
{
    Delta,
    Completed,
    Cancelled,
    Failed,
}
