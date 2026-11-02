using System.Collections;
using TimeTug.Platform.Settings;

namespace TimeTug.Platform.Tests.Settings;

/// <summary>The remembered removal of the Windows account (spec A1.3): one boolean in the app's local settings, no account data.</summary>
public class LocalSettingsWindowsAccountDismissalStoreTests
{
    [Fact]
    public void Defaults_to_not_dismissed() =>
        new LocalSettingsWindowsAccountDismissalStore(new Dictionary<string, object>()).IsDismissed.Should().BeFalse();

    [Fact]
    public void Dismiss_then_read_back()
    {
        var values = new Dictionary<string, object>();
        var store = new LocalSettingsWindowsAccountDismissalStore(values);

        store.Dismiss();

        store.IsDismissed.Should().BeTrue();
        values.Should().ContainKey("Accounts.WindowsAccountDismissed").WhoseValue.Should().Be(true);
    }

    [Fact]
    public void Survives_a_new_instance_over_the_same_values()
    {
        var values = new Dictionary<string, object>();
        new LocalSettingsWindowsAccountDismissalStore(values).Dismiss();

        new LocalSettingsWindowsAccountDismissalStore(values).IsDismissed.Should().BeTrue();
    }

    [Theory]
    [InlineData("yes")]
    [InlineData(1)]
    [InlineData(false)]
    public void A_value_of_the_wrong_kind_or_false_reads_as_not_dismissed(object stored)
    {
        var values = new Dictionary<string, object> { ["Accounts.WindowsAccountDismissed"] = stored };

        new LocalSettingsWindowsAccountDismissalStore(values).IsDismissed.Should().BeFalse();
    }

    [Fact]
    public void A_container_that_cannot_be_read_or_written_is_survived_and_logged_without_account_data()
    {
        var log = new RecordingDiagnostics();
        var store = new LocalSettingsWindowsAccountDismissalStore(new ThrowingValues("me@example.com"), diagnostics: log);

        store.IsDismissed.Should().BeFalse();
        var dismiss = () => store.Dismiss();

        dismiss.Should().NotThrow();
        log.Entries.Should().HaveCount(2).And.OnlyContain(e => e.Category == "accounts" && !e.Message.Contains("example.com"));
    }

    private sealed class ThrowingValues(string sentinel) : IDictionary<string, object>
    {
        private InvalidOperationException Boom() => new(sentinel);

        public object this[string key]
        {
            get => throw Boom();
            set => throw Boom();
        }

        public ICollection<string> Keys => throw Boom();
        public ICollection<object> Values => throw Boom();
        public int Count => throw Boom();
        public bool IsReadOnly => false;
        public void Add(string key, object value) => throw Boom();
        public void Add(KeyValuePair<string, object> item) => throw Boom();
        public void Clear() => throw Boom();
        public bool Contains(KeyValuePair<string, object> item) => throw Boom();
        public bool ContainsKey(string key) => throw Boom();
        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) => throw Boom();
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => throw Boom();
        public bool Remove(string key) => throw Boom();
        public bool Remove(KeyValuePair<string, object> item) => throw Boom();
        public bool TryGetValue(string key, out object value) => throw Boom();
        IEnumerator IEnumerable.GetEnumerator() => throw Boom();
    }
}
