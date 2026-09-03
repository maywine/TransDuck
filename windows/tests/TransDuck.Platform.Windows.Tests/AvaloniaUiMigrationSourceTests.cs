using System.Xml.Linq;

namespace TransDuck.Platform.Windows.Tests;

public sealed class AvaloniaUiMigrationSourceTests
{
    [Fact]
    public void WindowsApplication_UsesAvaloniaWin32WithoutWpf()
    {
        var repository = FindRepositoryRoot();
        var appProject = XDocument.Load(Path.Combine(
            repository,
            "windows",
            "src",
            "TransDuck.App",
            "TransDuck.App.csproj"));
        var platformProject = XDocument.Load(Path.Combine(
            repository,
            "windows",
            "src",
            "TransDuck.Platform.Windows",
            "TransDuck.Platform.Windows.csproj"));
        var program = File.ReadAllText(Path.Combine(
            repository,
            "windows",
            "src",
            "TransDuck.App",
            "Program.cs"));

        Assert.DoesNotContain(appProject.Descendants(), element => element.Name.LocalName == "UseWPF");
        Assert.DoesNotContain(platformProject.Descendants(), element => element.Name.LocalName == "UseWPF");
        Assert.Contains(appProject.Descendants(), element =>
            element.Name.LocalName == "PackageReference" &&
            string.Equals(element.Attribute("Include")?.Value, "Avalonia.Win32", StringComparison.Ordinal));
        Assert.Contains(".UseWin32()", program, StringComparison.Ordinal);
        Assert.Contains(".UseSkia()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsSourcesAndMarkup_DoNotReferenceWpf()
    {
        var repository = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repository, "windows", "src");
        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .ToArray();
        var source = string.Join(Environment.NewLine, sourceFiles.Select(File.ReadAllText));
        var markupFiles = Directory.EnumerateFiles(
            Path.Combine(sourceRoot, "TransDuck.App"),
            "*.axaml",
            SearchOption.AllDirectories).ToArray();

        Assert.DoesNotContain("using System.Windows", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HwndSource", source, StringComparison.Ordinal);
        Assert.NotEmpty(markupFiles);
        Assert.All(markupFiles, path => Assert.Contains(
            "xmlns=\"https://github.com/avaloniaui\"",
            File.ReadAllText(path),
            StringComparison.Ordinal));
    }

    [Fact]
    public void WindowsAndMacApplications_ConsumeTheSameThreeSharedWindows()
    {
        var repository = FindRepositoryRoot();
        var windowsProject = File.ReadAllText(Path.Combine(
            repository, "windows", "src", "TransDuck.App", "TransDuck.App.csproj"));
        var macProject = File.ReadAllText(Path.Combine(
            repository, "macos", "src", "TransDuck.App", "TransDuck.App.csproj"));
        var sharedViews = Directory.EnumerateFiles(
                Path.Combine(repository, "ui", "TransDuck.UI", "Views"),
                "*WindowBase.axaml",
                SearchOption.TopDirectoryOnly)
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("ui\\TransDuck.UI\\TransDuck.UI.csproj", windowsProject, StringComparison.Ordinal);
        Assert.Contains("ui\\TransDuck.UI\\TransDuck.UI.csproj", macProject, StringComparison.Ordinal);
        Assert.Equal(
            ["HistoryWindowBase.axaml", "SettingsWindowBase.axaml", "TranslationWindowBase.axaml"],
            sharedViews);
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(repository, "windows", "src", "TransDuck.App", "Windows"),
            "*.axaml",
            SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(repository, "macos", "src", "TransDuck.App", "Views"),
            "*.axaml",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void PortableZip_AuditsAvaloniaNativeClosureInsteadOfWpfNativeFiles()
    {
        var repository = FindRepositoryRoot();
        var packagingRoot = Path.Combine(repository, "windows", "packaging");
        var package = File.ReadAllText(Path.Combine(packagingRoot, "Package-Zip.ps1"));
        var audit = File.ReadAllText(Path.Combine(packagingRoot, "Test-Package-Zip.ps1"));
        var combined = package + Environment.NewLine + audit;

        Assert.Contains("av_libglesv2.dll", combined, StringComparison.Ordinal);
        Assert.Contains("libHarfBuzzSharp.dll", combined, StringComparison.Ordinal);
        Assert.Contains("libSkiaSharp.dll", combined, StringComparison.Ordinal);
        Assert.Contains("AvaloniaNativeRuntimeX64", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("PresentationNative_cor3.dll", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("wpfgfx_cor3.dll", combined, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "windows")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("The repository root was not found from the test host path.");
    }
}
