using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(TransDuck.UI.Tests.TestAppBuilder))]

namespace TransDuck.UI.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
        // Match the app's text measurement; the headless drawing stub overestimates
        // Latin glyph widths and gives misleading layout results for English UI.
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia()
        .UseHarfBuzz()
        .WithInterFont();
}

public sealed class TestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        UiStrings.InitializeForCurrentCulture();
    }
}
