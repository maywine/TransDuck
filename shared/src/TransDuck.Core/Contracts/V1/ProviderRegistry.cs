// Copyright (c) 2026 maywine. All rights reserved.

using System.Collections.Concurrent;

namespace TransDuck.Core.Contracts.V1;

/// <summary>
/// Describes stable provider features without coupling Core to a provider SDK.
/// </summary>
[Flags]
public enum ProviderCapability
{
    None = 0,
    Translation = 1,
    Dictionary = 2,
    Ocr = 4,
    Streaming = 8,
}

/// <summary>
/// Registers one provider kind; instance selection remains part of a query descriptor.
/// </summary>
public sealed record ProviderRegistration(
    ProviderDescriptor Provider,
    ProviderCapability Capabilities)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Provider);
        Provider.Validate();
        ContractValidation.RequireCondition(
            Capabilities != ProviderCapability.None &&
            (Capabilities & ~KnownCapabilities) == ProviderCapability.None,
            ContractValidationError.InvalidValue,
            "Provider capabilities must contain known values.");
    }

    private const ProviderCapability KnownCapabilities =
        ProviderCapability.Translation |
        ProviderCapability.Dictionary |
        ProviderCapability.Ocr |
        ProviderCapability.Streaming;
}

/// <summary>
/// Provides thread-safe, deterministic registration and lookup by stable providerId.
/// </summary>
public sealed class ProviderRegistry
{
    private readonly ConcurrentDictionary<string, ProviderRegistration> _registrations =
        new(StringComparer.Ordinal);

    public void Register(ProviderRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Validate();
        if (!_registrations.TryAdd(registration.Provider.ProviderId, registration))
        {
            throw new InvalidOperationException(
                $"A provider is already registered for providerId '{registration.Provider.ProviderId}'.");
        }
    }

    public bool TryResolve(string providerId, out ProviderRegistration? registration)
    {
        registration = null;
        return !string.IsNullOrEmpty(providerId) &&
            _registrations.TryGetValue(providerId, out registration);
    }

    public bool TryResolve(ProviderDescriptor provider, out ProviderRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return TryResolve(provider.ProviderId, out registration);
    }

    public IReadOnlyList<ProviderRegistration> List() =>
        _registrations.Values
            .OrderBy(registration => registration.Provider.ProviderId, StringComparer.Ordinal)
            .ToArray();
}
