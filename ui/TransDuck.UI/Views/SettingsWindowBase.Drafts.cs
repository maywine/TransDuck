using Avalonia.Controls;

namespace TransDuck.UI.Views;

public partial class SettingsWindowBase
{
    // Raw, possibly invalid input belongs to this window only. Never persist or log drafts.
    private readonly Dictionary<string, ProviderDraft> _providerDrafts = new(StringComparer.Ordinal);
    private string? _editingProvider;
    private ProviderDraft? _providerBaseline;
    private bool _changingProvider;

    private TextBox[] ProviderTextFields =>
    [
        InstanceIdTextBox, EndpointTextBox, ModelTextBox, SourceLanguageTextBox,
        TargetLanguageTextBox, TimeoutSecondsTextBox, CredentialPasswordBox,
        SecondaryCredentialTextBox, VolcengineAccessKeyIdPasswordBox,
    ];

    private void InitializeProviderDraftTracking()
    {
        foreach (var field in ProviderTextFields)
        {
            field.TextChanged += (_, _) => UpdateProviderDraftStatus();
        }

        TimeoutNumericUpDown.PropertyChanged += (_, e) =>
        {
            if (e.Property == NumericUpDown.ValueProperty) UpdateProviderDraftStatus();
        };
        ClearCredentialCheckBox.IsCheckedChanged += (_, _) => UpdateProviderDraftStatus();
        Closed += (_, _) => ClearProviderDrafts();
    }

    protected void BeginProviderChange()
    {
        if (_editingProvider is { } provider && _providerBaseline is { } baseline)
        {
            var draft = CaptureProviderDraft();
            if (draft.Matches(baseline)) _providerDrafts.Remove(provider);
            else _providerDrafts[provider] = draft;
        }

        _changingProvider = true;
    }

    protected void CompleteProviderChange(string providerId)
    {
        _editingProvider = providerId;
        _providerBaseline = CaptureProviderDraft();
        if (_providerDrafts.TryGetValue(providerId, out var draft))
        {
            var fields = ProviderTextFields;
            for (var index = 0; index < fields.Length; index++) fields[index].Text = draft.Text[index];
            TimeoutNumericUpDown.Value = draft.Timeout;
            ClearCredentialCheckBox.IsChecked = draft.ClearCredential;
        }

        _changingProvider = false;
        UpdateProviderDraftStatus();
    }

    protected void AcceptCurrentProviderDraft()
    {
        if (_editingProvider is { } provider) _providerDrafts.Remove(provider);
        _editingProvider = null;
        _providerBaseline = null;
    }

    protected void ClearProviderDrafts()
    {
        _providerDrafts.Clear();
        _editingProvider = null;
        _providerBaseline = null;
        CredentialPasswordBox.Clear();
        SecondaryCredentialTextBox.Clear();
        VolcengineAccessKeyIdPasswordBox.Clear();
        UpdateProviderDraftStatus();
    }

    private ProviderDraft CaptureProviderDraft() => new(
        ProviderTextFields.Select(field => field.Text ?? string.Empty).ToArray(),
        TimeoutNumericUpDown.Value,
        ClearCredentialCheckBox.IsChecked);

    private void UpdateProviderDraftStatus()
    {
        if (_changingProvider) return;
        var dirty = _providerBaseline is { } baseline && !CaptureProviderDraft().Matches(baseline);
        ProviderDraftStatusElement.Text = UiStrings.Get(dirty || _providerDrafts.Count > 0
            ? "settings.draft.unsaved"
            : "settings.draft.hint");
    }

    private sealed class ProviderDraft(string[] text, decimal? timeout, bool? clearCredential)
    {
        public string[] Text { get; } = text;
        public decimal? Timeout { get; } = timeout;
        public bool? ClearCredential { get; } = clearCredential;
        public bool Matches(ProviderDraft other) => Text.SequenceEqual(other.Text) &&
            Timeout == other.Timeout && ClearCredential == other.ClearCredential;
    }
}
