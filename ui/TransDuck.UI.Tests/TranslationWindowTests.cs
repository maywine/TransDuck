using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TransDuck.UI.Views;
using Xunit;

namespace TransDuck.UI.Tests;

public sealed class TranslationWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsResults_RemainReadableWhenTheApplicationThemeChanges(bool dark)
    {
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var window = new TestTranslationWindow();
        var result = new TranslationResultViewModel("test", "Test source", "Translated text", "Receiving");
        window.Results.Add(result);
        try
        {
            window.Show();
            foreach (var useDarkTheme in new[] { dark, !dark, dark })
            {
                Application.Current.RequestedThemeVariant = useDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var textBox = Assert.Single(ResultTextBoxes(window));
                Assert.Equal(result.Text, textBox.Text);
                AssertReadable(textBox, textBox.Foreground);
                var labels = window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(label => label.Text == result.DisplayName || label.Text == result.Status).ToArray();
                Assert.Equal(2, labels.Length);
                foreach (var label in labels)
                {
                    AssertReadable(label, label.Foreground);
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsResults_DisplayStreamingUpdatesAndFailuresAndCopyCompletedText(bool dark)
    {
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var window = new TestTranslationWindow();
        var result = new TranslationResultViewModel("first", "First source", "", "Waiting");
        var failed = new TranslationResultViewModel("second", "Second source", "", "Waiting");
        try
        {
            window.Show();
            window.Results.Add(result);
            window.Results.Add(failed);
            foreach (var text in new[] { "Translated", "Translated text" })
            {
                result.Text = text;
                result.Status = "Receiving";
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var textBox = Assert.Single(ResultTextBoxes(window), control => control.DataContext == result);
                Assert.Equal(text, textBox.Text);
            }

            result.Status = "";
            failed.Text = "Translation service is unavailable.";
            failed.Status = "Failed";
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { result.Text, failed.Text }, ResultTextBoxes(window).Select(control => control.Text));
            foreach (var textBox in ResultTextBoxes(window))
            {
                AssertReadable(textBox, textBox.Foreground);
            }

            string? copiedText = null;
            window.ResultCopyRequested += (_, text) => copiedText = text;
            window.FindControl<Button>("CopyResultButtonElement")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(
                $"{result.DisplayName}{Environment.NewLine}{result.Text}{Environment.NewLine}{Environment.NewLine}" +
                $"{failed.DisplayName}{Environment.NewLine}{failed.Text}",
                copiedText);
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<TextBox> ResultTextBoxes(Window window) =>
        window.GetVisualDescendants().OfType<TextBox>().Where(control => control.Classes.Contains("resultText"));

    private static void AssertReadable(Control control, IBrush? foregroundBrush)
    {
        var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(foregroundBrush).Color;
        var backgrounds = control.GetVisualAncestors().OfType<Border>()
            .Select(border => border.Background).OfType<ISolidColorBrush>().ToArray();
        Assert.Contains(backgrounds, brush => brush.Color.A == 255);
        var background = backgrounds.Reverse().Aggregate(Colors.Transparent,
            (under, brush) => Blend(brush.Color, under, brush.Opacity));
        foreground = Blend(foreground, background, control.Opacity * foregroundBrush!.Opacity);
        var lighter = Math.Max(Luminance(foreground), Luminance(background));
        var darker = Math.Min(Luminance(foreground), Luminance(background));
        Assert.True((lighter + 0.05) / (darker + 0.05) >= 4.5,
            $"Result text {foreground} is not readable on {background}.");
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
    }

    private static Color Blend(Color over, Color under, double opacity)
    {
        var alpha = over.A / 255d * opacity;
        return Color.FromRgb(
            (byte)Math.Round(over.R * alpha + under.R * (1 - alpha)),
            (byte)Math.Round(over.G * alpha + under.G * (1 - alpha)),
            (byte)Math.Round(over.B * alpha + under.B * (1 - alpha)));
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private sealed class TestTranslationWindow : TranslationWindowBase
    {
        public TestTranslationWindow()
        {
            ConfigureForWindowsFloatingWindow();
            ResultItemsControl.ItemsSource = Results;
        }

        public ObservableCollection<TranslationResultViewModel> Results { get; } = [];
    }
}
