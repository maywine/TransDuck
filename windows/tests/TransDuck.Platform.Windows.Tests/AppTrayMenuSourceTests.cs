// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.RegularExpressions;

namespace TransDuck.Platform.Windows.Tests;

public sealed class AppTrayMenuSourceTests
{
    private static readonly Regex TrayMenuItemCallPattern = new(
        "CreateMenuItem\\s*\\(\\s*\"(?<id>[A-Za-z][A-Za-z0-9]*)\"\\s*,\\s*" +
        "AppStrings\\.Get\\(\\s*\"(?<key>runtime\\.menu\\.[a-z_]+)\"\\s*\\)",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    [Fact]
    public void TrayMenu_UsesUniqueStableAutomationIdsAndResourceBackedLabels()
    {
        var source = ReadAppRuntimeCode();
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OpenInputTrayMenuItem"] = "runtime.menu.open_input",
            ["SettingsTrayMenuItem"] = "runtime.menu.settings",
            ["HistoryTrayMenuItem"] = "runtime.menu.history",
            ["ExitTrayMenuItem"] = "runtime.menu.exit",
        };
        var calls = TrayMenuItemCallPattern.Matches(source)
            .Cast<Match>()
            .ToArray();

        Assert.Contains("AutomationProperties.SetAutomationId", source, StringComparison.Ordinal);
        foreach (var menuItem in expected)
        {
            var matchingCalls = calls.Where(match => string.Equals(
                match.Groups["id"].Value,
                menuItem.Key,
                StringComparison.Ordinal)).ToArray();

            var call = Assert.Single(matchingCalls);
            Assert.Equal(menuItem.Value, call.Groups["key"].Value);
            Assert.Single(Regex.Matches(source, "\"" + Regex.Escape(menuItem.Key) + "\"").Cast<Match>());
        }
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
