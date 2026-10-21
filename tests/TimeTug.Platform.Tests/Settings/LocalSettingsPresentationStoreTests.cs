using TimeTug.Platform.Settings;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;

namespace TimeTug.Platform.Tests.Settings;

public class LocalSettingsPresentationStoreTests
{
    private static readonly PresentationSettings Custom = new(AppTheme.Dark, TrayIconStyle.IconOnly, FlyoutBackdrop.Solid, new WindowGeometry(-1280, 24, 1000, 700));

    [Fact]
    public void A_fresh_install_has_the_defaults()
    {
        new LocalSettingsPresentationStore(new Dictionary<string, object>()).Load().Should().Be(PresentationSettings.Defaults);
    }

    [Fact]
    public void Every_field_survives_a_save_and_load_in_a_new_store()
    {
        var values = new Dictionary<string, object>();

        new LocalSettingsPresentationStore(values).Save(Custom);

        new LocalSettingsPresentationStore(values).Load().Should().Be(Custom);
        values.Values.Should().OnlyContain(v => v is string, "the local settings container keeps plain strings");
    }

    [Fact]
    public void Clearing_the_window_geometry_removes_its_key()
    {
        var values = new Dictionary<string, object>();
        var store = new LocalSettingsPresentationStore(values);
        store.Save(Custom);

        store.Save(Custom with { SettingsWindow = null });

        values.Should().NotContainKey(PresentationSettingsCodec.SettingsWindowKey);
        store.Load().SettingsWindow.Should().BeNull();
    }

    [Fact]
    public void A_corrupt_value_is_that_fields_default_and_the_other_fields_are_kept()
    {
        var values = new Dictionary<string, object>();
        new LocalSettingsPresentationStore(values).Save(Custom);
        values[PresentationSettingsCodec.BackdropKey] = "Glass";
        values[PresentationSettingsCodec.SettingsWindowKey] = "1,2,3";

        var loaded = new LocalSettingsPresentationStore(values).Load();

        loaded.Should().Be(Custom with { Backdrop = FlyoutBackdrop.Material, SettingsWindow = null });
    }

    [Fact]
    public void A_value_of_the_wrong_type_is_the_default()
    {
        var values = new Dictionary<string, object> { [PresentationSettingsCodec.ThemeKey] = 3, [PresentationSettingsCodec.TrayStyleKey] = 1.5 };

        new LocalSettingsPresentationStore(values).Load().Should().Be(PresentationSettings.Defaults);
    }

    [Fact]
    public void Changed_fires_once_per_save_after_the_values_can_be_read()
    {
        var store = new LocalSettingsPresentationStore(new Dictionary<string, object>());
        var seen = new List<PresentationSettings>();
        store.Changed += () => seen.Add(store.Load());

        store.Save(Custom);
        store.Save(Custom with { Theme = AppTheme.Light });

        seen.Select(s => s.Theme).Should().Equal(AppTheme.Dark, AppTheme.Light);
    }

    [Fact]
    public void A_container_that_cannot_be_read_or_written_is_survived_and_logged()
    {
        var log = new RecordingDiagnostics();
        var store = new LocalSettingsPresentationStore(new ThrowingValues(), log);
        var changes = 0;
        store.Changed += () => changes++;

        store.Load().Should().Be(PresentationSettings.Defaults);
        var save = () => store.Save(Custom);

        save.Should().NotThrow();
        changes.Should().Be(1, "the person asked for the change, so the app applies it for this run even though it could not be kept");
        log.Entries.Should().HaveCount(2).And.OnlyContain(e => e.Category == "presentation-settings");
    }

    private sealed class ThrowingValues : IDictionary<string, object>
    {
        public object this[string key]
        {
            get => throw new InvalidOperationException("no settings");
            set => throw new InvalidOperationException("no settings");
        }

        public bool TryGetValue(string key, out object value) => throw new InvalidOperationException("no settings");

        public ICollection<string> Keys => [];

        public ICollection<object> Values => [];

        public int Count => 0;

        public bool IsReadOnly => false;

        public void Add(string key, object value) => throw new InvalidOperationException("no settings");

        public void Add(KeyValuePair<string, object> item) => throw new InvalidOperationException("no settings");

        public void Clear()
        {
        }

        public bool Contains(KeyValuePair<string, object> item) => false;

        public bool ContainsKey(string key) => false;

        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        {
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, object>>().GetEnumerator();

        public bool Remove(string key) => throw new InvalidOperationException("no settings");

        public bool Remove(KeyValuePair<string, object> item) => throw new InvalidOperationException("no settings");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
