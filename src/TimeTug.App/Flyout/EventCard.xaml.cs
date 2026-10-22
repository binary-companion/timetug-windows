using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;
using Windows.ApplicationModel.DataTransfer;

namespace TimeTug.App.Flyout;

/// <summary>What a card can ask for: Join, and the merge menu's three actions (all by event id; the flyout view model checks them).</summary>
internal sealed record CardActions(
    Action<string> Join,
    Action<string> Unmerge,
    Action<string, string> Separate,
    Action<string, string> Merge,
    Action<bool> MenuOpenChanged);

/// <summary>
/// One agenda item as the Mac's <c>EventCard</c> draws it: the time, the calendar's colour bar, the title with a "Now" or countdown
/// pill, the meta line, a progress bar for the meeting in progress, and a Join button. When opening the link failed, the card shows
/// the link in a box with a Copy button, like the tug. Everything it shows is already decided in <see cref="FlyoutRow"/>.
/// </summary>
internal sealed partial class EventCard : UserControl
{
    private readonly FlyoutRow _row;
    private readonly FlyoutPalette _palette;
    private readonly CardActions _actions;
    private readonly string _copiedText;

    public EventCard(FlyoutRow row, FlyoutPalette palette, ILocalizer text, CardActions actions)
    {
        _row = row;
        _palette = palette;
        _actions = actions;
        _copiedText = text.GetString("Tug_LinkCopied");
        InitializeComponent();
        Draw(text);
    }

    public FlyoutRow Row => _row;

    /// <summary>The Join button (null when the row has no link), for the keyboard path and the self-test.</summary>
    public Button? Join => _row.HasJoin ? JoinButton : null;

    private void Draw(ILocalizer text)
    {
        var row = _row;
        var palette = _palette;
        var dim = row.IsPast;
        var titleBrush = dim ? palette.Secondary : palette.Primary;

        // The next meeting is picked out with the accent border (and a faint wash on the Mac); everything else has the quiet card edge.
        Frame.Background = palette.CardFill;
        Frame.BorderBrush = row.IsNext || row.IsCurrent && palette.IsContrast ? palette.Blue : palette.CardStroke;
        Frame.BorderThickness = new Thickness(row.IsNext ? 1.5 : 1);
        AutomationProperties.SetName(this, row.AutomationName);

        TimeText.Text = row.TimeText;
        TimeText.Foreground = palette.Secondary;
        Bar.Background = FlyoutPalette.FromHex(row.CalendarColorHex) ?? palette.Blue;
        Bar.Opacity = dim ? 0.5 : 1;

        TitleText.Text = row.Title;
        TitleText.Foreground = titleBrush;
        ToolTipService.SetToolTip(TitleText, row.Title);

        MetaText.Text = row.AttendeeText is { Length: > 0 } attendees ? row.MetaText + text.GetString("Flyout_MetaSeparator") + attendees : row.MetaText;
        MetaText.Foreground = palette.Secondary;

        if (row.BadgeText is { Length: > 0 } badge)
        {
            Pill.Visibility = Visibility.Visible;
            PillText.Text = badge;
            // "Now" is blue; the countdown on the next meeting is amber, as on the Mac.
            Pill.Background = row.IsCurrent ? palette.PillBackground : palette.ChipBackground;
            PillText.Foreground = row.IsCurrent ? palette.PillText : palette.ChipText;
        }

        if (row.Merge is { } merge)
        {
            MergePanel.Visibility = Visibility.Visible;
            MergeText.Text = merge.Summary;
            MergeText.Foreground = palette.Secondary;
            MergeIcon.Foreground = palette.Secondary;
            ToolTipService.SetToolTip(MergePanel, merge.CalendarsText);
            MergeCalendarsText.Text = merge.CalendarsText;
            MergeCalendarsText.Foreground = palette.Secondary;
            MergeCalendarsText.Visibility = merge.CalendarsText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (row.HasMergeActions)
        {
            BuildMenu(text);
        }

        if (row.IsCurrent)
        {
            Progress.Visibility = Visibility.Visible;
            Progress.Value = Math.Clamp(row.Progress, 0, 1);
            Progress.Foreground = palette.Blue;
            Progress.Background = palette.ProgressTrack;
        }

        if (row.HasJoin)
        {
            JoinLabel.Text = text.GetString("Flyout_Join");
            AutomationProperties.SetName(JoinButton, row.JoinAutomationName);
            if (row.JoinLabel is { Length: > 0 } provider)
            {
                ToolTipService.SetToolTip(JoinButton, provider);
                AutomationProperties.SetHelpText(JoinButton, provider);
            }
        }
        else
        {
            JoinButton.Visibility = Visibility.Collapsed;
        }

        if (row.HasJoinFailed)
        {
            FailedPanel.Visibility = Visibility.Visible;
            FailedText.Text = row.JoinFailedMessage ?? string.Empty;
            FailedText.Foreground = palette.Primary;
            LinkBox.Text = row.JoinFailedUrl ?? string.Empty;
            AutomationProperties.SetName(LinkBox, text.GetString("Tug_JoinLinkAutomation"));
            CopyButton.Content = text.GetString("Tug_CopyLink");
            AutomationProperties.SetName(CopyButton, text.GetString("Tug_CopyLinkAutomation"));
            CopiedText.Text = _copiedText;
            CopiedText.Foreground = palette.Secondary;
        }
    }

    private void OnJoinClick(object sender, RoutedEventArgs e) => _actions.Join(_row.EventId);

    /// <summary>The card's merge menu (null when the card has nothing to offer).</summary>
    public MenuFlyout? Menu { get; private set; }

    /// <summary>The button that opens <see cref="Menu"/> (the right-click and the menu key open it too).</summary>
    public Button MenuButton => ActionsButton;

    /// <summary>
    /// The popup that holds a menu's items is a UI Automation "menu" with no name of its own, which a screen reader reads as nothing. Once an
    /// item is on screen its popup is named, for the root menu after the card and for a submenu after the item that opened it.
    /// </summary>
    private static void NamePresenterWhenLoaded(MenuFlyoutItemBase item, string name)
    {
        item.Loaded += (_, _) =>
        {
            for (DependencyObject? node = item; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            {
                if (node is MenuFlyoutPresenter presenter)
                {
                    if (string.IsNullOrEmpty(AutomationProperties.GetName(presenter)))
                    {
                        AutomationProperties.SetName(presenter, name);
                    }

                    return;
                }
            }
        };
    }

    /// <summary>
    /// Closes the menu with its submenu and then asks for the action. A click closes a menu by itself; the same item pressed through UI
    /// Automation does not.
    /// </summary>
    private static void Choose(MenuFlyout menu, Action action)
    {
        menu.Hide();
        action();
    }

    /// <summary>
    /// Hiding a menu from code leaves an open submenu's popup behind (it stays on screen over the flyout); closing the menu by a click, Esc or
    /// light dismiss does not. So whenever the menu closes, every menu popup still open is closed with it.
    /// </summary>
    private void CloseLeftoverMenuPopups()
    {
        if (XamlRoot is not { } root)
        {
            return;
        }

        foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child is MenuFlyoutPresenter)
            {
                popup.IsOpen = false;
            }
        }
    }

