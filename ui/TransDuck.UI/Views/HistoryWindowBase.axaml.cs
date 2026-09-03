using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TransDuck.UI.Views;

public partial class HistoryWindowBase : Window
{
    public HistoryWindowBase()
    {
        InitializeComponent();
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? ClearRequested;
    public event EventHandler? CloseRequested;

    protected ListBox HistoryListBox => HistoryListBoxElement;
    protected TextBox HistorySourceTextBox => HistorySourceTextBoxElement;
    protected TextBox HistoryResultTextBox => HistoryResultTextBoxElement;
    protected TextBlock HistoryStatusTextBlock => HistoryStatusTextBlockElement;
    protected TextBlock StatusTextBlock => HistoryStatusTextBlockElement;
    protected Button RefreshHistoryButton => RefreshHistoryButtonElement;
    protected Button ClearHistoryButton => ClearHistoryButtonElement;

    protected void ConfigureForWindowsHistoryWindow()
    {
        Width = 760;
        Height = 580;
        MinWidth = 600;
        MinHeight = 420;
        CloseHistoryButtonElement.IsVisible = true;
    }

    protected void ConfigureForMacHistoryWindow()
    {
        Width = 720;
        Height = 560;
        MinWidth = 520;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CloseHistoryButtonElement.IsVisible = false;
    }

    protected void SetHistoryItems(IReadOnlyList<HistoryItemViewModel> items)
    {
        HistoryListBoxElement.ItemsSource = items;
        if (items.Count > 0)
        {
            HistoryListBoxElement.SelectedIndex = 0;
        }
        else
        {
            Display(null);
        }
    }

    private void HandleSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs) =>
        Display(HistoryListBoxElement.SelectedItem as HistoryItemViewModel);

    private void Display(HistoryItemViewModel? item)
    {
        HistorySourceTextBoxElement.Text = item?.SourceText ?? string.Empty;
        HistoryResultTextBoxElement.Text = item?.ResultText ?? string.Empty;
    }

    private void HandleRefreshClick(object? sender, RoutedEventArgs eventArgs) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void HandleClearClick(object? sender, RoutedEventArgs eventArgs) =>
        ClearRequested?.Invoke(this, EventArgs.Empty);

    private void HandleCloseClick(object? sender, RoutedEventArgs eventArgs) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
