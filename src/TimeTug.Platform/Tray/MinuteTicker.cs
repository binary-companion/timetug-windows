using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Tray;

/// <summary>
/// Ticks on every whole minute of the time provider's clock, so tray wording that reads whole minutes flips with the
/// clock. A display refresh only; timers live in this project by rule.
/// </summary>
public sealed class MinuteTicker(TimeProvider time) : IMinuteTicker
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public IDisposable Start(Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        var intoMinute = TimeSpan.FromTicks(time.GetUtcNow().Ticks % Minute.Ticks);
        var untilNext = intoMinute == TimeSpan.Zero ? Minute : Minute - intoMinute;
        return time.CreateTimer(_ => tick(), null, untilNext, Minute);
    }
}
