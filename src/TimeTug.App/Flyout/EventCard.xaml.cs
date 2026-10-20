using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;
using Windows.ApplicationModel.DataTransfer;

namespace TimeTug.App.Flyout;

/// <summary>
/// One agenda item as the Mac's <c>EventCard</c> draws it: the time, the calendar's colour bar, the title with a "Now" or countdown
/// pill, the meta line, a progress bar for the meeting in progress, and a Join button. When opening the link failed, the card shows
/// the link in a box with a Copy button, like the tug. Everything it shows is already decided in <see cref="FlyoutRow"/>.
/// </summary>
internal sealed partial class EventCard : UserControl
{
    private readonly FlyoutRow _row;
    private readonly FlyoutPalette _palette;
    private readonly Action<string> _join;
    private readonly string _copiedText;

    public EventCard(FlyoutRow row, FlyoutPalette palette, ILocalizer text, Action<string> join)
    {
        _row = row;
        _palette = palette;
        _join = join;
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

        if (row.MergeSummary is { Length: > 0 } merge)
        {
            MergePanel.Visibility = Visibility.Visible;
            MergeText.Text = merge;
            MergeText.Foreground = palette.Secondary;
            MergeIcon.Foreground = palette.Secondary;
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

    private void OnJoinClick(object sender, RoutedEventArgs e) => _join(_row.EventId);

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
