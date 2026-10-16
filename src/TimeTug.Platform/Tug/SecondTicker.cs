using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Tug;

/// <summary>
/// Ticks just after every whole second of the time provider's clock, so a countdown that reads whole seconds flips on the
/// second instead of drifting between two ticks. A display refresh only; timers live in this project by rule.
/// </summary>
public sealed class SecondTicker(TimeProvider time) : ISecondTicker
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    public IDisposable Start(Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        var intoSecond = TimeSpan.FromMilliseconds(time.GetUtcNow().Millisecond);
        var untilNext = intoSecond == TimeSpan.Zero ? Second : Second - intoSecond;
        return time.CreateTimer(_ => tick(), null, untilNext, Second);
    }
}
