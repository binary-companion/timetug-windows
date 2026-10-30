namespace TimeTug.Presentation.Chip;

/// <summary>
/// When the countdown chip (a small always-on-top box with the next meeting and how long until it starts) is on screen. A presentation preference:
/// the engine decides which meetings exist and when each one is; this only says when the person wants to be shown them.
/// </summary>
public enum ChipMode
{
    /// <summary>Never shown. The default, so nothing changes for a person who did not ask for it.</summary>
    Off,

    /// <summary>Shown from a chosen number of minutes before the next timed meeting until that meeting ends.</summary>
    BeforeMeetings,

    /// <summary>Shown whenever today has a current or next timed meeting.</summary>
    Always,
}
