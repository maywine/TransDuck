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
/// Keeps provider signing credentials out of printable request data and diagnostic strings.
/// </summary>
public sealed class TranslationCredentials
{
    private readonly string? _apiKey;
    private readonly string? _secretKey;

    public TranslationCredentials(string? apiKey, string? secretKey = null)
    {
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        _secretKey = string.IsNullOrWhiteSpace(secretKey) ? null : secretKey;
    }

    public bool HasApiKey => _apiKey is not null;

    public bool HasSecretKey => _secretKey is not null;

    public string? GetApiKey() => _apiKey;

    public string? GetSecretKey() => _secretKey;

    public override string ToString() =>
        $"TranslationCredentials(ApiKey={(HasApiKey ? "***redacted***" : "<none>")}, " +
        $"SecretKey={(HasSecretKey ? "***redacted***" : "<none>")})";
}
