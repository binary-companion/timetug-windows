using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Abstractions;

/// <summary>Shows a takeover as a toast notification.</summary>
public interface IToastService
{
    /// <summary>True when Windows accepted the toast.</summary>
    ValueTask<bool> ShowAsync(TakeoverRequest request);
}
