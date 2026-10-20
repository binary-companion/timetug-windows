using TimeTug.Platform.Hotkey;
using TimeTug.Presentation.Settings;

namespace TimeTug.Platform.Tests.Hotkey;

public class LocalSettingsHotkeyStoreTests
{
    private static readonly HotkeyState Registered = new(
        new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T'), HotkeyStatus.Registered, new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Nothing_stored_is_no_shortcut()
    {
        new LocalSettingsHotkeyStore(new Dictionary<string, object>()).Load().Should().Be(HotkeyState.None);
    }

    [Fact]
    public void A_state_survives_a_save_and_load()
    {
        var values = new Dictionary<string, object>();

        new LocalSettingsHotkeyStore(values).Save(Registered);

        new LocalSettingsHotkeyStore(values).Load().Should().Be(Registered);
        values.Should().ContainKey("Hotkey.State").WhoseValue.Should().BeOfType<string>().Which.Should().Contain("Ctrl+Alt+T");
    }

    [Fact]
    public void A_stored_value_of_the_wrong_kind_or_garbage_reads_as_none()
    {
        var values = new Dictionary<string, object> { ["Hotkey.State"] = 42 };
        new LocalSettingsHotkeyStore(values).Load().Should().Be(HotkeyState.None);

        values["Hotkey.State"] = "not json";
        new LocalSettingsHotkeyStore(values).Load().Should().Be(HotkeyState.None);
    }

    [Fact]
    public void Another_key_keeps_separate_state()
    {
        var values = new Dictionary<string, object>();
        new LocalSettingsHotkeyStore(values, "Other").Save(Registered);

        new LocalSettingsHotkeyStore(values).Load().Should().Be(HotkeyState.None);
    }

    [Fact]
    public void A_settings_container_that_cannot_be_read_or_written_is_survived_and_logged()
    {
        var log = new RecordingDiagnostics();
        var store = new LocalSettingsHotkeyStore(new ThrowingValues(), diagnostics: log);

        store.Load().Should().Be(HotkeyState.None);
        var save = () => store.Save(Registered);

        save.Should().NotThrow();
        log.Entries.Should().HaveCount(2).And.OnlyContain(e => e.Category == "hotkey");
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

        public bool Remove(string key) => false;

        public bool Remove(KeyValuePair<string, object> item) => false;

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
