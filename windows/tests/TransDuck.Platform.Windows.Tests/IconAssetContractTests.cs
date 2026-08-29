// Copyright (c) 2026 maywine. All rights reserved.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

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
    public void AppProjectAndTraySource_UseTheEmbeddedProcessIconWithoutExternalIconLoading()
    {
        var project = File.ReadAllText(FindRepositoryFile(
            "windows", "src", "TransDuck.App", "TransDuck.App.csproj"));
        var tray = StripComments(File.ReadAllText(FindRepositoryFile(
            "windows", "src", "TransDuck.Platform.Windows", "Tray", "ShellNotifyIconTrayService.cs")));
        var interop = StripComments(File.ReadAllText(FindRepositoryFile(
            "windows", "src", "TransDuck.Platform.Windows", "Interop", "Win32ShellNative.cs")));
        var createData = Slice(tray, "private NotifyIconData CreateData()", "private void HandleMessage");
        var iconLoader = Slice(interop, "public static IntPtr LoadCurrentProcessIcon", "}");

        Assert.Contains("<ApplicationIcon>Assets\\TransDuck.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Drawing", project, StringComparison.Ordinal);
        Assert.Contains("IconHandle = Win32ShellNative.LoadCurrentProcessIcon(ApplicationIconId)", createData,
            StringComparison.Ordinal);
        Assert.DoesNotContain("TransDuck.ico", tray, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Drawing", tray + interop, StringComparison.Ordinal);
        Assert.True(iconLoader.IndexOf("GetModuleHandle(null)", StringComparison.Ordinal) >= 0);
        Assert.True(iconLoader.IndexOf("LoadIcon(module, iconName)", StringComparison.Ordinal) >= 0);
        Assert.True(iconLoader.IndexOf("LoadIcon(IntPtr.Zero, iconName)", StringComparison.Ordinal) >= 0);
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
