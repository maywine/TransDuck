// Copyright (c) 2026 maywine. All rights reserved.

using System.Runtime.CompilerServices;
using System.Net.Http;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;

namespace TransDuck.Platform.Windows.Translation;

/// <summary>
/// Adapts the legacy OpenAI-compatible SSE client to the provider-neutral translation contract.
/// </summary>
public sealed class OpenAiCompatibleProvider : ITranslationProvider
{
    private readonly OpenAiCompatibleSseClient _client;

    /// <summary>Creates a provider using an externally owned HttpClient through the legacy SSE client.</summary>
    public OpenAiCompatibleProvider(HttpClient httpClient)
        : this(new OpenAiCompatibleSseClient(httpClient))
    {
    }

    /// <summary>Creates a provider using one application-owned transport lease source.</summary>
    public OpenAiCompatibleProvider(ITranslationHttpClientLeaseSource clientLeaseSource)
        : this(new OpenAiCompatibleSseClient(clientLeaseSource))
    {
    }

    private OpenAiCompatibleProvider(OpenAiCompatibleSseClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public ProviderRegistration Registration { get; } = new(
        new ProviderDescriptor(TranslationProviderIds.OpenAiCompatible),
        ProviderCapability.Translation | ProviderCapability.Streaming);

    /// <inheritdoc />
    public IAsyncEnumerable<TranslationStreamEvent> TranslateAsync(
        TranslationProviderRequest request,
        CancellationToken cancellationToken) =>
        StreamAsync(request, cancellationToken);

    private async IAsyncEnumerable<TranslationStreamEvent> StreamAsync(
        TranslationProviderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (request is null)
        {
            yield return TranslationProviderFailures.InvalidRequest();
            yield break;
        }

        var requestIsValid = true;
        try
        {
            request.ValidateForProvider(TranslationProviderIds.OpenAiCompatible, modelRequired: true);
        }
        catch (Exception exception) when (exception is ArgumentException or ContractValidationException)
        {
            requestIsValid = false;
        }

        if (!requestIsValid)
        {
            yield return TranslationProviderFailures.InvalidRequest();
            yield break;
        }

        var legacyRequest = new TranslationRequest(
            request.Endpoint,
            request.Model!,
            request.Text,
            request.SourceLanguage,
            request.TargetLanguage,
            request.Credentials,
            request.Timeout);
        await foreach (var item in _client.TranslateAsync(legacyRequest, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
