using TimeTug.Presentation.JumpList;
using Windows.UI.StartScreen;
using WinJumpList = Windows.UI.StartScreen.JumpList;

namespace TimeTug.Platform.JumpList;

/// <summary>
/// <c>Windows.UI.StartScreen.JumpList</c> for the packaged app (docs/spikes/2026-10-chip-window-and-jumplist.md): it needs no window and no pin,
/// and Windows writes the saved list to disk, where it stays after the app exits. Every save replaces the whole list with the given tasks
/// (no group name, so they are tasks) and keeps the "Recent" category out. A process without package identity cannot have a list: the
/// calls throw and <see cref="JumpListService"/> records that and carries on.
/// </summary>
public sealed class WindowsJumpListPlatform : IJumpListPlatform
{
    public bool IsSupported
    {
        get
        {
            try
            {
                return WinJumpList.IsSupported();
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or TypeLoadException)
            {
                return false;
            }
        }
    }

    public async ValueTask<IReadOnlyList<JumpListEntry>> LoadAsync()
    {
        var list = await WinJumpList.LoadCurrentAsync().AsTask().ConfigureAwait(false);
        return list.Items
            .Where(i => i.Kind == JumpListItemKind.Arguments && string.IsNullOrEmpty(i.GroupName))
            .Select(i => new JumpListEntry(i.DisplayName, i.Arguments))
            .ToList();
    }

    public async ValueTask SaveAsync(IReadOnlyList<JumpListEntry> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var list = await WinJumpList.LoadCurrentAsync().AsTask().ConfigureAwait(false);
        list.SystemGroupKind = JumpListSystemGroupKind.None;
        list.Items.Clear();
        foreach (var task in tasks)
        {
            list.Items.Add(JumpListItem.CreateWithArguments(task.Arguments, task.DisplayName));
        }

        await list.SaveAsync().AsTask().ConfigureAwait(false);
    }
}
