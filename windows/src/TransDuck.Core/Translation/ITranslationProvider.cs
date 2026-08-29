// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Translation;

/// <summary>
/// Provides provider-neutral translation events for one stable provider registration.
/// </summary>
public interface ITranslationProvider
{
    /// <summary>Gets the provider registration and declared capabilities.</summary>
    ProviderRegistration Registration { get; }

    /// <summary>Translates one provider-neutral request without retaining request or credentials.</summary>
    IAsyncEnumerable<TranslationStreamEvent> TranslateAsync(
        TranslationProviderRequest request,
        CancellationToken cancellationToken);
}
