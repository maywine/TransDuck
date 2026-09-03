// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.RegularExpressions;

namespace TransDuck.Platform.Windows.Tests;

public sealed class TrayContextMenuOwnerSourceTests
{
    [Fact]
    public void AppRuntime_DelegatesOpeningToTheFrameworkIndependentTrayMenu()
    {
        var source = ReadSource("TransDuck.App", "AppRuntime.cs");
        var menuMethod = source.IndexOf("private void ShowTrayMenu()", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private void ShowSettings()", menuMethod, StringComparison.Ordinal);
        var show = source.IndexOf("_trayMenu.Show()", menuMethod, StringComparison.Ordinal);

        Assert.True(menuMethod >= 0, "The tray menu opening path must be present.");
        Assert.True(nextMethod > menuMethod, "The tray menu opening path must have a bounded method body.");
        Assert.True(show > menuMethod && show < nextMethod);
        Assert.Contains("_dispatcher.Post", source[menuMethod..nextMethod], StringComparison.Ordinal);
    }

    [Fact]
    public void NativeTrayMenu_ActivatesItsOwnerBeforeTrackingThePopup()
    {
        var source = ReadSource("TransDuck.Platform.Windows", "Tray", "ShellTrayContextMenu.cs");
        var method = source.IndexOf("public Action? Show()", StringComparison.Ordinal);
        var dispose = source.IndexOf("public void Dispose()", method, StringComparison.Ordinal);
        var nativeActivation = source.IndexOf("Win32ShellNative.SetForegroundWindow(_owner.Handle)", method, StringComparison.Ordinal);
        var popup = source.IndexOf("TrackPopupMenuEx(", nativeActivation, StringComparison.Ordinal);

        Assert.True(method >= 0, "The native tray menu opening path must be public and callable.");
        Assert.True(dispose > method, "The opening path must have a bounded method body.");
        Assert.True(nativeActivation > method && nativeActivation < dispose);
        Assert.True(popup > nativeActivation && popup < dispose);
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
