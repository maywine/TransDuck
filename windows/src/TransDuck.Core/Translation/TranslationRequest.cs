namespace TransDuck.Core.Translation;

/// <summary>
/// Describes one OpenAI-compatible streaming request without exposing printable credentials.
/// </summary>
public sealed record TranslationRequest(
    Uri Endpoint,
    string Model,
    string Text,
    string? SourceLanguage,
    string? TargetLanguage,
    TranslationCredentials Credentials,
    TimeSpan Timeout)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentNullException.ThrowIfNull(Credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(Text);

        if (!Endpoint.IsAbsoluteUri || Endpoint.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("The translation endpoint must be an absolute HTTP(S) URI.",
                nameof(Endpoint));
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout),
                "The streaming request timeout must be positive.");
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"TranslationRequest(Model={Model}, TextLength={Text?.Length ?? 0}, " +
        $"SourceLanguage={SourceLanguage ?? "<none>"}, TargetLanguage={TargetLanguage ?? "<none>"}, " +
        $"Timeout={Timeout})";
}

/// <summary>
/// Keeps an API key out of printable request data and diagnostic strings.
/// </summary>
public sealed class TranslationCredentials
{
    private readonly string? _apiKey;

    public TranslationCredentials(string? apiKey)
    {
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }

    public bool HasApiKey => _apiKey is not null;

    public string? GetApiKey() => _apiKey;

    public override string ToString() => HasApiKey
        ? "TranslationCredentials(ApiKey=***redacted***)"
        : "TranslationCredentials(ApiKey=<none>)";
}
