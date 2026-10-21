namespace TimeTug.Presentation.Settings;

/// <summary>
/// Where <see cref="PresentationSettings"/> are kept between runs. The Windows implementation is over
/// <c>ApplicationData.LocalSettings</c>. Both members never throw: a value that cannot be read gives its default, one that cannot be
/// written is dropped.
/// </summary>
public interface IPresentationSettingsStore
{
    /// <summary>The stored settings; every field that is missing or unreadable is at its default.</summary>
    PresentationSettings Load();

    /// <summary>Stores the settings and raises <see cref="Changed"/> once.</summary>
    void Save(PresentationSettings settings);

    /// <summary>Raised once per <see cref="Save"/>, after the new values are readable. Subscribers compare and apply what changed.</summary>
    event Action? Changed;
}

/// <summary>A store that lives only as long as the process (the self-test, and tests).</summary>
public sealed class MemoryPresentationSettingsStore(PresentationSettings? initial = null) : IPresentationSettingsStore
{
    private PresentationSettings _settings = initial ?? PresentationSettings.Defaults;

    public event Action? Changed;

    public PresentationSettings Load() => _settings;

    public void Save(PresentationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        Changed?.Invoke();
    }
}
