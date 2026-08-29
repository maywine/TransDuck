// Copyright (c) 2026 maywine. All rights reserved.

using System.Collections.Concurrent;
using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Translation;

/// <summary>
/// Defines stable identifiers for the phase 2 translation provider set.
/// </summary>
public static class TranslationProviderIds
{
    /// <summary>Gets the OpenAI-compatible provider identifier.</summary>
    public const string OpenAiCompatible = "openai-compatible";

    /// <summary>Gets the DeepL provider identifier.</summary>
    public const string DeepL = "deepl";

    /// <summary>Gets the Ollama provider identifier.</summary>
    public const string Ollama = "ollama";

    /// <summary>Gets the unofficial Bing Translator web provider identifier.</summary>
    public const string Bing = "bing";

    /// <summary>Gets the unofficial Google Translate web provider identifier.</summary>
    public const string Google = "google";

}

/// <summary>
/// Registers translation providers by unique stable providerId with deterministic enumeration.
/// </summary>
public sealed class TranslationProviderRegistry
{
    private readonly ConcurrentDictionary<string, ITranslationProvider> _providers =
        new(StringComparer.Ordinal);

    /// <summary>Registers one provider whose capabilities include translation.</summary>
    public void Register(ITranslationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        provider.Registration.Validate();
        if ((provider.Registration.Capabilities & ProviderCapability.Translation) == 0)
        {
            throw new ArgumentException("A translation provider must declare Translation capability.",
                nameof(provider));
        }

        if (!_providers.TryAdd(provider.Registration.Provider.ProviderId, provider))
        {
            throw new InvalidOperationException(
                $"A translation provider is already registered for providerId " +
                $"'{provider.Registration.Provider.ProviderId}'.");
        }
    }

    /// <summary>Resolves a provider by stable providerId.</summary>
    public bool TryResolve(string providerId, out ITranslationProvider? provider)
    {
        provider = null;
        return !string.IsNullOrWhiteSpace(providerId) &&
            _providers.TryGetValue(providerId, out provider);
    }

    /// <summary>Resolves a provider by descriptor while preserving instance selection for the adapter.</summary>
    public bool TryResolve(ProviderDescriptor descriptor, out ITranslationProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return TryResolve(descriptor.ProviderId, out provider);
    }

    /// <summary>Lists registered providers in ordinal providerId order.</summary>
    public IReadOnlyList<ITranslationProvider> List() =>
        _providers.Values
            .OrderBy(provider => provider.Registration.Provider.ProviderId, StringComparer.Ordinal)
            .ToArray();
}
