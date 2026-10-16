namespace TimeTug.Presentation.Abstractions;

/// <summary>One display, in virtual-screen pixels.</summary>
public sealed record MonitorInfo(string Id, int X, int Y, int Width, int Height, bool IsPrimary);

/// <summary>The connected displays.</summary>
public interface IMonitorService
{
    IReadOnlyList<MonitorInfo> Monitors { get; }

    MonitorInfo Primary { get; }

    event Action? Changed;
}
