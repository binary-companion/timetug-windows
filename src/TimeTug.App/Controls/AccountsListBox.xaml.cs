using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TimeTug.App.Settings;
using TimeTug.App.Theming;
using TimeTug.Presentation.Welcome;
using Windows.System;

namespace TimeTug.App.Controls;

/// <summary>
/// The one accounts control, shared by the welcome window and Settings > Accounts: a box with the pinned "This PC's Windows account" row (when Windows lists an
/// account the broker can use), the signed-in accounts as a selectable list, and a bar with "+" (a menu of what can be added) and "-" (removes the selected row
/// after a question). Under the box: the waiting row with Cancel, the error line, "Use your browser instead", the configuration warning and how to withdraw a
/// Windows consent. The behaviour contract is L1 to L10 of the shared accounts list spec; the values all come from <see cref="AccountsViewModel"/>.
/// The engine publishes often, so everything here is updated where it stands: rows come and go through the collection's own changes, nothing is rebuilt, and
/// keyboard focus moves only when the view model asks for it after a removal.
/// </summary>
public sealed partial class AccountsListBox : UserControl
{
    public static readonly DependencyProperty AccountsProperty =
        DependencyProperty.Register(nameof(Accounts), typeof(AccountsViewModel), typeof(AccountsListBox), new PropertyMetadata(null, OnAccountsChanged));

    private AccountsViewModel? _wired;
    private bool _syncing;
    private bool _collectionChanging;
    private string _builtMenu = string.Empty;
    private ContentDialog? _dialog;
    private bool _asking;
    private string? _removedId;
    private string? _removedName;
    private bool _deviceWasVisible;
    private bool _seenFirstSync;

    public AccountsListBox()
    {
        InitializeComponent();
        Loaded += (_, _) => Wire();
        Unloaded += (_, _) => Unwire();
    }

    /// <summary>The view model this control shows. Set once, before or after the control is loaded.</summary>
    public AccountsViewModel? Accounts
    {
        get => (AccountsViewModel?)GetValue(AccountsProperty);
        set => SetValue(AccountsProperty, value);
    }

    // ---- parts the self-tests look at ------------------------------------------------------------------------------------------------

    internal ListView Rows => List;

    internal Button AddPlus => AddButton;

    internal Button RemoveMinus => RemoveButton;

    internal MenuFlyout Menu => AddMenu;

    internal ToggleSwitch DeviceToggle => DeviceSwitch;

    internal Button DeviceSignInAgain => DeviceSignIn;

    internal FrameworkElement DeviceRowPanel => DevicePanel;

    internal TextBlock DeviceTitleText => DeviceTitle;

    internal TextBlock DeviceEmailText => DeviceEmail;

    internal TextBlock DeviceDescriptionText => DeviceDescription;

    internal TextBlock DeviceStatusText => DeviceStatus;

    internal TextBlock EmptyLineText => EmptyLine;

    internal FrameworkElement WaitingPanel => WaitingRow;

    internal TextBlock WaitingLabel => WaitingText;

    internal Button CancelSignIn => CancelButton;

    internal TextBlock ErrorLine => ErrorText;

    internal TextBlock ErrorDetailLine => ErrorDetail;

    internal HyperlinkButton BrowserFallbackLink => BrowserLink;

    internal InfoBar UnavailableNotice => UnavailableBar;

    internal TextBlock WithdrawConsent => WithdrawText;

    internal ContentDialog? OpenDialog => _dialog;

    /// <summary>The realized "Sign in again" button of this row, or null (not realized, or the account does not need it).</summary>
    internal Button? RowSignInAgain(string connectionId) =>
        Accounts is { } model && model.ListAccounts.FirstOrDefault(a => a.ConnectionId == connectionId) is { } item
        && List.ContainerFromItem(item) is ListViewItem container && FindButton(container) is { Visibility: Visibility.Visible } button ? button : null;

