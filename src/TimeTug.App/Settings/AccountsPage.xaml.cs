using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.App.Controls;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Diagnostics;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.Settings;

/// <summary>
/// The Accounts pane: the one accounts control (the pinned Windows row, the connected accounts as a list, "+" to add and "-" to remove the selected one, and under
/// it the waiting row with Cancel, the error line, "Use your browser instead", the configuration warning and how to withdraw a Windows consent) and Diagnostics.
/// It shows the view models' values and passes the person's choices to them. The control updates what it shows where it stands, so keyboard focus stays where it is.
/// </summary>
public sealed partial class AccountsPage : UserControl
{
    private readonly AccountsViewModel _model;
    private readonly DiagnosticsViewModel _diagnostics;
    private readonly SettingsShellViewModel _shell;

    public AccountsPage(AccountsViewModel model, DiagnosticsViewModel diagnostics, SettingsShellViewModel shell, SettingsText text, ILocalizer localizer, string title)
    {
        _model = model;
        _diagnostics = diagnostics;
        _shell = shell;
        InitializeComponent();
        Heading.Text = title;
        Intro.Text = model.IntroText;
        Box.Accounts = model;
        DiagnosticsHeader.Text = diagnostics.Title;
        DiagnosticsCard.Header = localizer.GetString("Diagnostics_LogTitle");
        DiagnosticsCard.Description = diagnostics.ExplanationText;
        AutomationProperties.SetName(DiagnosticsCard, localizer.GetString("Diagnostics_LogTitle"));
        CopyButton.Content = diagnostics.CopyText;
        AutomationProperties.SetName(CopyButton, diagnostics.CopyText);
        ClearButton.Content = diagnostics.ClearText;
        AutomationProperties.SetName(ClearButton, diagnostics.ClearText);
        SettingsHighlight.SetId(AccountsHost, AccountsViewModel.Ids.Accounts);
        SettingsHighlight.SetId(DiagnosticsHost, AccountsViewModel.Ids.Diagnostics);

        // Listen only while the page is on screen (the window keeps the page when another pane is open).
        Loaded += (_, _) =>
        {
            model.ListAccounts.CollectionChanged += OnRowsChanged;
            diagnostics.PropertyChanged += OnDiagnosticsChanged;
            SyncDiagnostics();
            ApplyHighlight();
        };
        Unloaded += (_, _) =>
        {
            model.ListAccounts.CollectionChanged -= OnRowsChanged;
            diagnostics.PropertyChanged -= OnDiagnosticsChanged;
        };
        SyncDiagnostics();
    }

    /// <summary>The accounts control (for the self-test).</summary>
    internal AccountsListBox AccountsList => Box;

    internal Button Copy => CopyButton;

    internal Button Clear => ClearButton;

    internal TextBlock DiagnosticsMessage => DiagnosticsStatus;

    internal ScrollViewer ScrollArea => Scroller;

    private void OnDiagnosticsChanged(object? sender, PropertyChangedEventArgs e) => SyncDiagnostics();

    // The rows are realized a moment after the collection changes; the account the flyout asked to open is tinted once its row exists.
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => DispatcherQueue.TryEnqueue(ApplyHighlight);

    private void ApplyHighlight() => SettingsHighlight.Apply(this, _shell.HighlightedId, _shell.AnimateHighlight);

    private void SyncDiagnostics()
    {
        DiagnosticsStatus.Text = _diagnostics.StatusText ?? string.Empty;
        DiagnosticsStatus.Visibility = _diagnostics.StatusText is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCopy(object sender, RoutedEventArgs e) => _diagnostics.Copy();

    private void OnClear(object sender, RoutedEventArgs e) => _diagnostics.Clear();
}
