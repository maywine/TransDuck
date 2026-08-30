// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Text;

namespace TransDuck.Infrastructure.Persistence;

/// <summary>
/// Writes same-directory temporary files and atomically moves them into place on success.
/// </summary>
public static class AtomicFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);

    public static Task WriteUtf8Async(
        string destinationPath,
        string content,
        CancellationToken cancellationToken) =>
        WriteBytesAsync(destinationPath, Utf8NoBom.GetBytes(content), cancellationToken);

    public static async Task WriteBytesAsync(
        string destinationPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("Persistence destination has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            "." + Path.GetFileName(destinationPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationPath))
            {
                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task<string> ReadUtf8Async(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Utf8NoBom.GetString(bytes);
    }
}
