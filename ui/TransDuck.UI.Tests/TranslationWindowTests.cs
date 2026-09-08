using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
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
    public void KeyboardTranslation_UsesPlatformModifierAndRespectsBusyState(bool mac)
    {
        var window = new TestTranslationWindow(mac);
        var input = window.FindControl<TextBox>("InputTextBoxElement")!;
        var translate = window.FindControl<Button>("TranslateButtonElement")!;
        var calls = new List<string>();
        window.TranslationRequested += (_, text) => calls.Add(text);
        try
        {
            window.Show();
            input.Text = "Example input";
            var modifier = mac ? RawInputModifiers.Meta : RawInputModifiers.Control;
            input.Focus();
            window.KeyPress(Key.Enter, modifier, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, modifier, PhysicalKey.Enter, "\r");
            Assert.Equal(new[] { "Example input" }, calls);
            translate.IsEnabled = false;
            window.KeyPress(Key.Enter, modifier, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, modifier, PhysicalKey.Enter, "\r");
            Assert.Single(calls);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceCopy_CopiesOnlyThatCardAndTracksStreamingText(bool mac)
    {
        var window = new TestTranslationWindow(mac);
        var first = new TranslationResultViewModel("first", "First", "", "Waiting", targetLanguage: "zh-Hans");
        window.Results.Add(first);
        window.Results.Add(new TranslationResultViewModel("second", "Second", "Other result", ""));
        try
        {
            window.Show();
            window.UpdateLayout();
            var copy = window.GetVisualDescendants().OfType<Button>().Single(button =>
                button.DataContext == first && AutomationProperties.GetAutomationId(button) == "CopySourceButton");
            Assert.False(copy.IsEnabled);
            first.Text = "First result";
            Dispatcher.UIThread.RunJobs();
            Assert.True(copy.IsEnabled);
            string? copied = null;
            window.ResultCopyRequested += (_, text) => copied = text;
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("First result", copied);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Text == first.TargetLanguageLabel && text.IsEffectivelyVisible);
            Assert.Equal(first.TargetLanguage, first.WithStatus("Cancelled").TargetLanguage);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactLayout_KeepsResultsVisibleAndShowsOnlyRelevantRecoveryActions(bool mac)
    {
        var window = new TestTranslationWindow(mac);
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        window.FindControl<TextBox>("InputTextBoxElement")!.Text = string.Join("\n", Enumerable.Repeat("Long input", 30));
        window.Results.Add(new TranslationResultViewModel("test", "Test", "A readable result", ""));
        try
        {
            window.Show();
            window.UpdateLayout();
            var cancel = window.FindControl<Button>("CancelButtonElement")!;
            var retry = window.FindControl<Button>("RetryButtonElement")!;
            Assert.False(cancel.IsVisible);
            Assert.False(retry.IsVisible);
            cancel.IsEnabled = true;
            Assert.True(cancel.IsVisible);
            cancel.IsEnabled = false;
            retry.IsEnabled = true;
            Assert.True(retry.IsVisible);
            window.UpdateLayout();
            var results = window.FindControl<ItemsControl>("ResultsItemsControlElement")!;
            var viewport = results.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(viewport.Bounds.Height >= 64, $"Result viewport is only {viewport.Bounds.Height} high.");
        }
        finally { window.Close(); }
    }

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
        public TestTranslationWindow(bool mac = false)
        {
            if (mac) ConfigureForMacDesktopWindow();
            else ConfigureForWindowsFloatingWindow();
            ResultItemsControl.ItemsSource = Results;
        }

        public ObservableCollection<TranslationResultViewModel> Results { get; } = [];
    }
}
