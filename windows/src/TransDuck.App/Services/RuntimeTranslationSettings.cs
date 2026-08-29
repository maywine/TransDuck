using TransDuck.Core.Translation;

namespace TransDuck.App.Services;

/// <summary>
/// Reads opt-in development settings without providing a real endpoint or credential default.
/// </summary>
internal sealed class RuntimeTranslationSettings
{
    private const string EndpointVariable = "TRANSDUCK_OPENAI_ENDPOINT";
    private const string ModelVariable = "TRANSDUCK_OPENAI_MODEL";
    private const string ApiKeyVariable = "TRANSDUCK_OPENAI_API_KEY";

    private RuntimeTranslationSettings(Uri? endpoint, string? model, TranslationCredentials credentials)
    {
        Endpoint = endpoint;
        Model = model;
        Credentials = credentials;
    }

    public Uri? Endpoint { get; }

    public string? Model { get; }

    public TranslationCredentials Credentials { get; }

    public bool IsConfigured => Endpoint is not null && !string.IsNullOrWhiteSpace(Model);

    public static RuntimeTranslationSettings Load()
    {
        var rawEndpoint = Environment.GetEnvironmentVariable(EndpointVariable);
        var endpoint = Uri.TryCreate(rawEndpoint, UriKind.Absolute, out var parsedEndpoint)
            ? parsedEndpoint
            : null;
        return new RuntimeTranslationSettings(
            endpoint,
            Environment.GetEnvironmentVariable(ModelVariable),
            new TranslationCredentials(Environment.GetEnvironmentVariable(ApiKeyVariable)));
    }

    public bool TryCreateRequest(
        string text,
        out TranslationRequest? request,
        out string? errorMessage)
    {
        if (Endpoint is null || string.IsNullOrWhiteSpace(Model))
        {
            request = null;
            errorMessage = AppStrings.Format(
                "legacy.translation.settings_missing",
                EndpointVariable,
                ModelVariable);
            return false;
        }

        request = new TranslationRequest(
            Endpoint,
            Model,
            text,
            SourceLanguage: null,
            TargetLanguage: null,
            Credentials,
            Timeout: TimeSpan.FromSeconds(45));
        errorMessage = null;
        return true;
    }
}
