// Copyright (c) 2026 maywine. All rights reserved.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TransDuck.Platform.Windows.Tray;

namespace TransDuck.Platform.Windows.Tests;

public sealed class IconAssetContractTests
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    [Fact]
    public void TransDuckIco_ContainsFiveExpected32BitPngFramesMatchingTheBrandSourceAssets()
    {
        var icoPath = FindRepositoryFile("windows", "src", "TransDuck.App", "Assets", "TransDuck.ico");
        var iconBytes = File.ReadAllBytes(icoPath);
        var expectedSources = new Dictionary<int, string>
        {
            [16] = "icon_16x16.png",
            [32] = "icon_32x32.png",
            [64] = "icon_64x64.png",
            [128] = "icon_128x128.png",
            [256] = "icon_256x256.png",
        };

        Assert.True(iconBytes.Length >= 6 + (16 * expectedSources.Count));
        Assert.Equal((ushort)0, ReadUInt16(iconBytes, 0));
        Assert.Equal((ushort)1, ReadUInt16(iconBytes, 2));
        Assert.Equal((ushort)expectedSources.Count, ReadUInt16(iconBytes, 4));

        var frames = Enumerable.Range(0, expectedSources.Count)
            .Select(index => ReadFrame(iconBytes, 6 + (16 * index)))
            .OrderBy(frame => frame.Width)
            .ToArray();

        Assert.Equal(expectedSources.Keys.OrderBy(size => size), frames.Select(frame => frame.Width));
        Assert.Equal(frames.Length, frames.Select(frame => frame.Offset).Distinct().Count());
        foreach (var frame in frames)
        {
            Assert.Equal(frame.Width, frame.Height);
            Assert.Equal((ushort)1, frame.Planes);
            Assert.Equal((ushort)32, frame.BitCount);
            Assert.Equal((byte)0, frame.ColorCount);
            Assert.Equal((byte)0, frame.Reserved);
            Assert.True(frame.Offset >= 6 + (16 * expectedSources.Count));
            Assert.True(frame.Size > 0 && frame.Offset <= iconBytes.Length - frame.Size);

            var payload = iconBytes.AsSpan(frame.Offset, frame.Size).ToArray();
            Assert.True(payload.AsSpan().StartsWith(PngSignature));
            AssertPngDimensionsAndRgba(payload, frame.Width, frame.Height);

            var sourcePath = FindRepositoryFile(
                "assets",
                "brand-source-icon",
                expectedSources[frame.Width]);
            var sourceBytes = File.ReadAllBytes(sourcePath);

            Assert.Equal(Convert.ToHexString(SHA256.HashData(sourceBytes)), Convert.ToHexString(SHA256.HashData(payload)));
            Assert.Equal(sourceBytes, payload);
        }
    }

    [Fact]
    public void TransDuckTrayIco_ContainsFourDedicatedPngFramesMatchingTheDuckAssets()
    {
        var icoPath = FindRepositoryFile(
            "windows", "src", "TransDuck.Platform.Windows", "Assets", "TransDuck.Tray.ico");
        var iconBytes = File.ReadAllBytes(icoPath);
        var expectedSources = new Dictionary<int, string>
        {
            [16] = "tray_duck_color_16x16.png",
            [20] = "tray_duck_color_20x20.png",
            [24] = "tray_duck_color_24x24.png",
            [32] = "tray_duck_color_32x32.png",
        };

        Assert.Equal((ushort)0, ReadUInt16(iconBytes, 0));
        Assert.Equal((ushort)1, ReadUInt16(iconBytes, 2));
        Assert.Equal((ushort)expectedSources.Count, ReadUInt16(iconBytes, 4));
        var frames = Enumerable.Range(0, expectedSources.Count)
            .Select(index => ReadFrame(iconBytes, 6 + (16 * index)))
            .OrderBy(frame => frame.Width)
            .ToArray();

        Assert.Equal(expectedSources.Keys.OrderBy(static size => size),
            frames.Select(static frame => frame.Width));
        foreach (var frame in frames)
        {
            Assert.Equal(frame.Width, frame.Height);
            Assert.Equal((ushort)1, frame.Planes);
            Assert.Equal((ushort)32, frame.BitCount);
            Assert.True(frame.Offset >= 6 + (16 * expectedSources.Count));
            Assert.True(frame.Size > 0 && frame.Offset <= iconBytes.Length - frame.Size);
            var payload = iconBytes.AsSpan(frame.Offset, frame.Size).ToArray();
            var source = File.ReadAllBytes(FindRepositoryFile(
                "assets", "brand-source-icon", expectedSources[frame.Width]));

            Assert.Equal(source, payload);
            AssertPngDimensionsAndRgba(payload, frame.Width, frame.Height);
        }
    }

    [Fact]
    public void EmbeddedTrayIcon_EmbedsTheIcoAndSelectsTheNearestDpiFrame()
    {
        var source = File.ReadAllBytes(FindRepositoryFile(
            "windows", "src", "TransDuck.Platform.Windows", "Assets", "TransDuck.Tray.ico"));
        using var resource = typeof(ShellNotifyIconTrayService).Assembly
            .GetManifestResourceStream(EmbeddedTrayIcon.ResourceName);

        Assert.NotNull(resource);
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        Assert.Equal(source, buffer.ToArray());
        Assert.Equal(16, EmbeddedTrayIcon.SelectFrame(source, 16).Size);
        Assert.Equal(20, EmbeddedTrayIcon.SelectFrame(source, 17).Size);
        Assert.Equal(32, EmbeddedTrayIcon.SelectFrame(source, 25).Size);
        Assert.Equal(32, EmbeddedTrayIcon.SelectFrame(source, 64).Size);
    }

    [Fact]
    public void BrandIconSourceAndGenerator_PinTheApprovedMultiSizeAssetFlow()
    {
        var sourceBytes = File.ReadAllBytes(FindRepositoryFile(
            "assets", "brand-source-icon", "icon_source.png"));
        var generator = File.ReadAllText(FindRepositoryFile(
            "windows", "packaging", "New-AppIcon.ps1"));

        Assert.True(sourceBytes.AsSpan().StartsWith(PngSignature));
        AssertPngDimensionsAndRgba(sourceBytes, 1254, 1254);
        Assert.Contains("$pngSizes = @(16, 32, 64, 128, 256, 512, 1024)", generator,
            StringComparison.Ordinal);
        Assert.Contains("$icoSizes = @(16, 32, 64, 128, 256)", generator,
            StringComparison.Ordinal);
        Assert.Contains("$traySizes = @(16, 20, 24, 32)", generator,
            StringComparison.Ordinal);
        Assert.Contains("$icnsTypes = [ordered]@{", generator, StringComparison.Ordinal);
        Assert.Contains("Format32bppArgb", generator, StringComparison.Ordinal);
        Assert.Contains("icon_{0}x{0}.png", generator, StringComparison.Ordinal);
        Assert.Contains("tray_duck_color_{0}x{0}.png", generator, StringComparison.Ordinal);
        Assert.Contains("TrayIcoPath", generator, StringComparison.Ordinal);
        Assert.Contains("IO.BinaryWriter", generator, StringComparison.Ordinal);
    }

    [Fact]
    public void TransDuckIcns_ContainsEveryGeneratedPngFrame()
    {
        var expectedTypes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["icp4"] = 16,
            ["icp5"] = 32,
            ["icp6"] = 64,
            ["ic07"] = 128,
            ["ic08"] = 256,
            ["ic09"] = 512,
            ["ic10"] = 1024,
        };
        var iconBytes = File.ReadAllBytes(FindRepositoryFile(
            "assets", "brand-source-icon", "TransDuck.icns"));

        Assert.Equal("icns", System.Text.Encoding.ASCII.GetString(iconBytes, 0, 4));
        Assert.Equal(iconBytes.Length, checked((int)BinaryPrimitives.ReadUInt32BigEndian(iconBytes.AsSpan(4, 4))));
        var offset = 8;
        var found = new HashSet<string>(StringComparer.Ordinal);
        while (offset < iconBytes.Length)
        {
            var type = System.Text.Encoding.ASCII.GetString(iconBytes, offset, 4);
            var chunkLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(iconBytes.AsSpan(offset + 4, 4)));
            Assert.True(expectedTypes.TryGetValue(type, out var size));
            Assert.True(chunkLength > 8 && offset <= iconBytes.Length - chunkLength);
            var payload = iconBytes.AsSpan(offset + 8, chunkLength - 8).ToArray();
            var source = File.ReadAllBytes(FindRepositoryFile(
                "assets", "brand-source-icon", $"icon_{size}x{size}.png"));

            Assert.True(found.Add(type));
            Assert.Equal(source, payload);
            AssertPngDimensionsAndRgba(payload, size, size);
            offset += chunkLength;
        }

        Assert.Equal(iconBytes.Length, offset);
        Assert.Equal(expectedTypes.Keys.OrderBy(static key => key), found.OrderBy(static key => key));
    }

    [Fact]
    public void AppAndTrayUseSeparateEmbeddedIconsWithoutExternalIconLoading()
    {
        var project = File.ReadAllText(FindRepositoryFile(
            "windows", "src", "TransDuck.App", "TransDuck.App.csproj"));
        var platformProject = File.ReadAllText(FindRepositoryFile(
            "windows", "src", "TransDuck.Platform.Windows", "TransDuck.Platform.Windows.csproj"));
        var tray = StripComments(File.ReadAllText(FindRepositoryFile(
            "windows", "src", "TransDuck.Platform.Windows", "Tray", "ShellNotifyIconTrayService.cs")));
        var loader = StripComments(File.ReadAllText(FindRepositoryFile(
            "windows", "src", "TransDuck.Platform.Windows", "Tray", "EmbeddedTrayIcon.cs")));
        var interop = StripComments(File.ReadAllText(FindRepositoryFile(
            "windows", "src", "TransDuck.Platform.Windows", "Interop", "Win32ShellNative.cs")));
        var addIcon = Slice(tray, "private TrayOperationResult AddIcon()", "private NotifyIconData CreateIdentityData()");
        var createAddData = Slice(tray, "private NotifyIconData CreateAddData", "private void HandleMessage");

        Assert.Contains("<ApplicationIcon>Assets\\TransDuck.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("<EmbeddedResource Include=\"Assets\\TransDuck.Tray.ico\"", platformProject,
            StringComparison.Ordinal);
        Assert.Contains("LogicalName=\"TransDuck.Platform.Windows.Assets.TransDuck.Tray.ico\"", platformProject,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CopyToOutputDirectory", platformProject, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyToPublishDirectory", platformProject, StringComparison.Ordinal);
        Assert.Contains("using var icon = EmbeddedTrayIcon.Load(_messageWindow.Handle)", addIcon,
            StringComparison.Ordinal);
        Assert.Contains("CreateAddData(icon.DangerousGetHandle())", addIcon, StringComparison.Ordinal);
        Assert.Contains("data.IconHandle = iconHandle", createAddData, StringComparison.Ordinal);
        Assert.Contains("GetManifestResourceStream(ResourceName)", loader, StringComparison.Ordinal);
        Assert.Contains("CreateIconFromResourceEx", loader + interop, StringComparison.Ordinal);
        Assert.Contains("GetSystemMetricsForDpi", loader + interop, StringComparison.Ordinal);
        Assert.Contains("ReleaseHandle() => Win32ShellNative.DestroyIcon(handle)", interop,
            StringComparison.Ordinal);
        Assert.DoesNotContain("LoadCurrentProcessIcon", tray + interop, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Drawing", project + platformProject + tray + loader + interop,
            StringComparison.Ordinal);
        Assert.DoesNotContain("File.Open", tray + loader, StringComparison.Ordinal);
    }

    private static IconFrame ReadFrame(byte[] iconBytes, int offset)
    {
        var encodedWidth = iconBytes[offset];
        var encodedHeight = iconBytes[offset + 1];
        return new IconFrame(
            encodedWidth == 0 ? 256 : encodedWidth,
            encodedHeight == 0 ? 256 : encodedHeight,
            iconBytes[offset + 2],
            iconBytes[offset + 3],
            ReadUInt16(iconBytes, offset + 4),
            ReadUInt16(iconBytes, offset + 6),
            checked((int)ReadUInt32(iconBytes, offset + 8)),
            checked((int)ReadUInt32(iconBytes, offset + 12)));
    }

    private static void AssertPngDimensionsAndRgba(byte[] payload, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((uint)13, BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(8, 4)));
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(payload, 12, 4));
        Assert.Equal((uint)expectedWidth, BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(16, 4)));
        Assert.Equal((uint)expectedHeight, BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(20, 4)));
        Assert.Equal((byte)8, payload[24]);
        Assert.Equal((byte)6, payload[25]);
        Assert.Equal((byte)0, payload[26]);
        Assert.Equal((byte)0, payload[27]);
    }

    private static ushort ReadUInt16(byte[] source, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset, sizeof(ushort)));

    private static uint ReadUInt32(byte[] source, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset, sizeof(uint)));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, "The required source method must be present.");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, "The required source method must have a bounded body.");
        return source[start..end];
    }

    private static string FindRepositoryFile(params string[] relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine([directory.FullName, .. relativePath]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("The requested repository asset was not found from the test host path.");
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, "/\\*[\\s\\S]*?\\*/", string.Empty);
        return string.Join(
            Environment.NewLine,
            source.Split('\n').Select(line =>
            {
                var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
                return commentIndex < 0 ? line : line[..commentIndex];
            }));
    }

    private sealed record IconFrame(
        int Width,
        int Height,
        byte ColorCount,
        byte Reserved,
        ushort Planes,
        ushort BitCount,
        int Size,
        int Offset);
}
