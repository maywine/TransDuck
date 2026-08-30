using TransDuck.Platform.MacOS.Ocr;

namespace TransDuck.Platform.MacOS.Tests.Ocr;

public sealed class VisionOcrServiceTests
{
    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("en", "en-US")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("zh-CN", "zh-Hans")]
    public async Task SupportedLanguage_IsCanonicalizedBeforeNativeRecognition(
        string languageTag,
        string expectedLanguage)
    {
        using var image = new OcrTemporaryImage();
        var backend = new FakeVisionBackend
        {
            Result = new VisionBackendResult(VisionBackendStatus.Succeeded, "recognized\n"),
        };
        var service = new VisionOcrService(backend);

        var result = await service.RecognizeAsync(image.Path, languageTag, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("recognized", result.Text);
        Assert.Equal(expectedLanguage, backend.Language);
        Assert.Equal(expectedLanguage == "en-US", backend.UseLanguageCorrection);
        Assert.Equal(Path.GetFullPath(image.Path), backend.ImagePath);
    }

    [Fact]
    public async Task UnsupportedLanguage_DoesNotInvokeNativeBackend()
    {
        using var image = new OcrTemporaryImage();
        var backend = new FakeVisionBackend();

        var result = await new VisionOcrService(backend)
            .RecognizeAsync(image.Path, "fr-FR", CancellationToken.None);

        Assert.Equal(MacOcrStatus.LanguageUnavailable, result.Status);
        Assert.Equal(0, backend.CallCount);
    }

    [Fact]
    public async Task MissingImageAndPreCancellation_DoNotInvokeNativeBackend()
    {
        var backend = new FakeVisionBackend();
        var service = new VisionOcrService(backend);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var missing = await service.RecognizeAsync("/missing/transduck.png", "en-US", CancellationToken.None);
        var cancelled = await service.RecognizeAsync("/missing/transduck.png", "en-US", cancellation.Token);

        Assert.Equal(MacOcrStatus.Failed, missing.Status);
        Assert.Equal(MacOcrStatus.Cancelled, cancelled.Status);
        Assert.Equal(0, backend.CallCount);
    }

    [Theory]
    [InlineData(VisionBackendStatus.Succeeded, null, MacOcrStatus.NoText)]
    [InlineData(VisionBackendStatus.Unsupported, null, MacOcrStatus.Unsupported)]
    [InlineData(VisionBackendStatus.Failed, null, MacOcrStatus.Failed)]
    public async Task BackendResults_MapToClosedStatuses(
        VisionBackendStatus backendStatus,
        string? text,
        MacOcrStatus expected)
    {
        using var image = new OcrTemporaryImage();
        var backend = new FakeVisionBackend { Result = new VisionBackendResult(backendStatus, text) };

        var result = await new VisionOcrService(backend)
            .RecognizeAsync(image.Path, "en-US", CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }
}

internal sealed class FakeVisionBackend : IVisionTextRecognizerBackend
{
    public VisionBackendResult Result { get; init; } =
        new(VisionBackendStatus.Succeeded, "text");

    public int CallCount { get; private set; }

    public string? ImagePath { get; private set; }

    public string? Language { get; private set; }

    public bool? UseLanguageCorrection { get; private set; }

    public VisionBackendResult Recognize(string imagePath, VisionRecognitionOptions options)
    {
        CallCount++;
        ImagePath = imagePath;
        Language = options.Language;
        UseLanguageCorrection = options.UseLanguageCorrection;
        return Result;
    }
}

internal sealed class OcrTemporaryImage : IDisposable
{
    public OcrTemporaryImage()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "TransDuck.Vision.Tests." + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(Path, [1, 2, 3]);
    }

    public string Path { get; }

    public void Dispose() => File.Delete(Path);
}
