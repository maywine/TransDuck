using System.Runtime.InteropServices;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace TransDuck.Platform.Windows.Ocr;

/// <summary>
/// Wraps Windows.Media.Ocr and makes its package identity and language prerequisites explicit.
/// </summary>
public sealed class WindowsOcrService : IOcrService
{
    public async Task<WindowsOcrReadResult> RecognizeAsync(
        SoftwareBitmap bitmap,
        string languageTag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        if (!PackageIdentity.HasIdentity())
        {
            return WindowsOcrReadResult.RequiresPackageIdentity();
        }

        Language language;
        try
        {
            language = new Language(languageTag);
        }
        catch (ArgumentException)
        {
            return WindowsOcrReadResult.Failed($"无效的 OCR 语言标记：{languageTag}。");
        }

        if (!OcrEngine.IsLanguageSupported(language))
        {
            return WindowsOcrReadResult.LanguageUnavailable(languageTag);
        }

        var maxDimension = OcrEngine.MaxImageDimension;
        if (bitmap.PixelWidth > maxDimension || bitmap.PixelHeight > maxDimension)
        {
            return WindowsOcrReadResult.ImageTooLarge(maxDimension);
        }

        var engine = OcrEngine.TryCreateFromLanguage(language);
        if (engine is null)
        {
            return WindowsOcrReadResult.Failed($"无法为 {languageTag} 创建 OCR 引擎。");
        }

        try
        {
            var operation = engine.RecognizeAsync(bitmap);
            using var registration = cancellationToken.Register(operation.Cancel);
            var result = await operation.AsTask(cancellationToken).ConfigureAwait(false);
            return WindowsOcrReadResult.Succeeded(result.Text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return WindowsOcrReadResult.Cancelled();
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            return WindowsOcrReadResult.Failed($"Windows OCR 失败：{exception.Message}");
        }
    }

    public void Dispose()
    {
        // Windows.Media.Ocr creates an engine per call and retains no native state here.
    }
}

public sealed record WindowsOcrReadResult(
    WindowsOcrStatus Status,
    string? Text = null,
    string? ErrorMessage = null)
{
    public static WindowsOcrReadResult Succeeded(string text) =>
        new(WindowsOcrStatus.Succeeded, Text: text);

    public static WindowsOcrReadResult RequiresPackageIdentity() =>
        new(WindowsOcrStatus.PackageIdentityRequired,
            ErrorMessage: "Windows.Media.Ocr 只能由已安装并以 MSIX package identity 运行的桌面应用调用。");

    public static WindowsOcrReadResult LanguageUnavailable(string languageTag) =>
        new(WindowsOcrStatus.LanguageUnavailable,
            ErrorMessage: $"系统没有安装 {languageTag} 所需的 OCR 语言资源。");

    public static WindowsOcrReadResult LocalLanguageUnavailable(string? languageTag) =>
        new(WindowsOcrStatus.LanguageUnavailable,
            ErrorMessage: string.IsNullOrWhiteSpace(languageTag)
                ? "本地 Tesseract OCR 未指定语言。"
                : $"本地 Tesseract OCR 不支持 {languageTag}。");

    public static WindowsOcrReadResult ImageTooLarge(uint maxDimension) =>
        new(WindowsOcrStatus.ImageTooLarge,
            ErrorMessage: $"OCR 图像的宽或高不能超过 {maxDimension} 像素。");

    public static WindowsOcrReadResult Cancelled() => new(WindowsOcrStatus.Cancelled);

    public static WindowsOcrReadResult Failed(string message) =>
        new(WindowsOcrStatus.Failed, ErrorMessage: message);
}

public enum WindowsOcrStatus
{
    Succeeded,
    PackageIdentityRequired,
    LanguageUnavailable,
    ImageTooLarge,
    Cancelled,
    Failed,
}

internal static partial class PackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentPackageFullName")]
    private static partial int GetCurrentPackageFullName(
        ref uint packageFullNameLength,
        IntPtr packageFullName);

    public static bool HasIdentity()
    {
        var length = 0u;
        var status = GetCurrentPackageFullName(ref length, IntPtr.Zero);
        return status == ErrorInsufficientBuffer;
    }
}
