// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Persistence;
using TransDuck.Core.Translation;

namespace TransDuck.Core.Tests.Persistence;

public sealed class ProviderSettingsModelTests
{
    [Fact]
    public void ProviderSettingsDocument_EmptyProfilesAreValidAndVersionIsStable()
    {
        var document = new ProviderSettingsDocument(ProviderSettingsMigration.CurrentVersion, []);

        document.Validate();

        Assert.Equal(1, ProviderSettingsMigration.CurrentVersion);
        Assert.Empty(document.Profiles);
    }

    [Fact]
    public void ProviderSettingsInterfaces_DoNotExposeCredentialsOrSecrets()
    {
        var types = new[] { typeof(ProviderSettingsDocument), typeof(IProviderSettingsStore) };
        var members = types.SelectMany(type => type.GetMembers()).Select(member => member.Name);

        Assert.DoesNotContain(members, name =>
            name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("apikey", StringComparison.OrdinalIgnoreCase));
    }
}
