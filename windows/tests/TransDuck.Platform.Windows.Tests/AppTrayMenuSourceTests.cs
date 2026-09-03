// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.RegularExpressions;

namespace TransDuck.Platform.Windows.Tests;

public sealed class AppTrayMenuSourceTests
{
    private static readonly Regex TrayMenuItemCallPattern = new(
        "ShellTrayMenuEntry\\.Command\\s*\\(\\s*" +
        "AppStrings\\.Get\\(\\s*\"(?<key>runtime\\.menu\\.[a-z_]+)\"\\s*\\)",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    [Fact]
    public void NativeTrayMenu_UsesResourceBackedLabelsAndASeparatorBeforeExit()
    {
        var source = ReadAppRuntimeCode();
        var expected = new[]
        {
            "runtime.menu.open_input",
            "runtime.menu.settings",
            "runtime.menu.history",
            "runtime.menu.exit",
        };
        var calls = TrayMenuItemCallPattern.Matches(source)
            .Cast<Match>()
            .ToArray();

        Assert.Equal(expected, calls.Select(match => match.Groups["key"].Value));
        var separator = source.IndexOf("ShellTrayMenuEntry.Separator()", StringComparison.Ordinal);
        var exit = source.IndexOf("AppStrings.Get(\"runtime.menu.exit\")", StringComparison.Ordinal);
        Assert.True(separator >= 0 && exit > separator);
    }

    private static string ReadAppRuntimeCode()
    {
        var source = File.ReadAllText(FindAppRuntimePath());
        source = Regex.Replace(source, "/\\*[\\s\\S]*?\\*/", string.Empty);
        return string.Join(
            Environment.NewLine,
            source.Split('\n').Select(line =>
            {
                var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
                return commentIndex < 0 ? line : line[..commentIndex];
            }));
    }

    private static string FindAppRuntimePath()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "windows",
                    "src",
                    "TransDuck.App",
                    "AppRuntime.cs");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("AppRuntime.cs was not found from the test host path.");
    }
}
