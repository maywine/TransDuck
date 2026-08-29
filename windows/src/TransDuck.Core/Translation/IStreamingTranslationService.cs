namespace TransDuck.Core.Translation;

/// <summary>
/// Streams provider output without exposing provider-specific SDK types to the UI.
/// </summary>
public interface IStreamingTranslationService
{
    IAsyncEnumerable<TranslationStreamEvent> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken);
}
