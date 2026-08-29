// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Runtime.InteropServices;
using Tesseract;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TransDuck.Platform.Windows.Ocr;

/// <summary>
/// Runs the shipped Tesseract models without a package identity or network dependency.
/// </summary>
public sealed class TesseractOcrService : IOcrService
{
    private const string TessdataDirectoryName = "tessdata";
    private readonly Dictionary<string, TesseractEngine> _engines = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _engineGate = new(1, 1);
    private readonly string _tessdataDirectory;
    private int _disposeRequested;

    public TesseractOcrService(string? tessdataDirectory = null)
    {
        _tessdataDirectory = Path.GetFullPath(
            tessdataDirectory ?? Path.Combine(AppContext.BaseDirectory, TessdataDirectoryName));
    }

    public async Task<WindowsOcrReadResult> RecognizeAsync(
        SoftwareBitmap bitmap,
        string languageTag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        if (cancellationToken.IsCancellationRequested)
        {
            return WindowsOcrReadResult.Cancelled();
        }

        var language = MapLanguage(languageTag);
        if (language is null)
        {
            return WindowsOcrReadResult.LocalLanguageUnavailable(languageTag);
        }

        try
        {
            await _engineGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return WindowsOcrReadResult.Cancelled();
        }

        try
        {
            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                return WindowsOcrReadResult.Failed("本地 Tesseract OCR 服务已关闭。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(Path.Combine(_tessdataDirectory, language + ".traineddata")))
            {
                return WindowsOcrReadResult.Failed("本地 Tesseract OCR 模型不可用。");
            }

            byte[] png;
            try
            {
                png = await EncodePngAsync(bitmap, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return WindowsOcrReadResult.Cancelled();
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                return WindowsOcrReadResult.Failed("无法准备 OCR 图像。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var engine = GetOrCreateEngine(language);
                using var image = Pix.LoadFromMemory(png);
                cancellationToken.ThrowIfCancellationRequested();

                // Tesseract has no cancellation API. Keep the gate until Process returns so an
                // in-flight native call cannot overlap a subsequent request or engine disposal.
                using var page = engine.Process(image);
                var text = page.GetText().TrimEnd();
                return cancellationToken.IsCancellationRequested
                    ? WindowsOcrReadResult.Cancelled()
                    : WindowsOcrReadResult.Succeeded(text);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return WindowsOcrReadResult.Cancelled();
            }
            catch (Exception exception) when (IsNativeRuntimeFailure(exception))
            {
                return WindowsOcrReadResult.Failed("本地 Tesseract 运行时不可用。");
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                return WindowsOcrReadResult.Failed("本地 Tesseract OCR 识别失败。");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return WindowsOcrReadResult.Cancelled();
        }
        finally
        {
            _engineGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        _engineGate.Wait();
        try
        {
            foreach (var engine in _engines.Values)
            {
                engine.Dispose();
            }

            _engines.Clear();
        }
        finally
        {
            _engineGate.Release();
        }
    }

    private TesseractEngine GetOrCreateEngine(string language)
    {
        if (_engines.TryGetValue(language, out var existing))
        {
            return existing;
        }

        var engine = new TesseractEngine(_tessdataDirectory, language, EngineMode.Default);
        try
        {
            engine.DefaultPageSegMode = PageSegMode.SingleBlock;
            _engines.Add(language, engine);
            return engine;
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    private static async Task<byte[]> EncodePngAsync(
        SoftwareBitmap bitmap,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (stream.Size > int.MaxValue)
        {
            throw new InvalidOperationException("Encoded OCR image exceeds the supported size.");
        }

        stream.Seek(0);
        using var reader = new DataReader(stream);
        var byteCount = checked((uint)stream.Size);
        var loaded = await reader.LoadAsync(byteCount).AsTask(cancellationToken).ConfigureAwait(false);
        if (loaded != byteCount)
        {
            throw new InvalidOperationException("Could not read the complete encoded OCR image.");
        }

        var png = new byte[checked((int)stream.Size)];
        reader.ReadBytes(png);
        return png;
    }

    private static string? MapLanguage(string? languageTag) =>
        languageTag?.Trim().ToLowerInvariant() switch
        {
            "en-us" => "eng",
            "zh-hans" or "zh-cn" or "zh-hans-cn" => "chi_sim",
            _ => null,
        };

    private static bool IsNativeRuntimeFailure(Exception exception) =>
        exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException ||
        exception is TypeInitializationException { InnerException: { } innerException } &&
            IsNativeRuntimeFailure(innerException);

    private static bool IsFatal(Exception exception) =>
        exception is AccessViolationException or OutOfMemoryException or StackOverflowException;
}