    /// <summary>
    /// "Not the same meeting" (a submenu with the card's member meetings by calendar and time), "Unmerge", and "Merge with…" (a submenu with
    /// the events the engine listed as possible duplicates). Each item only reports the ids; the view model decides whether they still apply.
    /// </summary>
    private void BuildMenu(ILocalizer text)
    {
        // The menus are windows of their own, so a submenu is not squeezed into the 360 DIP flyout (constrained, it ended up over the header).
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight, ShouldConstrainToRootBounds = false };
        var row = _row;
        if (row.Merge is { } merge)
        {
            if (merge.CanSeparate)
            {
                var separate = new MenuFlyoutSubItem { Text = text.GetString("Flyout_NotSameMeeting") };
                AutomationProperties.SetName(separate, text.GetString("Flyout_NotSameMeeting"));
                foreach (var member in merge.Members)
                {
                    var item = new MenuFlyoutItem { Text = member.Label };
                    AutomationProperties.SetName(item, member.AutomationName);
                    var memberId = member.MemberId;
                    item.Click += (_, _) => Choose(menu, () => _actions.Separate(row.EventId, memberId));
                    NamePresenterWhenLoaded(item, text.GetString("Flyout_NotSameMeeting"));
                    separate.Items.Add(item);
                }

                NamePresenterWhenLoaded(separate, string.Format(System.Globalization.CultureInfo.CurrentCulture, text.GetString("Flyout_MergeActionsAutomation"), row.Title));
                menu.Items.Add(separate);
            }

            var unmerge = new MenuFlyoutItem { Text = text.GetString("Flyout_Unmerge") };
            AutomationProperties.SetName(unmerge, text.GetString("Flyout_UnmergeAutomation"));
            unmerge.Click += (_, _) => Choose(menu, () => _actions.Unmerge(row.EventId));
            NamePresenterWhenLoaded(unmerge, string.Format(System.Globalization.CultureInfo.CurrentCulture, text.GetString("Flyout_MergeActionsAutomation"), row.Title));
            menu.Items.Add(unmerge);
        }

        if (row.Candidates.Count > 0)
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            var mergeWith = new MenuFlyoutSubItem { Text = text.GetString("Flyout_MergeWith") };
            AutomationProperties.SetName(mergeWith, text.GetString("Flyout_MergeWith"));
            foreach (var candidate in row.Candidates)
            {
                var item = new MenuFlyoutItem { Text = candidate.Label };
                AutomationProperties.SetName(item, candidate.AutomationName);
                var otherId = candidate.EventId;
                item.Click += (_, _) => Choose(menu, () => _actions.Merge(row.EventId, otherId));
                NamePresenterWhenLoaded(item, text.GetString("Flyout_MergeWith"));
                mergeWith.Items.Add(item);
            }

            NamePresenterWhenLoaded(mergeWith, string.Format(System.Globalization.CultureInfo.CurrentCulture, text.GetString("Flyout_MergeActionsAutomation"), row.Title));
            menu.Items.Add(mergeWith);
        }

        menu.Opened += (_, _) => _actions.MenuOpenChanged(true);
        menu.Closed += (_, _) =>
        {
            CloseLeftoverMenuPopups();
            _actions.MenuOpenChanged(false);
        };
        Menu = menu;
        ActionsButton.Flyout = menu;
        ActionsButton.Visibility = Visibility.Visible;
        ToolTipService.SetToolTip(ActionsButton, text.GetString("Flyout_MergeActions"));
        AutomationProperties.SetName(ActionsButton, string.Format(System.Globalization.CultureInfo.CurrentCulture, text.GetString("Flyout_MergeActionsAutomation"), row.Title));
        ActionsButton.Foreground = _palette.Secondary;
        // Right-click, Shift+F10 and the menu key open the same menu on the card.
        Frame.ContextFlyout = menu;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_row.JoinFailedUrl is not { Length: > 0 } url)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(url);
        Clipboard.SetContent(package);
        CopiedText.Visibility = Visibility.Visible;
    }

    /// <summary>The copy button and the link box (for the self-test).</summary>
    public Button CopyLinkButton => CopyButton;

    public TextBox LinkText => LinkBox;

    public bool CopiedShown => CopiedText.Visibility == Visibility.Visible;
}
