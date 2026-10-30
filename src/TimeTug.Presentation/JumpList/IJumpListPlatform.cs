namespace TimeTug.Presentation.JumpList;

/// <summary>One task in the jump list: the name Windows shows and the arguments the app is started with when it is chosen.</summary>
public sealed record JumpListEntry(string DisplayName, string Arguments);

/// <summary>
/// The system's jump list for this app (<c>Windows.UI.StartScreen.JumpList</c>). It works with no window open and whether or not the app
/// is pinned; Windows keeps the saved list on disk, so it outlives the process. Tests use a fake.
/// </summary>
public interface IJumpListPlatform
{
    /// <summary>False when this system or process cannot have a jump list (for example no package identity).</summary>
    bool IsSupported { get; }

    /// <summary>The tasks Windows has stored for the app now.</summary>
    ValueTask<IReadOnlyList<JumpListEntry>> LoadAsync();

    /// <summary>Replaces the whole list with these tasks (and no "recent" category). Throws when Windows refuses.</summary>
    ValueTask SaveAsync(IReadOnlyList<JumpListEntry> tasks);
}
