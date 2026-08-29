// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Translation;

/// <summary>
/// Describes one provider-neutral translation request without printable endpoint or credential data.
/// </summary>
public sealed record TranslationProviderRequest(
    ProviderDescriptor Provider,
    Uri Endpoint,
    string? Model,
    string Text,
    string? SourceLanguage,
    string TargetLanguage,
    TranslationCredentials Credentials,
    TimeSpan Timeout)
{
    /// <summary>Validates fields shared by all translation providers.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Provider);
        Provider.Validate();
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentNullException.ThrowIfNull(Credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(TargetLanguage);

        if (!Endpoint.IsAbsoluteUri || Endpoint.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("The provider endpoint must be an absolute HTTP(S) URI.",
                nameof(Endpoint));
        }

        if (Model is not null && string.IsNullOrWhiteSpace(Model))
        {
            throw new ArgumentException("The optional model cannot be empty when supplied.", nameof(Model));
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout),
                "The provider request timeout must be positive.");
        }
    }

    /// <summary>Validates provider identity and requires a model when an adapter needs one.</summary>
    public void ValidateForProvider(string providerId, bool modelRequired)
    {
        Validate();
        if (!string.Equals(Provider.ProviderId, providerId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The request provider does not match this adapter.", nameof(Provider));
        }

        if (modelRequired && string.IsNullOrWhiteSpace(Model))
        {
            throw new ArgumentException("This provider requires a model.", nameof(Model));
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"TranslationProviderRequest(ProviderId={Provider.ProviderId}, HasModel={!string.IsNullOrWhiteSpace(Model)}, " +
        $"TextLength={Text?.Length ?? 0}, SourceLanguage={SourceLanguage ?? "<none>"}, " +
        $"TargetLanguage={TargetLanguage ?? "<none>"}, Timeout={Timeout})";
}
