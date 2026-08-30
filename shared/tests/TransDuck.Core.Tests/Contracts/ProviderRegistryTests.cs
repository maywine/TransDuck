// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Tests.Contracts;

public sealed class ProviderRegistryTests
{
    [Fact]
    public void Register_ResolvesByStableProviderIdAndListsInOrdinalOrder()
    {
        var registry = new ProviderRegistry();
        var beta = Registration("beta-provider", ProviderCapability.Dictionary);
        var alpha = Registration("alpha-provider", ProviderCapability.Translation | ProviderCapability.Streaming);

        registry.Register(beta);
        registry.Register(alpha);

        Assert.True(registry.TryResolve("alpha-provider", out var byId));
        Assert.Equal(alpha, byId);
        Assert.True(registry.TryResolve(new ProviderDescriptor("beta-provider", "instance-a"), out var byDescriptor));
        Assert.Equal(beta, byDescriptor);
        Assert.False(registry.TryResolve(string.Empty, out var missing));
        Assert.Null(missing);
        Assert.Equal(
            new[] { "alpha-provider", "beta-provider" },
            registry.List().Select(registration => registration.Provider.ProviderId));
    }

    [Fact]
    public void Register_RejectsDuplicateProviderId()
    {
        var registry = new ProviderRegistry();
        registry.Register(Registration("duplicate-provider", ProviderCapability.Translation));

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(Registration("duplicate-provider", ProviderCapability.Ocr)));

        Assert.Single(registry.List());
    }

    [Fact]
    public async Task Register_IsSafeForConcurrentUniqueAndDuplicateRegistrations()
    {
        var registry = new ProviderRegistry();
        var uniqueTasks = Enumerable.Range(0, 16)
            .Select(index => Task.Run(() => registry.Register(
                Registration($"provider-{index:D2}", ProviderCapability.Translation))))
            .ToArray();

        await Task.WhenAll(uniqueTasks);

        var duplicateRegistry = new ProviderRegistry();
        var duplicateResults = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
        {
            try
            {
                duplicateRegistry.Register(Registration("shared-provider", ProviderCapability.Ocr));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        })));

        Assert.Equal(16, registry.List().Count);
        Assert.Equal(
            registry.List().Select(registration => registration.Provider.ProviderId).OrderBy(id => id, StringComparer.Ordinal),
            registry.List().Select(registration => registration.Provider.ProviderId));
        Assert.Equal(1, duplicateResults.Count(result => result));
        Assert.Single(duplicateRegistry.List());
    }

    private static ProviderRegistration Registration(string providerId, ProviderCapability capabilities) =>
        new(new ProviderDescriptor(providerId), capabilities);
}
