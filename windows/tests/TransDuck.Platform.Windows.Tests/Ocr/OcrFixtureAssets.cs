// Copyright (c) 2026 maywine. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TransDuck.Platform.Windows.Tests.Ocr;

internal static class OcrFixtureAssets
{
    public const string CleanEnglish = "clean-en.png";
    public const string CleanChinese = "clean-zh.png";
    public const string Mixed = "mixed.png";

    private static readonly IReadOnlyDictionary<string, string> ExpectedSha256 =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CleanEnglish] = "e34ffe24d47368d79871b18ad74416aa750689aaa3f4e7da9f7f666981e79939",
            [CleanChinese] = "3120924597767adb68e24ec7a20b05d2a9069370adf8e257e0c04adfed13c571",
            [Mixed] = "3c83dab1c54e85bd44fa8e01813539d7d6f69667b1a8e2e71a0163dbab7bdd66",
        };

    public static string GetPath(string fileName) => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        fileName);

    public static async Task<SoftwareBitmap> LoadBitmapAsync(string fileName)
    {
        var path = GetPath(fileName);
        AssertCanonicalHash(fileName);
        var bytes = await File.ReadAllBytesAsync(path);
        using var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        try
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            writer.DetachStream();
            writer.Dispose();
        }
    }

    public static void AssertCanonicalHash(string fileName)
    {
        var expected = ExpectedSha256.TryGetValue(fileName, out var hash)
            ? hash
            : throw new ArgumentOutOfRangeException(nameof(fileName));
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(GetPath(fileName))))
            .ToLowerInvariant();
        Assert.Equal(expected, actual);
    }

    public static string Normalize(string text)
    {
        var collapsed = Regex.Replace(text.Normalize(NormalizationForm.FormKC), "\\s+", " ").Trim();
        var builder = new StringBuilder(collapsed.Length);
        for (var index = 0; index < collapsed.Length; index++)
        {
            if (collapsed[index] != ' ')
            {
                builder.Append(collapsed[index]);
                continue;
            }

            var next = index + 1;
            while (next < collapsed.Length && collapsed[next] == ' ')
            {
                next++;
            }

            if (builder.Length > 0 && next < collapsed.Length &&
                IsCjk(builder[^1]) && IsCjk(collapsed[next]))
            {
                index = next - 1;
                continue;
            }

            builder.Append(' ');
        }

        return builder.ToString();
    }

    private static bool IsCjk(char character) => character is >= '\u3400' and <= '\u4DBF' or
        >= '\u4E00' and <= '\u9FFF' or
        >= '\uF900' and <= '\uFAFF';
}
