using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Diagnostics;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.Settings;

/// <summary>
/// The Accounts pane: the connected accounts (name, provider, the engine's status in words, "Sign in again" when it is needed and a menu to
/// remove the account after a question), the places to add an account from (those this build cannot sign in to are disabled with the reason),
/// the waiting state with Cancel during a sign-in, and Diagnostics. It shows the view models' values and passes the person's choices to them. The
/// engine publishes often, so the account cards are built again only when an account's row really changed, and the provider cards are updated where
/// they stand; keyboard focus stays where it is.
/// </summary>
public sealed partial class AccountsPage : UserControl
{
    private readonly AccountsViewModel _model;
    private readonly DiagnosticsViewModel _diagnostics;
    private readonly SettingsShellViewModel _shell;
    private readonly ILocalizer _text;
    private readonly List<AccountCard> _accountCards = [];
    private readonly Dictionary<ProviderItem, ProviderCard> _providerCards = [];
    private List<ConnectedAccount> _builtAccounts = [];
    private string _builtProviders = string.Empty;
    private ContentDialog? _dialog;
    private bool _asking;

    public AccountsPage(AccountsViewModel model, DiagnosticsViewModel diagnostics, SettingsShellViewModel shell, SettingsText text, ILocalizer localizer, string title)
    {
        _model = model;
        _diagnostics = diagnostics;
        _shell = shell;
        _text = localizer;
        InitializeComponent();
        Heading.Text = title;
        Intro.Text = model.IntroText;
        ConnectedHeader.Text = model.ConnectedTitle;
        EmptyText.Text = model.EmptyText;
        AddHeader.Text = model.AddTitle;
        CancelButton.Content = model.CancelText;
        AutomationProperties.SetName(CancelButton, model.CancelAutomationName);
        AutomationProperties.SetName(WaitingRing, model.CancelAutomationName);
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
            model.PropertyChanged += OnModelChanged;
            model.Providers.CollectionChanged += OnProvidersChanged;
            diagnostics.PropertyChanged += OnDiagnosticsChanged;
            Sync();
        };
        Unloaded += (_, _) =>
        {
            model.PropertyChanged -= OnModelChanged;
            model.Providers.CollectionChanged -= OnProvidersChanged;
            diagnostics.PropertyChanged -= OnDiagnosticsChanged;
            ForgetProviderCards();
        };
        Sync();
    }

    /// <summary>The account cards in order (for the self-test).</summary>
    internal IReadOnlyList<AccountCard> AccountCards => _accountCards;

    /// <summary>The provider card of this kind (for the self-test), or null.</summary>
    internal ProviderCard? ProviderCardOf(string kindId) => _providerCards.FirstOrDefault(p => p.Key.Id == kindId).Value;

    internal Border EmptyRowElement => EmptyRow;

    internal StackPanel WaitingPanel => WaitingRow;

    internal TextBlock WaitingLabel => WaitingText;

    internal Button CancelSignIn => CancelButton;

    internal InfoBar UnavailableNotice => UnavailableBar;

    internal InfoBar ErrorNotice => ErrorBar;

    internal Button Copy => CopyButton;

    internal Button Clear => ClearButton;

    internal TextBlock DiagnosticsMessage => DiagnosticsStatus;

    internal ScrollViewer ScrollArea => Scroller;

    internal ContentDialog? OpenDialog => _dialog;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    private void OnProvidersChanged(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

    private void OnDiagnosticsChanged(object? sender, PropertyChangedEventArgs e) => SyncDiagnostics();

    private void Sync()
    {
        EmptyRow.Visibility = _model.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (!_builtAccounts.SequenceEqual(_model.ConnectedAccounts))
        {
            BuildAccounts();
        }

        foreach (var card in _accountCards)
        {
            card.Update(_model.IsBusy);
        }

        var structure = string.Join('\n', _model.Providers.Select(p => $"{p.Id}|{p.Name}|{p.IsConfigured}|{p.Hint}|{p.AutomationName}"));
        if (structure != _builtProviders)
        {
            _builtProviders = structure;
            BuildProviders();
        }

        foreach (var card in _providerCards.Values)
        {
            card.Update();
        }

        WaitingRow.Visibility = _model.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        WaitingRing.IsActive = _model.IsBusy;
        WaitingText.Text = _model.WaitingText;
        ErrorBar.Title = _model.ErrorText ?? string.Empty;
        ErrorBar.IsOpen = _model.ErrorText is not null;
        UnavailableBar.Title = _model.UnavailableNotice ?? string.Empty;
        UnavailableBar.IsOpen = _model.UnavailableNotice is not null;
        SyncDiagnostics();
        SyncDialog();
    }

    private void SyncDiagnostics()
    {
        DiagnosticsStatus.Text = _diagnostics.StatusText ?? string.Empty;
        DiagnosticsStatus.Visibility = _diagnostics.StatusText is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- the account cards -------------------------------------------------------------------------------------------------------

    private void BuildAccounts()
    {
        _builtAccounts = [.. _model.ConnectedAccounts];
        _accountCards.Clear();
        AccountList.Children.Clear();
        foreach (var account in _builtAccounts)
        {
            var card = new AccountCard(account, _model);
            _accountCards.Add(card);
            AccountList.Children.Add(card.Host);
        }

        SettingsHighlight.Apply(this, _shell.HighlightedId, _shell.AnimateHighlight);
    }

    // ---- the provider cards ------------------------------------------------------------------------------------------------------

    private void ForgetProviderCards()
    {
        foreach (var (item, card) in _providerCards)
        {
            item.PropertyChanged -= card.OnItemChanged;
        }

        _providerCards.Clear();
        _builtProviders = string.Empty;
    }

    private void BuildProviders()
    {
        ForgetProviderCards();
        _builtProviders = string.Join('\n', _model.Providers.Select(p => $"{p.Id}|{p.Name}|{p.IsConfigured}|{p.Hint}|{p.AutomationName}"));
        ProviderList.Children.Clear();
        foreach (var item in _model.Providers)
        {
            var card = new ProviderCard(item, _model.AddButtonText);
            _providerCards[item] = card;
            item.PropertyChanged += card.OnItemChanged;
            ProviderList.Children.Add(card.Card);
        }
    }

    // ---- the person's choices ------------------------------------------------------------------------------------------------------

    private async void OnCancel(object sender, RoutedEventArgs e) => await RunAsync(_model.CancelAsync);

    private void OnCopy(object sender, RoutedEventArgs e) => _diagnostics.Copy();

    private void OnClear(object sender, RoutedEventArgs e) => _diagnostics.Clear();

    /// <summary>Shows the question for the account the view model has open, and closes it when the view model forgets it (the account is gone).</summary>
    private void SyncDialog()
    {
        if (_model.PendingRemoval is null)
        {
            _dialog?.Hide();
            return;
        }

        if (!_asking && IsLoaded)
        {
            _ = AskToRemoveAsync();
        }
    }

    private async Task AskToRemoveAsync()
    {
        _asking = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = _model.RemovalTitle,
                Content = _model.RemovalBody,
                PrimaryButtonText = _model.RemovalConfirmText,
                CloseButtonText = _model.RemovalCancelText,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            Theming.ThemeService.Match(dialog, XamlRoot);
            _dialog = dialog;
            var result = await dialog.ShowAsync();
            _dialog = null;
            if (result == ContentDialogResult.Primary)
            {
                await RunAsync(_model.ConfirmRemoveAsync);
            }
            else
            {
                _model.CancelRemove();
            }
        }
        finally
        {
            _dialog = null;
            _asking = false;
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

    // ---- cards ---------------------------------------------------------------------------------------------------------------------

    /// <summary>One connected account's card: icon, name, status, "Sign in again" when needed and the menu with Remove.</summary>
    internal sealed class AccountCard
    {
        private readonly Button? _signIn;
        private readonly MenuFlyoutItem _remove;

        public AccountCard(ConnectedAccount account, AccountsViewModel model)
        {
            Account = account;
            var description = new StackPanel { Spacing = 2 };
            description.Children.Add(IconLine(account.NeedsAttention ? Warning() : null, new TextBlock { Text = account.StatusText, TextWrapping = TextWrapping.Wrap }));
            if (account.NoticeText is { } notice)
            {
                description.Children.Add(IconLine(Warning(), new TextBlock { Text = notice, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] }));
            }

            Card = new SettingsCard
            {
                Header = account.Name,
                Description = description,
                HeaderIcon = new FontIcon { Glyph = ProviderGlyphs.Of(account.KindId) },
            };
            AutomationProperties.SetName(Card, account.AutomationName);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (account.CanSignInAgain)
            {
                _signIn = new Button { Content = model.SignInAgainText };
                AutomationProperties.SetName(_signIn, account.SignInAutomationName);
                _signIn.Click += async (_, _) => await RunAsync(() => model.ReauthorizeAsync(account.ConnectionId));
                actions.Children.Add(_signIn);
            }

            _remove = new MenuFlyoutItem { Text = model.RemoveText, Icon = new FontIcon { Glyph = "\uE74D" } };
            AutomationProperties.SetName(_remove, account.RemoveAutomationName);
            _remove.Click += (_, _) => model.BeginRemove(account.ConnectionId);
            var menu = new Button
            {
                Content = new FontIcon { Glyph = "\uE712", FontSize = 16 },
                Flyout = new MenuFlyout { Items = { _remove } },
                Padding = new Thickness(8, 6, 8, 6),
            };
            AutomationProperties.SetName(menu, account.ActionsAutomationName);
            ToolTipService.SetToolTip(menu, account.ActionsAutomationName);
            Menu = menu;
            actions.Children.Add(menu);
            Card.Content = actions;

            Host = new Grid();
            Host.Children.Add(Card);
            SettingsHighlight.SetId(Host, AccountsViewModel.Ids.AccountPrefix + account.ConnectionId);
        }

        public ConnectedAccount Account { get; }

        public SettingsCard Card { get; }

        public Grid Host { get; }

        public Button Menu { get; }

        public Button? SignInAgain => _signIn;

        public MenuFlyoutItem RemoveItem => _remove;

        /// <summary>Signing in again and removing are off while a sign-in runs.</summary>
        public void Update(bool busy)
        {
            if (_signIn is not null)
            {
                _signIn.IsEnabled = !busy;
            }

            _remove.IsEnabled = !busy;
        }

        /// <summary>An optional icon before text that wraps (a horizontal stack would measure the text without a width limit and cut it off).</summary>
        internal static Grid IconLine(FontIcon? icon, TextBlock text)
        {
            var line = new Grid { ColumnSpacing = 6 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            if (icon is not null)
            {
                line.Children.Add(icon);
            }
            else
            {
                line.ColumnDefinitions[0].Width = new GridLength(0);
                line.ColumnSpacing = 0;
            }

            Grid.SetColumn(text, 1);
            line.Children.Add(text);
            return line;
        }

        private static FontIcon Warning()
        {
            var icon = new FontIcon
            {
                Glyph = "\uE7BA",
                FontSize = 14,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            return icon;
        }
    }

    /// <summary>One place to add an account from: its icon, name, how it signs in (or why it cannot), the Add button and, after a failure, what went wrong.</summary>
    internal sealed class ProviderCard
    {
        private readonly ProviderItem _item;
        private readonly TextBlock _error;
        private readonly TextBlock _detail;
        private readonly StackPanel _errorRow;

        public ProviderCard(ProviderItem item, string addText)
        {
            _item = item;
            var description = new StackPanel { Spacing = 2 };
            description.Children.Add(new TextBlock { Text = item.Hint, TextWrapping = TextWrapping.Wrap });
            _error = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            };
            AutomationProperties.SetLiveSetting(_error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
            var icon = new FontIcon
            {
                Glyph = "\uE783",
                FontSize = 14,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            _errorRow = new StackPanel { Visibility = Visibility.Collapsed };
            _errorRow.Children.Add(AccountCard.IconLine(icon, _error));
            _detail = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Visibility = Visibility.Collapsed,
            };
            description.Children.Add(_errorRow);
            description.Children.Add(_detail);

            Card = new SettingsCard
            {
                Header = item.Name,
                Description = description,
                HeaderIcon = new FontIcon { Glyph = ProviderGlyphs.Of(item.Id) },
            };
            AutomationProperties.SetName(Card, item.AutomationName);
            Add = new Button { Content = addText, MinWidth = 72, Command = item.ConnectCommand };
            AutomationProperties.SetName(Add, item.AutomationName);
            Card.Content = Add;
            Update();
        }

        public SettingsCard Card { get; }

        public Button Add { get; }

        public ProviderItem Item => _item;

        /// <summary>The error text beside the provider (for the self-test).</summary>
        public string ErrorText => _error.Text;

        public string DetailText => _detail.Text;

        public void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Update();

        public void Update()
        {
            Add.IsEnabled = _item.IsEnabled;
            _error.Text = _item.Error ?? string.Empty;
            _errorRow.Visibility = _item.HasError ? Visibility.Visible : Visibility.Collapsed;
            _detail.Text = _item.ErrorDetail ?? string.Empty;
            _detail.Visibility = string.IsNullOrEmpty(_item.ErrorDetail) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
