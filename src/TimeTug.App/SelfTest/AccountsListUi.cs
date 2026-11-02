#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TimeTug.App.Controls;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.SelfTest;

/// <summary>Drives the accounts control the way Narrator or a keyboard user would: through UI Automation patterns, never through the code behind.</summary>
internal static class AccountsListUi
{
    public delegate Task<bool> Wait(Func<bool> condition, int milliseconds);

    /// <summary>The rows' automation names, in order ("Provider, name, status[, On this PC]").</summary>
    public static List<string> RowNames(AccountsListBox box) =>
        [.. (box.Accounts?.ListAccounts ?? []).Select(a => ItemPeer(box, a.ConnectionId)?.GetName() ?? "(no peer)")];

    /// <summary>The automation peer a UI Automation client (Narrator) sees for this row: the list's own child for the data item.</summary>
    public static ItemAutomationPeer? ItemPeer(AccountsListBox box, string connectionId)
    {
        var item = box.Accounts?.ListAccounts.FirstOrDefault(a => a.ConnectionId == connectionId);
        var children = FrameworkElementAutomationPeer.CreatePeerForElement(box.Rows)?.GetChildren();
        return item is null || children is null ? null : children.OfType<ItemAutomationPeer>().FirstOrDefault(p => ReferenceEquals(p.Item, item));
    }

    public static ListViewItem? Container(AccountsListBox box, string connectionId) =>
        box.Accounts?.ListAccounts.FirstOrDefault(a => a.ConnectionId == connectionId) is { } item ? box.Rows.ContainerFromItem(item) as ListViewItem : null;

    /// <summary>Selects a row through its SelectionItem pattern.</summary>
    public static bool Select(AccountsListBox box, string connectionId)
    {
        if (ItemPeer(box, connectionId)?.GetPattern(PatternInterface.SelectionItem) is not ISelectionItemProvider selection)
        {
            return false;
        }

        selection.Select();
        return true;
    }

    /// <summary>True when the list exposes the Selection pattern and every realized row the SelectionItem pattern (what Narrator needs to say "selected").</summary>
    public static bool ExposesListPatterns(AccountsListBox box) => ListPatternReport(box).Ok;

    /// <summary>As <see cref="ExposesListPatterns"/>, with what was found (for a failure message).</summary>
    public static (bool Ok, string Detail) ListPatternReport(AccountsListBox box)
    {
        var listPeer = FrameworkElementAutomationPeer.CreatePeerForElement(box.Rows);
        var detail = $"list peer {listPeer?.GetType().Name ?? "none"}, Selection {(listPeer?.GetPattern(PatternInterface.Selection) is not null)}";
        var ok = listPeer?.GetPattern(PatternInterface.Selection) is not null;
        foreach (var account in box.Accounts?.ListAccounts ?? [])
        {
            var container = Container(box, account.ConnectionId);
            var peer = ItemPeer(box, account.ConnectionId);
            var has = peer?.GetPattern(PatternInterface.SelectionItem) is not null;
            detail += $"; row {account.ConnectionId}: container {(container is null ? "none" : "yes")}, item peer {peer?.GetType().Name ?? "none"}, SelectionItem {has}";
            ok &= has;
        }

        return (ok, detail);
    }

    /// <summary>Opens the "+" menu through its button and returns the realized items (after the popup appeared).</summary>
    public static async Task<List<MenuFlyoutItem>> OpenMenuAsync(Wait wait, AccountsListBox box)
    {
        if (!ScenarioUi.Invoke(box.AddPlus))
        {
            return [];
        }

        var items = new List<MenuFlyoutItem>();
        await wait(() =>
        {
            items = FindAllInPopups<MenuFlyoutItem>(box.XamlRoot);
            return items.Count > 0;
        }, 3000);
        return items;
    }

    /// <summary>Opens the menu and invokes the entry with this automation name. False when the entry is missing or disabled.</summary>
    public static async Task<bool> ChooseAsync(Wait wait, AccountsListBox box, string entryAutomationName)
    {
        // A button pressed while the menu is still closing does not open it again, so a missing menu is asked for once more.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var items = await OpenMenuAsync(wait, box);
            if (items.Count == 0)
            {
                await Task.Delay(400);
                continue;
            }

            if (items.FirstOrDefault(i => AutomationProperties.GetName(i) == entryAutomationName) is not { IsEnabled: true } item
                || FrameworkElementAutomationPeer.CreatePeerForElement(item)?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
            {
                return false;
            }

            invoke.Invoke();
            return true;
        }

        return false;
    }

    /// <summary>Closes an open menu with Escape's effect (hides the flyout) and reports whether focus is back on "+".</summary>
    public static async Task<bool> CloseMenuAsync(Wait wait, AccountsListBox box)
    {
        box.Menu.Hide();
        var closed = await wait(() => FindAllInPopups<MenuFlyoutItem>(box.XamlRoot).Count == 0, 3000);
        await Task.Delay(400);   // the closing animation: a button pressed meanwhile would not open the menu again
        return closed;
    }

    public static async Task<bool> PressDialogAsync(Wait wait, XamlRoot root, string buttonName)
    {
        Button? button = null;
        if (!await wait(() => (button = FindInPopups<Button>(root, b => b.Name == buttonName && b.Visibility == Visibility.Visible)) is not null, 3000))
        {
            return false;
        }

        if (FrameworkElementAutomationPeer.CreatePeerForElement(button).GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
        {
            return false;
        }

        invoke.Invoke();
        return true;
    }

    public static T? FindInPopups<T>(XamlRoot root, Func<T, bool> match)
        where T : DependencyObject
    {
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child is { } child && Find(child, match) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    public static List<T> FindAllInPopups<T>(XamlRoot root)
        where T : DependencyObject
    {
        var found = new List<T>();
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child is { } child)
            {
                Collect(child, found);
            }
        }

        return found;
    }

    /// <summary>The element with keyboard focus, if any, as a short description (for a failure message).</summary>
    public static string FocusedName(XamlRoot root) =>
        Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) is FrameworkElement f ? $"{f.GetType().Name} '{AutomationProperties.GetName(f)}'" : "none";

    private static T? Find<T>(DependencyObject node, Func<T, bool> match)
        where T : DependencyObject
    {
        if (node is T typed && match(typed))
        {
            return typed;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (Find(VisualTreeHelper.GetChild(node, i), match) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    private static void Collect<T>(DependencyObject node, List<T> found)
        where T : DependencyObject
    {
        if (node is T typed)
        {
            found.Add(typed);
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            Collect(VisualTreeHelper.GetChild(node, i), found);
        }
    }
}
#endif
