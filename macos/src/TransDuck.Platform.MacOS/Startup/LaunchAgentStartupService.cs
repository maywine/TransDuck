using System.Text;
using System.Xml;
using System.Xml.Linq;
using TransDuck.Infrastructure.Persistence;

namespace TransDuck.Platform.MacOS.Startup;

public enum MacStartupStatus
{
    Enabled,
    Disabled,
    Stale,
    Conflict,
    Unavailable,
    Failed,
}

public sealed record MacStartupResult(MacStartupStatus Status)
{
    public bool IsEnabled => Status == MacStartupStatus.Enabled;

    public bool IsOwned => Status is MacStartupStatus.Enabled or MacStartupStatus.Stale;
}

/// <summary>
/// Manages only TransDuck-owned per-user LaunchAgent property lists.
/// </summary>
public sealed class LaunchAgentStartupService
{
    public const string Label = "com.transduck.app";
    public const string FileName = Label + ".plist";

    private readonly string _launchAgentPath;
    private readonly string _executablePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LaunchAgentStartupService(
        string? executablePath = null,
        string? homeDirectory = null,
        string? launchAgentPath = null)
    {
        executablePath ??= Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("The current macOS executable path is unavailable.");
        }

        if (launchAgentPath is null)
        {
            homeDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(homeDirectory))
            {
                throw new InvalidOperationException("The current macOS user home directory is unavailable.");
            }

            launchAgentPath = Path.Combine(homeDirectory, "Library", "LaunchAgents", FileName);
        }

        _executablePath = Path.GetFullPath(executablePath);
        _launchAgentPath = Path.GetFullPath(launchAgentPath);
    }

    public string LaunchAgentPath => _launchAgentPath;

    public MacStartupResult GetStatus()
    {
        try
        {
            if (!File.Exists(_launchAgentPath))
            {
                return new MacStartupResult(MacStartupStatus.Disabled);
            }

            var document = ReadDocument(_launchAgentPath);
            if (!TryReadOwnedExecutable(
                    document,
                    out var registeredExecutable,
                    out var startsInBackground))
            {
                return new MacStartupResult(MacStartupStatus.Conflict);
            }

            return startsInBackground && PathsEqual(registeredExecutable, _executablePath)
                ? new MacStartupResult(MacStartupStatus.Enabled)
                : new MacStartupResult(MacStartupStatus.Stale);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or XmlException or InvalidOperationException)
        {
            return new MacStartupResult(MacStartupStatus.Failed);
        }
    }

    public async Task<MacStartupResult> EnableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new MacStartupResult(MacStartupStatus.Unavailable);
        }

        try
        {
            if (!IsTransDuckBundleExecutable(_executablePath))
            {
                return new MacStartupResult(MacStartupStatus.Unavailable);
            }

            var status = GetStatus();
            if (status.Status == MacStartupStatus.Conflict)
            {
                return status;
            }

            if (status.Status == MacStartupStatus.Enabled)
            {
                return status;
            }

            var content = CreateDocument(_executablePath).ToString(SaveOptions.DisableFormatting) + "\n";
            await AtomicFileWriter.WriteUtf8Async(_launchAgentPath, content, cancellationToken)
                .ConfigureAwait(false);
            return new MacStartupResult(MacStartupStatus.Enabled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new MacStartupResult(MacStartupStatus.Unavailable);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or XmlException or InvalidOperationException)
        {
            return new MacStartupResult(MacStartupStatus.Failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MacStartupResult> DisableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new MacStartupResult(MacStartupStatus.Unavailable);
        }

        try
        {
            var status = GetStatus();
            if (status.Status == MacStartupStatus.Conflict)
            {
                return status;
            }

            if (status.Status == MacStartupStatus.Disabled)
            {
                return status;
            }

            if (!status.IsOwned)
            {
                return status;
            }

            File.Delete(_launchAgentPath);
            return new MacStartupResult(MacStartupStatus.Disabled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new MacStartupResult(MacStartupStatus.Failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static XDocument CreateDocument(string executablePath) => new(
        new XDeclaration("1.0", "UTF-8", null),
        new XDocumentType(
            "plist",
            "-//Apple//DTD PLIST 1.0//EN",
            "http://www.apple.com/DTDs/PropertyList-1.0.dtd",
            null),
        new XElement("plist",
            new XAttribute("version", "1.0"),
            new XElement("dict",
                new XElement("key", "Label"),
                new XElement("string", Label),
                new XElement("key", "ProgramArguments"),
                new XElement("array",
                    new XElement("string", Path.GetFullPath(executablePath)),
                    new XElement("string", "--background")),
                new XElement("key", "RunAtLoad"),
                new XElement("true"))));

    private static XDocument ReadDocument(string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static bool TryReadOwnedExecutable(
        XDocument document,
        out string executablePath,
        out bool startsInBackground)
    {
        executablePath = string.Empty;
        startsInBackground = false;
        var dictionary = document.Root?.Element("dict");
        if (document.Root?.Name != "plist" || dictionary is null)
        {
            return false;
        }

        var entries = ReadDictionary(dictionary);
        if (dictionary.Elements().Count() != 6 || entries.Count != 3 ||
            !entries.TryGetValue("Label", out var labelElement) ||
            labelElement.Name != "string" ||
            !string.Equals(labelElement.Value, Label, StringComparison.Ordinal) ||
            !entries.TryGetValue("RunAtLoad", out var runAtLoad) ||
            runAtLoad.Name != "true" ||
            !entries.TryGetValue("ProgramArguments", out var arguments) ||
            arguments.Name != "array")
        {
            return false;
        }

        var values = arguments.Elements("string").ToArray();
        if (values.Length is < 1 or > 2 || string.IsNullOrWhiteSpace(values[0].Value) ||
            values.Length == 2 &&
            !string.Equals(values[1].Value, "--background", StringComparison.Ordinal))
        {
            return false;
        }

        executablePath = Path.GetFullPath(values[0].Value);
        startsInBackground = values.Length == 2;
        return IsTransDuckBundleExecutable(executablePath);
    }

    private static Dictionary<string, XElement> ReadDictionary(XElement dictionary)
    {
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var elements = dictionary.Elements().ToArray();
        for (var index = 0; index + 1 < elements.Length; index += 2)
        {
            if (elements[index].Name != "key" || string.IsNullOrEmpty(elements[index].Value))
            {
                continue;
            }

            result[elements[index].Value] = elements[index + 1];
        }

        return result;
    }

    private static bool IsTransDuckBundleExecutable(string executablePath)
    {
        var normalized = executablePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var suffix = Path.Combine("TransDuck.app", "Contents", "MacOS", "TransDuck");
        return normalized.EndsWith(suffix, StringComparison.Ordinal);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.Ordinal);
}
