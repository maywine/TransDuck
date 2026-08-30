// Copyright (c) 2026 maywine. All rights reserved.

using System.Reflection;
using System.Runtime.InteropServices;
using TransDuck.Core.Lookup;
using TransDuck.Platform.MacOS.Dictionary;

namespace TransDuck.Platform.MacOS.Tests.Dictionary;

public sealed class MacSystemDictionaryProviderTests
{
    [Fact]
    public async Task LookupAsync_ValidatesInputAndDoesNotInvokeAppleFrameworksOffMacOS()
    {
        var provider = new MacSystemDictionaryProvider();

        var empty = await provider.LookupAsync(" ", null, CancellationToken.None);
        var platformResult = await provider.LookupAsync("duck", null, CancellationToken.None);

        Assert.Equal(LocalDictionaryIds.MacSystem, provider.Registration.ProviderId);
        Assert.False(provider.Registration.RequiresDataFile);
        Assert.Equal(DictionaryLookupStatus.InvalidRequest, empty.Status);
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Equal(DictionaryLookupStatus.Unavailable, platformResult.Status);
        }
    }

    [Fact]
    public async Task LookupAsync_HonorsPreCancellationWithoutNativeAccess()
    {
        var provider = new MacSystemDictionaryProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await provider.LookupAsync("duck", null, cancellation.Token);

        Assert.Equal(DictionaryLookupStatus.Cancelled, result.Status);
    }

    [Fact]
    public void NativeDeclaration_MatchesTheCoreServicesCfRangeAbi()
    {
        var assembly = typeof(MacSystemDictionaryProvider).Assembly;
        var rangeType = assembly.GetType(
            "TransDuck.Platform.MacOS.Dictionary.CoreFoundationRange",
            throwOnError: true)!;
        var nativeType = assembly.GetType(
            "TransDuck.Platform.MacOS.Dictionary.DictionaryServicesNative",
            throwOnError: true)!;
        var method = nativeType.GetMethod(
            "DCSCopyTextDefinition",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var import = method.GetCustomAttribute<LibraryImportAttribute>();

        Assert.Equal(IntPtr.Size * 2, Marshal.SizeOf(rangeType));
        Assert.NotNull(import);
        Assert.Equal(
            "/System/Library/Frameworks/CoreServices.framework/CoreServices",
            import.LibraryName);
        Assert.Equal(typeof(IntPtr), method.ReturnType);
        Assert.Equal(
            [typeof(IntPtr), typeof(IntPtr), rangeType],
            method.GetParameters().Select(static parameter => parameter.ParameterType));
    }
}
