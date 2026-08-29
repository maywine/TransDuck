// Copyright (c) 2026 maywine. All rights reserved.

namespace TransDuck.Platform.Windows.Tests.Persistence;

internal sealed class PersistenceTestDirectory : IDisposable
{
    private static readonly object ParentDirectoryGate = new();
    private readonly string _parentDirectory;

    public PersistenceTestDirectory()
    {
        _parentDirectory = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "TransDuck.Persistence.Tests"));
        RootDirectory = Path.Combine(_parentDirectory, Guid.NewGuid().ToString("N"));
        lock (ParentDirectoryGate)
        {
            Directory.CreateDirectory(RootDirectory);
        }
    }

    public string RootDirectory { get; }

    public string FilePath(string fileName) => Path.Combine(RootDirectory, fileName);

    public string DirectoryPath(string directoryName) => Path.Combine(RootDirectory, directoryName);

    public void AssertNoTemporaryFiles() =>
        Assert.Empty(Directory.EnumerateFiles(RootDirectory, "*.tmp", SearchOption.AllDirectories));

    public void Dispose()
    {
        if (!Directory.Exists(RootDirectory))
        {
            return;
        }

        var requiredPrefix = _parentDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(RootDirectory).StartsWith(requiredPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete a path outside the test temporary directory.");
        }

        Directory.Delete(RootDirectory, recursive: true);
        lock (ParentDirectoryGate)
        {
            try
            {
                if (Directory.Exists(_parentDirectory) &&
                    !Directory.EnumerateFileSystemEntries(_parentDirectory).Any())
                {
                    Directory.Delete(_parentDirectory);
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Parallel test cleanup may remove the empty shared parent first.
            }
            catch (IOException)
            {
                // Shared-parent cleanup is optional after this instance removed its isolated child.
            }
            catch (UnauthorizedAccessException)
            {
                // Another test host may still hold the shared temporary directory.
            }
        }
    }
}
