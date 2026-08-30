// Copyright (c) 2026 maywine. All rights reserved.

using System.Runtime.InteropServices;
using TransDuck.Core.Lookup;
using TransDuck.Platform.MacOS.Interop;

namespace TransDuck.Platform.MacOS.Dictionary;

/// <summary>
/// Looks up words and phrases in the dictionaries enabled by the current macOS user.
/// </summary>
public sealed class MacSystemDictionaryProvider : IDictionaryProvider
{
    public DictionaryProviderRegistration Registration { get; } = new(
        LocalDictionaryIds.MacSystem,
        "macOS Dictionary",
        RequiresDataFile: false);

    public Task<DictionaryLookupResult> LookupAsync(
        string text,
        string? dataFilePath,
        CancellationToken cancellationToken)
    {
        var term = text?.Trim();
        if (string.IsNullOrWhiteSpace(term) || term.Length > 512)
        {
            return Task.FromResult(
                DictionaryLookupResult.FromStatus(DictionaryLookupStatus.InvalidRequest));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(
                DictionaryLookupResult.FromStatus(DictionaryLookupStatus.Cancelled));
        }

        if (!OperatingSystem.IsMacOS())
        {
            return Task.FromResult(
                DictionaryLookupResult.FromStatus(DictionaryLookupStatus.Unavailable));
        }

        return Task.Run(() => LookupCore(term, cancellationToken), CancellationToken.None);
    }

    private static DictionaryLookupResult LookupCore(
        string term,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return DictionaryLookupResult.FromStatus(DictionaryLookupStatus.Cancelled);
        }

        try
        {
            using var scope = new CoreFoundationScope();
            var source = scope.String(term);
            var definition = DictionaryServicesNative.DCSCopyTextDefinition(
                IntPtr.Zero,
                source,
                new CoreFoundationRange(0, term.Length));
            if (cancellationToken.IsCancellationRequested)
            {
                if (definition != IntPtr.Zero)
                {
                    CoreFoundationNative.CFRelease(definition);
                }

                return DictionaryLookupResult.FromStatus(DictionaryLookupStatus.Cancelled);
            }

            if (definition == IntPtr.Zero)
            {
                return DictionaryLookupResult.FromStatus(DictionaryLookupStatus.NotFound);
            }

            try
            {
                var textDefinition = CoreFoundationNative.CopyString(definition);
                if (string.IsNullOrWhiteSpace(textDefinition))
                {
                    return DictionaryLookupResult.FromStatus(DictionaryLookupStatus.NotFound);
                }

                return DictionaryLookupResult.Found(new DictionaryLookupEntry(
                    term,
                    Phonetic: null,
                    Translation: null,
                    Definition: textDefinition,
                    PartOfSpeech: null));
            }
            finally
            {
                CoreFoundationNative.CFRelease(definition);
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or
                ExternalException)
        {
            return DictionaryLookupResult.FromStatus(DictionaryLookupStatus.Unavailable);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct CoreFoundationRange(nint Location, nint Length);

internal static partial class DictionaryServicesNative
{
    private const string CoreServices =
        "/System/Library/Frameworks/CoreServices.framework/CoreServices";

    [LibraryImport(CoreServices)]
    internal static partial IntPtr DCSCopyTextDefinition(
        IntPtr dictionary,
        IntPtr text,
        CoreFoundationRange range);
}
