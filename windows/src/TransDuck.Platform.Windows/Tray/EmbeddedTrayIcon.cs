// Copyright (c) 2026 maywine. All rights reserved.

using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Tray;

internal static class EmbeddedTrayIcon
{
    internal const string ResourceName = "TransDuck.Platform.Windows.Assets.TransDuck.Tray.ico";
    private const uint DefaultDpi = 96;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static unsafe SafeIconHandle Load(IntPtr windowHandle)
    {
        var dpi = Win32ShellNative.GetDpiForWindow(windowHandle);
        if (dpi == 0)
        {
            dpi = DefaultDpi;
        }

        var width = Win32ShellNative.GetSystemMetricsForDpi(Win32ShellNative.SmCxSmallIcon, dpi);
        var height = Win32ShellNative.GetSystemMetricsForDpi(Win32ShellNative.SmCySmallIcon, dpi);
        if (width <= 0 || height <= 0)
        {
            width = 16;
            height = 16;
        }

        using var resource = typeof(EmbeddedTrayIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The embedded tray icon resource is missing.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var frame = SelectFrame(buffer.ToArray(), Math.Max(width, height));

        fixed (byte* payload = frame.Payload)
        {
            var icon = Win32ShellNative.CreateIconFromResourceEx(
                payload,
                checked((uint)frame.Payload.Length),
                isIcon: true,
                Win32ShellNative.IconResourceVersion,
                width,
                height,
                Win32ShellNative.LrDefaultColor);
            if (!icon.IsInvalid)
            {
                return icon;
            }

            var error = Marshal.GetLastWin32Error();
            icon.Dispose();
            throw new Win32Exception(error, "The embedded tray icon could not be created.");
        }
    }

    internal static EmbeddedTrayIconFrame SelectFrame(byte[] iconBytes, int desiredSize)
    {
        ArgumentNullException.ThrowIfNull(iconBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(desiredSize);
        if (iconBytes.Length < 6 ||
            BinaryPrimitives.ReadUInt16LittleEndian(iconBytes.AsSpan(0, 2)) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(iconBytes.AsSpan(2, 2)) != 1)
        {
            throw new InvalidDataException("The embedded tray icon header is invalid.");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(iconBytes.AsSpan(4, 2));
        if (count == 0 || iconBytes.Length < 6 + (16 * count))
        {
            throw new InvalidDataException("The embedded tray icon directory is invalid.");
        }

        EmbeddedTrayIconFrame? smallestLarger = null;
        EmbeddedTrayIconFrame? largestSmaller = null;
        for (var index = 0; index < count; index++)
        {
            var entryOffset = 6 + (16 * index);
            var width = iconBytes[entryOffset] == 0 ? 256 : iconBytes[entryOffset];
            var height = iconBytes[entryOffset + 1] == 0 ? 256 : iconBytes[entryOffset + 1];
            var payloadSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                iconBytes.AsSpan(entryOffset + 8, 4)));
            var payloadOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                iconBytes.AsSpan(entryOffset + 12, 4)));
            if (width != height || payloadSize <= 0 || payloadOffset < 6 + (16 * count) ||
                payloadOffset > iconBytes.Length - payloadSize)
            {
                throw new InvalidDataException("The embedded tray icon frame is invalid.");
            }

            var payload = iconBytes.AsSpan(payloadOffset, payloadSize).ToArray();
            if (!payload.AsSpan().StartsWith(PngSignature))
            {
                throw new InvalidDataException("The embedded tray icon frame is not PNG encoded.");
            }

            var frame = new EmbeddedTrayIconFrame(width, payload);
            if (width >= desiredSize && (smallestLarger is null || width < smallestLarger.Size))
            {
                smallestLarger = frame;
            }
            else if (width < desiredSize && (largestSmaller is null || width > largestSmaller.Size))
            {
                largestSmaller = frame;
            }
        }

        return smallestLarger ?? largestSmaller
            ?? throw new InvalidDataException("The embedded tray icon has no usable frames.");
    }
}

internal sealed record EmbeddedTrayIconFrame(int Size, byte[] Payload);
