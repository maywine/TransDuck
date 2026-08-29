// Copyright (c) 2026 maywine. All rights reserved.

using Windows.Graphics.Imaging;

namespace TransDuck.Platform.Windows.Ocr;

/// <summary>
/// Defines an OCR adapter that owns any native engine resources it creates.
/// </summary>
public interface IOcrService : IDisposable
{
    Task<WindowsOcrReadResult> RecognizeAsync(
        SoftwareBitmap bitmap,
        string languageTag,
        CancellationToken cancellationToken);
}
