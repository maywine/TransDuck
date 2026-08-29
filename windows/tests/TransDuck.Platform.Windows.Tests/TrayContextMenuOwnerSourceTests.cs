// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.RegularExpressions;

namespace TransDuck.Platform.Windows.Tests;

public sealed class TrayContextMenuOwnerSourceTests
{
    [Fact]
    public void AppRuntime_ActivatesTrayContextMenuOwnerBeforeOpeningMenu()
    {
        var source = ReadSource("TransDuck.App", "AppRuntime.cs");
        var menuMethod = source.IndexOf("private void ShowTrayMenu()", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private void ShowSettings()", menuMethod, StringComparison.Ordinal);
        var activation = source.IndexOf("_trayService.TryActivateContextMenuOwner()", menuMethod, StringComparison.Ordinal);
        var isOpen = source.IndexOf("_trayMenu.IsOpen = true", menuMethod, StringComparison.Ordinal);

        Assert.True(menuMethod >= 0, "The tray menu opening path must be present.");
        Assert.True(nextMethod > menuMethod, "The tray menu opening path must have a bounded method body.");
        Assert.True(activation > menuMethod && activation < nextMethod);
        Assert.True(isOpen > activation && isOpen < nextMethod);
    }

    [Fact]
    public void TrayService_DisposedContextMenuOwnerActivationReturnsFalseBeforeNativeAccess()
    {
        var source = ReadSource("TransDuck.Platform.Windows", "Tray", "ShellNotifyIconTrayService.cs");
        var method = source.IndexOf("public bool TryActivateContextMenuOwner()", StringComparison.Ordinal);
        var dispose = source.IndexOf("public void Dispose()", method, StringComparison.Ordinal);
        var disposedGuard = source.IndexOf("if (_disposed)", method, StringComparison.Ordinal);
        var falseReturn = source.IndexOf("return false", disposedGuard, StringComparison.Ordinal);
        var nativeActivation = source.IndexOf("Win32ShellNative.SetForegroundWindow(_messageWindow.Handle)", method, StringComparison.Ordinal);

        Assert.True(method >= 0, "The tray owner activation boundary must be public and callable.");
        Assert.True(dispose > method, "The activation boundary must have a bounded method body.");
        Assert.True(disposedGuard > method && disposedGuard < dispose);
        Assert.True(falseReturn > disposedGuard && falseReturn < nativeActivation);
        Assert.True(nativeActivation > falseReturn && nativeActivation < dispose);
    }

    private static string ReadSource(string projectDirectory, params string[] relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(
                    [directory.FullName, "windows", "src", projectDirectory, .. relativePath]);
                if (File.Exists(candidate))
                {
                    return StripComments(File.ReadAllText(candidate));
                }
            }
        }

        throw new FileNotFoundException("The requested Windows source file was not found from the test host path.");
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, "/\\*[\\s\\S]*?\\*/", string.Empty);
        return string.Join(
            Environment.NewLine,
            source.Split('\n').Select(line =>
            {
                var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
                return commentIndex < 0 ? line : line[..commentIndex];
            }));
    }
}
