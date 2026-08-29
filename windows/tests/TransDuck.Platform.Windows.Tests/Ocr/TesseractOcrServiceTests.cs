// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Platform.Windows.Ocr;
using Windows.Graphics.Imaging;

namespace TransDuck.Platform.Windows.Tests.Ocr;

public sealed class TesseractOcrServiceTests
{
    [Fact]
    public async Task RecognizeAsync_RecognizesEnglishWithoutPackageIdentity()
    {
        using var bitmap = await OcrFixtureAssets.LoadBitmapAsync(OcrFixtureAssets.CleanEnglish);
        using var service = CreateService();

        var result = await service.RecognizeAsync(bitmap, "en-US", CancellationToken.None);

        AssertRecognized(result, "EASYDICT OCR 314");
    }

    [Fact]
    public async Task RecognizeAsync_RecognizesSimplifiedChineseWithCanonicalWhitespace()
    {
        using var bitmap = await OcrFixtureAssets.LoadBitmapAsync(OcrFixtureAssets.CleanChinese);
        using var service = CreateService();

        var result = await service.RecognizeAsync(bitmap, "zh-Hans", CancellationToken.None);

        AssertRecognized(result, "易词 OCR 314");
        Assert.Contains("易", result.Text!);
        Assert.Contains("词", result.Text!);
    }

    [Fact]
    public async Task RecognizeAsync_RecognizesMixedChineseAndEnglish()
    {
        using var bitmap = await OcrFixtureAssets.LoadBitmapAsync(OcrFixtureAssets.Mixed);
        using var service = CreateService();

        var result = await service.RecognizeAsync(bitmap, "zh-CN", CancellationToken.None);

        AssertRecognized(result, "Easydict 翻译 OCR 2026");
    }

    [Theory]
    [InlineData("")]
    [InlineData("fr-FR")]
    [InlineData("not-a-language")]
    public async Task RecognizeAsync_RejectsInvalidOrUnsupportedLanguageTag(string languageTag)
    {
        using var bitmap = await OcrFixtureAssets.LoadBitmapAsync(OcrFixtureAssets.CleanEnglish);
        using var service = CreateService();

        var result = await service.RecognizeAsync(bitmap, languageTag, CancellationToken.None);

        Assert.Equal(WindowsOcrStatus.LanguageUnavailable, result.Status);
        Assert.Null(result.Text);
        var errorMessage = Assert.IsType<string>(result.ErrorMessage);
        Assert.False(errorMessage.Contains("系统没有安装", StringComparison.Ordinal));
        Assert.False(errorMessage.Contains(GetTessdataDirectory(), StringComparison.Ordinal));
        Assert.False(errorMessage.Contains("Exception", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RecognizeAsync_ReportsMissingTessdata()
    {
        using var bitmap = await OcrFixtureAssets.LoadBitmapAsync(OcrFixtureAssets.CleanEnglish);
        var missingDirectory = Path.Combine(Path.GetTempPath(), "TransDuckMissingTessdata", Guid.NewGuid().ToString("N"));
        using var service = new TesseractOcrService(missingDirectory);

        var result = await service.RecognizeAsync(bitmap, "en-US", CancellationToken.None);

        Assert.Equal(WindowsOcrStatus.Failed, result.Status);
        Assert.Null(result.Text);
        var errorMessage = Assert.IsType<string>(result.ErrorMessage);
        Assert.False(errorMessage.Contains(missingDirectory, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecognizeAsync_ReturnsCancelledForPreCancelledRequest()
    {
        using var bitmap = await OcrFixtureAssets.LoadBitmapAsync(OcrFixtureAssets.CleanEnglish);
        using var service = CreateService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.RecognizeAsync(bitmap, "en-US", cancellation.Token);

        Assert.Equal(WindowsOcrStatus.Cancelled, result.Status);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task RecognizeAsync_SerializesConcurrentNativeRequestsWithoutCorruptingResults()
    {
        using var english = await OcrFixtureAssets.LoadBitmapAsync(OcrFixtureAssets.CleanEnglish);
        using var chinese = await OcrFixtureAssets.LoadBitmapAsync(OcrFixtureAssets.CleanChinese);
        using var service = CreateService();

        var results = await Task.WhenAll(
            service.RecognizeAsync(english, "en-US", CancellationToken.None),
            service.RecognizeAsync(chinese, "zh-Hans", CancellationToken.None));

        AssertRecognized(results[0], "EASYDICT OCR 314");
        AssertRecognized(results[1], "易词 OCR 314");
    }

    private static TesseractOcrService CreateService() => new(GetTessdataDirectory());

    private static string GetTessdataDirectory()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var tessdata = Path.Combine(
                    directory.FullName,
                    "windows",
                    "third_party",
                    "tesseract",
                    "tessdata-best");
                if (File.Exists(Path.Combine(tessdata, "eng.traineddata")) &&
                    File.Exists(Path.Combine(tessdata, "chi_sim.traineddata")))
                {
                    return tessdata;
                }
            }
        }

        throw new DirectoryNotFoundException("The repository Tesseract tessdata-best directory was not found.");
    }

    private static void AssertRecognized(WindowsOcrReadResult result, string expectedText)
    {
        Assert.Equal(WindowsOcrStatus.Succeeded, result.Status);
        var text = Assert.IsType<string>(result.Text);
        Assert.Equal(expectedText, OcrFixtureAssets.Normalize(text));
        Assert.Null(result.ErrorMessage);
    }
}