    private static void OnAccountsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (AccountsListBox)d;
        box.Unwire();
        if (box.IsLoaded)
        {
            box.Wire();
        }
    }

    // ---- wiring ----------------------------------------------------------------------------------------------------------------------

    private void Wire()
    {
        if (Accounts is not { } model || ReferenceEquals(_wired, model))
        {
            return;
        }

        _wired = model;
        CancelButton.Content = model.CancelText;
        AutomationProperties.SetName(CancelButton, model.CancelAutomationName);
        AutomationProperties.SetName(List, model.Text("Accounts_ListAutomation"));
        AutomationProperties.SetName(AddButton, model.Text("Accounts_AddAutomation"));
        ToolTipService.SetToolTip(AddButton, model.Text("Accounts_AddAutomation"));
        AutomationProperties.SetName(DeviceSwitch, model.DeviceRow.SwitchAutomationName);
        DeviceTitle.Text = model.DeviceRow.Title;
        EmptyLine.Text = model.ListEmptyText;
        WithdrawText.Text = model.WithdrawConsentText;
        BrowserLink.Content = model.BrowserFallbackText;
        AutomationProperties.SetName(BrowserLink, model.BrowserFallbackAutomationName);

        // This handler is attached before the list is given its items, so it runs first and can tell the selection handler that a change of rows is not the person's choice.
        model.ListAccounts.CollectionChanged += OnRowsChanging;
        List.ItemsSource = model.ListAccounts;
        model.PropertyChanged += OnModelChanged;
        model.DeviceRow.PropertyChanged += OnDeviceChanged;
        model.MenuEntries.CollectionChanged += OnMenuChanged;
        model.FocusRequested += OnFocusRequested;
        Sync();
    }

    private void Unwire()
    {
        if (_wired is not { } model)
        {
            return;
        }

        model.ListAccounts.CollectionChanged -= OnRowsChanging;
        _syncing = true;   // taking the items away makes the list drop its selection; that is not the person's choice
        List.ItemsSource = null;
        _syncing = false;
        model.PropertyChanged -= OnModelChanged;
        model.DeviceRow.PropertyChanged -= OnDeviceChanged;
        model.MenuEntries.CollectionChanged -= OnMenuChanged;
        model.FocusRequested -= OnFocusRequested;
        _wired = null;
        _builtMenu = string.Empty;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    private void OnDeviceChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    private void OnMenuChanged(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

    private void OnRowsChanging(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _collectionChanging = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _collectionChanging = false;
            Sync();
        });
    }

    // ---- showing the view model ------------------------------------------------------------------------------------------------------

    private void Sync()
    {
        if (_wired is not { } model)
        {
            return;
        }

        _syncing = true;
        try
        {
            SyncDeviceRow(model);
            SyncList(model);
            SyncBar(model);
            SyncUnderBox(model);
            SyncRemovedAnnouncement(model);
        }
        finally
        {
            _syncing = false;
        }

        SyncDialog(model);
        _seenFirstSync = true;
    }

    private void SyncDeviceRow(AccountsViewModel model)
    {
        var row = model.DeviceRow;
        DevicePanel.Visibility = row.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        DeviceDivider.Visibility = row.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        DeviceEmail.Text = row.Email ?? string.Empty;
        DeviceEmail.Visibility = row.Email is null ? Visibility.Collapsed : Visibility.Visible;
        DeviceDescription.Text = row.Description;
        DeviceDescription.Visibility = row.Description.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        DeviceStatus.Text = row.StatusText;
        DeviceStatusLine.Visibility = row.StatusText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        DeviceStatusIcon.Visibility = row.NeedsSignIn ? Visibility.Visible : Visibility.Collapsed;
        DeviceSwitch.IsOn = row.IsOn;
        DeviceSwitch.IsEnabled = row.IsSwitchEnabled;
        DeviceSignIn.Visibility = row.NeedsSignIn ? Visibility.Visible : Visibility.Collapsed;
        DeviceSignIn.IsEnabled = !model.IsBusy;
        DeviceSignIn.Content = model.SignInAgainText;
        AutomationProperties.SetName(DeviceSignIn, model.Text("Accounts_DeviceRowSignInAutomation"));
        AutomationProperties.SetName(DeviceSwitch, row.SwitchAutomationName);
        if (row.IsVisible && !_deviceWasVisible && _seenFirstSync && ActualWidth > 0)
        {
            // The listing answered after the control was on screen (not while an earlier step still hides it): Narrator hears it, and nothing takes focus.
            Announce(DeviceSwitch, row.Email is { } email ? $"{row.Title}, {email}" : row.Title, "device-row");
        }

        _deviceWasVisible = row.IsVisible;
    }

    private void SyncList(AccountsViewModel model)
    {
        EmptyLine.Visibility = model.ShowListEmptyText ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = model.ListAccounts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var wanted = model.SelectedAccountId is { } id ? model.ListAccounts.FirstOrDefault(a => a.ConnectionId == id) : null;
        if (!ReferenceEquals(List.SelectedItem, wanted))
        {
            List.SelectedItem = wanted;
        }

        foreach (var item in model.ListAccounts)
        {
            if (List.ContainerFromItem(item) is ListViewItem container && FindButton(container) is { } button)
            {
                button.IsEnabled = !model.IsBusy;
            }
        }
    }

    private void SyncBar(AccountsViewModel model)
    {
        AddButton.IsEnabled = model.IsAddEnabled;
        RemoveButton.IsEnabled = model.CanRemoveSelected;
        var removeName = model.RemoveSelectedAutomationName;
        AutomationProperties.SetName(RemoveButton, removeName);
        ToolTipService.SetToolTip(RemoveButton, removeName);

        var structure = string.Join('\n', model.MenuEntries.Select(e => $"{e.Id}|{e.Name}|{e.IsConfigured}|{e.Hint}|{e.AutomationName}"));
        if (structure != _builtMenu)
        {
            _builtMenu = structure;
            AddMenu.Items.Clear();
            foreach (var entry in model.MenuEntries)
            {
                var item = new MenuFlyoutItem
                {
                    Text = entry.Name,
                    IsEnabled = entry.IsConfigured,
                    Icon = new FontIcon { Glyph = ProviderGlyphs.Of(entry.Id) },
                    // The second line (why it is off, or "Use your browser instead") sits at the right edge of the item.
                    KeyboardAcceleratorTextOverride = entry.Hint,
                    Command = entry.ConnectCommand,
                };
                AutomationProperties.SetName(item, entry.AutomationName);
                if (entry.Hint.Length > 0)
                {
                    AutomationProperties.SetHelpText(item, entry.Hint);
                }

                AddMenu.Items.Add(item);
            }
        }
    }

    private void SyncUnderBox(AccountsViewModel model)
    {
        WaitingRow.Visibility = model.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        WaitingRing.IsActive = model.IsBusy;
        WaitingText.Text = model.WaitingText;

        var line = model.FallbackMessage ?? model.SignInFailureText ?? model.ErrorText;
        var detail = model.FallbackMessage is null ? model.SignInFailureDetail : null;
        ErrorText.Text = line ?? string.Empty;
        ErrorDetail.Text = detail ?? string.Empty;
        ErrorDetail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        ErrorPanel.Visibility = line is null ? Visibility.Collapsed : Visibility.Visible;
        BrowserLink.Visibility = model.ShowBrowserFallbackLink ? Visibility.Visible : Visibility.Collapsed;
        UnavailableBar.Title = model.UnavailableNotice ?? string.Empty;
        UnavailableBar.IsOpen = model.UnavailableNotice is not null;
        WithdrawText.Visibility = model.ShowWithdrawConsent ? Visibility.Visible : Visibility.Collapsed;
    }

    // "name removed", once the engine's state no longer holds the account the person confirmed removing.
    private void SyncRemovedAnnouncement(AccountsViewModel model)
    {
        if (_removedId is { } id && model.ConnectedAccounts.All(a => a.ConnectionId != id))
        {
            var name = _removedName;
            _removedId = null;
            _removedName = null;
            if (name is not null)
            {
                Announce(List, model.Text("Accounts_RemovedAnnouncement").Replace("{0}", name, StringComparison.Ordinal), "account-removed");
            }
        }
    }

    private static void Announce(FrameworkElement element, string text, string activityId)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.MostRecent, text, activityId);
    }

    // ---- the person's choices ------------------------------------------------------------------------------------------------------------

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _collectionChanging || _wired is not { } model)
        {
            return;
        }

        var id = (List.SelectedItem as ConnectedAccount)?.ConnectionId;
        if (id is not null && id != model.SelectedAccountId)
        {
            model.SelectedAccountId = id;
        }
        else if (id is null && model.SelectedAccountId is not null && List.SelectedItem is null && e.AddedItems.Count == 0 && e.RemovedItems.Count > 0)
        {
            // Nothing in the list gets deselected by the person (a single-selection list keeps its row); a plain clear is the view model's own doing.
            Sync();
        }
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e) => e.Handled = HandleKey(e.Key);

    /// <summary>Delete on the list asks the removal question for the selected row; with nothing selected (or a sign-in running) it does nothing. True when the key was taken.</summary>
    internal bool HandleKey(VirtualKey key)
    {
        if (key != VirtualKey.Delete || _wired is not { } model)
        {
            return false;
        }

        if (model.RemoveSelectedCommand.CanExecute(null))
        {
            model.RemoveSelectedCommand.Execute(null);
        }

        return true;
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_wired is { } model && model.RemoveSelectedCommand.CanExecute(null))
        {
            model.RemoveSelectedCommand.Execute(null);
        }
    }

    private async void OnDeviceToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing || _wired is not { } model)
        {
            return;
        }

        await RunAsync(() => model.DeviceRow.SetOnAsync(DeviceSwitch.IsOn));
        // The switch shows what the model says, which can differ from what the person asked for (a refused or cancelled action).
        Sync();
    }

    private async void OnDeviceSignInAgain(object sender, RoutedEventArgs e)
    {
        if (_wired is { } model && model.DeviceRow.SignInAgainCommand.CanExecute(null))
        {
            await RunAsync(() => model.DeviceRow.SignInAgainCommand.ExecuteAsync(null));
        }
    }

    private async void OnRowSignInAgain(object sender, RoutedEventArgs e)
    {
        if (_wired is { } model && ((FrameworkElement)sender).Tag is string id)
        {
            await RunAsync(() => model.ReauthorizeAsync(id));
        }
    }

    private async void OnCancel(object sender, RoutedEventArgs e)
    {
        if (_wired is { } model)
        {
            await RunAsync(model.CancelAsync);
        }
    }

    private async void OnBrowserLink(object sender, RoutedEventArgs e)
    {
        if (_wired is { } model)
        {
            await RunAsync(() => model.BrowserFallbackCommand.ExecuteAsync(null));
        }
    }

    // A row that is (re)bound gets its button state; nothing else about a row changes the control's focus.
    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is ListViewItem container && args.Item is ConnectedAccount account)
        {
            // What Narrator reads for the row: "Provider, name, status" plus ", On this PC" for a Windows account.
            AutomationProperties.SetName(container, account.AutomationName);
        }

        if (_wired is { } model && args.ItemContainer is ListViewItem row && FindButton(row) is { } button)
        {
            button.IsEnabled = !model.IsBusy;
        }
    }

    // ---- focus, as the view model asks it ------------------------------------------------------------------------------------------------

    private void OnFocusRequested(object? sender, FocusTarget target) =>
        DispatcherQueue.TryEnqueue(() => MoveFocus(target));

    private void MoveFocus(FocusTarget target)
    {
        if (target.Kind == FocusKind.AddButton || _wired is not { } model)
        {
            AddButton.Focus(FocusState.Programmatic);
            return;
        }

        if (model.ListAccounts.FirstOrDefault(a => a.ConnectionId == target.AccountId) is { } item)
        {
            List.UpdateLayout();
            if (List.ContainerFromItem(item) is ListViewItem container)
            {
                container.Focus(FocusState.Programmatic);
                return;
            }
        }

        AddButton.Focus(FocusState.Programmatic);
    }

    // ---- the removal question -----------------------------------------------------------------------------------------------------------

    private void SyncDialog(AccountsViewModel model)
    {
        if (model.PendingRemoval is null)
        {
            _dialog?.Hide();
            return;
        }

        if (!_asking && IsLoaded)
        {
            _ = AskToRemoveAsync(model);
        }
    }

    private async Task AskToRemoveAsync(AccountsViewModel model)
    {
        _asking = true;
        try
        {
            var body = model.RemovalWithdrawText is { } withdraw ? model.RemovalBody + "\n\n" + withdraw : model.RemovalBody;
            var removing = model.PendingRemoval;
            var dialog = new ContentDialog
            {
                Title = model.RemovalTitle,
                Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = model.RemovalConfirmText,
                CloseButtonText = model.RemovalCancelText,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            ThemeService.Match(dialog, XamlRoot);
            _dialog = dialog;
            var result = await dialog.ShowAsync();
            _dialog = null;
            if (result == ContentDialogResult.Primary)
            {
                _removedId = removing?.ConnectionId;
                _removedName = removing?.Name;
                await RunAsync(model.ConfirmRemoveAsync);
            }
            else
            {
                model.CancelRemove();
            }
        }
        finally
        {
            _dialog = null;
            _asking = false;
            Sync();
        }
    }

    private static async Task RunAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // The window is closing; there is nobody left to tell.
        }
    }

    private static Button? FindButton(DependencyObject node)
    {
        if (node is Button { Name: "SignInAgainButton" } found)
        {
            return found;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (FindButton(VisualTreeHelper.GetChild(node, i)) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }
}
