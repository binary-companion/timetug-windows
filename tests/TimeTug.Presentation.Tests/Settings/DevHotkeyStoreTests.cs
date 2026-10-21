using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Settings;

namespace TimeTug.Presentation.Tests.Settings;

public sealed class DevHotkeyStoreTests : IDisposable
{
    private static readonly HotkeyBinding Chord = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T');
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Directory.CreateTempSubdirectory("dev-hotkey").FullName;
    private readonly Memory _inner = new();
    private readonly FakeTimeProvider _time = new(Now);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Path => System.IO.Path.Combine(_folder, DevHotkeyStore.FileName);

    private DevHotkeyStore Store() => new(_inner, Path, _time);

    [Fact]
    public void Without_the_file_the_stored_state_is_used()
    {
        _inner.State = new HotkeyState(Chord, HotkeyStatus.Registered, Now.AddDays(-1));

        Store().Load().Should().Be(_inner.State);
    }

    [Fact]
    public void A_shortcut_in_the_file_is_used_when_nothing_is_stored()
    {
        File.WriteAllText(Path, "Ctrl+Alt+T\n");

        Store().Load().Should().Be(new HotkeyState(Chord, HotkeyStatus.Registered, Now));
    }

    [Fact]
    public void A_shortcut_in_the_file_wins_over_a_different_stored_one()
    {
        _inner.State = new HotkeyState(new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'Y'), HotkeyStatus.Registered, Now.AddDays(-1));
        File.WriteAllText(Path, "Ctrl+Alt+T");

        Store().Load().Binding.Should().Be(Chord);
    }

    [Fact]
    public void When_the_stored_shortcut_is_the_same_the_stored_state_with_its_conflict_is_kept()
    {
        _inner.State = new HotkeyState(Chord, HotkeyStatus.Conflict, Now.AddDays(-1), Chord);
        File.WriteAllText(Path, "Ctrl+Alt+T");

        Store().Load().Should().Be(_inner.State, "the conflict found at the last start is still true");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a shortcut")]
    [InlineData("T")]
    public void A_file_that_does_not_hold_a_valid_shortcut_is_ignored(string text)
    {
        _inner.State = new HotkeyState(Chord, HotkeyStatus.Registered, Now.AddDays(-1));
        File.WriteAllText(Path, text);

        Store().Load().Should().Be(_inner.State);
    }

    [Fact]
    public void Saving_goes_to_the_real_store()
    {
        File.WriteAllText(Path, "Ctrl+Alt+T");
        var state = new HotkeyState(Chord, HotkeyStatus.Conflict, Now, Chord);

        Store().Save(state);

        _inner.State.Should().Be(state);
    }

    private sealed class Memory : IHotkeyStore
    {
        public HotkeyState State { get; set; } = HotkeyState.None;

        public HotkeyState Load() => State;

        public void Save(HotkeyState state) => State = state;
    }
}
