namespace TimeTug.Presentation.Settings;

/// <summary>
/// Whether on-device duplicate finding can run, from the engine's own status (<c>inferenceStatus.kind</c>). On Windows 1.0 no text
/// generator is registered, so the engine says <see cref="NoEngine"/> and the switch stays off and disabled.
/// </summary>
public enum InferenceAvailability
{
    /// <summary>The person turned it off. Says nothing about the hardware.</summary>
    Disabled,

    /// <summary>No on-device model is registered.</summary>
    NoEngine,

    /// <summary>A model exists but cannot run now (the engine gives a reason).</summary>
    Unavailable,

    /// <summary>The model does not run on the device, so it is not used.</summary>
    NotOnDevice,

    /// <summary>It is running.</summary>
    Active,
}

public static class InferenceAvailabilityExtensions
{
    /// <summary>
    /// False only when on-device intelligence definitively cannot run here (the Mac's <c>isAvailableOnThisMac</c>). A feature the person
    /// turned off says nothing about the machine, so <see cref="InferenceAvailability.Disabled"/> counts as available.
    /// </summary>
    public static bool CanRunHere(this InferenceAvailability availability) =>
        availability is InferenceAvailability.Disabled or InferenceAvailability.Active;
}
