using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TransDuck.UI.Views;
using Xunit;

namespace TransDuck.UI.Tests;

public sealed class SettingsDraftTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProviderSwitch_PreservesInvalidInputAndKeepsCredentialsWithTheirProvider(bool mac)
    {
        var window = new TestSettingsWindow(mac);
        try
        {
            window.Show();
            window.SwitchTo("first");
            window.Endpoint.Text = "unfinished address";
            window.Credential.Text = "unsaved test input";
            window.Target.Text = "unfinished language";
            window.SwitchTo("second");
            Assert.Equal("second endpoint", window.Endpoint.Text);
            Assert.Empty(window.Credential.Text!);
            window.Endpoint.Text = "second draft";
            window.SwitchTo("first");
            Assert.Equal("unfinished address", window.Endpoint.Text);
            Assert.Equal("unsaved test input", window.Credential.Text);
            Assert.Equal("unfinished language", window.Target.Text);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(UiStrings.Get("settings.draft.unsaved"),
                window.FindControl<TextBlock>("ProviderDraftStatusElement")!.Text);

            window.AcceptSavedProvider();
            window.SwitchTo("first");
            Assert.Empty(window.Credential.Text!);
            window.SwitchTo("second");
            Assert.Equal("second draft", window.Endpoint.Text);
            window.DiscardDrafts();
            window.SwitchTo("second");
            Assert.Equal("second endpoint", window.Endpoint.Text);
        }
        finally { window.Close(); }
    }

    private sealed class TestSettingsWindow : SettingsWindowBase
    {
        public TestSettingsWindow(bool mac)
        {
            if (mac) ConfigureForMacSettingsWindow();
            else ConfigureForWindowsSettingsWindow();
        }

        public TextBox Endpoint => EndpointTextBox;
        public TextBox Credential => CredentialPasswordBox;
        public TextBox Target => TargetLanguageTextBox;

        public void SwitchTo(string provider)
        {
            BeginProviderChange();
            Endpoint.Text = provider + " endpoint";
            Credential.Text = string.Empty;
            Target.Text = "zh-Hans";
            CompleteProviderChange(provider);
        }

        public void AcceptSavedProvider() => AcceptCurrentProviderDraft();
        public void DiscardDrafts() => ClearProviderDrafts();
    }
}
