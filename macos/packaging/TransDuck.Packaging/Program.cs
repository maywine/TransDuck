using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TransDuck.Packaging;

internal static class Program
{
    private const string AppName = "TransDuck.app";
    private static readonly byte[][] ForbiddenCanaries =
    [
        "APIKEY_CANARY"u8.ToArray(),
        "QUERY_CANARY"u8.ToArray(),
    ];

    public static int Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["pack", var appPath, var zipPath] => Pack(appPath, zipPath),
                ["verify", var zipPath, var runtimeIdentifier, var version] =>
                    Verify(zipPath, runtimeIdentifier, version),
                _ => throw new ArgumentException(
                    "Usage: TransDuck.Packaging pack <TransDuck.app> <zip> | " +
                    "verify <zip> <osx-x64|osx-arm64> <version>"),
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            Console.Error.WriteLine("packaging_error: " + exception.Message);
            return 1;
        }
    }

    private static int Pack(string appPath, string zipPath)
    {
        var appRoot = Path.GetFullPath(appPath);
        var destination = Path.GetFullPath(zipPath);
        if (!Directory.Exists(appRoot) ||
            !string.Equals(Path.GetFileName(appRoot), AppName, StringComparison.Ordinal))
        {
            throw new DirectoryNotFoundException("The package source must be a TransDuck.app directory.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination) ??
            throw new IOException("The ZIP destination has no parent directory."));
        File.Delete(destination);
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        foreach (var filePath in Directory.EnumerateFiles(appRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(Path.GetDirectoryName(appRoot)!, filePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
            var mode = (int)(OperatingSystem.IsWindows()
                ? string.Equals(Path.GetFileName(filePath), "TransDuck", StringComparison.Ordinal)
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                      UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                      UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite |
                      UnixFileMode.GroupRead | UnixFileMode.OtherRead
                : File.GetUnixFileMode(filePath));
            entry.ExternalAttributes = unchecked(mode << 16);
            entry.LastWriteTime = File.GetLastWriteTimeUtc(filePath);
            using var input = File.OpenRead(filePath);
            using var output = entry.Open();
            input.CopyTo(output);
        }

        Console.WriteLine(destination);
        return 0;
    }

    private static int Verify(string zipPath, string runtimeIdentifier, string version)
    {
        var path = Path.GetFullPath(zipPath);
        if (runtimeIdentifier is not ("osx-x64" or "osx-arm64"))
        {
            throw new ArgumentException("The runtime identifier is unsupported.");
        }

        using var archive = ZipFile.OpenRead(path);
        var names = archive.Entries.Select(static entry => entry.FullName).ToArray();
        if (names.Length == 0 || names.Any(IsUnsafeEntry) ||
            names.Any(name => !name.StartsWith(AppName + "/", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The ZIP contains an unsafe or unexpected top-level entry.");
        }

        RequireEntry(archive, AppName + "/Contents/Info.plist");
        var executable = RequireEntry(archive, AppName + "/Contents/MacOS/TransDuck");
        var uioHook = RequireEntry(archive, AppName + "/Contents/MacOS/libuiohook.dylib");
        var avaloniaNative = RequireEntry(archive, AppName + "/Contents/MacOS/libAvaloniaNative.dylib");
        var skia = RequireEntry(archive, AppName + "/Contents/MacOS/libSkiaSharp.dylib");
        var harfBuzz = RequireEntry(archive, AppName + "/Contents/MacOS/libHarfBuzzSharp.dylib");
        RequireEntry(archive, AppName + "/Contents/Resources/TransDuck.icns");
        RequireEntry(archive, AppName + "/Contents/Resources/LICENSE");
        var notices = RequireEntry(
            archive,
            AppName + "/Contents/Resources/THIRD-PARTY-NOTICES.md");
        RequireEntry(archive, AppName + "/Contents/Resources/licenses/libuiohook-LGPL-3.0.txt");
        RequireEntry(archive, AppName + "/Contents/Resources/licenses/libuiohook-GPL-3.0.txt");
        RequireEntry(archive, AppName + "/Contents/Resources/licenses/MicroCom-MIT.txt");
        RequireEntry(archive, AppName + "/Contents/Resources/licenses/Inter-OFL-1.1.txt");
        RequireEntry(
            archive,
            AppName + "/Contents/Resources/licenses/SkiaSharp-HarfBuzz-ThirdPartyNotices.txt");
        RequireUtf8Text(
            notices,
            "a41658fb2bef7503a3bcb305ab8bf849755fe906");

        var executableMode = (executable.ExternalAttributes >> 16) & 0xffff;
        if ((executableMode & 0x40) == 0)
        {
            throw new InvalidDataException("The app executable does not retain its owner execute bit.");
        }

        VerifyMachO(executable, runtimeIdentifier);
        VerifyMachOArchitecture(uioHook, runtimeIdentifier);
        VerifyMachOArchitecture(avaloniaNative, runtimeIdentifier);
        VerifyMachOArchitecture(skia, runtimeIdentifier);
        VerifyMachOArchitecture(harfBuzz, runtimeIdentifier);
        VerifyMinimumMacOS(executable, runtimeIdentifier);
        foreach (var nativeLibrary in archive.Entries.Where(static entry =>
                     entry.FullName.StartsWith(AppName + "/Contents/MacOS/", StringComparison.Ordinal) &&
                     entry.FullName.EndsWith(".dylib", StringComparison.Ordinal)))
        {
            VerifyMinimumMacOS(nativeLibrary, runtimeIdentifier);
        }
        VerifyInfoPlist(RequireEntry(archive, AppName + "/Contents/Info.plist"), version);
        if (names.Any(name => name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The release bundle contains debug symbols.");
        }

        var forbiddenDesktopPayloads = new[]
        {
            "Avalonia.Desktop.dll",
            "Avalonia.FreeDesktop.dll",
            "Avalonia.FreeDesktop.AtSpi.dll",
            "Avalonia.Win32.dll",
            "Avalonia.Win32.Automation.dll",
            "Avalonia.X11.dll",
            "Tmds.DBus.Protocol.dll",
        };
        if (names.Any(name => forbiddenDesktopPayloads.Contains(
                Path.GetFileName(name),
                StringComparer.Ordinal)))
        {
            throw new InvalidDataException("The macOS bundle contains an unrelated desktop platform payload.");
        }

        foreach (var entry in archive.Entries.Where(static entry => entry.Length > 0))
        {
            using var input = entry.Open();
            using var memory = new MemoryStream();
            input.CopyTo(memory);
            var bytes = memory.GetBuffer().AsSpan(0, checked((int)memory.Length));
            foreach (var canary in ForbiddenCanaries)
            {
                if (bytes.IndexOf(canary) >= 0)
                {
                    throw new InvalidDataException("The release bundle contains a test secret canary.");
                }
            }
        }

        Console.WriteLine("package_verified: " + path);
        return 0;
    }

    private static void VerifyMachO(ZipArchiveEntry executable, string runtimeIdentifier)
    {
        Span<byte> header = stackalloc byte[8];
        using var stream = executable.Open();
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual(new byte[] { 0xcf, 0xfa, 0xed, 0xfe }))
        {
            throw new InvalidDataException("The app host is not a little-endian 64-bit Mach-O executable.");
        }

        var cpuType = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var expected = runtimeIdentifier == "osx-x64" ? 0x01000007u : 0x0100000cu;
        if (cpuType != expected)
        {
            throw new InvalidDataException("The app host architecture does not match the package name.");
        }
    }

    private static void VerifyMachOArchitecture(ZipArchiveEntry entry, string runtimeIdentifier)
    {
        var expected = runtimeIdentifier == "osx-x64" ? 0x01000007u : 0x0100000cu;
        Span<byte> header = stackalloc byte[48];
        using var stream = entry.Open();
        stream.ReadExactly(header[..8]);
        if (header[..4].SequenceEqual(new byte[] { 0xcf, 0xfa, 0xed, 0xfe }))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]) != expected)
            {
                throw new InvalidDataException(entry.Name + " has the wrong Mach-O architecture.");
            }

            return;
        }

        if (!header[..4].SequenceEqual(new byte[] { 0xca, 0xfe, 0xba, 0xbe }))
        {
            throw new InvalidDataException(entry.Name + " is not a supported Mach-O library.");
        }

        var architectureCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header[4..8]));
        if (architectureCount is < 1 or > 2)
        {
            throw new InvalidDataException(entry.Name + " has an invalid universal Mach-O header.");
        }

        stream.ReadExactly(header[..(architectureCount * 20)]);
        for (var index = 0; index < architectureCount; index++)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(header.Slice(index * 20, 4)) == expected)
            {
                return;
            }
        }

        throw new InvalidDataException(entry.Name + " lacks the package's Mach-O architecture.");
    }

    private static void VerifyMinimumMacOS(ZipArchiveEntry entry, string runtimeIdentifier)
    {
        const uint BuildVersionCommand = 0x32;
        const uint MinimumMacOSCommand = 0x24;
        const uint MacOSPlatform = 1;
        const uint MaximumSupportedMinimum = 14u << 16;
        var bytes = ReadEntry(entry);
        var expectedCpu = runtimeIdentifier == "osx-x64" ? 0x01000007u : 0x0100000cu;
        var (sliceOffset, sliceSize) = FindMachOSlice(bytes, expectedCpu, entry.Name);
        if (sliceSize < 32 || !bytes.AsSpan(sliceOffset, 4).SequenceEqual(
                new byte[] { 0xcf, 0xfa, 0xed, 0xfe }))
        {
            throw new InvalidDataException(entry.Name + " has an invalid 64-bit Mach-O slice.");
        }

        var commandCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(sliceOffset + 16, 4)));
        var commandBytes = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(sliceOffset + 20, 4)));
        var commandOffset = sliceOffset + 32;
        var commandEnd = checked(commandOffset + commandBytes);
        if (commandCount < 1 || commandEnd > sliceOffset + sliceSize)
        {
            throw new InvalidDataException(entry.Name + " has invalid Mach-O load commands.");
        }

        var foundMinimum = false;
        for (var index = 0; index < commandCount; index++)
        {
            if (commandOffset > commandEnd - 8)
            {
                throw new InvalidDataException(entry.Name + " has a truncated Mach-O load command.");
            }

            var command = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(commandOffset, 4));
            var commandSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(commandOffset + 4, 4)));
            if (commandSize < 8 || commandOffset > commandEnd - commandSize)
            {
                throw new InvalidDataException(entry.Name + " has an invalid Mach-O load command size.");
            }

            uint? minimum = null;
            if (command == BuildVersionCommand)
            {
                if (commandSize < 24 || BinaryPrimitives.ReadUInt32LittleEndian(
                        bytes.AsSpan(commandOffset + 8, 4)) != MacOSPlatform)
                {
                    throw new InvalidDataException(entry.Name + " does not target the macOS platform.");
                }

                minimum = BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.AsSpan(commandOffset + 12, 4));
            }
            else if (command == MinimumMacOSCommand)
            {
                if (commandSize < 16)
                {
                    throw new InvalidDataException(entry.Name + " has an invalid minimum macOS command.");
                }

                minimum = BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.AsSpan(commandOffset + 8, 4));
            }

            if (minimum is { } value)
            {
                foundMinimum = true;
                if (value > MaximumSupportedMinimum)
                {
                    throw new InvalidDataException(entry.Name + " requires a newer macOS version than 14.0.");
                }
            }

            commandOffset += commandSize;
        }

        if (!foundMinimum)
        {
            throw new InvalidDataException(entry.Name + " does not declare a minimum macOS version.");
        }
    }

    private static (int Offset, int Size) FindMachOSlice(
        byte[] bytes,
        uint expectedCpu,
        string entryName)
    {
        if (bytes.Length < 8)
        {
            throw new InvalidDataException(entryName + " is too small to be a Mach-O file.");
        }

        var magic = bytes.AsSpan(0, 4);
        if (magic.SequenceEqual(new byte[] { 0xcf, 0xfa, 0xed, 0xfe }))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != expectedCpu)
            {
                throw new InvalidDataException(entryName + " has the wrong Mach-O architecture.");
            }

            return (0, bytes.Length);
        }

        var isFat32 = magic.SequenceEqual(new byte[] { 0xca, 0xfe, 0xba, 0xbe });
        var isFat64 = magic.SequenceEqual(new byte[] { 0xca, 0xfe, 0xba, 0xbf });
        if (!isFat32 && !isFat64)
        {
            throw new InvalidDataException(entryName + " is not a supported Mach-O file.");
        }

        var count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4, 4)));
        var recordSize = isFat32 ? 20 : 32;
        if (count is < 1 or > 16 || bytes.Length < 8 + (count * recordSize))
        {
            throw new InvalidDataException(entryName + " has an invalid universal Mach-O header.");
        }

        for (var index = 0; index < count; index++)
        {
            var record = bytes.AsSpan(8 + (index * recordSize), recordSize);
            if (BinaryPrimitives.ReadUInt32BigEndian(record[..4]) != expectedCpu)
            {
                continue;
            }

            var offset = isFat32
                ? BinaryPrimitives.ReadUInt32BigEndian(record.Slice(8, 4))
                : BinaryPrimitives.ReadUInt64BigEndian(record.Slice(8, 8));
            var size = isFat32
                ? BinaryPrimitives.ReadUInt32BigEndian(record.Slice(12, 4))
                : BinaryPrimitives.ReadUInt64BigEndian(record.Slice(16, 8));
            if (offset > int.MaxValue || size > int.MaxValue || size < 32 ||
                offset > (ulong)bytes.Length - size)
            {
                throw new InvalidDataException(entryName + " has an invalid Mach-O slice range.");
            }

            return (checked((int)offset), checked((int)size));
        }

        throw new InvalidDataException(entryName + " lacks the package's Mach-O architecture.");
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > int.MaxValue)
        {
            throw new InvalidDataException(entry.FullName + " is too large to audit.");
        }

        var bytes = new byte[checked((int)entry.Length)];
        using var stream = entry.Open();
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void VerifyInfoPlist(ZipArchiveEntry entry, string version)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader);
        var dictionary = document.Root?.Element("dict") ??
            throw new InvalidDataException("Info.plist has no dictionary.");
        var values = ReadDictionary(dictionary);
        RequireString(values, "CFBundleIdentifier", "com.transduck.app");
        RequireString(values, "CFBundleExecutable", "TransDuck");
        RequireString(values, "CFBundleIconFile", "TransDuck.icns");
        RequireString(values, "CFBundleShortVersionString", version);
        RequireString(values, "CFBundleVersion", version);
        RequireString(values, "LSMinimumSystemVersion", "14.0");
        RequireString(
            values,
            "NSScreenCaptureUsageDescription",
            "TransDuck needs screen access only when you choose screenshot OCR.");
        if (!values.TryGetValue("LSUIElement", out var uiElement) || uiElement.Name != "true" ||
            !values.TryGetValue("NSHighResolutionCapable", out var highResolution) ||
            highResolution.Name != "true")
        {
            throw new InvalidDataException("Info.plist is missing required boolean bundle settings.");
        }
    }

    private static Dictionary<string, XElement> ReadDictionary(XElement dictionary)
    {
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var children = dictionary.Elements().ToArray();
        for (var index = 0; index + 1 < children.Length; index += 2)
        {
            if (children[index].Name == "key")
            {
                result[children[index].Value] = children[index + 1];
            }
        }

        return result;
    }

    private static void RequireString(
        IReadOnlyDictionary<string, XElement> values,
        string key,
        string expected)
    {
        if (!values.TryGetValue(key, out var value) || value.Name != "string" ||
            !string.Equals(value.Value, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Info.plist has an invalid " + key + ".");
        }
    }

    private static ZipArchiveEntry RequireEntry(ZipArchive archive, string name) =>
        archive.GetEntry(name) ?? throw new InvalidDataException("The ZIP is missing " + name + ".");

    private static void RequireUtf8Text(ZipArchiveEntry entry, string expected)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        if (!reader.ReadToEnd().Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(entry.FullName + " is missing required notice text.");
        }
    }

    private static bool IsUnsafeEntry(string name) =>
        string.IsNullOrEmpty(name) || name.StartsWith("/", StringComparison.Ordinal) ||
        name.Split('/').Any(static component => component is ".." or ".");
}
