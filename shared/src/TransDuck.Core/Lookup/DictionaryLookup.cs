// Copyright (c) 2026 maywine. All rights reserved.

namespace TransDuck.Core.Lookup;

public static class LocalDictionaryIds
{
    public const string File = "local-dictionary";

    public const string MacSystem = "macos-system-dictionary";

    // History written before the provider rename keeps its original stable source ID.
    public static bool IsFile(string providerId) =>
        string.Equals(providerId, File, StringComparison.Ordinal) ||
        string.Equals(providerId, "ecdict", StringComparison.Ordinal);
}

public sealed record DictionaryProviderRegistration(
    string ProviderId,
    string DisplayName,
    bool RequiresDataFile)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
    }
}

public sealed record DictionaryLookupEntry(
    string Term,
    string? Phonetic,
    string? Translation,
    string? Definition,
    string? PartOfSpeech)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Term);
        if (string.IsNullOrWhiteSpace(Translation) && string.IsNullOrWhiteSpace(Definition))
        {
            throw new InvalidOperationException("A dictionary entry requires a translation or definition.");
        }
    }

    public string ToDisplayText()
    {
        Validate();
        var sections = new List<string>();
        var heading = string.IsNullOrWhiteSpace(Phonetic)
            ? Term.Trim()
            : $"{Term.Trim()}  [{Phonetic.Trim()}]";
        sections.Add(string.IsNullOrWhiteSpace(PartOfSpeech)
            ? heading
            : heading + Environment.NewLine + PartOfSpeech.Trim());
        if (!string.IsNullOrWhiteSpace(Translation))
        {
            sections.Add(Translation.Trim());
        }

        if (!string.IsNullOrWhiteSpace(Definition))
        {
            sections.Add(Definition.Trim());
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }
}

public enum DictionaryLookupStatus
{
    Found,
    NotFound,
    InvalidRequest,
    Unavailable,
    InvalidData,
    Cancelled,
}

public sealed record DictionaryLookupResult(
    DictionaryLookupStatus Status,
    DictionaryLookupEntry? Entry = null)
{
    public bool Succeeded => Status == DictionaryLookupStatus.Found && Entry is not null;

    public static DictionaryLookupResult Found(DictionaryLookupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Validate();
        return new DictionaryLookupResult(DictionaryLookupStatus.Found, entry);
    }

    public static DictionaryLookupResult FromStatus(DictionaryLookupStatus status)
    {
        if (status == DictionaryLookupStatus.Found)
        {
            throw new ArgumentException("Found results require an entry.", nameof(status));
        }

        return new DictionaryLookupResult(status);
    }
}

public interface IDictionaryProvider
{
    DictionaryProviderRegistration Registration { get; }

    Task<DictionaryLookupResult> LookupAsync(
        string text,
        string? dataFilePath,
        CancellationToken cancellationToken);
}
