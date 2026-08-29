using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TransDuck.Platform.Windows.Capture;

/// <summary>
/// Captures one real Windows.Graphics.Capture monitor frame and crops it in physical pixels.
/// </summary>
public sealed class WindowsGraphicsCaptureService : IDisposable
{
    private readonly IDirect3DDevice _device = Direct3D11DeviceFactory.Create();
    private bool _disposed;

    public async Task<ScreenCaptureResult> CaptureAsync(
        ScreenSelection selection,
        CancellationToken cancellationToken,
        int framesToDiscard = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (framesToDiscard < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesToDiscard));
        }

        if (!selection.IsValid)
        {
            return ScreenCaptureResult.Failed("截图区域必须完全位于一个显示器的物理像素边界内。");
        }

        if (!GraphicsCaptureSession.IsSupported())
        {
            return ScreenCaptureResult.NotSupported();
        }

        try
        {
            var item = GraphicsCaptureItemFactory.CreateForMonitor(selection.Monitor.Handle);
            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size);
            using var session = framePool.CreateCaptureSession(item);
            var frameTask = new TaskCompletionSource<CapturedBitmap>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var frameReaderActive = 0;
            var remainingFramesToDiscard = framesToDiscard;
            using var captureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                captureTimeout.Token);
            var captureToken = linkedCancellation.Token;

            framePool.FrameArrived += OnFrameArrived;
            try
            {
                session.StartCapture();
                using var registration = captureToken.Register(
                    () => frameTask.TrySetCanceled(captureToken));
                var captured = await frameTask.Task;
                using (captured.Bitmap)
                {
                    var crop = MapToFramePixels(selection, captured.Size);
                    if (crop.IsEmpty)
                    {
                        return ScreenCaptureResult.Failed("截图帧与选择区域没有重叠。");
                    }

                    var croppedBitmap = await CropAsync(captured.Bitmap, crop, captureToken);
                    return ScreenCaptureResult.Succeeded(croppedBitmap, selection);
                }
            }
            finally
            {
                framePool.FrameArrived -= OnFrameArrived;
            }

            void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
            {
                if (frameTask.Task.IsCompleted || Interlocked.CompareExchange(ref frameReaderActive, 1, 0) != 0)
                {
                    return;
                }

                try
                {
                    Task<SoftwareBitmap> bitmapTask;
                    SizeInt32 frameSize;
                    using (var frame = sender.TryGetNextFrame())
                    {
                        if (frame is null)
                        {
                            throw new InvalidOperationException(
                                "Windows.Graphics.Capture 没有返回可读取的帧。");
                        }

                        frameSize = frame.ContentSize;
                        if (Interlocked.CompareExchange(ref remainingFramesToDiscard, 0, 0) > 0)
                        {
                            Interlocked.Decrement(ref remainingFramesToDiscard);
                            Volatile.Write(ref frameReaderActive, 0);
                            return;
                        }

                        // The frame and its surface stay on this callback apartment. The copy operation
                        // retains the surface reference and yields an independent SoftwareBitmap later.
                        bitmapTask = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface)
                            .AsTask(captureToken);
                    }

                    _ = CompleteFrameCopyAsync(bitmapTask, frameSize);
                }
                catch (Exception exception)
                {
                    frameTask.TrySetException(exception);
                    Volatile.Write(ref frameReaderActive, 0);
                }
            }

            async Task CompleteFrameCopyAsync(Task<SoftwareBitmap> bitmapTask, SizeInt32 frameSize)
            {
                try
                {
                    var bitmap = await bitmapTask.ConfigureAwait(false);
                    if (!frameTask.TrySetResult(new CapturedBitmap(bitmap, frameSize)))
                    {
                        bitmap.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    frameTask.TrySetException(exception);
                }
                finally
                {
                    Volatile.Write(ref frameReaderActive, 0);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ScreenCaptureResult.Cancelled();
        }
        catch (OperationCanceledException)
        {
            return ScreenCaptureResult.Failed("等待 Windows.Graphics.Capture 帧超时。");
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            return ScreenCaptureResult.Failed($"Windows.Graphics.Capture 失败：{exception.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _device.Dispose();
        _disposed = true;
    }

    private static PixelRect MapToFramePixels(ScreenSelection selection, SizeInt32 frameSize)
    {
        var monitor = selection.Monitor.PhysicalBounds;
        var relative = selection.PhysicalBounds.Offset(-monitor.Left, -monitor.Top);
        var scaleX = frameSize.Width / (double)monitor.Width;
        var scaleY = frameSize.Height / (double)monitor.Height;
        return new PixelRect(
            (int)Math.Floor(relative.Left * scaleX),
            (int)Math.Floor(relative.Top * scaleY),
            (int)Math.Ceiling(relative.Right * scaleX),
            (int)Math.Ceiling(relative.Bottom * scaleY))
            .Intersect(new PixelRect(0, 0, frameSize.Width, frameSize.Height));
    }

    private static async Task<SoftwareBitmap> CropAsync(
        SoftwareBitmap source,
        PixelRect crop,
        CancellationToken cancellationToken)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream)
            .AsTask(cancellationToken);
        encoder.SetSoftwareBitmap(source);
        await encoder.FlushAsync().AsTask(cancellationToken);
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        var transform = new BitmapTransform
        {
            Bounds = new BitmapBounds
            {
                X = (uint)crop.Left,
                Y = (uint)crop.Top,
                Width = (uint)crop.Width,
                Height = (uint)crop.Height,
            },
        };
        return await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage)
            .AsTask(cancellationToken);
    }

    private sealed record CapturedBitmap(SoftwareBitmap Bitmap, SizeInt32 Size);
}

/// <summary>
/// Represents one capture outcome. The caller owns and must dispose Bitmap on success.
/// </summary>
public sealed record ScreenCaptureResult(
    ScreenCaptureStatus Status,
    SoftwareBitmap? Bitmap = null,
    ScreenSelection? Selection = null,
    string? ErrorMessage = null)
{
    public static ScreenCaptureResult Succeeded(SoftwareBitmap bitmap, ScreenSelection selection) =>
        new(ScreenCaptureStatus.Succeeded, bitmap, selection);

    public static ScreenCaptureResult NotSupported() =>
        new(ScreenCaptureStatus.NotSupported,
            ErrorMessage: "当前设备不支持 Windows.Graphics.Capture。");

    public static ScreenCaptureResult Cancelled() => new(ScreenCaptureStatus.Cancelled);

    public static ScreenCaptureResult Failed(string message) =>
        new(ScreenCaptureStatus.Failed, ErrorMessage: message);
}

public enum ScreenCaptureStatus
{
    Succeeded,
    NotSupported,
    Cancelled,
    Failed,
}
